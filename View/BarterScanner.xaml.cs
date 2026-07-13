using iBarter.Localization;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.Windows.Shared;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Syncfusion.UI.Xaml.ScrollAxis;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class BarterScanner : ChromelessWindow {

        // Phase 2 (i18n): MappingName -> resource key for direct GridTextColumns,
        // plus a separate map for the GridMultiColumnDropDownList outer HeaderText.
        // The dropdown inner column map (Islands / Parley / Remaining sub-cols;
        // ItemID / Item Name / Item LV / Item Number on the item dropdowns) is
        // shared with the planner where the labels are identical.
        private static readonly IReadOnlyDictionary<string, string> _headerKeyMap =
            new Dictionary<string, string> {
                ["IslandRemaining"] = "str.Grid.Scanner.Col.No",
                ["Parley"]          = "str.Grid.Scanner.Col.Parley",
                ["Item1Number"]     = "str.Grid.Scanner.Col.I1No",
                ["Item2Number"]     = "str.Grid.Scanner.Col.I2No",
            };

        private static readonly IReadOnlyDictionary<string, string> _dropdownOuterKeyMap =
            new Dictionary<string, string> {
                ["Islands"] = "str.Grid.Scanner.Col.Islands",
                ["Item1"]   = "str.Grid.Scanner.Col.Item1Name",
                ["Item2"]   = "str.Grid.Scanner.Col.Item2Name",
            };

        private static readonly IReadOnlyDictionary<string, string> _islandDropdownInnerKeyMap =
            new Dictionary<string, string> {
                ["IslandsNameDisplay"] = "str.Grid.Scanner.Col.IslandsSub",
                ["Parley"]             = "str.Grid.Scanner.Col.Parley",
                ["Remaining"]          = "str.Grid.Scanner.Col.Remaining",
            };

        private static readonly IReadOnlyDictionary<string, string> _itemDropdownInnerKeyMap =
            new Dictionary<string, string> {
                ["ItemID"]          = "str.Grid.Scanner.Col.DropdownItemID",
                ["ItemNameDisplay"] = "str.Grid.Scanner.Col.DropdownItemName",
                ["ItemLV"]          = "str.Grid.Scanner.Col.DropdownItemLV",
                ["ItemNumber"]      = "str.Grid.Scanner.Col.DropdownItemNumber",
            };

        public BarterScanner() {
            InitializeComponent();

            this.DataContext = App.mySVM;
            BarterScanResults.ItemsSource = App.mySVM.BarterDetails;
            RegisterLocalizedDropDownRenderer();
            GridMultiColumnDropDownList_Item1.ItemsSource = App.mySVM.ItemsCollection;
            GridMultiColumnDropDownList_Item2.ItemsSource = App.mySVM.ItemsCollection;
            GridMultiColumnDropDownList_Islands.ItemsSource = App.mySVM.IslandsCollection;

            InitializeScannerState();
            ApplyLocalization();
            LanguageService.Instance.LanguageChanged += (_, _) => ApplyLocalization();
        }

        public void InitializeScannerState() {
            if (App.mySVM != null) {
                DataContext = App.mySVM;
                BarterScanResults.ItemsSource = App.mySVM.BarterDetails;
                GridMultiColumnDropDownList_Item1.ItemsSource = App.mySVM.ItemsCollection;
                GridMultiColumnDropDownList_Item2.ItemsSource = App.mySVM.ItemsCollection;
                GridMultiColumnDropDownList_Islands.ItemsSource = App.mySVM.IslandsCollection;
            }

            App.myCFun?.RefreshScannerGameWindowState(out _);
        }

        private void RegisterLocalizedDropDownRenderer() {
            BarterScanResults.CellRenderers.Remove("MultiColumnDropDown");
            BarterScanResults.CellRenderers.Add("MultiColumnDropDown", new LocalizedMultiColumnDropDownRenderer());
        }

        private void ApplyLocalization() {
            ApplyLocalizedHeaders();
            RefreshLocalizedDisplay();
        }

        private void ApplyLocalizedHeaders() {
            var svc = LanguageService.Instance;
            GridHeaderLocalization.ApplyHeaders(BarterScanResults, _headerKeyMap);

            if (GridMultiColumnDropDownList_Islands != null
                && _dropdownOuterKeyMap.TryGetValue("Islands", out var ki))
                GridMultiColumnDropDownList_Islands.HeaderText = svc.Localize(ki);
            if (GridMultiColumnDropDownList_Item1 != null
                && _dropdownOuterKeyMap.TryGetValue("Item1", out var k1))
                GridMultiColumnDropDownList_Item1.HeaderText = svc.Localize(k1);
            if (GridMultiColumnDropDownList_Item2 != null
                && _dropdownOuterKeyMap.TryGetValue("Item2", out var k2))
                GridMultiColumnDropDownList_Item2.HeaderText = svc.Localize(k2);

            ApplyDropdownInnerHeaders(GridMultiColumnDropDownList_Islands, _islandDropdownInnerKeyMap);
            ApplyDropdownInnerHeaders(GridMultiColumnDropDownList_Item1, _itemDropdownInnerKeyMap);
            ApplyDropdownInnerHeaders(GridMultiColumnDropDownList_Item2, _itemDropdownInnerKeyMap);
        }

        private void RefreshLocalizedDisplay() {
            if (BarterScanResults == null) {
                return;
            }
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.Invoke(RefreshLocalizedDisplay);
                return;
            }

            BarterScanResults.View?.Refresh();
            BarterScanResults.InvalidateVisual();
        }

        private static void ApplyDropdownInnerHeaders(
            GridMultiColumnDropDownList? dropdown,
            IReadOnlyDictionary<string, string> map) {
            if (dropdown is null || dropdown.Columns is null) {
                return;
            }
            var svc = LanguageService.Instance;
            foreach (var col in dropdown.Columns) {
                if (col is GridTextColumn tc
                    && !string.IsNullOrEmpty(tc.MappingName)
                    && map.TryGetValue(tc.MappingName, out var key)) {
                    tc.HeaderText = svc.Localize(key);
                }
            }
        }

        GridRowSizingOptions gridRowResizingOptions = new GridRowSizingOptions();

        //To get the calculated height from GetAutoRowHeight method.    
        double autoHeight = double.NaN;


        private async void ButtonAdv_Scan_ClickAsync(object sender, RoutedEventArgs e) {
            // 2026-07-09: removed both the scan cooldown gate and the
            // pre-scan capture probe. Both were failing on the user's
            // dx hook build and blocking legitimate scans:
            //   - cooldown's 10-s gate stopped the user from re-clicking
            //     during a normal 1-2-anchor scan session.
            //   - pre-scan probe's 3x retry/800ms backoff (and the
            //     post-sleep probe before it) both returned ret=0 from
            //     PureDM.DM.Capture even on a healthy hook, causing
            //     the scan to bail with a "capture-stale" message
            //     before any anchors were looked up.
            // The user has opted to let the scan run unconditionally;
            // the per-anchor fallback paths (smart skip after 2
            // consecutive failures) already cover the rare case
            // where the hook actually IS dead.

            App.myCFun.Log("[DIAG-ui] scan click received", Brushes.LightSlateGray);
            ButtonAdv_Scan.IsEnabled = false;

            try {
                App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.Starting"), Brushes.Blue);
                await App.myCFun.IdentifyRoutes();
            }
            catch (Exception ex) {
                App.myCFun.Log(ex.Message, Brushes.Red);
            }
            finally {
                App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Scanner.Done"), Brushes.DarkGreen);
                ButtonAdv_Scan.IsEnabled = true;
            }
        }

        public void RefreshDataGrid() {
            if (!Application.Current.Dispatcher.CheckAccess()) {
                // Scanner completion runs on a background task. Never make it
                // wait synchronously for WPF layout/text rendering; queue the
                // refresh and let the UI dispatcher process it when available.
                Application.Current.Dispatcher.BeginInvoke(new Action(RefreshDataGrid));
                return;
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

            // 2026-07-08: auto-save planner on Add. The user previously had
            // to also click Planner's "Save" button or risk losing changes
            // if the app crashed / closed mid-session. The existing
            // PlannerControl.SaveData() handles both the XML layout dump
            // and the JSON bartered-items dump, so reusing it here keeps
            // a single source of truth for the on-disk format.
            if (App.myfmMain?.myPlannerControl != null) {
                App.myfmMain.myPlannerControl.SaveData();
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
                // Numeric-cell edits auto-refresh via their direct bindings
                // (Parley / IslandRemaining / Item1Number / Item2Number all
                // raise the right PropertyChanged in their own setters), so
                // we skip the expensive full View.Refresh() for them. Only
                // GridImageColumn (Item1Icon / Item2Icon) needs a forced
                // refresh when the dropdown selection changes underneath
                // it - the icon-path change after an item-name edit does
                // NOT propagate automatically. Mirror the pattern in
                // View/PlannerControl.xaml.cs:603-609. Visual index 0 is
                // the row header; the actual columns start at index 1.
                int colIdx = e.RowColumnIndex.ColumnIndex - 1;
                if (colIdx >= 0 && colIdx < BarterScanResults.Columns.Count) {
                    string mapping = BarterScanResults.Columns[colIdx].MappingName;
                    if (mapping == "Item1Name" || mapping == "Item2Name") {
                        BarterScanResults.View?.Refresh();
                    }
                }
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
