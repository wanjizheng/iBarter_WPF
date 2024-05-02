using Newtonsoft.Json;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace iBarter.Model {
    public class CargoProperty : INotifyPropertyChanged {
        private double propExtraLT, propTotalLT, doubCurrentLT;

        public CargoProperty(double _extralLT = 2109, double _totalLT = 21500, double _currentLT = 0) {
            propExtraLT = _extralLT;
            propTotalLT = _totalLT;
            doubCurrentLT = _currentLT;
        }

        [Category("CargoProperty"), Description("Extra LT"), DisplayName("ExtraLT")]
        public double ExtraLT {
            get { return propExtraLT; }
            set { propExtraLT = value; OnPropertyChanged(); }
        }

        [Category("CargoProperty"), Description("Total LT"), DisplayName("TotalLT")]
        public double TotalLT {
            get { return propTotalLT; }
            set { propTotalLT = value; OnPropertyChanged(); }
        }

        [Category("CargoProperty"), Description("Current LT"), DisplayName("CurrentLT")]
        public double CurrentLT {
            get { return doubCurrentLT; }
            set { doubCurrentLT = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (App.myfmMain != null) {
                if (CurrentLT > TotalLT - ExtraLT) {
                    App.myfmMain.myShipCargo.PropertyGrid_Ship.Foreground = Brushes.Red;
                    App.myfmMain.myShipCargo.PropertyGrid_Ship.FontWeight = FontWeights.Bold;
                    App.myfmMain.myShipCargo.PropertyGrid_Ship.ViewBackgroundColor = Brushes.Cyan;
                }
                else {
                    App.myfmMain.myShipCargo.PropertyGrid_Ship.Foreground = Brushes.Black;
                    App.myfmMain.myShipCargo.PropertyGrid_Ship.FontWeight = FontWeights.Normal;
                    App.myfmMain.myShipCargo.PropertyGrid_Ship.ViewBackgroundColor = Brushes.White;
                }

                SaveData();
            }
        }
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

        private void SaveData() {
            try {
                if (App.myCargoProperty != null) {
                    string strPath_Data = AppDomain.CurrentDomain.BaseDirectory + "Resources\\myShipProperty_Data.json";

                    using (FileStream streamData = new FileStream(strPath_Data, FileMode.Create, FileAccess.Write)) {
                        string jsonData = JsonConvert.SerializeObject(App.myCargoProperty);
                        //File.WriteAllText(strPath_Data, jsonData);
                        byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(jsonData);
                        streamData.Write(byteArray, 0, byteArray.Length);
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
