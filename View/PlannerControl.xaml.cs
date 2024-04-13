using System;
using System.Collections.Generic;
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
using Newtonsoft.Json;
using Syncfusion.UI.Xaml.Grid;

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
                        App.myPVM.BarterDetails.Add(barter);
                    }

                    DataGrid_Planner.EndInit();
                }
                catch (Exception e) {
                }
            }
        }

        private void ButtonAdv_Refresh_Click(object sender, RoutedEventArgs e) {
            int intGroup = 0;
            foreach (Barter barter in App.myPVM.BarterDetails.Where(b => b.Item1.getLV().Equals("0"))) {
                int intLV = 1;
                do {
                    barter.BarterGroup = intGroup;
                    Barter myBarter = App.myPVM.BarterDetails.Where(b => b.Item1.getLV().Equals(Convert.ToString(intLV)) && b.Item2Name.Equals(barter.Item1Name)).FirstOrDefault();
                    if (myBarter != null) {
                        myBarter.BarterGroup = intGroup;
                    }

                    intLV++;
                } while (!barter.Item1LV.Equals("5"));

                intGroup++;
            }

            DataGrid_Planner.View.BeginInit();
            DataGrid_Planner.GroupColumnDescriptions.Add(new GroupColumnDescription() { ColumnName = "BarterGroup" });
            DataGrid_Planner.View.EndInit();


            int a = 0;
        }

        private void ButtonAdv_Load_Click(object sender, RoutedEventArgs e) {
            string strPath_Setting = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\myPlan_Setting.xml";
            string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\myPlan_Data.json";

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
                }
                catch (Exception exception) {
                    App.myCFun.Log(exception.Message, Brushes.Red);
                }
                //myPlannerControl.DataGrid_Planner.ItemsSource = dataSource;
            }
        }

        private void ButtonAdv_Save_Click(object sender, RoutedEventArgs e) {
            try {
                if (App.myPVM.BarterDetails.Count > 0) {
                    string strPath_Setting = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\myPlan_Setting.xml";
                    string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\myPlan_Data.json";

                    using (FileStream streamSetting = new FileStream(strPath_Setting, FileMode.Create, FileAccess.Write)) {

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
                        streamData.Write(byteArray,0,byteArray.Length);
                    }
                }
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
            }
        }
    }
}