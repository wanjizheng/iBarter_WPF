using iBarter.Model;
using Newtonsoft.Json;
using System.Collections.Generic;
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
                string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) +
                                      "\\Resources\\myShipCargoItems_Data.json";

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
                        strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) +
                                       "\\Resources\\myShipProperty_Data.json";
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
                    string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) +
                                          "\\Resources\\myShipCargoItems_Data.json";

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
            foreach (Barter myCvmCargoDetail in App.myCVM.CargoDetails) {
                myCvmCargoDetail.CalculatedAlready = false;
            }

            App.myCargoProperty.CurrentLT = 0;
            List<Barter> myList = (List<Barter>)App.myCVM.CargoDetails.ToList();
            myList.Sort((b1, b2) => { return int.Parse(b1.Item1.ItemLV).CompareTo(int.Parse(b2.Item1.ItemLV)); });

            foreach (Barter barter in myList) {
                IdentifyChain(barter, int.Parse(barter.Item1.ItemLV));

                // int intWeight = GetWeight(barter.Item2.ItemLV);
                //
                // App.myCargoProperty.CurrentLT += intWeight * barter.TotalItem2ExchangeQuantity;
            }
        }

        private int GetWeight(string _lv) {
            int intWeight = 0;
            switch (_lv) {
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

            return intWeight;
        }

        private void IdentifyChain(Barter _barter, int _lv) {
            Barter myBarter = App.myCVM.CargoDetails.FirstOrDefault(b =>
                b.Item1.ItemLV.Equals(Convert.ToString(_lv + 1)) && b.Item1Name.Equals(_barter.Item2Name) && b.CalculatedAlready == false)!;
            if (myBarter != null) {
                App.myCargoProperty.CurrentLT += (_barter.TotalItem2ExchangeQuantity - myBarter.TotalItem1ExchangeQuantity) * GetWeight(_barter.Item2.ItemLV);
                _barter.CalculatedAlready = true;
                if (!myBarter.Item1.ItemLV.Equals("5")) {
                    IdentifyChain(myBarter, ++_lv);
                }
            }
            else {
                if (!_barter.CalculatedAlready) {
                    App.myCargoProperty.CurrentLT += GetWeight(_barter.Item2.ItemLV) * _barter.TotalItem2ExchangeQuantity;
                    _barter.CalculatedAlready = true;
                }
            }
        }
    }
}