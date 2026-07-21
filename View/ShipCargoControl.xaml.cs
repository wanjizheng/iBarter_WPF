using System.Collections;
using iBarter.Model;
using iBarter.Routing;
using iBarter.ViewModel;
using Newtonsoft.Json;
using System.ComponentModel;
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
        private bool IsDesignMode => DesignerProperties.GetIsInDesignMode(this);

        public ShipCargoControl() {
            InitializeComponent();
            if (IsDesignMode) return;
            DataContext = App.myCVM;
            //PropertyGrid_Ship.Items = App.myCVM.CargoProperty;

            if (App.myCargoProperty == null)
                App.myCargoProperty = new CargoProperty();
            Loaded += ShipCargoControl_Loaded;
            App.myMainWVM.ThemeChanged += ApplyThemeAwareSurfaces;
            Localization.LanguageService.Instance.LanguageChanged += (_, _) => {
                App.myRouteCoordinator?.RefreshLocalization();
                if (App.myRouteCoordinator?.Mode != CargoMode.AutomaticRoute)
                    UpdateCurrentLV();
                RefreshLocalizedDisplay();
            };
        }

        private bool updatingRouteSelector;

        // PropertyGrid caches its view surface internally, so the dynamic
        // resource in XAML alone is not enough after a live skin change.
        // Reapply the two visual properties after the app-level theme has
        // replaced its resource dictionaries.
        private void ApplyThemeAwareSurfaces() {
            if (!IsLoaded || PropertyGrid_Ship is null) return;

            if (Application.Current?.Resources["AppTextBrush"] is Brush foreground)
                PropertyGrid_Ship.Foreground = foreground;
            if (Application.Current?.Resources["AppSurfaceBrush"] is Brush surface)
                PropertyGrid_Ship.ViewBackgroundColor = surface;

            // Do not call UpdateLayout here.  Syncfusion is in the middle of
            // replacing its template while ThemeChanged is raised; forcing a
            // synchronous layout dereferences an internal presenter that has
            // not been recreated yet.  Setting these properties already
            // invalidates the necessary visuals safely.
        }
        private bool updatingRouteGuideCheckBox;

        private void ShipCargoControl_Loaded(object sender, RoutedEventArgs e) {
            if (App.myRouteCoordinator != null) {
                App.myRouteCoordinator.RouteDisplayChanged -= RouteCoordinator_RouteDisplayChanged;
                App.myRouteCoordinator.RouteDisplayChanged += RouteCoordinator_RouteDisplayChanged;
            }
            ApplyThemeAwareSurfaces();
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
            if (IsDesignMode || App.myCVM is null) return;
            var coordinator = App.myRouteCoordinator;
            bool automatic = coordinator?.Mode == CargoMode.AutomaticRoute;
            ListBox_ShipCargo.ItemsSource = automatic
                ? App.myCVM.AutomaticSteps
                : App.myCVM.ManualSteps;
            ListBox_ShipCargo.AllowDrop = !automatic;
            ButtonAdv_Clean.IsEnabled = !automatic;
            Panel_AutomaticRouteSelector.Visibility = Visibility.Visible;
            ComboBoxAdv_RouteSelector.IsEnabled = coordinator?.RouteOptions.Count > 0;
            CheckBox_ShowRouteGuides.IsEnabled = automatic;

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
            updatingRouteGuideCheckBox = true;
            try {
                CheckBox_ShowRouteGuides.IsChecked = coordinator?.ShowRouteGuides ?? true;
            }
            finally {
                updatingRouteGuideCheckBox = false;
            }
            PropertyGrid_Ship.SelectedObject = App.myCargoProperty;
            CollectionViewSource.GetDefaultView(ListBox_ShipCargo.ItemsSource)?.Refresh();
            Dispatcher.BeginInvoke(FocusSelectedAutomaticStep, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ComboBoxAdv_RouteSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (updatingRouteSelector || ComboBoxAdv_RouteSelector.SelectedItem is not RouteSelectionOption option)
                return;
            if (option.IsAll) App.myRouteCoordinator.SelectAll();
            else if (option.RouteNumber is int routeNumber) App.myRouteCoordinator.SelectRoute(routeNumber);
        }

        private void CheckBox_ShowRouteGuides_Changed(object sender, RoutedEventArgs e) {
            if (updatingRouteGuideCheckBox || App.myRouteCoordinator is null) return;
            App.myRouteCoordinator.SetShowRouteGuides(
                CheckBox_ShowRouteGuides.IsChecked == true);
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

        private Barter? _draggedItem;

        private void ListBox_ShipCargo_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            var scrollViewer = FindDescendant<ScrollViewer>(ListBox_ShipCargo);
            if (scrollViewer is null) return;

            switch (ShipCargoScrollBehavior.GetLogicalItemDelta(e.Delta)) {
                case > 0:
                    scrollViewer.LineDown();
                    break;
                case < 0:
                    scrollViewer.LineUp();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        private void ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (App.myRouteCoordinator?.Mode == CargoMode.AutomaticRoute) return;
            // 找到被点击的 ListBoxItem
            var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null) {
                // 开始拖动操作
                _draggedItem = ResolveContentBarter(item.DataContext);
                if (_draggedItem is not null)
                    DragDrop.DoDragDrop(item, _draggedItem, System.Windows.DragDropEffects.Move);
            }
        }

        private void ListBox_Drop(object sender, System.Windows.DragEventArgs e) {
            if (App.myRouteCoordinator?.Mode == CargoMode.AutomaticRoute) return;
            if (_draggedItem != null) {
                // 获取原项目的位置和新放置位置
                var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                Barter? targetBarter = targetItem is null ? null : ResolveContentBarter(targetItem.DataContext);
                if (targetBarter != null && !ReferenceEquals(targetBarter, _draggedItem)) {
                    // 将项目在集合中移动到新位置
                    var targetIndex = App.myCVM.CargoDetails.IndexOf(targetBarter);
                    var draggedIndex = App.myCVM.CargoDetails.IndexOf(_draggedItem);

                    if (draggedIndex >= 0 && targetIndex >= 0) {
                        App.myCVM.CargoDetails.Move(draggedIndex, targetIndex);
                        UpdateCurrentLV();
                        SaveData();
                    }
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

        private static T? FindDescendant<T>(DependencyObject current) where T : DependencyObject {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++) {
                var child = VisualTreeHelper.GetChild(current, index);
                if (child is T match) return match;
                var descendant = FindDescendant<T>(child);
                if (descendant is not null) return descendant;
            }
            return null;
        }

        public void RefreshData() {
            App.myRouteCoordinator?.ActivateManual();
            if (App.myfmMain != null) {
                LoadCargoPropertyData();
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

                UpdateCurrentLV();
                PropertyGrid_Ship.SelectedObject = App.myCargoProperty;
            }
        }

        private static void LoadCargoPropertyData() {
            string path = AppDomain.CurrentDomain.BaseDirectory + "\\Resources\\myShipProperty_Data.json";
            if (!File.Exists(path)) return;
            try {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return;
                var loaded = JsonConvert.DeserializeObject<CargoProperty>(json);
                if (loaded is null) return;
                App.myCargoProperty.ExtraLT = loaded.ExtraLT;
                App.myCargoProperty.TotalLT = loaded.TotalLT;
                App.myCargoProperty.CurrentLT = loaded.CurrentLT;
                App.myCargoProperty.InitialLT = loaded.InitialLT;
                App.myCargoProperty.PeakLT = loaded.PeakLT;
            }
            catch (Exception exception) {
                App.myCFun?.Log(exception.Message, Brushes.Red);
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

            UpdateCargoList();
            int extraLT = Convert.ToInt32(Math.Round(
                App.myCargoProperty.ExtraLT, MidpointRounding.AwayFromZero));
            var projection = App.myCVM.RefreshManualSteps(extraLT);
            App.myCargoProperty.InitialLT = projection.InitialLT;
            App.myCargoProperty.CurrentLT = projection.CurrentLT;
            App.myCargoProperty.PeakLT = projection.PeakLT;
            RefreshRouteMode();
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

        private void ListBoxItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (sender is ListBoxItem { Content: BarterRouteStepViewModel focusedStep }) {
                if (focusedStep.SourceBarter is null)
                    App.myRouteCoordinator?.FocusBarterStep(focusedStep.RowId);
            }
            if (e.ClickCount == 2) {
                try {
                    ListBoxItem myItem = sender as ListBoxItem;
                    if (myItem?.Content is BarterRouteStepViewModel automaticStep) {
                        Barter? automaticBarter = automaticStep.SourceBarter
                            ?? ResolveAutomaticBarter(automaticStep.RowId);
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
                        Barter? automaticBarter = automaticStep.SourceBarter
                            ?? ResolveAutomaticBarter(automaticStep.RowId);
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

        public void FocusSelectedAutomaticStep() {
            if (IsDesignMode
                || App.myRouteCoordinator?.Mode != CargoMode.AutomaticRoute
                || string.IsNullOrWhiteSpace(App.myRouteCoordinator.SelectedBarterRowId))
                return;
            var selected = App.myCVM.AutomaticSteps
                .OfType<BarterRouteStepViewModel>()
                .FirstOrDefault(step => StringComparer.Ordinal.Equals(
                    step.RowId, App.myRouteCoordinator.SelectedBarterRowId));
            if (selected is null) return;
            ListBox_ShipCargo.SelectedItem = selected;
            ListBox_ShipCargo.ScrollIntoView(selected);
            ListBox_ShipCargo.UpdateLayout();
            if (ListBox_ShipCargo.ItemContainerGenerator.ContainerFromItem(selected)
                is ListBoxItem container)
                container.BringIntoView();
        }

        private static Barter? ResolveAutomaticBarter(string rowId) {
            string plannerRowId = RouteTaskIdentity.PlannerRowId(rowId);
            var current = App.myPVM.BarterCollection.FirstOrDefault(barter =>
                StringComparer.Ordinal.Equals(barter.PlannerRowId, plannerRowId));
            if (current is not null) return current;

            int separator = rowId.IndexOf(':');
            if (separator <= 0 || !int.TryParse(rowId[..separator], out int index)) return null;
            return index >= 0 && index < App.myPVM.BarterCollection.Count
                ? App.myPVM.BarterCollection[index]
                : null;
        }

        private static Barter? ResolveContentBarter(object? content) => content switch {
            Barter barter => barter,
            BarterRouteStepViewModel step => step.SourceBarter,
            _ => null,
        };
    }
}
