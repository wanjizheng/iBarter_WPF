using Newtonsoft.Json;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Syncfusion.Windows.PropertyGrid;

namespace iBarter.Model {
    public class CargoProperty : INotifyPropertyChanged {
        private double propExtraLT, propTotalLT, doubCurrentLT, doubInitialLT, doubAfterRunLT;

        public CargoProperty(double _extralLT = -1, double _totalLT = -1, double _currentLT = 0, double _initialLT = 0, double _afterRunLT = 0) {
            propExtraLT = _extralLT;
            propTotalLT = _totalLT;
            doubCurrentLT = _currentLT;
            doubInitialLT = _initialLT;
            doubAfterRunLT = _initialLT + _currentLT;
        }

        [Category("CargoProperty"), Description("Extra LT"), DisplayName("ExtraLT")]
        public double ExtraLT {
            get { return propExtraLT; }
            set {
                propExtraLT = value;
                OnPropertyChanged();
            }
        }

        [Category("CargoProperty"), Description("Total LT"), DisplayName("TotalLT")]
        public double TotalLT {
            get { return propTotalLT; }
            set {
                propTotalLT = value;
                OnPropertyChanged();
            }
        }

        [Category("CargoProperty"), Description("Current LT"), DisplayName("CurrentLT")]
        public double CurrentLT {
            get { return doubCurrentLT; }
            set {
                doubCurrentLT = value;
                OnPropertyChanged();
            }
        }

        [Category("CargoProperty"), Description("Initial LT"), DisplayName("InitialLT")]
        public double InitialLT {
            get { return doubInitialLT; }
            set {
                doubInitialLT = value;
                OnPropertyChanged();
            }
        }

        // Total LT on the ship AFTER all CargoDetails exchanges are done:
        //   = InitialLT  (extra Item1 stock that had to be loaded for
        //                chain roots - still on the ship because the chain
        //                consumed them through the barter)
        //   + CurrentLT (net Item2 leftover after chain consumption)
        // Total LT of all goods on the ship at run-completion.
        [Category("CargoProperty"), Description("After-Run LT"), DisplayName("AfterRunLT")]
        public double AfterRunLT {
            get { return doubAfterRunLT; }
            set {
                doubAfterRunLT = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            Application.Current?.Dispatcher.Invoke(() => {
                var pg = App.myfmMain?.myShipCargo?.PropertyGrid_Ship;
                if (pg == null) return;

                if (AfterRunLT > TotalLT && AfterRunLT <= TotalLT * 1.7 && InitialLT <= TotalLT) {
                    pg.Foreground = Brushes.Red;
                    pg.FontWeight = FontWeights.Bold;
                    pg.ViewBackgroundColor = Brushes.IndianRed;
                }
                else if (AfterRunLT > TotalLT * 1.7 || InitialLT > TotalLT) {
                    pg.Foreground = Brushes.Red;
                    pg.FontWeight = FontWeights.Bold;
                    pg.ViewBackgroundColor = Brushes.DarkRed;
                }
                else {
                    pg.Foreground = Brushes.Black;
                    pg.FontWeight = FontWeights.Normal;
                    pg.ViewBackgroundColor = Brushes.White;
                }

                SaveData();
            });
        }

        private void SaveData() {
            try {
                if (App.myCargoProperty != null) {
                    string strPath_Data = AppDomain.CurrentDomain.BaseDirectory + "Resources\\myShipProperty_Data.json";
                    if (App.myCargoProperty != null && App.myCargoProperty.TotalLT != -1 && App.myCargoProperty.ExtraLT != -1) {
                        using (FileStream streamData = new FileStream(strPath_Data, FileMode.OpenOrCreate, FileAccess.Write)) {
                            streamData.SetLength(0);

                            string jsonData = JsonConvert.SerializeObject(App.myCargoProperty);
                            if (jsonData.Length > 3) {
                                //File.WriteAllText(strPath_Data, jsonData);
                                byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(jsonData);
                                streamData.Write(byteArray, 0, byteArray.Length);
                            }
                        }
                    }
                    //App.myCFun.Log("Saved data.", Brushes.DarkOliveGreen);
                }
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
            }
        }
    }
}