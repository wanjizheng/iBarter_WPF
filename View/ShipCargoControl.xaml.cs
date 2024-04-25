using Newtonsoft.Json;
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
using iBarter.Model;
using iBarter.ViewModel;
using Syncfusion.Windows.PropertyGrid;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for ShipCargoControl.xaml
    /// </summary>
    public partial class ShipCargoControl : UserControl {
        public ShipCargoControl() {
            InitializeComponent();
            DataContext = App.myCVM;
            RefreshData();
            //PropertyGrid_Ship.Items = App.myCVM.CargoProperty;
        }

        public void RefreshData() {
            string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\myCargoProperty_Data.json";

            if (File.Exists(strPath_Data)) {
                try {
                    string readJsonData = File.ReadAllText(strPath_Data);
                    App.myCVM = JsonConvert.DeserializeObject<ShipCargoViewModel>(readJsonData);

                    PropertyGrid_Ship.SelectedObject = App.myCVM;
                }
                catch (Exception exception) {
                    App.myCFun.Log(exception.Message, Brushes.Red);
                }
                //myPlannerControl.DataGrid_Planner.ItemsSource = dataSource;
            }
            else {
                ShipCargo myCargo = new ShipCargo(new List<Barter>());
                myCargo.ExtraLT = 1009;
                myCargo.TotalLT = 21500;
                myCargo.CurrentLT = 0;
                PropertyGrid_Ship.SelectedObject = myCargo;
            }
        }
    }
}
