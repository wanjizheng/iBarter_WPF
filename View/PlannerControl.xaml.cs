using System;
using System.Collections.Generic;
using System.Linq;
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
using System.Windows.Shapes;
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

        private void ButtonAdv_Refresh_Click(object sender, RoutedEventArgs e) {
            int intGroup = 0;
            foreach (Barter barter in App.myPVM.BarterDetails.Where(b=>b.Item1.getLV().Equals("0"))) {
                int intLV = 1;
                do {
                    barter.BarterGroup = intGroup;
                    Barter myBarter = App.myPVM.BarterDetails.Where(b => b.Item1.getLV().Equals(Convert.ToString(intLV)) && b.Item2Name.Equals(barter.Item1Name)).FirstOrDefault();
                    if (myBarter != null) {
                        myBarter.BarterGroup = intGroup;
                    }

                    intLV++;
                }while (!barter.Item1LV.Equals("5"));

                intGroup++;
            }

            DataGrid_Planner.View.BeginInit();
            DataGrid_Planner.GroupColumnDescriptions.Add(new GroupColumnDescription(){ColumnName = "BarterGroup"});
            DataGrid_Planner.View.EndInit();


            int a = 0;
        }
    }
}