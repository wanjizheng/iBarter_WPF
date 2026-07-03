using Newtonsoft.Json;
using Syncfusion.Pdf.Grid;
using Syncfusion.UI.Xaml.Grid;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for PlannerControl.xaml
    /// </summary>
    public partial class PlannerControl : UserControl {
        // Highest barter item LV in the game; chain recursion stops one step before reaching this.
        private const int MAX_BARTER_LV = 7;

        public PlannerControl() {
            InitializeComponent();
            this.DataContext = App.myPVM;
            DataGrid_Planner.ItemsSource = App.myPVM.BarterCollection;

            DataGrid_Planner.AutoScroller.AutoScrolling = AutoScrollOrientation.Both;
            GridMultiColumnDropDownList_Item.ItemsSource = App.myPVM.ItemsCollection;
            GridMultiColumnDropDownList_Exchange.ItemsSource = App.myPVM.ItemsCollection;
            GridMultiColumnDropDownList_Islands.ItemsSource = App.myPVM.IslandsCollection;

            // ComboBox_AltLevel.ItemsSource = App.myPVM.AltCollection;
            // ComboBox_AltLevel.DisplayMemberPath = "Level";
            // ComboBox_AltLevel.SelectedValuePath = "Value";
            // ComboBox_AltLevel.SelectedIndex = 1;


            //DataGrid_Planner.SortColumnDescriptions.Add(new SortColumnDescription() { ColumnName = "x:Column_LV", SortDirection = ListSortDirection.Ascending });
            SetupDataGridStyle();
            LoadSavedComboBoxValue();
        }

        private void SetupDataGridStyle() {
            // 创建转换器实例并添加到资源中
            // var colorConverter = new ColorConverter();
            // this.Resources.Add("converter", colorConverter);

            // 创建样式
            Style vccStyle = new Style(typeof(VirtualizingCellsControl));
            Style grhcStyle = new Style(typeof(GridRowHeaderCell));

            // 创建绑定
            Binding backgroundBinding = new Binding {
                Converter = (IValueConverter)this.Resources["converter"]
            };

            // 设置样式属性
            vccStyle.Setters.Add(new Setter(VirtualizingCellsControl.BackgroundProperty, backgroundBinding));
            grhcStyle.Setters.Add(new Setter(GridRowHeaderCell.BackgroundProperty, backgroundBinding));

            // 应用样式到 SfDataGrid (您可能需要调整这部分以确保它正确应用到 VirtualizingCellsControl)
            //DataGrid_Planner.RowStyle = vccStyle;
            this.Resources.Add(typeof(VirtualizingCellsControl), vccStyle);
            this.Resources.Add(typeof(GridRowHeaderCell), grhcStyle);
        }

        public void RefreshDataGrid() {
            if (!Application.Current.Dispatcher.CheckAccess()) {
                Application.Current.Dispatcher.Invoke(new Action(() => RefreshDataGrid()));
            }
            else {
                try {
                    DataGrid_Planner.BeginInit();

                    if (App.myPVM.BarterCollection != null) {
                        App.myPVM.BarterCollection.Clear();
                    }

                    foreach (Barter barter in App.listBarterPlanner) {
                        Barter myBarter = new Barter(barter.IsLand, barter.Item1, barter.Item2, barter.ExchangeQuantity,
                            barter.ExchangeDone, barter.BarterGroup, barter.InvQuantity, barter.InvQuantityChange, barter.UsingALT, barter.CalculatedAlready, barter.TotalItem1ExchangeQuantity);
                        App.myPVM.BarterCollection.Add(myBarter);
                    }

                    DataGrid_Planner.EndInit();
                }
                catch (Exception e) {
                }
            }
        }

        private void UpdateParley() {
            if (Label_SelectedParley != null) {
                int intParley = 0;
                foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.ExchangeDone == false && b.ExchangeQuantity > 0)) {
                    if (!barter.UsingALT) {
                        intParley += barter.Parley * barter.ExchangeQuantity;
                    }
                    else {
                        int intParleyTemp = 0;
                        double doubValuePack = 1;
                        switch (barter.IsLand.Island) {
                            case EnumLists.Island.Halmad:
                                intParleyTemp = 29430;
                                break;
                            case EnumLists.Island.Kashuma:
                                intParleyTemp = 29430;
                                break;
                            case EnumLists.Island.Hakoven:
                                intParleyTemp = 43780;
                                break;
                            case EnumLists.Island.Haran:
                                intParleyTemp = 46544;
                                break;
                            case EnumLists.Island.Unfinished:
                                intParleyTemp = 46544;
                                break;
                            case EnumLists.Island.Lantinia:
                                intParleyTemp = 46544;
                                break;
                            case EnumLists.Island.Pakio:
                                intParleyTemp = 58180;
                                break;
                            case EnumLists.Island.Ancient:
                                intParleyTemp = 58180;
                                break;
                            case EnumLists.Island.Crow:
                                intParleyTemp = 58180;
                                break;
                            case EnumLists.Island.Cholace:
                                intParleyTemp = 58180;
                                break;
                            case EnumLists.Island.Rickun:
                                intParleyTemp = 58180;
                                break;
                            case EnumLists.Island.Cox_Pirate:
                                intParleyTemp = 58180;
                                break;
                            case EnumLists.Island.Wandering:
                                intParleyTemp = 58180;
                                break;
                            case EnumLists.Island.Derko:
                                intParleyTemp = 36420;
                                break;
                            case EnumLists.Island.Marine:
                                intParleyTemp = 58180;
                                break;
                            default:
                                intParleyTemp = 14286;
                                break;
                        }

                        // if (CheckBox_ValuePack.IsChecked == true) {
                        //     doubValuePack = 0.9;
                        // }

                        intParley += (int)(intParleyTemp * barter.ExchangeQuantity * doubValuePack);
                    }
                }

                Label_SelectedParley.Content = intParley;
                if (intParley > 1000000) {
                    Label_SelectedParley.Foreground = Brushes.Red;
                }
                else {
                    Label_SelectedParley.Foreground = Brushes.Black;
                }
            }
        }

        private void ButtonAdv_Refresh_Click(object sender, RoutedEventArgs e) {
            Grouping();
        }

        public void Grouping() {
            int intGroup = 1;
            foreach (Barter barter in App.myPVM.BarterCollection) {
                barter.BarterGroup = 0;
                barter.Grouped = false;
            }

            foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.Item1.ItemLV.Equals("0") && !b.Grouped)) {
                int intLV = 1;
                // do {
                //     barter.BarterGroup = intGroup;
                //     Barter myBarter = App.myPVM.StorageCollection.FirstOrDefault(b => b.Item1.ItemLV.Equals(Convert.ToString(intLV)) && b.Item1Name.Equals(barter.Item2Name))!;
                //     if (myBarter != null) {
                //         myBarter.BarterGroup = intGroup;
                //     }
                //
                //     intLV++;
                // } while (!barter.Item1.ItemLV.Equals("5"));
                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.Item1.ItemLV.Equals("1") && b.BarterGroup == 0 && !b.Grouped)) {
                int intLV = 2;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.Item1.ItemLV.Equals("2") && b.BarterGroup == 0 && !b.Grouped)) {
                int intLV = 3;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.Item1.ItemLV.Equals("3") && b.BarterGroup == 0 && !b.Grouped)) {
                int intLV = 4;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            // LV4 and LV5 seeds: previously only LV0..LV3 were seeds, so LV4/LV5/LV6/LV7
            // barters were only ever reached as chain tails. Adding them as seeds lets a
            // chain start at any LV tier and walk upward.
            foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.Item1.ItemLV.Equals("4") && b.BarterGroup == 0 && !b.Grouped)) {
                int intLV = 5;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.Item1.ItemLV.Equals("5") && b.BarterGroup == 0 && !b.Grouped)) {
                int intLV = 6;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            // LV6 and LV7 seeds. Previously only LV0..LV5 were seeds, so barters
            // starting at LV6 (or LV7) without an upstream LV5 (or LV6) chain
            // would be stranded. Walking the seed loop ensures every catalog tier
            // gets a group assignment.
            foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.Item1.ItemLV.Equals("6") && b.BarterGroup == 0 && !b.Grouped)) {
                int intLV = 7;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.Item1.ItemLV.Equals("7") && b.BarterGroup == 0 && !b.Grouped)) {
                int intLV = 8;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            for (int i = 1; i < intGroup; i++) {
                if (App.myPVM.BarterCollection.Where(b => b.BarterGroup == i).ToList().Count == 1) {
                    App.myPVM.BarterCollection.FirstOrDefault(b => b.BarterGroup == i)!.BarterGroup = 0;
                }
            }

            // 初始化分组/排序 —— 必须包在 BeginInit/EndInit 之间
            DataGrid_Planner.View.BeginInit();
            try {
                DataGrid_Planner.SortColumnDescriptions.Clear();
                DataGrid_Planner.GroupColumnDescriptions.Clear();

                DataGrid_Planner.SortColumnDescriptions.Add(new SortColumnDescription {
                    ColumnName = "Item1LV",
                    SortDirection = ListSortDirection.Ascending
                });

                DataGrid_Planner.GroupColumnDescriptions.Add(new GroupColumnDescription {
                    ColumnName = "BarterGroup",
                    // SortGroupRecords = true  // 若确有此属性且你需要再打开；不同版本可能没有
                });
            }
            finally {
                DataGrid_Planner.View.EndInit();
            }

            // 让分组自动展开（可选；开了它一般就不需要手动 ExpandAllGroup）
            DataGrid_Planner.AutoExpandGroups = true;

            // 若你坚持手动展开，请务必在 EndInit 之后，并做空值保护；或者丢到 Dispatcher
            if (DataGrid_Planner.View?.TopLevelGroup != null &&
                DataGrid_Planner.GroupColumnDescriptions.Count > 0 &&
                DataGrid_Planner.View.Records.Count > 0) {
                DataGrid_Planner.ExpandAllGroup();
            }

            //RefreshDataGrid();
            UpdateParley();
            UpdateMapControl();
        }

        private void FindBarterGroup(Barter _barter, int _lv, int _group) {
            _barter.BarterGroup = _group;

            int item1Lv = int.Parse(_barter.Item1.ItemLV);
            int item2Lv = _barter.Item2.ItemLV == "-1" ? -1 : int.Parse(_barter.Item2.ItemLV);

            if ((item2Lv == -1 || item2Lv <= item1Lv) && _barter.Item2.ItemName != "Crow Coin") {
                _barter.BarterGroup = 0;
                _barter.Grouped = true;
                return;
            }

            // Pick the next barter at the LV=_lv tier. Old code used FirstOrDefault and
            // its arbitrary order split chains at branching points: e.g. when 'Headless
            // Dragon Figurine' could be traded at Halmad (-> Crow Coin, dead-end) or Ajir
            // (-> Faded Gold Dragon Figurine -> Midnight), the first one in collection
            // order won, even if a much longer downstream chain existed from the other.
            // Now we enumerate all candidates, score each by max downstream chain length,
            // and pick the longest. Crow Coin terminals are tie-broken against non-Crow-Coin.
            var candidates = App.myPVM.BarterCollection.Where(b =>
                !b.Grouped &&
                b.Item1.ItemLV == _lv.ToString() &&
                b.Item1Name == _barter.Item2Name &&
                (int.Parse(b.Item2.ItemLV) > int.Parse(b.Item1.ItemLV) ||
                 (b.Item2Name == "Crow Coin" && b.Item1Name == _barter.Item2Name)))
                .ToList();

            Barter myBarter;
            if (candidates.Count == 0) {
                return;
            }
            else if (candidates.Count == 1) {
                myBarter = candidates[0];
            }
            else {
                myBarter = candidates[0];
                int bestScore = ScoreDownstream(candidates[0], _lv + 1);
                int bestTb = candidates[0].Item2Name == "Crow Coin" ? 0 : 1;
                for (int i = 1; i < candidates.Count; i++) {
                    var c = candidates[i];
                    int s = ScoreDownstream(c, _lv + 1);
                    int tb = c.Item2Name == "Crow Coin" ? 0 : 1;
                    if (s > bestScore || (s == bestScore && tb > bestTb)) {
                        myBarter = c;
                        bestScore = s;
                        bestTb = tb;
                    }
                }
            }

            myBarter.BarterGroup = _group;
            myBarter.Grouped = true;

            if (int.TryParse(myBarter.Item1.ItemLV, out int nextLv) && nextLv < MAX_BARTER_LV) {
                FindBarterGroup(myBarter, _lv + 1, _group);
            }
        }

        // Pure-function downstream chain scorer: returns the maximum number of
        // barters reachable from `start` going forward (inclusive). Does NOT mutate
        // any Barter.Grouped state; safe to call repeatedly for alternative-branch
        // evaluation in FindBarterGroup. Each branch respects !b.Grouped to avoid
        // double-counting barters already committed to another chain.
        private int ScoreDownstream(Barter start, int lv) {
            if (lv > MAX_BARTER_LV) return 1;
            int depth = 1;
            var candidates = App.myPVM.BarterCollection.Where(b =>
                !b.Grouped &&
                b.Item1.ItemLV == lv.ToString() &&
                b.Item1Name == start.Item2Name &&
                (int.Parse(b.Item2.ItemLV) > int.Parse(b.Item1.ItemLV) ||
                 (b.Item2Name == "Crow Coin" && b.Item1Name == start.Item2Name)))
                .ToList();

            if (candidates.Count == 0) return depth;

            int maxSub = 0;
            foreach (var c in candidates) {
                int s = ScoreDownstream(c, lv + 1);
                if (s > maxSub) maxSub = s;
            }

            return depth + maxSub;
        }

        private void ButtonAdv_Load_Click(object sender, RoutedEventArgs e) {
            string strPath_Setting = AppDomain.CurrentDomain.BaseDirectory +
                                     "\\Resources\\myPlan_Setting.xml";
            string strPath_Data = AppDomain.CurrentDomain.BaseDirectory +
                                  "\\Resources\\myPlan_Data.json";

            if (File.Exists(strPath_Setting) && File.Exists(strPath_Data)) {
                try {
                    using (var file = File.Open(strPath_Setting, FileMode.Open)) {
                        DataGrid_Planner.Deserialize(file);
                    }
                }
                catch (Exception exception) {
                    App.myCFun.Log(exception.Message, Brushes.Red);
                }


                try {
                    string readJsonData = File.ReadAllText(strPath_Data);
                    List<Barter> dataSource = JsonConvert.DeserializeObject<List<Barter>>(readJsonData);
                    if (dataSource != null && dataSource.Count > 0) {
                        App.listBarterPlanner.Clear();
                        for (int i = 0; i < dataSource.Count; i++) {
                            Barter myBarter = dataSource[i];
                            App.listBarterPlanner.Add(myBarter);
                        }

                        RefreshDataGrid();
                        //Grouping();
                        App.myCFun.Log("Loaded...", Brushes.Blue);
                    }
                }
                catch (Exception exception) {
                    App.myCFun.Log(exception.Message, Brushes.Red);
                }
                //myPlannerControl.DataGrid_Planner.ItemsSource = dataSource;
            }

            UpdateParley();

            App.myfmMain.myShipCargo.RefreshData();
        }

        private void ButtonAdv_Save_Click(object sender, RoutedEventArgs e) {
            App.myCFun.Log("Saved...", Brushes.Blue);
            SaveData();
        }

        public void SaveData() {
            try {
                if (App.myPVM.BarterCollection.Count > 0) {
                    string strPath_Setting = AppDomain.CurrentDomain.BaseDirectory + "\\Resources\\myPlan_Setting.xml";
                    string strPath_Data = AppDomain.CurrentDomain.BaseDirectory + "\\Resources\\myPlan_Data.json";

                    using (FileStream streamSetting =
                           new FileStream(strPath_Setting, FileMode.OpenOrCreate, FileAccess.Write)) {
                        streamSetting.SetLength(0);
                        DataGrid_Planner.Serialize(streamSetting);
                    }

                    using (FileStream streamData = new FileStream(strPath_Data, FileMode.OpenOrCreate, FileAccess.Write)) {
                        streamData.SetLength(0);
                        App.listBarterPlanner.Clear();
                        for (int i = 0; i < App.myPVM.BarterCollection.Count; i++) {
                            Barter myBarter = App.myPVM.BarterCollection[i];
                            if (!App.listBarterPlanner.Contains(myBarter)) {
                                App.listBarterPlanner.Add(myBarter);
                            }
                        }

                        string jsonData = JsonConvert.SerializeObject(App.listBarterPlanner);
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

        private void DataGrid_Planner_CurrentCellEndEdit(object sender, CurrentCellEndEditEventArgs e) {
            if (e.RowColumnIndex.ColumnIndex == 5) {
                Barter barter = (Barter)DataGrid_Planner.CurrentItem;
                Barter? myBarter = App.myPVM.BarterCollection.FirstOrDefault(b => b.IsLandName == barter.IsLandName);
                if (myBarter.ExchangeQuantity > myBarter.IslandRemaining || myBarter.ExchangeQuantity < 0) {
                    myBarter.ExchangeQuantity = myBarter.IslandRemaining;
                }
        
                UpdateInvChange(myBarter.BarterGroup);
                App.myfmMain.myShipCargo.UpdateCurrentLV();
                App.myfmMain.myShipCargo.SaveData();
            }

            SaveData();
            // View.Refresh() removed: bindings auto-refresh the edited cell,
            // and the old call forced a full N-row redraw on every keystroke.
            UpdateParley();
            //Grouping();
            UpdateMapControl();
        }


        private void DataGrid_Planner_CurrentCellValueChanged(object sender, CurrentCellValueChangedEventArgs e) {
            if (e.Column.MappingName == "ExchangeDone") {
                Barter myBarter = (Barter)e.Record;
                if (App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == myBarter.IsLandName) != null) {
                    App.myCVM.CargoDetails.Remove(App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == myBarter.IsLandName));
                    App.myfmMain.myShipCargo.UpdateCurrentLV();
                    App.myfmMain.myShipCargo.SaveData();
                }

                SaveData();
                UpdateParley();
                UpdateMapControl();
            }
            else if (e.Column.MappingName == "UsingALT") {
                SaveData();
                UpdateParley();
            }
        }

        private Barter _rightClickedBarter;

        private void DataGrid_Planner_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            // Walk up the visual tree from the hit point to find the row
            // (VirtualizingCellsControl). Its DataContext is the underlying
            // Barter record. We stash it on a field so the ContextMenu's
            // Click handler — which lives in a separate visual-tree subtree —
            // can read it back.
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null && current != DataGrid_Planner) {
                if (current is VirtualizingCellsControl vcc && vcc.DataContext is Barter b) {
                    _rightClickedBarter = b;
                    ShowRowContextMenu();
                    e.Handled = true;
                    return;
                }
                current = VisualTreeHelper.GetParent(current);
            }

            // Click landed on a header / summary row — no menu.
            _rightClickedBarter = null;
        }

        private void ShowRowContextMenu() {
            // Build a fresh ContextMenu each time. Reusing one across
            // multiple rows causes WPF to close the previous one early
            // when the next open is requested.
            ContextMenu cm = new ContextMenu();
            MenuItem deleteItem = new MenuItem { Header = "Delete this row" };
            deleteItem.Click += MenuItem_DeleteRow_Click;
            cm.Items.Add(deleteItem);

            cm.PlacementTarget = DataGrid_Planner;
            cm.Placement = PlacementMode.MousePoint;
            cm.IsOpen = true;
        }

        private void MenuItem_DeleteRow_Click(object sender, RoutedEventArgs e) {
            // Prefer the row we stashed from PreviewMouseRightButtonDown;
            // fall back to the MenuItem's DataContext in case the menu was
            // opened some other way.
            Barter barter = _rightClickedBarter;
            _rightClickedBarter = null;

            if (barter == null && sender is MenuItem mi && mi.DataContext is Barter b) {
                barter = b;
            }

            if (barter == null) {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"Delete this row ({barter.Item1Name} → {barter.Item2Name})?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) {
                return;
            }

            App.myPVM.BarterCollection.Remove(barter);
            App.listBarterPlanner.Remove(barter);

            // Sync CargoDetails: when the user ticks ExchangeDone the
            // matching CargoDetail is removed; do the same here so we
            // don't leave a stale entry pointing at a deleted barter.
            App.myCVM.CargoDetails.Remove(App.myCVM.CargoDetails.FirstOrDefault(b => b.IsLandName == barter.IsLandName));
            App.myfmMain.myShipCargo.UpdateCurrentLV();
            App.myfmMain.myShipCargo.SaveData();

            SaveData();
            UpdateParley();
            UpdateMapControl();
        }


        private void UpdateInvChange(int _groupNumber) {
            // _groupNumber is kept for backward compatibility with the per-cell
            // CurrentCellEndEdit caller, but no longer scopes the computation.
            // The unified formula below correctly handles in-group and cross-group
            // chains, items consumed by multiple barters, and chain intermediates.

            foreach (string item1Name in App.myPVM.BarterCollection
                .Select(b => b.Item1Name)
                .Distinct()
                .Where(n => !string.IsNullOrEmpty(n))) {

                var consumers = App.myPVM.BarterCollection.Where(b => b.Item1Name == item1Name).ToList();
                if (consumers.Count == 0) {
                    continue;
                }

                var producers = App.myPVM.BarterCollection.Where(b => b.Item2Name == item1Name).ToList();

                // consumers.First().InvQuantity reads the live 4-city sum via the
                // Barter.InvQuantity getter (which goes through StorageCollection).
                int currentTotal = consumers.First().InvQuantity;
                int totalProduced = producers.Sum(p => p.ExchangeQuantity * p.Item2Number);
                int totalConsumed = consumers.Sum(c => c.ExchangeQuantity * c.Item1Number);
                int newValue = Math.Max(0, currentTotal + totalProduced - totalConsumed);

                // LV cap: only apply when the user explicitly selected a max value
                // (SelectedIndex >= 0). The ComboBoxes default to -1 with no XAML
                // override, so the previous unconditional cap was clamping every
                // positive LV5/6/7 InvQuantityChange to -1, which then became 0 via
                // Math.Max(0, -1) in the Done handler and zeroed the entire plan.
                int lvMaxIndex = -1;
                var firstConsumer = consumers.First();
                if (firstConsumer.Item1 != null) {
                    if (firstConsumer.Item1.ItemLV == "5") {
                        lvMaxIndex = App.myfmMain.myPlannerControl.ComboBox_LV5Max.SelectedIndex;
                    }
                    else if (firstConsumer.Item1.ItemLV == "6") {
                        lvMaxIndex = App.myfmMain.myPlannerControl.ComboBox_LV6Max.SelectedIndex;
                    }
                    else if (firstConsumer.Item1.ItemLV == "7") {
                        lvMaxIndex = App.myfmMain.myPlannerControl.ComboBox_LV7Max.SelectedIndex;
                    }
                }
                if (lvMaxIndex >= 0 && newValue > lvMaxIndex) {
                    newValue = lvMaxIndex;
                }

                // All barters that consume this item share the same post-plan quantity.
                foreach (var consumer in consumers) {
                    consumer.InvQuantityChange = newValue;
                }
            }
        }



        private void UpdateMapControl() {
            for (int i = App.myfmMain.myMapControl.Grid_MapMain.Children.Count - 1; i > 0; i--) {
                var child = App.myfmMain.myMapControl.Grid_MapMain.Children[i];
                if (child is Grid && ((Grid)child).Name.StartsWith("GridContainer_") && !((Grid)child).Name.EndsWith("Temp")) {
                    App.myfmMain.myMapControl.Grid_MapMain.Children.Remove(child);
                }
            }

            App.myfmMain.myMapControl.IslandsButtonInitialisation();
        }

        private Brush GetBursh(Barter _barter) {
            switch (_barter.BarterGroup) {
                case -1:
                    return Brushes.Bisque;
                    break;
                case 0:
                    return Brushes.Aquamarine;
                    break;
                case 1:
                    return Brushes.CornflowerBlue;
                    break;
                case 2:
                    return Brushes.DarkCyan;
                    break;
                case 3:
                    return Brushes.CadetBlue;
                    break;
                case 4:
                    return Brushes.Chocolate;
                    break;
                case 5:
                    return Brushes.BurlyWood;
                    break;
                case 6:
                    return Brushes.DarkTurquoise;
                    break;
                case 7:
                    return Brushes.Ivory;
                    break;
                case 8:
                    return Brushes.DeepSkyBlue;
                    break;
                case 9:
                    return Brushes.DarkSalmon;
                    break;
                case 10:
                    return Brushes.LawnGreen;
                    break;
                case 11:
                    return Brushes.Orchid;
                    break;
                case 12:
                    return Brushes.Olive;
                    break;
                case 13:
                    return Brushes.Orange;
                    break;
                case 14:
                    return Brushes.Plum;
                    break;
                case 15:
                    return Brushes.DarkViolet;
                    break;
                case 16:
                    return Brushes.Yellow;
                    break;
                case 17:
                    return Brushes.Tomato;
                    break;
                case 18:
                    return Brushes.MediumSlateBlue;
                    break;
                case 19:
                    return Brushes.SpringGreen;
                    break;
                case 20:
                    return Brushes.SandyBrown;
                    break;
                default:
                    return Brushes.RoyalBlue;
                    break;
            }
        }

        private void ButtonAdv_Done_Click(object sender, RoutedEventArgs e) {
            MessageBoxResult result = MessageBox.Show("Are you sure you have completed this plan?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) {
                return;
            }

            if (App.myStorageVM == null || App.myStorageVM.StorageCollection == null || App.myPVM == null) {
                return;
            }

            // X4: lazy-init the storage window so a Done click before the user has ever
            // opened StorageManagement doesn't NRE on ComboBoxAdv_DefaultStorage.
            if (App.myStorageManagement == null) {
                App.myStorageManagement = new StorageManagement();
            }

            // X3: refresh InvQuantityChange for every barter before reading it, so the
            // values written below reflect the user's current ExchangeQuantity edits
            // rather than whatever was left by the last CurrentCellEndEdit.
            //
            // The unified formula in UpdateInvChange correctly handles in-group and
            // cross-group chains, multi-consumer items, and the LV Max ComboBox bug
            // (default SelectedIndex = -1 used to clamp every LV5/6/7 item to -1).
            UpdateInvChange(-1);

            // For each storage item, find barters that consume it (Item1 == name) and
            // write the post-plan quantity from matching[0].InvQuantityChange, which
            // UpdateInvChange has already computed via the unified formula. All
            // consumers of the same item share the same value.
            foreach (Items item in App.myStorageVM.StorageCollection) {
                var matching = App.myPVM.BarterCollection.Where(b => b.Item1Name == item.ItemName).ToList();
                if (matching.Count == 0) {
                    continue;
                }

                int newTarget = Math.Max(0, matching[0].InvQuantityChange);

                // Mirror the LV cap guard from UpdateInvChange so this path is consistent
                // when matching.Count > 1 (the cap was already applied to InvQuantityChange
                // in UpdateInvChange; this is defense-in-depth in case the formula ever
                // changes to bypass UpdateInvChange).
                int lvMaxIndex = -1;
                if (item.ItemLV == "5") {
                    lvMaxIndex = App.myfmMain.myPlannerControl.ComboBox_LV5Max.SelectedIndex;
                }
                else if (item.ItemLV == "6") {
                    lvMaxIndex = App.myfmMain.myPlannerControl.ComboBox_LV6Max.SelectedIndex;
                }
                else if (item.ItemLV == "7") {
                    lvMaxIndex = App.myfmMain.myPlannerControl.ComboBox_LV7Max.SelectedIndex;
                }
                if (lvMaxIndex >= 0 && newTarget > lvMaxIndex) {
                    newTarget = lvMaxIndex;
                }

                // Decide which city to update for this item. Pick the city where the item
                // currently has the largest non-zero stock (Velia → Iliya → Epheria → Ancado on ties),
                // so we write to the city where the item already 'lives' rather than
                // scattering it across cities. If the item has no stock anywhere (a brand
                // new item the user hasn't seeded yet), fall back to ComboBoxAdv_DefaultStorage
                // as the seed city. A cleared combo (-1) falls back to Velia with a warning.
                int targetCity = -1;
                int maxQty = 0;
                if (item.StorageVeliaQuantity_Velia > maxQty) {
                    maxQty = item.StorageVeliaQuantity_Velia;
                    targetCity = 0;
                }
                if (item.StorageVeliaQuantity_Iliya > maxQty) {
                    maxQty = item.StorageVeliaQuantity_Iliya;
                    targetCity = 1;
                }
                if (item.StorageVeliaQuantity_Epheria > maxQty) {
                    maxQty = item.StorageVeliaQuantity_Epheria;
                    targetCity = 2;
                }
                if (item.StorageVeliaQuantity_Ancado > maxQty) {
                    maxQty = item.StorageVeliaQuantity_Ancado;
                    targetCity = 3;
                }

                if (targetCity == -1) {
                    int fallback = App.myStorageManagement.ComboBoxAdv_DefaultStorage.SelectedIndex;
                    if (fallback < 0 || fallback > 3) {
                        App.myCFun.Log("ComboBoxAdv_DefaultStorage.SelectedIndex out of range; defaulting to Velia.", System.Windows.Media.Brushes.Orange);
                        fallback = 0;
                    }
                    targetCity = fallback;
                }

                switch (targetCity) {
                    case 0:
                        item.StorageVeliaQuantity_Velia = newTarget;
                        break;
                    case 1:
                        item.StorageVeliaQuantity_Iliya = newTarget;
                        break;
                    case 2:
                        item.StorageVeliaQuantity_Epheria = newTarget;
                        break;
                    case 3:
                        item.StorageVeliaQuantity_Ancado = newTarget;
                        break;
                }
            }

            App.listBarterPlanner.Clear();
            DataGrid_Planner.BeginInit();
            App.myPVM.BarterCollection.Clear();
            DataGrid_Planner.EndInit();
            App.myStorageVM.SaveData();
        }



        private void ButtonAdv_New_Click(object sender, RoutedEventArgs e) {
            DataGrid_Planner.BeginInit();
            if (App.myPVM != null) {
                App.myPVM.BarterCollection.Clear();
            }

            DataGrid_Planner.SortColumnDescriptions.Clear();
            DataGrid_Planner.EndInit();
        }

        private void ButtonAdv_Clean_Click(object sender, RoutedEventArgs e) {
            MessageBoxResult result = MessageBox.Show("Are you sure you want to clean this plan?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) {
                foreach (Barter barter in App.myPVM.BarterCollection) {
                    barter.ExchangeDone = false;
                    barter.ExchangeQuantity = 0;
                    barter.InvQuantityChange = barter.InvQuantity;
                    barter.BarterGroup = 0;
                }

                DataGrid_Planner.SortColumnDescriptions.Clear();
                // SortColumnDescription mySCD = new SortColumnDescription();
                // mySCD.ColumnName = "Item1LV";
                // mySCD.SortDirection = ListSortDirection.Ascending;
                // DataGrid_Planner.SortColumnDescriptions.Add(mySCD);

                UpdateParley();
                SaveData();
                // View.Refresh() removed: full N-row redraw on every
                // action - needless churn for an action that just toggled booleans.
                UpdateMapControl();
            }
        }

        private void CheckBox_ValuePack_Checked(object sender, RoutedEventArgs e) {
            UpdateParley();
        }

        private void CheckBox_ValuePack_Unchecked(object sender, RoutedEventArgs e) {
            UpdateParley();
        }

        private double zoomFactor = 1.0;
        private const double ZoomStep = 0.1;
        private const double MinZoom = 0.5;
        private const double MaxZoom = 2.0;

        private void DataGrid_Planner_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) {
                // 计算缩放比例
                zoomFactor += (e.Delta > 0) ? ZoomStep : -ZoomStep;
                zoomFactor = Math.Max(MinZoom, Math.Min(MaxZoom, zoomFactor));

                // 应用缩放到SfDataGrid
                DataGrid_Planner.LayoutTransform = new ScaleTransform(zoomFactor, zoomFactor);

                e.Handled = true; // 标记事件已处理，避免默认滚动行为
            }
        }

        private void ComboBox_LV5Max_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
            if (ComboBox_LV5Max.SelectedItem != null) {
                // 保存选中的值
                Properties.Settings.Default.SelectedComboBoxValueLV5 = ComboBox_LV5Max.SelectedIndex;
                Properties.Settings.Default.Save();
            }
        }

        private void ComboBox_LV6Max_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
            if (ComboBox_LV6Max.SelectedItem != null) {
                Properties.Settings.Default.SelectedComboBoxValueLV6 = ComboBox_LV6Max.SelectedIndex;
                Properties.Settings.Default.Save();
            }
        }

        private void ComboBox_LV7Max_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
            if (ComboBox_LV7Max.SelectedItem != null) {
                Properties.Settings.Default.SelectedComboBoxValueLV7 = ComboBox_LV7Max.SelectedIndex;
                Properties.Settings.Default.Save();
            }
        }


        private void LoadSavedComboBoxValue() {
            int savedValue = Properties.Settings.Default.SelectedComboBoxValueLV5;
            if (savedValue >= 0 && savedValue < ComboBox_LV5Max.Items.Count)
                ComboBox_LV5Max.SelectedIndex = savedValue;
            savedValue = Properties.Settings.Default.SelectedComboBoxValueLV6;
            if (savedValue >= 0 && savedValue < ComboBox_LV6Max.Items.Count)
                ComboBox_LV6Max.SelectedIndex = savedValue;
            savedValue = Properties.Settings.Default.SelectedComboBoxValueLV7;
            if (savedValue >= 0 && savedValue < ComboBox_LV7Max.Items.Count)
                ComboBox_LV7Max.SelectedIndex = savedValue;
        }
    }
}