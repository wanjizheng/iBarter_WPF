using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Emgu.CV.CvEnum;
using Newtonsoft.Json;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.Windows.Controls.PivotGrid;
using Syncfusion.Windows.Shared;
using Brush = System.Windows.Media.Brush;

namespace iBarter {
    /// <summary>
    /// Interaction logic for PlannerControl.xaml
    /// </summary>
    public partial class PlannerControl : UserControl {
        public PlannerControl() {
            InitializeComponent();
            this.DataContext = App.myPVM;
            DataGrid_Planner.ItemsSource = App.myPVM.BarterDetails;
            DataGrid_Planner.AutoScroller.AutoScrolling = AutoScrollOrientation.Both;
            GridMultiColumnDropDownList_Item.ItemsSource = App.myPVM.ItemsCollection;
            GridMultiColumnDropDownList_Exchange.ItemsSource = App.myPVM.ItemsCollection;
            GridMultiColumnDropDownList_Islands.ItemsSource = App.myPVM.IslandsCollection;

            //DataGrid_Planner.SortColumnDescriptions.Add(new SortColumnDescription() { ColumnName = "x:Column_LV", SortDirection = ListSortDirection.Ascending });
            //SetupDataGridStyle();
        }

        private void SetupDataGridStyle() {
            // 创建转换器实例并添加到资源中
            var colorConverter = new ColorConverter();
            this.Resources.Add("converter", colorConverter);

            // 创建样式
            Style vccStyle = new Style(typeof(VirtualizingCellsControl));

            // 创建绑定
            Binding backgroundBinding = new Binding {
                Converter = (IValueConverter)this.Resources["converter"]
            };

            // 设置样式属性
            vccStyle.Setters.Add(new Setter(VirtualizingCellsControl.BackgroundProperty, backgroundBinding));

            // 应用样式到 SfDataGrid (您可能需要调整这部分以确保它正确应用到 VirtualizingCellsControl)
            DataGrid_Planner.CellStyle = vccStyle;
        }

        public void RefreshDataGrid() {
            if (!Application.Current.Dispatcher.CheckAccess()) {
                Application.Current.Dispatcher.Invoke(new Action(() => RefreshDataGrid()));
            }
            else {
                try {
                    DataGrid_Planner.BeginInit();

                    if (App.myPVM.BarterDetails != null) {
                        App.myPVM.BarterDetails.Clear();
                    }

                    foreach (Barter barter in App.listBarterPlanner) {
                        Barter myBarter = new Barter(barter.IsLand, barter.Item1, barter.Item2, barter.ExchangeQuantity,
                            barter.ExchangeDone, barter.BarterGroup, barter.InvQuantity, barter.InvQuantityChange);
                        App.myPVM.BarterDetails.Add(myBarter);
                    }

                    DataGrid_Planner.EndInit();
                }
                catch (Exception e) {
                }
            }
        }

        private void UpdateParley() {
            int intParley = 0;
            foreach (Barter barter in App.myPVM.BarterDetails.Where(b=>b.ExchangeDone==false && b.ExchangeQuantity>0)) {
                intParley += barter.Parley * barter.ExchangeQuantity;
            }

            Label_SelectedParley.Content = intParley;
            if (intParley > 1000000) {
                Label_SelectedParley.Foreground = Brushes.Red;
            }
            else {
                Label_SelectedParley.Foreground = Brushes.Black;
            }
        }

        private void ButtonAdv_Refresh_Click(object sender, RoutedEventArgs e) {
            Grouping();
            UpdateParley();
        }

