using iBarter.Model;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for ShipCargoControl.xaml
    /// </summary>
    public partial class ShipCargoControl : System.Windows.Controls.UserControl {
        public ShipCargoControl() {
            InitializeComponent();
            DataContext = App.myCVM;
            //PropertyGrid_Ship.Items = App.myCVM.CargoProperty;

            App.myCargoProperty = new CargoProperty();
        }

        private object _draggedItem;

        private void ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            // 找到被点击的 ListBoxItem
            var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null) {
                // 开始拖动操作
                _draggedItem = item.DataContext;
                DragDrop.DoDragDrop(item, _draggedItem, System.Windows.DragDropEffects.Move);
            }
        }

        private void ListBox_Drop(object sender, System.Windows.DragEventArgs e) {
            if (_draggedItem != null) {
                // 获取原项目的位置和新放置位置
                var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                if (targetItem != null && targetItem.DataContext != _draggedItem) {
                    // 将项目在集合中移动到新位置
                    var targetIndex = ListBox_ShipCargo.Items.IndexOf(targetItem.DataContext);
                    var draggedIndex = ListBox_ShipCargo.Items.IndexOf(_draggedItem);

                    // 这里假设你的 ItemsSource 是 ObservableCollection 类型
                    (ListBox_ShipCargo.ItemsSource as ObservableCollection<Barter>).Move(draggedIndex, targetIndex);
                }
            }
        }

        public static T FindAncestor<T>(DependencyObject current)
            where T : DependencyObject {
            do {
                if (current is T) {
                    return (T)current;
                }

                current = VisualTreeHelper.GetParent(current);
            } while (current != null);

            return null;
        }

        public void RefreshData() {
            if (App.myfmMain != null) {
                string strPath_Data = AppDomain.CurrentDomain.BaseDirectory +
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
                        strPath_Data = AppDomain.CurrentDomain.BaseDirectory +
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
                    string strPath_Data = AppDomain.CurrentDomain.BaseDirectory +
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
                myCvmCargoDetail.TotalItem1ExchangeQuantity = myCvmCargoDetail.ExchangeQuantity * myCvmCargoDetail.Item1Number;
            }

            App.myCargoProperty.CurrentLT = 0;
            List<Barter> myList = (List<Barter>)App.myCVM.CargoDetails.ToList();
            myList.Sort((b1, b2) => { return int.Parse(b1.Item1.ItemLV).CompareTo(int.Parse(b2.Item1.ItemLV)); });

            foreach (Barter barter in myList) {
                IdentifyChain(App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == barter.IsLandName), int.Parse(barter.Item1.ItemLV));

                // int intWeight = GetWeight(barter.Item2.ItemLV);
                //
                // App.myCargoProperty.CurrentLT += intWeight * barter.TotalItem2ExchangeQuantity;
            }

            UpdateCargoList();
            // foreach (Barter myCvmCargoDetail in App.myCVM.CargoDetails) {
            //     App.myCFun.Log(myCvmCargoDetail.Item1Name+"=>"+myCvmCargoDetail.TotalItem1ExchangeQuantity,Brushes.Blue);
            // }
        }

        public void UpdateCargoList() {
            for (int i = App.myCVM.CargoDetails.Count - 1; i >= 0; i--) {
                Barter myBarter = App.myPVM.BarterCollection.FirstOrDefault(b => b.IsLandName == App.myCVM.CargoDetails[i].IsLandName);
                if (myBarter!= null && (myBarter.ExchangeDone || myBarter.ExchangeQuantity == 0)) {
                    App.myCVM.CargoDetails.Remove(App.myCVM.CargoDetails[i]);
                }
            }

            ListBox_ShipCargo.ItemsSource = null;
            ListBox_ShipCargo.Items.Clear();
            ListBox_ShipCargo.ItemsSource = App.myCVM.CargoDetails;
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
                    Barter barterTemp = App.myCVM.CargoDetails.FirstOrDefault(b => b.Item2Name.Equals(_barter.Item1Name) && b.CalculatedAlready == true);
                    if (barterTemp != null) {
                        _barter.TotalItem1ExchangeQuantity = Math.Max(0, _barter.TotalItem1ExchangeQuantity - barterTemp.TotalItem2ExchangeQuantity);
                    }
                    else {
                        _barter.TotalItem1ExchangeQuantity = _barter.TotalItem1ExchangeQuantity;
                    }
                }
            }
        }

        private void ButtonAdv_Clean_Click(object sender, RoutedEventArgs e) {
            if (App.myCVM != null) {
                App.myCVM.CargoDetails.Clear();
                App.myCargoProperty.CurrentLT = 0;
                UpdateCurrentLV();
                SaveData();
            }
        }

        private void ListBoxItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ClickCount == 2) {
                try {
                    ListBoxItem myItem = sender as ListBoxItem;
                    Barter myBarter = (Barter)myItem.Content;
                    if (myBarter != null) {
                        Clipboard.SetText(myBarter.Item1Name);
                        App.myCFun.Log(myBarter.Item1Name + " copied to the clipboard.", Brushes.DarkGreen);
                    }
                }
                catch (Exception exception) {
                    App.myCFun.Log(exception.Message, Brushes.Red);
                }
            }
        }

        private void ListBoxItem_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            try {
                if (e.ClickCount == 2) {
                    ListBoxItem myItem = sender as ListBoxItem;
                    Barter myBarter = (Barter)myItem.Content;
                    if (myBarter != null) {
                        Clipboard.SetText(myBarter.Item2Name);
                        App.myCFun.Log(myBarter.Item2Name + " copied to the clipboard.", Brushes.DarkGreen);
                    }
                }
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
                ;
            }
        }
    }
}