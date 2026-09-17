using Newtonsoft.Json;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Syncfusion.Windows.PropertyGrid;

namespace iBarter.Model {
    public class CargoProperty : INotifyPropertyChanged {
        private double propExtraLT, propTotalLT, doubCurrentLT, doubInitialLT, doubPeakLT;

        public CargoProperty(double _extralLT = -1, double _totalLT = -1, double _currentLT = 0, double _initialLT = 0) {
            propExtraLT = _extralLT;
            propTotalLT = _totalLT;
            doubCurrentLT = _currentLT;
            doubInitialLT = _initialLT;
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

        [Category("CargoProperty"), Description("Peak LT"), DisplayName("PeakLT")]
        public double PeakLT {
            get { return doubPeakLT; }
            set {
                doubPeakLT = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            Application.Current?.Dispatcher.Invoke(() => {
                var pg = App.myfmMain?.myShipCargo?.PropertyGrid_Ship;
                if (pg == null) return;

                // UX-researched palette: avoid green (low readability on
                // both white PropertyGrid and the dark map background);
                // use blue for safe, orange for warning, crimson for
                // danger. All three have high contrast on both light
                // and dark surrounds. AfterRunLT was removed (duplicates
                // CurrentLT) so this logic now keys off CurrentLT directly.
                if (CurrentLT > TotalLT && CurrentLT <= TotalLT * 1.7 && InitialLT <= TotalLT) {
                    // warning group: amber foreground, soft amber
                    // background (no green, no flat-red clash with the
                    // map's red island markers)
                    pg.Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x8A, 0x00)); // DarkOrange
                    pg.FontWeight = FontWeights.SemiBold;
                    pg.ViewBackgroundColor = new SolidColorBrush(Color.FromRgb(0xFF, 0xE5, 0xCC));
                }
                else if (CurrentLT > TotalLT * 1.7 || InitialLT > TotalLT) {
                    // danger group: crimson foreground, light-pink
                    // background (clear alert without being garish red)
                    pg.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E)); // Crimson
                    pg.FontWeight = FontWeights.Bold;
                    pg.ViewBackgroundColor = new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0xD6));
                }
                else {
                    // safe group: deep blue (high contrast on both
                    // PropertyGrid white and the map's dark navy)
                    pg.Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0xA8)); // DarkSlateBlue
                    pg.FontWeight = FontWeights.Normal;
                    pg.ViewBackgroundColor = new SolidColorBrush(Color.FromRgb(0xF5, 0xF8, 0xFC)); // very light blue
                }

                bool isAutomaticDisplayValue = App.myRouteCoordinator?.Mode == ViewModel.CargoMode.AutomaticRoute
                    && propertyName is nameof(InitialLT) or nameof(CurrentLT) or nameof(PeakLT);
                if (!isAutomaticDisplayValue)
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