        private void Grouping() {
            int intGroup = 0;
            foreach (Barter barter in App.myPVM.BarterDetails.Where(b => b.Item1.ItemLV.Equals("0"))) {
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

            foreach (Barter barter in App.myPVM.BarterDetails.Where(b =>
                         b.Item1.ItemLV.Equals("1") && b.BarterGroup == -1)) {
                int intLV = 2;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            foreach (Barter barter in App.myPVM.BarterDetails.Where(b =>
                         b.Item1.ItemLV.Equals("2") && b.BarterGroup == -1)) {
                int intLV = 3;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            foreach (Barter barter in App.myPVM.BarterDetails.Where(b =>
                         b.Item1.ItemLV.Equals("3") && b.BarterGroup == -1)) {
                int intLV = 4;

                FindBarterGroup(barter, intLV, intGroup);

                intGroup++;
            }

            try {
                DataGrid_Planner.View.BeginInit();
                DataGrid_Planner.GroupColumnDescriptions.Add(
                    new GroupColumnDescription() { ColumnName = "BarterGroup" });
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

            UpdateMapControl();
        }

        private void FindBarterGroup(Barter _barter, int _lv, int _group) {
            _barter.BarterGroup = _group;
            Barter myBarter = App.myPVM.BarterDetails.FirstOrDefault(b =>
                b.Item1.ItemLV.Equals(Convert.ToString(_lv)) && b.Item1Name.Equals(_barter.Item2Name))!;
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
            string strPath_Setting = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) +
                                     "\\Resources\\myPlan_Setting.xml";
            string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) +
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

                    App.listBarterPlanner.Clear();
                    for (int i = 0; i < dataSource.Count; i++) {
                        Barter myBarter = dataSource[i];
                        App.listBarterPlanner.Add(myBarter);
                    }

                    RefreshDataGrid();
                    //Grouping();
                    App.myCFun.Log("Loaded...", Brushes.Blue);
                }
                catch (Exception exception) {
                    App.myCFun.Log(exception.Message, Brushes.Red);
                }
                //myPlannerControl.DataGrid_Planner.ItemsSource = dataSource;
            }
            UpdateParley();
        }

        private void ButtonAdv_Save_Click(object sender, RoutedEventArgs e) {
            SaveData();
        }

        private void SaveData() {
            try {
                if (App.myPVM.BarterDetails.Count > 0) {
                    string strPath_Setting = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) +
                                             "\\Resources\\myPlan_Setting.xml";
                    string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) +
                                          "\\Resources\\myPlan_Data.json";

                    using (FileStream streamSetting =
                           new FileStream(strPath_Setting, FileMode.Create, FileAccess.Write)) {
                        DataGrid_Planner.Serialize(streamSetting);
                    }

                    using (FileStream streamData = new FileStream(strPath_Data, FileMode.Create, FileAccess.Write)) {
                        App.listBarterPlanner.Clear();
                        for (int i = 0; i < App.myPVM.BarterDetails.Count; i++) {
                            Barter myBarter = App.myPVM.BarterDetails[i];
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
            SaveData();
            RefreshDataGrid();
            UpdateParley();
            //Grouping();
            UpdateMapControl();
        }

        private void DataGrid_Planner_CurrentCellValueChanged(object sender, CurrentCellValueChangedEventArgs e) {
            if (e.Column.MappingName == "ExchangeDone") {
                SaveData();
                UpdateParley();
                // if (e.RowColumnIndex.ColumnIndex == 5) { //Eq.
                //     
                // }
                // Barter myBarter = (Barter)DataGrid_Planner.CurrentItem;
                // if (myBarter.ExchangeDone) {
                //     App.myCFun.Log("Checked", Brushes.Blue);
                // }
                // else {
                //     App.myCFun.Log("UnChecked", Brushes.Red);
                // }
                //
                // int a = 0;
                UpdateMapControl();
            }
        }

        private void UpdateMapControl() {
            for (int i = App.myfmMain.myMapControl.Grid_MapMain.Children.Count - 1; i > 0; i--) {
                var child = App.myfmMain.myMapControl.Grid_MapMain.Children[i];
                if (child is Grid && ((Grid)child).Name.StartsWith("GridContainer_")) {
                    App.myfmMain.myMapControl.Grid_MapMain.Children.Remove(child);
                }
            }
            foreach (Barter myBarter in App.myPVM.BarterDetails.Where(b => b.ExchangeDone == false && b.ExchangeQuantity > 0)) {
                App.myfmMain.myMapControl.IslandsButtonInitialisation(myBarter, GetBursh(myBarter));
            }
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
            if (App.myStorageVM.StorageCollection != null) {
                foreach (Items item in App.myStorageVM.StorageCollection) {
                    if (App.myPVM != null) {
                        Barter myBarter = App.myPVM.BarterDetails.FirstOrDefault(b => b.Item2Name == item.ItemName);
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
                App.myPVM.BarterDetails.Clear();
                DataGrid_Planner.EndInit();
            }
        }

        private void ButtonAdv_New_Click(object sender, RoutedEventArgs e) {
            DataGrid_Planner.BeginInit();
            if (App.myPVM != null) {
                App.myPVM.BarterDetails.Clear();
            }

            DataGrid_Planner.EndInit();
        }
    }
}