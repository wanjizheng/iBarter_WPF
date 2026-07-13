using System.Collections;
using iBarter.Model;
using iBarter.Routing;
using iBarter.ViewModel;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for ShipCargoControl.xaml
    /// </summary>
    public partial class ShipCargoControl : System.Windows.Controls.UserControl {
        // Highest barter item LV in the game; IdentifyChain recursion stops one step before reaching this.
        private const int MAX_BARTER_LV = 7;

        public ShipCargoControl() {
            InitializeComponent();
            DataContext = App.myCVM;
            //PropertyGrid_Ship.Items = App.myCVM.CargoProperty;

            if (App.myCargoProperty == null)
                App.myCargoProperty = new CargoProperty();
            Loaded += ShipCargoControl_Loaded;
            Localization.LanguageService.Instance.LanguageChanged += (_, _) => RefreshLocalizedDisplay();
        }

        private bool updatingRouteSelector;

        private void ShipCargoControl_Loaded(object sender, RoutedEventArgs e) {
            if (App.myRouteCoordinator != null) {
                App.myRouteCoordinator.RouteDisplayChanged -= RouteCoordinator_RouteDisplayChanged;
                App.myRouteCoordinator.RouteDisplayChanged += RouteCoordinator_RouteDisplayChanged;
            }
            RefreshRouteMode();
        }

        private void RouteCoordinator_RouteDisplayChanged(object? sender, EventArgs e) {
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.Invoke(RefreshRouteMode);
                return;
            }
            RefreshRouteMode();
        }

        private void RefreshRouteMode() {
            var coordinator = App.myRouteCoordinator;
            bool automatic = coordinator?.Mode == CargoMode.AutomaticRoute;
            ListBox_ShipCargo.ItemsSource = automatic
                ? App.myCVM.AutomaticSteps
                : App.myCVM.CargoDetails;
            ListBox_ShipCargo.AllowDrop = !automatic;
            ButtonAdv_OptimalRoute.IsEnabled = !automatic;
            ButtonAdv_Clean.IsEnabled = !automatic;
            Panel_AutomaticRouteSelector.Visibility = automatic && coordinator!.RouteOptions.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            updatingRouteSelector = true;
            try {
                ComboBoxAdv_RouteSelector.SelectedItem = automatic
                    ? coordinator!.RouteOptions.FirstOrDefault(x => coordinator.ShowAllRoutes
                        ? x.IsAll
                        : x.RouteNumber == coordinator.SelectedRouteNumber)
                    : null;
            }
            finally {
                updatingRouteSelector = false;
            }
            PropertyGrid_Ship.SelectedObject = App.myCargoProperty;
            CollectionViewSource.GetDefaultView(ListBox_ShipCargo.ItemsSource)?.Refresh();
        }

        private void ComboBoxAdv_RouteSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (updatingRouteSelector || ComboBoxAdv_RouteSelector.SelectedItem is not RouteSelectionOption option)
                return;
            if (option.IsAll) App.myRouteCoordinator.SelectAll();
            else if (option.RouteNumber is int routeNumber) App.myRouteCoordinator.SelectRoute(routeNumber);
        }

        private void RefreshLocalizedDisplay() {
            if (ListBox_ShipCargo == null) {
                return;
            }
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.Invoke(RefreshLocalizedDisplay);
                return;
            }

            CollectionViewSource.GetDefaultView(ListBox_ShipCargo.ItemsSource)?.Refresh();
            ListBox_ShipCargo.InvalidateVisual();
        }

        private object _draggedItem;

        private void ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (App.myRouteCoordinator?.Mode == CargoMode.AutomaticRoute) return;
            // 找到被点击的 ListBoxItem
            var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null) {
                // 开始拖动操作
                _draggedItem = item.DataContext;
                DragDrop.DoDragDrop(item, _draggedItem, System.Windows.DragDropEffects.Move);
            }
        }

        private void ListBox_Drop(object sender, System.Windows.DragEventArgs e) {
            if (App.myRouteCoordinator?.Mode == CargoMode.AutomaticRoute) return;
            if (_draggedItem != null) {
                // 获取原项目的位置和新放置位置
                var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                if (targetItem != null && targetItem.DataContext != _draggedItem) {
                    // 将项目在集合中移动到新位置
                    var targetIndex = ListBox_ShipCargo.Items.IndexOf(targetItem.DataContext);
                    var draggedIndex = ListBox_ShipCargo.Items.IndexOf(_draggedItem);

                    // 这里假设你的 ItemsSource 是 ObservableCollection 类型
                    (ListBox_ShipCargo.ItemsSource as ObservableCollection<Barter>)?.Move(draggedIndex, targetIndex);
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
            App.myRouteCoordinator?.ActivateManual();
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
                        strPath_Data = AppDomain.CurrentDomain.BaseDirectory + "\\Resources\\myShipProperty_Data.json";
                        if (File.Exists(strPath_Data)) {
                            readJsonData = File.ReadAllText(strPath_Data);
                            if (readJsonData.Length > 0) {
                                var loaded = JsonConvert.DeserializeObject<CargoProperty>(readJsonData);
                                if (loaded != null) {
                                    App.myCargoProperty.ExtraLT = loaded.ExtraLT;
                                    App.myCargoProperty.TotalLT = loaded.TotalLT;
                                    App.myCargoProperty.CurrentLT = loaded.CurrentLT;
                                    App.myCargoProperty.InitialLT = loaded.InitialLT;
                                    App.myCargoProperty.PeakLT = loaded.PeakLT;
                                }
                            }
                            else {
                                App.myCargoProperty = new CargoProperty();
                            }
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
                    if (App.myCargoProperty == null)
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

                    using (FileStream streamData = new FileStream(strPath_Data, FileMode.OpenOrCreate, FileAccess.Write)) {
                        streamData.SetLength(0);
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
            if (App.myRouteCoordinator?.Mode == CargoMode.AutomaticRoute) return;
            foreach (Barter myCvmCargoDetail in App.myCVM.CargoDetails) {
                myCvmCargoDetail.CalculatedAlready = false;
                myCvmCargoDetail.TotalItem1ExchangeQuantity = myCvmCargoDetail.ExchangeQuantity * myCvmCargoDetail.Item1Number;
            }

            App.myCargoProperty.CurrentLT = 0;
            App.myCargoProperty.InitialLT = 0;
            List<Barter> myList = (List<Barter>)App.myCVM.CargoDetails.ToList();
            myList.Sort((b1, b2) => { return int.Parse(b1.Item1.ItemLV).CompareTo(int.Parse(b2.Item1.ItemLV)); });

            Hashtable htItem1 = new Hashtable();
            Hashtable htItem2 = new Hashtable();

            foreach (Barter barter in myList) {
                //IdentifyChain(App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == barter.IsLandName), int.Parse(barter.Item1.ItemLV));

                if (htItem1.ContainsKey(barter.Item1Name)) {
                    htItem1[barter.Item1Name] = (int)htItem1[barter.Item1Name] + barter.TotalItem1ExchangeQuantity;
                }
                else {
                    htItem1.Add(barter.Item1Name, barter.TotalItem1ExchangeQuantity);
                }

                if (htItem2.ContainsKey(barter.Item2Name)) {
                    htItem2[barter.Item2Name] = (int)htItem2[barter.Item2Name] + barter.TotalItem2ExchangeQuantity;
                }
                else {
                    htItem2.Add(barter.Item2Name, barter.TotalItem2ExchangeQuantity);
                }

                // int intWeight = GetWeight(barter.Item2.ItemLV);
                //
                // App.myCargoProperty.CurrentLT += intWeight * barter.TotalItem2ExchangeQuantity;
            }

            foreach (Barter barter in myList) {
                if (htItem2.ContainsKey(barter.Item1Name) && (int)htItem2[barter.Item1Name] >= barter.TotalItem1ExchangeQuantity) {
                    int availableQty = (int)htItem2[barter.Item1Name]; 
                    htItem2[barter.Item1Name] = Math.Max(0, availableQty - barter.TotalItem1ExchangeQuantity);

                    barter.CalculatedAlready = true;
                    barter.TotalItem1ExchangeQuantity = 0;
                }
            }


            foreach (Barter barter in myList) {
                App.myCargoProperty.InitialLT += GetWeightFromLevel(barter.Item1.ItemLV) * barter.TotalItem1ExchangeQuantity;
                App.myCargoProperty.CurrentLT += GetWeightFromLevel(barter.Item2.ItemLV) * (int)htItem2[barter.Item2Name];
            }

            App.myCargoProperty.CurrentLT += App.myCargoProperty.ExtraLT;
            App.myCargoProperty.InitialLT += App.myCargoProperty.ExtraLT;
            App.myCargoProperty.PeakLT = Math.Max(App.myCargoProperty.InitialLT, App.myCargoProperty.CurrentLT);
            UpdateCargoList();
            // foreach (Barter myCvmCargoDetail in App.myCVM.CargoDetails) {
            //     App.myCFun.Log(myCvmCargoDetail.Item1Name+"=>"+myCvmCargoDetail.TotalItem1ExchangeQuantity,Brushes.Blue);
            // }
        }

        public void UpdateCargoList() {
            if (App.myRouteCoordinator?.Mode == CargoMode.AutomaticRoute) return;
            for (int i = App.myCVM.CargoDetails.Count - 1; i >= 0; i--) {
                Barter myBarter = App.myPVM.BarterCollection.FirstOrDefault(b => b.IsLandName == App.myCVM.CargoDetails[i].IsLandName);
                if (myBarter != null && (myBarter.ExchangeDone || myBarter.ExchangeQuantity == 0)) {
                    App.myCVM.CargoDetails.Remove(App.myCVM.CargoDetails[i]);
                }
            }

            ListBox_ShipCargo.ItemsSource = null;
            ListBox_ShipCargo.Items.Clear();
            ListBox_ShipCargo.ItemsSource = App.myCVM.CargoDetails;
        }

        private static int GetWeightFromLevel(string level) =>
            int.TryParse(level, out int parsed) ? CargoWeightTable.GetWeightForLevel(parsed) : 0;

        private void IdentifyChain(Barter _barter, int _lv) {
            Barter myBarter = App.myCVM.CargoDetails.FirstOrDefault(b => b.Item1.ItemLV.Equals(Convert.ToString(_lv + 1)) && b.Item1Name.Equals(_barter.Item2Name))!;
            if (myBarter != null) {
                App.myCargoProperty.CurrentLT += (_barter.TotalItem2ExchangeQuantity - myBarter.TotalItem1ExchangeQuantity) * GetWeightFromLevel(_barter.Item2.ItemLV);
                // App.myCargoProperty.InitialLT += _barter.TotalItem1ExchangeQuantity * GetWeight(_barter.Item1.ItemLV);
                // Barter myBarter2 = App.myCVM.CargoDetails.FirstOrDefault(b => b.Item2Name.Equals(_barter.Item1Name) && b.CalculatedAlready == false)!;
                // if (myBarter2 == null)
                _barter.CalculatedAlready = true;
                // Stop when next-bar LV reaches MAX_BARTER_LV (so LV6/LV7 chains walk; LV7 is leaf).
                if (int.TryParse(myBarter.Item1.ItemLV, out int nextLv) && nextLv < MAX_BARTER_LV) {
                    IdentifyChain(myBarter, ++_lv);
                }
            }
            else {
                if (!_barter.CalculatedAlready) {
                    App.myCargoProperty.CurrentLT += GetWeightFromLevel(_barter.Item2.ItemLV) * _barter.TotalItem2ExchangeQuantity;
                    // App.myCargoProperty.InitialLT += GetWeight(_barter.Item1.ItemLV) * _barter.TotalItem1ExchangeQuantity;
                    _barter.CalculatedAlready = true;
                    Barter barterTemp = App.myCVM.CargoDetails.FirstOrDefault(b => b.Item2Name.Equals(_barter.Item1Name));
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
            if (App.myRouteCoordinator?.Mode == CargoMode.AutomaticRoute) return;
            if (App.myCVM != null) {
                App.myCVM.CargoDetails.Clear();
                App.myCargoProperty.CurrentLT = 0;
                App.myCargoProperty.InitialLT = 0;
                UpdateCurrentLV();
                SaveData();
            }
        }

        // Reorder the ship cargo as an optimal sailing route starting at
        // Cox_Pirate (遇难的酷斯海贼船), respecting the chain precedence
        // enforced by SortByBarterChain. The reorder + persistence is
        // handled by ShipCargoViewModel.SolveOptimalRoute (Held-Karp DP
        // with precedence for N <= 18, greedy nearest-neighbor for larger
        // cargoes); we refresh the ListBox here and call SaveData so the
        // new order is written to disk.
        private void ButtonAdv_OptimalRoute_Click(object sender, RoutedEventArgs e) {
            if (App.myRouteCoordinator?.Mode == CargoMode.AutomaticRoute) return;
            if (App.myCVM == null) {
                return;
            }
            App.myCVM.SolveOptimalRoute();
            CollectionViewSource.GetDefaultView(ListBox_ShipCargo.ItemsSource)?.Refresh();
            ListBox_ShipCargo.InvalidateVisual();
            SaveData();
        }

        private void ListBoxItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ClickCount == 2) {
                try {
                    ListBoxItem myItem = sender as ListBoxItem;
                    if (myItem?.Content is BarterRouteStepViewModel automaticStep) {
                        Barter? automaticBarter = ResolveAutomaticBarter(automaticStep.RowId);
                        if (automaticBarter != null) {
                            Clipboard.SetText(automaticBarter.Item1Name);
                            App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.ShipCargo.CopiedToClipboard", automaticBarter.Item1NameDisplay), Brushes.DarkGreen);
                        }
                        return;
                    }
                    Barter myBarter = (Barter)myItem.Content;
                    if (myBarter != null) {
                        Clipboard.SetText(myBarter.Item1Name);
                        App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.ShipCargo.CopiedToClipboard", myBarter.Item1NameDisplay), Brushes.DarkGreen);
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
                    if (myItem?.Content is BarterRouteStepViewModel automaticStep) {
                        Barter? automaticBarter = ResolveAutomaticBarter(automaticStep.RowId);
                        if (automaticBarter != null) {
                            Clipboard.SetText(automaticBarter.Item2Name);
                            App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.ShipCargo.CopiedToClipboard", automaticBarter.Item2NameDisplay), Brushes.DarkGreen);
                        }
                        return;
                    }
                    Barter myBarter = (Barter)myItem.Content;
                    if (myBarter != null) {
                        Clipboard.SetText(myBarter.Item2Name);
                        App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.ShipCargo.CopiedToClipboard", myBarter.Item2NameDisplay), Brushes.DarkGreen);
                    }
                }
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
            }
        }

        private static Barter? ResolveAutomaticBarter(string rowId) {
            int separator = rowId.IndexOf(':');
            if (separator <= 0 || !int.TryParse(rowId[..separator], out int index)) return null;
            return index >= 0 && index < App.myPVM.BarterCollection.Count
                ? App.myPVM.BarterCollection[index]
                : null;
        }
    }
}
