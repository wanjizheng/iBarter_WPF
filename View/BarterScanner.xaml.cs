using Syncfusion.UI.Xaml.Grid;
using Syncfusion.Windows.Shared;
using System.Windows;
using System.Windows.Media;
using Syncfusion.UI.Xaml.ScrollAxis;

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
                //await Task.Run(() => { App.myCFun.SearchBarter(); });

                await Task.Run(() => App.myCFun.IdentifyRoutes());
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


            // After line 82: scroll Planner grid to the last item
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var grid = App.myfmMain.myPlannerControl.DataGrid_Planner;
                
                if (grid == null) return;

                grid.View.BeginInit();
                try {
                    grid.GroupColumnDescriptions.Clear();          // removes column-based grouping
                    grid.View.GroupDescriptions?.Clear();          // defensive: clears any programmatic group descriptions
                    grid.AutoExpandGroups = false;                 // optional: disable auto expand
                }
                finally {
                    grid.View.EndInit();
                }
                grid.View.Refresh(); // or grid.UpdateLayout();


                grid.UpdateLayout();

                var view = grid.View;
                if (view == null || view.Records.Count == 0) return;

                var lastEntry = (Syncfusion.Data.RecordEntry)view.Records[view.Records.Count - 1];
                var lastData = lastEntry.Data;

                int rowIndex = grid.ResolveToRowIndex(lastData);
                if (rowIndex <= 0) return;

                // Pick the first visible column and resolve its visible index
                Syncfusion.UI.Xaml.Grid.GridColumn firstVisible = null;
                foreach (var c in grid.Columns) {
                    if (!c.IsHidden) { firstVisible = c; break; }
                }
                if (firstVisible == null && grid.Columns.Count > 0)
                    firstVisible = grid.Columns[0];

                int colIndex = 1;
                if (firstVisible != null) {
                    // Replace this line:
                    // colIndex = grid.ResolveToGridVisibleColumnIndex(firstVisible.MappingName);

                    // With this line:
                    colIndex = grid.ResolveToGridVisibleColumnIndex(grid.Columns.IndexOf(firstVisible));

                    if (colIndex < 1) colIndex = 1;
                }

                var cell = new Syncfusion.UI.Xaml.ScrollAxis.RowColumnIndex(rowIndex, colIndex);
                grid.MoveCurrentCell(cell);
                grid.ScrollInView(cell);
                grid.SelectedItem = lastData; // optional
            }, System.Windows.Threading.DispatcherPriority.Render);

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

        private void PinWindow_Click(object sender, RoutedEventArgs e) {
            if (this.Topmost) {
                this.Topmost = false;
                PinWindow.IsChecked = false;
            }
            else {
                this.Topmost = true;
                PinWindow.IsChecked = true;
            }
        }
    }
}