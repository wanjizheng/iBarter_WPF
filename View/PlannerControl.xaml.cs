using Newtonsoft.Json;
using Syncfusion.UI.Xaml.Grid;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for PlannerControl.xaml
    /// </summary>
    public partial class PlannerControl : UserControl {
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
            }

            foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.Item1.ItemLV.Equals("0"))) {
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

            foreach (Barter barter in App.myPVM.BarterCollection.Where(b =>
                         b.Item1.ItemLV.Equals("1") && b.BarterGroup == 0)) {
                int intLV = 2;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            foreach (Barter barter in App.myPVM.BarterCollection.Where(b =>
                         b.Item1.ItemLV.Equals("2") && b.BarterGroup == 0)) {
                int intLV = 3;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            foreach (Barter barter in App.myPVM.BarterCollection.Where(b =>
                         b.Item1.ItemLV.Equals("3") && b.BarterGroup == 0)) {
                int intLV = 4;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            for (int i = 1; i < intGroup; i++) {
                if (App.myPVM.BarterCollection.Where(b => b.BarterGroup == i).ToList().Count == 1) {
                    App.myPVM.BarterCollection.FirstOrDefault(b => b.BarterGroup == i)!.BarterGroup = 0;
                }
            }

            try {
                DataGrid_Planner.View.BeginInit();
                DataGrid_Planner.SortColumnDescriptions.Clear();
                DataGrid_Planner.GroupColumnDescriptions.Clear();
                SortColumnDescription mySCD_ItemLV = new SortColumnDescription();
                mySCD_ItemLV.ColumnName = "Item1LV";
                mySCD_ItemLV.SortDirection = ListSortDirection.Ascending;
                DataGrid_Planner.SortColumnDescriptions.Add(mySCD_ItemLV);
                GroupColumnDescription mySCD_Group = new GroupColumnDescription();
                mySCD_Group.ColumnName = "BarterGroup";
                mySCD_Group.SortGroupRecords = true;

                DataGrid_Planner.GroupColumnDescriptions.Add(mySCD_Group);
                // DataGrid_Planner.GroupColumnDescriptions.Add(
                //     new GroupColumnDescription() { ColumnName = "BarterGroup" });
            }
            catch (Exception exception) {
            }
            finally {
                DataGrid_Planner.AutoExpandGroups = true;
                try {
                    if (intGroup > 0) {
                        DataGrid_Planner.ExpandAllGroup();
                    }
                }
                catch (Exception e) {
                }

                DataGrid_Planner.View.EndInit();
            }

            //RefreshDataGrid();
            UpdateParley();
            UpdateMapControl();
        }

        private void FindBarterGroup(Barter _barter, int _lv, int _group) {
            _barter.BarterGroup = _group;
            if ((_barter.Item2.ItemLV == "-1" || int.Parse(_barter.Item2.ItemLV) <= int.Parse(_barter.Item1.ItemLV)) && _barter.Item2.ItemName!="Crow Coin") {
                _barter.BarterGroup = 0;
                return;
            }

            Barter myBarter = App.myPVM.BarterCollection.FirstOrDefault(b =>
                b.Item1.ItemLV.Equals(Convert.ToString(_lv)) && b.Item1Name.Equals(_barter.Item2Name) && (int.Parse(b.Item2.ItemLV) > int.Parse(b.Item1.ItemLV) || b.Item2Name == "Crow Coin"))!;
            if (myBarter != null) {
                myBarter.BarterGroup = _group;
                if (!myBarter.Item1.ItemLV.Equals("5")) {
                    FindBarterGroup(myBarter, ++_lv, _group);
                }
            }
            else {
                //App.myCFun.Log("Error: can't identify the barter group =>" + _barter.IsLandName, Brushes.Red);
            }
        }

        private void ButtonAdv_Load_Click(object sender, RoutedEventArgs e) {
            string strPath_Setting = AppDomain.CurrentDomain.BaseDirectory +
                                     "\\Resources\\myPlan_Setting.xml";
            string strPath_Data = AppDomain.CurrentDomain.BaseDirectory +
                                  "\\Resources\\myPlan_Data.json";

            if (File.Exists(strPath_Setting) && File.Exists(strPath_Data)) {
                try {
                    using (var file = File.Open(strPath_Setting, FileMode.Open)) {
                        //DataGrid_Planner.Deserialize(file);
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
                    string strPath_Setting = AppDomain.CurrentDomain.BaseDirectory +
                                             "\\Resources\\myPlan_Setting.xml";
                    string strPath_Data = AppDomain.CurrentDomain.BaseDirectory +
                                          "\\Resources\\myPlan_Data.json";

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
            //RefreshDataGrid();
            DataGrid_Planner.View.Refresh();
            UpdateParley();
            //Grouping();
            UpdateMapControl();
        }

        private void UpdateInvChange(int _groupNumber) {
            foreach (Barter barter in App.myPVM.BarterCollection.Where(b => b.BarterGroup == _groupNumber).OrderBy(b => b.Item1LV)) {
                if (barter.Item1.ItemLV == "0") {
                    continue;
                }

                Barter? myBarter = App.myPVM.BarterCollection.FirstOrDefault(b => b.BarterGroup == barter.BarterGroup && b.Item2Name.Equals(barter.Item1Name));
                if (myBarter != null) {
                    barter.InvQuantityChange = Math.Max(0, myBarter.ExchangeQuantity * myBarter.Item2Number + barter.InvQuantity - barter.ExchangeQuantity * barter.Item1Number);
                }
                else {
                    barter.InvQuantityChange = Math.Max(0, barter.InvQuantity - barter.ExchangeQuantity * barter.Item1Number);
                }

                if (barter.Item1.ItemLV == "5" && barter.InvQuantityChange > App.myfmMain.myPlannerControl.ComboBox_LV5Max.SelectedIndex + 1) {
                    barter.InvQuantityChange = App.myfmMain.myPlannerControl.ComboBox_LV5Max.SelectedIndex + 1;
                }
            }
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

            if (result == MessageBoxResult.Yes) {
                if (App.myStorageVM.StorageCollection != null) {
                    foreach (Items item in App.myStorageVM.StorageCollection) {
                        if (App.myPVM != null) {
                            Barter myBarter = App.myPVM.BarterCollection.FirstOrDefault(b => b.Item1Name == item.ItemName);
                            if (myBarter != null) {
                                switch (App.myStorageManagement.ComboBoxAdv_DefaultStorage.SelectedIndex) {
                                    case 0:
                                        item.StorageVeliaQuantity_Velia = myBarter.InvQuantityChange;
                                        break;
                                    case 1:
                                        item.StorageVeliaQuantity_Iliya = myBarter.InvQuantityChange;
                                        break;
                                    case 2:
                                        item.StorageVeliaQuantity_Epheria = myBarter.InvQuantityChange;
                                        break;
                                    case 3:
                                        item.StorageVeliaQuantity_Ancado = myBarter.InvQuantityChange;
                                        break;
                                }
                            }
                        }
                    }
                }

                if (App.myPVM != null) {
                    App.listBarterPlanner.Clear();
                    DataGrid_Planner.BeginInit();
                    App.myPVM.BarterCollection.Clear();
                    DataGrid_Planner.EndInit();
                    App.myStorageVM.SaveData();
                }
            }
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
                DataGrid_Planner.View.Refresh();
                UpdateMapControl();
            }
        }

        private void CheckBox_ValuePack_Checked(object sender, RoutedEventArgs e) {
            UpdateParley();
        }

        private void CheckBox_ValuePack_Unchecked(object sender, RoutedEventArgs e) {
            UpdateParley();
        }
    }
}