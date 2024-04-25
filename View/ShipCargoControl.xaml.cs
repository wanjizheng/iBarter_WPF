using iBarter.Model;
using Newtonsoft.Json;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for ShipCargoControl.xaml
    /// </summary>
    public partial class ShipCargoControl : UserControl {
        public ShipCargoControl() {
            InitializeComponent();
            DataContext = App.myCVM;
            //PropertyGrid_Ship.Items = App.myCVM.CargoProperty;
        }

        public void RefreshData() {
            if (App.myfmMain != null) {
                string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\myShipCargoItems_Data.json";

                if (File.Exists(strPath_Data)) {
                    try {
                        string readJsonData = File.ReadAllText(strPath_Data);
                        App.listCargoItems = JsonConvert.DeserializeObject<List<Barter>>(readJsonData);

                        if (App.myCVM != null) {
                            App.myCVM.CargoDetails.Clear();
                        }

                        ListBox_ShipCargo.BeginInit();
                        for (int i = 0; i < App.listCargoItems.Count; i++) {
                            App.myCVM.CargoDetails.Add(App.listCargoItems[i]);
                        }

                        ListBox_ShipCargo.EndInit();
                        strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\myShipProperty_Data.json";
                        if (File.Exists(strPath_Data)) {
                            readJsonData = File.ReadAllText(strPath_Data);
                            App.myCargoProperty = JsonConvert.DeserializeObject<CargoProperty>(readJsonData);
                        }
                    }
                    catch (Exception exception) {
                        App.myCFun.Log(exception.Message, Brushes.Red);
                    }
                    //myPlannerControl.DataGrid_Planner.ItemsSource = dataSource;
                }
                else {
                    // ShipCargo myCargo = new ShipCargo(new List<Barter>());
                    // myCargo.ExtraLT = 1009;
                    // myCargo.TotalLT = 21500;
                    // myCargo.CurrentLT = 0;
                    // PropertyGrid_Ship.SelectedObject = myCargo;
                    App.myCargoProperty = new CargoProperty();
                    //PropertyGrid_Ship.SelectedObject = App.myCargoProperty;
                }
                ListBox_ShipCargo.ItemsSource = App.myCVM.CargoDetails;
                PropertyGrid_Ship.SelectedObject = App.myCargoProperty;
            }
        }

        public void SaveData() {
            try {
                if (App.myCVM != null) {
                    string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\myShipCargoItems_Data.json";

                    using (FileStream streamData = new FileStream(strPath_Data, FileMode.Create, FileAccess.Write)) {
                        App.listCargoItems.Clear();
                        for (int i = 0; i < App.myCVM.CargoDetails.Count; i++) {
                            Barter myItem = App.myCVM.CargoDetails[i];
                            if (!App.listCargoItems.Contains(myItem)) {
                                App.listCargoItems.Add(myItem);
                            }
                        }

                        string jsonData = JsonConvert.SerializeObject(App.listCargoItems);
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

        public void UpdateCurrentLV() {
            App.myCargoProperty.CurrentLT = 0;
            foreach (Barter barter in App.myCVM.CargoDetails) {
                int intWeight = 0;
                switch (barter.Item2.ItemLV) {
                    case "1":
                        intWeight = 100;
                        break;
                    case "2":
                        intWeight = 800;
                        break;
                    case "3":
                        intWeight = 900;
                        break;
                    case "4":
                    case "5":
                        intWeight = 1000;
                        break;
                    default:
                        intWeight = 0;
                        break;
                }

                App.myCargoProperty.CurrentLT += intWeight * barter.TotalItem2ExchangeQuantity;
            }
        }
    }
}