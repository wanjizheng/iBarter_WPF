using Syncfusion.UI.Xaml.Grid;
using Syncfusion.Windows.Shared;
using System.Windows;
using System.Windows.Media;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class BarterScanner : ChromelessWindow {
        public BarterScanner() {
            InitializeComponent();

            this.DataContext = App.mySVM;
            BarterScanResults.ItemsSource = App.mySVM.BarterDetails;
            GridMultiColumnDropDownList_Item1.ItemsSource = App.mySVM.ItemsCollection;
            GridMultiColumnDropDownList_Item2.ItemsSource = App.mySVM.ItemsCollection;
            GridMultiColumnDropDownList_Islands.ItemsSource = App.mySVM.IslandsCollection;
        }

        GridRowSizingOptions gridRowResizingOptions = new GridRowSizingOptions();

        //To get the calculated height from GetAutoRowHeight method.    
        double autoHeight = double.NaN;


        private async void ButtonAdv_Scan_ClickAsync(object sender, RoutedEventArgs e) {
            // Thread myThread_SearchBarter = new Thread(App.myCFun.SearchBarter);
            // myThread_SearchBarter.IsBackground = true;
            // myThread_SearchBarter.Start();

            ButtonAdv_Scan.IsEnabled = false;

            try {
                App.myCFun.Log("Start scanning...", Brushes.Blue);
                await Task.Run(() => { App.myCFun.SearchBarter(); });
            }
            catch (Exception ex) {
                App.myCFun.Log(ex.Message, Brushes.Red);
            }
            finally {
                App.myCFun.Log("Done!", Brushes.DarkGreen);
                ButtonAdv_Scan.IsEnabled = true;
            }
        }

        public void RefreshDataGrid() {
            if (!Application.Current.Dispatcher.CheckAccess()) {
                Application.Current.Dispatcher.Invoke(new Action(() => RefreshDataGrid()));
            }
            else {
                try {
                    BarterScanResults.BeginInit();

                    if (App.mySVM.BarterDetails != null) {
                        App.mySVM.BarterDetails.Clear();
                    }

                    foreach (Barter barter in App.listBarterScanner) {
                        App.mySVM.BarterDetails.Add(barter);
                    }

                    BarterScanResults.EndInit();
                }
                catch (Exception e) {
                    App.myCFun.Log(e.Message, Brushes.Red);
                }
            }
        }

        private void ButtonAdv_Add_Click(object sender, RoutedEventArgs e) {
            for (int i = 0; i < App.mySVM.BarterDetails.Count; i++) {
                Barter barter = App.mySVM.BarterDetails[i];
                if (App.myPVM.BarterCollection.FirstOrDefault(b => b.IsLandName.Equals(barter.IsLandName)) == null) {
                    App.myPVM.BarterCollection.Add(barter);
                }
            }

            App.listBarterScanner.Clear();
            RefreshDataGrid();
        }

        private void BarterScanResults_CurrentCellEndEdit(object sender, CurrentCellEndEditEventArgs e) {
            try {
                BarterScanResults.View.Refresh();
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
            }
        }


        private void BarterScanResults_CurrentCellDropDownSelectionChanged(object sender,
            CurrentCellDropDownSelectionChangedEventArgs e) {
            try {
                //BarterScanResults.View.Refresh();
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
            }
        }
    }
}