using Syncfusion.UI.Xaml.Grid;
using Syncfusion.Windows.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using iBarter.ViewModel;
using Syncfusion.Data;

namespace iBarter {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class BarterScanner : ChromelessWindow {
        public BarterScanner() {
            InitializeComponent();
            BarterScanResults.QueryRowHeight += dataGrid_QueryRowHeight;
            BarterScanResults.RowDragDropController.Dropped += RowDragDropController_Dropped;

            this.DataContext = App.mySVM;
            BarterScanResults.ItemsSource = App.mySVM.BarterDetails;
            GridMultiColumnDropDownList_Item1.ItemsSource = App.mySVM.ItemsCollection;
            GridMultiColumnDropDownList_Item2.ItemsSource = App.mySVM.ItemsCollection;
            GridMultiColumnDropDownList_Islands.ItemsSource = App.mySVM.IslandsCollection;
        }

        GridRowSizingOptions gridRowResizingOptions = new GridRowSizingOptions();

        //To get the calculated height from GetAutoRowHeight method.    
        double autoHeight = double.NaN;

        private void RowDragDropController_Dropped(object sender, GridRowDroppedEventArgs e) {
            if (e.DropPosition != DropPosition.None) {
                // Get Dragging records
                ObservableCollection<object> draggingRecords = e.Data.GetData("Records") as ObservableCollection<object>;

                // Gets the TargetRecord from the underlying collection using record index of the TargetRecord (e.TargetRecord)
                ScannerViewModel model = BarterScanResults.DataContext as ScannerViewModel;
                //OrderInfo targetRecord = model.OrdersFirstGrid[(int)e.TargetRecord];
                Barter targetRecord = model.BarterDetails[(int)e.TargetRecord];

                // Use Batch update to avoid data operatons in SfDataGrid during records removing and inserting
                BarterScanResults.BeginInit();

                // Removes the dragging records from the underlying collection
                foreach (Barter item in draggingRecords) {
                    model.BarterDetails.Remove(item);
                }

                // Find the target record index after removing the records
                int targetIndex = model.BarterDetails.IndexOf(targetRecord);
                int insertionIndex = e.DropPosition == DropPosition.DropAbove ? targetIndex : targetIndex + 1;
                insertionIndex = insertionIndex < 0 ? 0 : insertionIndex;

                // Insert dragging records to the target position
                for (int i = draggingRecords.Count - 1; i >= 0; i--) {
                    Barter myNewScript = draggingRecords[i] as Barter;
                    //myNewScript.ID = insertionIndex + 1;
                    model.BarterDetails.Insert(insertionIndex, myNewScript);
                }

                for (int i = 0; i < model.BarterDetails.Count; i++) {
                    Barter myScript = model.BarterDetails[i] as Barter;
                    //myScript = i + 1;
                }

                BarterScanResults.EndInit();
            }
        }

        private void dataGrid_QueryRowHeight(object sender, QueryRowHeightEventArgs e) {
            if (BarterScanResults.GridColumnSizer.GetAutoRowHeight(e.RowIndex, gridRowResizingOptions, out autoHeight)) {
                if (autoHeight > 24) {
                    e.Height = autoHeight;
                    e.Handled = true;
                }
            }
        }

        private async void ButtonAdv_Scan_ClickAsync(object sender, RoutedEventArgs e) {
            // Thread myThread_SearchBarter = new Thread(App.myCFun.SearchBarter);
            // myThread_SearchBarter.IsBackground = true;
            // myThread_SearchBarter.Start();

            await Task.Run(() => {
                App.myCFun.SearchBarter();
            });
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
                }
            }
        }

        private void ButtonAdv_Add_Click(object sender, RoutedEventArgs e) {
            for (int i = 0; i < App.mySVM.BarterDetails.Count; i++) {
                Barter barter = App.mySVM.BarterDetails[i];
                if (!App.myPVM.BarterDetails.Contains(barter)) {
                    App.myPVM.BarterDetails.Add(barter);
                }
            }

            App.listBarterScanner.Clear();
            RefreshDataGrid();
        }
    }
}