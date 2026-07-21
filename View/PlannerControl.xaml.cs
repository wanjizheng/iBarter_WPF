using iBarter.Localization;
using iBarter.Persistence;
using iBarter.Planning;
using iBarter.Routing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncfusion.Pdf.Grid;
using Syncfusion.UI.Xaml.Grid;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Brush = System.Windows.Media.Brush;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for PlannerControl.xaml
    /// </summary>
    public partial class PlannerControl : UserControl {
        // Highest barter item LV in the game; chain recursion stops one step before reaching this.
        private const int MAX_BARTER_LV = 7;
        private bool IsDesignMode => DesignerProperties.GetIsInDesignMode(this);
        private bool startupLoadScheduled;
        private bool startupLoadCompleted;

        // Phase 2 (i18n): column-header MappingName -> resource key for every
        // direct GridTextColumn on DataGrid_Planner. HeaderText is a plain CLR
        // property so {DynamicResource} cannot refresh it; the code-behind
        // ApplyLocalizedHeaders runs once at ctor and on every
        // LanguageService.LanguageChanged notification.
        private static readonly IReadOnlyDictionary<string, string> _headerKeyMap =
            new Dictionary<string, string> {
                ["BarterGroup"]       = "str.Grid.Planner.Col.Group",
                ["Item1LV"]           = "str.Grid.Planner.Col.LV",
                ["ExchangeDone"]      = "str.Grid.Planner.Col.CK",
                ["ExchangeQuantity"]  = "str.Grid.Planner.Col.Eq",
                ["IslandRemaining"]   = "str.Grid.Planner.Col.No",
                ["Item1Number"]       = "str.Grid.Planner.Col.I1No",
                ["Item2Number"]       = "str.Grid.Planner.Col.I2No",
                ["InvQuantity"]       = "str.Grid.Planner.Col.Inv",
                ["InvQuantityChange"] = "str.Grid.Planner.Col.InvChange",
            };

        // Multi-column dropdown outer-HeaderText overrides (MappingName isn't usable
        // for the dropdowns because they own their own column list, not the grid's).
        private static readonly IReadOnlyDictionary<string, string> _dropdownOuterKeyMap =
            new Dictionary<string, string> {
                ["Islands"]  = "str.Grid.Planner.Col.Location",
                ["Item"]     = "str.Grid.Planner.Col.Item",
                ["Exchange"] = "str.Grid.Planner.Col.Exchange",
            };

        // Per-dropdown inner column MappingName -> resource key.  Islands dropdown
        // shows the Island row itself (Islands/Parley/Remaining); the two Item
        // dropdowns share the same four sub-column keys.
        private static readonly IReadOnlyDictionary<string, string> _islandDropdownInnerKeyMap =
            new Dictionary<string, string> {
                ["IslandsNameDisplay"] = "str.Grid.Planner.Col.Islands",
                ["Parley"]             = "str.Grid.Planner.Col.IslandParley",
                ["Remaining"]          = "str.Grid.Planner.Col.IslandRemaining",
            };
        private static readonly IReadOnlyDictionary<string, string> _itemDropdownInnerKeyMap =
            new Dictionary<string, string> {
                ["ItemID"]          = "str.Grid.Planner.Col.ItemID",
                ["ItemNameDisplay"] = "str.Grid.Planner.Col.ItemName",
                ["ItemLV"]          = "str.Grid.Planner.Col.ItemLV",
                ["ItemNumber"]      = "str.Grid.Planner.Col.ItemNumber",
            };

        public PlannerControl() {
            InitializeComponent();
            if (IsDesignMode) return;
            this.DataContext = App.myPVM;
            DataGrid_Planner.ItemsSource = App.myPVM.BarterCollection;

            DataGrid_Planner.AutoScroller.AutoScrolling = AutoScrollOrientation.Both;
            RegisterLocalizedDropDownRenderer();
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
            ApplyTypography();
            Loaded += (_, _) => ApplyTypography();

            // Phase 2 (i18n): one-shot apply + subscribe for live re-render.
            ApplyLocalization();
            LanguageService.Instance.LanguageChanged += (_, _) => ApplyLocalization();
        }

        public void LoadSavedDataAndAutomaticRouteAtStartup() {
            if (IsDesignMode || startupLoadCompleted || startupLoadScheduled) return;
            startupLoadScheduled = true;

            void QueueLoad() {
                Dispatcher.BeginInvoke(new Action(() => {
                    if (startupLoadCompleted) return;
                    startupLoadCompleted = true;
                    startupLoadScheduled = false;
                    ButtonAdv_Load_Click(this, new RoutedEventArgs());
                }), DispatcherPriority.ContextIdle);
            }

            if (IsLoaded) {
                QueueLoad();
                return;
            }

            RoutedEventHandler? loadedHandler = null;
            loadedHandler = (_, _) => {
                Loaded -= loadedHandler;
                QueueLoad();
            };
            Loaded += loadedHandler;
        }

        private void TryRestoreAutomaticRouteAfterLoad() {
            try {
                App.myRouteCoordinator?.RefreshCompletedBarters(App.myPVM.BarterCollection);

                // Discover the optimization mode that was used to generate the
                // saved plan. v2 envelopes persist it explicitly; v1 envelopes
                // do not, so we fall back to trying each profile until one
                // matches the saved fingerprint. This is the fix for the
                // "saved route plan does not appear after restart" bug:
                // previously the restore path hard-coded MaxLocalMoves=2000,
                // which only matched the Deep profile's fingerprint.
                var persistencePath = AutomaticRoutePlanStorage.RuntimeResourcesPath;
                RouteOptimizationMode[]? modesToTry = null;
                if (RoutePlanPersistence.TryReadMetadata(
                        persistencePath,
                        out int schemaVersion,
                        out _,
                        out var savedMode,
                        out _)
                    && savedMode.HasValue) {
                    modesToTry = new[] { savedMode.Value };
                }
                else {
                    modesToTry = new[] {
                        RouteOptimizationMode.Quick,
                        RouteOptimizationMode.Balanced,
                        RouteOptimizationMode.Deep,
                        RouteOptimizationMode.Extreme,
                    };
                }

                RoutePlanLoadResult? lastResult = null;
                foreach (var mode in modesToTry) {
                    var profile = RouteOptimizationProfile.For(mode);
                    var request = BuildCurrentAutomaticRouteRequest(profile);
                    if (request is null) continue;
                    var result = App.myRouteCoordinator!.TryRestore(request);
                    lastResult = result;
                    if (result.Status == RoutePlanLoadStatus.Loaded) {
                        LogRestoreSuccess(result);
                        App.myfmMain?.ActivateShipCargoSelection();
                        return;
                    }
                }

                // No mode matched (or no request could be built). Log the
                // most informative failure we saw so the user knows whether
                // to retry AutoPlan or whether the save file is broken.
                if (lastResult is not null) {
                    LogRestoreFailure(lastResult);
                }
            }
            catch (Exception exception) {
                // A stale or partially edited Planner must never make startup fail.
                App.myCFun?.Log(exception.Message, Brushes.OrangeRed);
            }
        }

        private void LogRestoreSuccess(RoutePlanLoadResult result) {
            string mode = result.SavedOptimizationMode?.ToString() ?? "Balanced";
            int routeCount = result.Snapshot?.Plan.Routes.Count ?? 0;
            App.myCFun?.Log(
                $"[AutoRoute] 已恢复上次保存的路线（{mode} 模式，{routeCount} 条路线）。",
                Brushes.DarkOliveGreen);
        }

        private void LogRestoreFailure(RoutePlanLoadResult result) {
            string fingerprintShort = result.SavedFingerprint is { } saved
                ? saved.Substring(0, Math.Min(12, saved.Length))
                : "<none>";
            string message = result.Status switch {
                RoutePlanLoadStatus.FileNotFound =>
                    "[AutoRoute] 首次启动，没有可恢复的自动路线。",
                RoutePlanLoadStatus.FingerprintMismatch =>
                    $"[AutoRoute] 已保存的路线与当前 Planner、仓库、船舶设置或搜索模式不一致，需重新生成 (saved={fingerprintShort})。",
                RoutePlanLoadStatus.UnsupportedSchema =>
                    $"[AutoRoute] 路线保存格式版本 (Schema {result.SchemaVersion}) 不受支持，需重新生成。",
                RoutePlanLoadStatus.CorruptFile =>
                    "[AutoRoute] 自动路线保存文件已损坏，无法恢复。",
                _ => $"[AutoRoute] 自动路线恢复失败 ({result.Status})。",
            };
            App.myCFun?.Log(message, Brushes.OrangeRed);
        }

        public AutomaticRoutePlanningRequest? BuildCurrentAutomaticRouteRequest(
            RouteOptimizationProfile profile) {
            if (App.myPVM?.BarterCollection is null || App.myPVM.BarterCollection.Count == 0
                || App.myStorageVM?.StorageCollection is null
                || App.myCargoProperty is null
                || App.myCargoProperty.ExtraLT < 0
                || App.myCargoProperty.TotalLT <= 0)
                return null;

            var routeRows = App.myPVM.BarterCollection.Select(b => new PlannerRouteSnapshot(
                RowId: b.PlannerRowId,
                ExchangeDone: b.ExchangeDone,
                ExchangeQuantity: b.ExchangeQuantity,
                IslandId: b.IsLandName,
                Item1Id: b.Item1.ItemID,
                Item1DisplayName: b.Item1NameDisplay,
                Item1Level: int.TryParse(b.Item1.ItemLV, NumberStyles.Integer, CultureInfo.InvariantCulture, out int item1Level) ? item1Level : 0,
                Item1Number: b.Item1Number,
                Item2Id: b.Item2.ItemID,
                Item2DisplayName: b.Item2NameDisplay,
                Item2Level: int.TryParse(b.Item2.ItemLV, NumberStyles.Integer, CultureInfo.InvariantCulture, out int item2Level) ? item2Level : 0,
                Item2Number: b.Item2Number)).ToArray();
            var storageRows = App.myStorageVM.StorageCollection.Select(item => new StorageItemSnapshot(
                item.ItemID,
                int.TryParse(item.ItemLV, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) ? level : 0,
                item.StorageVeliaQuantity_Velia,
                item.StorageVeliaQuantity_Iliya,
                item.StorageVeliaQuantity_Epheria,
                item.StorageVeliaQuantity_Ancado)).ToArray();
            var islandRows = App.listIslands.Where(island => island.HasNavigationCoordinates)
                .Select(island => new IslandRouteSnapshot(
                    island.IslandsName,
                    new RoutePoint(island.NavigationX!.Value, island.NavigationY!.Value)))
                .ToArray();
            var cargo = new CargoCapacitySnapshot(
                Convert.ToInt32(Math.Round(App.myCargoProperty.ExtraLT, MidpointRounding.AwayFromZero)),
                Convert.ToInt32(Math.Round(App.myCargoProperty.TotalLT, MidpointRounding.AwayFromZero)));
            // Forward the caller-supplied profile's MaxLocalEvaluations into
            // the request limits. The fingerprint includes MaxLocalMoves, so
            // using the saved mode's value here is what lets restore match
            // the fingerprint that was computed at save time.
            return AutomaticRoutePlanningAdapter.BuildRequest(
                routeRows, storageRows, islandRows, cargo,
                AutomaticRoutePlanningAdapter.WithProfileLimits(
                    new RouteSearchLimits(250_000, 2_000), profile),
                profile);
        }

        private void RegisterLocalizedDropDownRenderer() {
            DataGrid_Planner.CellRenderers.Remove("MultiColumnDropDown");
            DataGrid_Planner.CellRenderers.Add("MultiColumnDropDown", new LocalizedMultiColumnDropDownRenderer());
        }

        private RouteOptimizationProfile ResolveSelectedOptimizationProfile() {
            return ResolveSelectedOptimizationProfileSafe();
        }

        public RouteOptimizationProfile ResolveSelectedOptimizationProfileSafe() {
            var mode = RouteOptimizationMode.Balanced;
            if (ComboBoxAdv_OptimMode?.SelectedItem is System.Windows.Controls.ContentControl { Tag: string tag }) {
                if (Enum.TryParse(tag, out RouteOptimizationMode parsed)) mode = parsed;
            }
            return RouteOptimizationProfile.For(mode);
        }

        private void ComboBoxAdv_OptimMode_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e) {
            var profile = ResolveSelectedOptimizationProfile();
            App.myRouteCoordinator?.SetOptimizationMode(profile.Mode);
        }

        private static string OptimModeLocalizationKey(RouteOptimizationMode mode) => mode switch {
            RouteOptimizationMode.Quick => "str.Planner.AutoPlan.OptimQuick",
            RouteOptimizationMode.Balanced => "str.Planner.AutoPlan.OptimBalanced",
            RouteOptimizationMode.Deep => "str.Planner.AutoPlan.OptimDeep",
            RouteOptimizationMode.Extreme => "str.Planner.AutoPlan.OptimExtreme",
            _ => "str.Planner.AutoPlan.OptimBalanced",
        };

        private static string FormatRouteDiagnostic(
            RouteDiagnostic? diagnostic,
            AutomaticRoutePlanningRequest request,
            LanguageService language) {
            if (diagnostic is null) return "";

            if (diagnostic.Code == "task-overweight") {
                var task = request.Tasks.FirstOrDefault(candidate =>
                    StringComparer.Ordinal.Equals(candidate.RowId, diagnostic.RowId));
                string island = task?.IslandId ?? diagnostic.RowId;
                string itemName = request.Items.TryGetValue(diagnostic.ItemId, out var item)
                    ? item.DisplayName
                    : diagnostic.ItemId;
                return language.Localize(
                    "str.Log.AutoRoute.TaskOverweight",
                    island,
                    itemName,
                    diagnostic.Detail,
                    request.TotalLT);
            }

            if (!string.IsNullOrWhiteSpace(diagnostic.Detail))
                return $"{diagnostic.Code}: {diagnostic.Detail}";
            if (!string.IsNullOrWhiteSpace(diagnostic.ItemId))
                return $"{diagnostic.Code}: {diagnostic.ItemId}";
            if (!string.IsNullOrWhiteSpace(diagnostic.RowId))
                return $"{diagnostic.Code}: {diagnostic.RowId}";
            return diagnostic.Code;
        }

        private void ApplyLocalization() {
            ApplyTypography();
            ApplyLocalizedHeaders();
            RefreshLocalizedDisplay();
        }

        private void ApplyTypography() {
            SfDataGridTypography.Apply(DataGrid_Planner);
        }

        private void ApplyLocalizedHeaders() {
            var svc = LanguageService.Instance;
            GridHeaderLocalization.ApplyHeaders(DataGrid_Planner, _headerKeyMap);

            if (GridMultiColumnDropDownList_Islands != null
                && _dropdownOuterKeyMap.TryGetValue("Islands", out var ki))
                GridMultiColumnDropDownList_Islands.HeaderText = svc.Localize(ki);
            if (GridMultiColumnDropDownList_Item != null
                && _dropdownOuterKeyMap.TryGetValue("Item", out var kIt))
                GridMultiColumnDropDownList_Item.HeaderText = svc.Localize(kIt);
            if (GridMultiColumnDropDownList_Exchange != null
                && _dropdownOuterKeyMap.TryGetValue("Exchange", out var kEx))
                GridMultiColumnDropDownList_Exchange.HeaderText = svc.Localize(kEx);

            ApplyDropdownInnerHeaders(GridMultiColumnDropDownList_Islands, _islandDropdownInnerKeyMap);
            ApplyDropdownInnerHeaders(GridMultiColumnDropDownList_Item, _itemDropdownInnerKeyMap);
            ApplyDropdownInnerHeaders(GridMultiColumnDropDownList_Exchange, _itemDropdownInnerKeyMap);
        }

        private void RefreshLocalizedDisplay() {
            if (DataGrid_Planner == null) {
                return;
            }
            if (!Dispatcher.CheckAccess()) {
                Dispatcher.Invoke(RefreshLocalizedDisplay);
                return;
            }

            DataGrid_Planner.View?.Refresh();
            DataGrid_Planner.InvalidateVisual();
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
                bool gridInitialising = false;
                try {
                    DataGrid_Planner.BeginInit();
                    gridInitialising = true;
                }
                catch (Exception exception) {
                    App.myCFun?.Log(
                        "[DIAG-planner-load] grid BeginInit skipped: " + exception.Message,
                        Brushes.OrangeRed);
                }

                try {
                    App.myPVM.BarterCollection.Clear();

                    foreach (Barter barter in App.listBarterPlanner) {
                        // Audit round 7: the parameter constructor
                        // generates a fresh PlannerRowId; the v1 code
                        // path here silently re-id'd every row on
                        // refresh, which broke the
                        // RoutePlanFingerprint round trip. Use the
                        // clone constructor so the row keeps its
                        // persisted id.
                        Barter myBarter = new Barter(barter);
                        App.myPVM.BarterCollection.Add(myBarter);
                    }
                }
                finally {
                    if (gridInitialising) DataGrid_Planner.EndInit();
                }
            }
        }

        private void UpdateParley() {
            if (Label_SelectedParley != null) {
                // Audit round 2: route the entire UI label through the
                // authoritative RouteParleyCalculator. No second LINQ
                // formula lives here; if anyone wants to change the
                // parley contract, they must change the calculator and
                // every consumer (Planner CK, map double-click,
                // Auto Plan) moves in lockstep.
                long intParley = RouteParleyCalculator.CalculateRemaining(
                    App.myPVM.BarterCollection.Select(b => new PlannerParleyRow(b)));
                Label_SelectedParley.Content = intParley;
                if (intParley > 1000000) {
                    Label_SelectedParley.Foreground = Brushes.Red;
                }
                else {
                    // Do not freeze the normal value to black.  A dynamic
                    // resource follows the application's active light/dark
                    // palette whenever the theme changes.
                    Label_SelectedParley.SetResourceReference(
                        Control.ForegroundProperty,
                        "AppTextBrush");
                }
            }
        }

        /// <summary>
        /// Adapter that exposes the Planner's <see cref="Barter"/>
        /// as the <see cref="IRouteParleyRow"/> contract without forcing
        /// the model to implement the interface directly.
        /// </summary>
        private sealed class PlannerParleyRow : IRouteParleyRow {
            private readonly Barter _barter;
            public PlannerParleyRow(Barter barter) { _barter = barter; }
            public string RowId => string.Empty;
            public int Parley => PlannerControl.GetEffectiveParley(_barter);
            public int ExchangeQuantity => _barter.ExchangeQuantity;
            public bool ExchangeDone => _barter.ExchangeDone;
        }

        // Phase 6 (i18n) / Task 6: pull the UsingALT island switch out of UpdateParley
        // so the Auto Plan adapter can charge the same per-exchange parley the UI
        // displays. If the two formulas ever drift, the budget constraint on the
        // planner would silently disagree with the live counter.
        private static int GetEffectiveParley(Barter barter) {
            if (!barter.UsingALT) {
                return barter.Parley;
            }
            switch (barter.IsLand.Island) {
                case EnumLists.Island.Halmad:
                case EnumLists.Island.Kashuma:
                    return 29430;
                case EnumLists.Island.Hakoven:
                    return 43780;
                case EnumLists.Island.Haran:
                case EnumLists.Island.Unfinished:
                case EnumLists.Island.Lantinia:
                    return 46544;
                case EnumLists.Island.Pakio:
                case EnumLists.Island.Ancient:
                case EnumLists.Island.Crow:
                case EnumLists.Island.Cholace:
                case EnumLists.Island.Rickun:
                case EnumLists.Island.Cox_Pirate:
                case EnumLists.Island.Wandering:
                case EnumLists.Island.Marine:
                    return 58180;
                case EnumLists.Island.Derko:
                    return 36420;
                default:
                    return 14286;
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
            // A manual load supersedes any startup load still waiting in the
            // Dispatcher queue, preventing a second reload moments later.
            startupLoadCompleted = true;
            startupLoadScheduled = false;
            App.myRouteCoordinator?.Invalidate("planner-load");
            string strPath_Setting = AppDomain.CurrentDomain.BaseDirectory +
                                     "\\Resources\\myPlan_Setting.xml";
            string strPath_Data = AppDomain.CurrentDomain.BaseDirectory +
                                  "\\Resources\\myPlan_Data.json";

            // 2026-07-09: load the two files independently instead of
            // bailing if either is missing. The XML carries the
            // DataGrid UI state (column widths / sort) and is nice-to-
            // have; the JSON carries the actual barter rows and is the
            // one the user actually wants back. Previously a missing
            // XML silently aborted the whole load with no log entry,
            // which looked like "click does nothing" from the UI.
            bool loadedSetting = false;
            if (File.Exists(strPath_Setting)) {
                try {
                    using (var file = File.Open(strPath_Setting, FileMode.Open)) {
                        DataGrid_Planner.Deserialize(file);
                        // Deserialize restores the user's column layout, but it
                        // also restores legacy 24px row heights and can replace
                        // column styles. Reassert current typography afterwards.
                        ApplyTypography();
                        loadedSetting = true;
                    }
                }
                catch (Exception exception) {
                    App.myCFun.Log("[DIAG-planner-load] setting deserialize failed: "
                        + exception.Message, Brushes.OrangeRed);
                }
            }

            if (File.Exists(strPath_Data) || File.Exists(strPath_Data + ".bak")) {
                try {
                    if (!AtomicFileStore.TryReadValidated(strPath_Data, IsValidPlannerJson,
                            out string readJsonData, out bool recoveredFromBackup)) {
                        App.myCFun.Log(Localization.LanguageService.Instance.Current == AppLanguage.TraditionalChinese
                            ? "规划数据无效；原文件已保留，没有被覆盖。"
                            : "Planner data is invalid. The existing file was preserved and was not overwritten.", Brushes.Red);
                        return;
                    }
                    List<Barter> dataSource = JsonConvert.DeserializeObject<List<Barter>>(readJsonData);
                    if (dataSource != null && dataSource.Count > 0) {
                        App.listBarterPlanner.Clear();
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        int migratedCount = 0;
                        for (int i = 0; i < dataSource.Count; i++) {
                            Barter myBarter = dataSource[i];
                            // Audit round 8: load-time migration
                            // handles all four invalid cases in one
                            // pass:
                            //   * null / empty PlannerRowId
                            //   * "INVALID:" sentinel from the v1
                            //     migration path
                            //   * duplicate id (collision)
                            //   * any malformed value
                            // The rule: the FIRST valid unique id is
                            // kept; subsequent duplicates / invalids
                            // are REGENERATED with a fresh br-* GUID.
                            // After the pass, one SaveData() commits
                            // every new id atomically.
                            if (!myBarter.HasPlannerRowId
                                || myBarter.PlannerRowId.StartsWith("INVALID:", StringComparison.Ordinal)
                                || !seen.Add(myBarter.PlannerRowId)) {
                                myBarter.RegeneratePlannerRowId();
                                migratedCount++;
                            }
                            seen.Add(myBarter.PlannerRowId);
                            App.listBarterPlanner.Add(myBarter);
                        }

                        RefreshDataGrid();
                        //Grouping();
                        if (migratedCount > 0) {
                            // Audit round 7: the v1 deferred the
                            // "save to persist the new ids" until the
                            // next user action. That meant a restart
                            // before any save could trigger the
                            // migration again. Atomic save here so
                            // the next reload reuses the same ids.
                            SaveData();
                            App.myCFun.Log(
                                $"[Planner] 已为 {migratedCount} 条旧记录生成持久化 PlannerRowId，已立即持久化。",
                                Brushes.SteelBlue);
                        }
                        App.myCFun.Log(Localization.LanguageService.Instance.Localize(
                            "str.Log.Planner.Loaded")
                            + (loadedSetting ? "" : " (UI state XML missing, skipped)")
                            + (recoveredFromBackup
                                ? (Localization.LanguageService.Instance.Current == AppLanguage.TraditionalChinese
                                    ? "（已从备份恢复）"
                                    : " (recovered from backup)")
                                : ""),
                            Brushes.Blue);
                    }
                    else {
                        App.listBarterPlanner.Clear();
                        App.myPVM.BarterCollection.Clear();
                    }
                }
                catch (Exception exception) {
                    App.myCFun.Log("[DIAG-planner-load] json deserialize failed: "
                        + exception.Message, Brushes.Red);
                }
            }
            // No JSON plan is a normal first-run state. Do not produce a
            // diagnostic/error entry merely because the user has not saved a
            // planner configuration yet.

            UpdateParley();

            App.myfmMain.myShipCargo.RefreshData();
            TryRestoreAutomaticRouteAfterLoad();
        }

        private void ButtonAdv_Save_Click(object sender, RoutedEventArgs e) {
            App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Planner.Saved"), Brushes.Blue);
            SaveData();
        }

        public void SaveData() {
            try {
                string resourceDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
                string settingPath = Path.Combine(resourceDirectory, "myPlan_Setting.xml");
                string dataPath = Path.Combine(resourceDirectory, "myPlan_Data.json");
                var snapshot = App.myPVM.BarterCollection.ToList();

                if (snapshot.Count > 0) {
                    using var settingStream = new MemoryStream();
                    DataGrid_Planner.Serialize(settingStream);
                    string settingXml = System.Text.Encoding.UTF8.GetString(settingStream.ToArray());
                    AtomicFileStore.WriteValidated(settingPath, settingXml, IsValidXml);
                }

                string jsonData = JsonConvert.SerializeObject(snapshot);
                AtomicFileStore.WriteValidated(dataPath, jsonData, IsValidPlannerJson, HasPlannerRows);
                App.listBarterPlanner.Clear();
                App.listBarterPlanner.AddRange(snapshot);
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
            }
        }

        private static bool IsValidPlannerJson(string json) {
            try {
                _ = JArray.Parse(json);
                return true;
            }
            catch {
                return false;
            }
        }

        private static bool HasPlannerRows(string json) {
            try {
                return JArray.Parse(json).Count > 0;
            }
            catch {
                return false;
            }
        }

        private static bool IsValidXml(string xml) {
            try {
                _ = XDocument.Parse(xml);
                return true;
            }
            catch {
                return false;
            }
        }

        private void DataGrid_Planner_CurrentCellEndEdit(object sender, CurrentCellEndEditEventArgs e) {
            App.myRouteCoordinator?.Invalidate("planner-edit");
            if (e.RowColumnIndex.ColumnIndex == 5) {
                Barter barter = DataGrid_Planner.CurrentItem as Barter;
                Barter myBarter = barter == null
                    ? null
                    : App.myPVM.BarterCollection.FirstOrDefault(b => b.IsLandName == barter.IsLandName);
                if (myBarter != null) {
                    if (myBarter.ExchangeQuantity > myBarter.IslandRemaining || myBarter.ExchangeQuantity < 0) {
                        myBarter.ExchangeQuantity = myBarter.IslandRemaining;
                    }

                    UpdateInvChange(myBarter.BarterGroup);
                    App.myfmMain.myShipCargo.UpdateCurrentLV();
                    App.myfmMain.myShipCargo.SaveData();
                }
            }

            SaveData();
            // Numeric-cell edits auto-refresh via their bindings, so we skip the
            // expensive full View.Refresh() for them (perf commit 055942b). BUT a
            // GridImageColumn (Item1Icon/Item2Icon) does NOT pick up the icon-path
            // change that follows an item-name edit, so refresh the view only when
            // the edited column was one of the item dropdowns.
            int colIdx = e.RowColumnIndex.ColumnIndex - 1; // -1: row header occupies visual index 0
            if (colIdx >= 0 && colIdx < DataGrid_Planner.Columns.Count) {
                string mapping = DataGrid_Planner.Columns[colIdx].MappingName;
                if (mapping == "Item1Name" || mapping == "Item2Name") {
                    DataGrid_Planner.View?.Refresh();
                }
            }
            UpdateParley();
            //Grouping();
            UpdateMapControl();
        }


        private void DataGrid_Planner_CurrentCellValueChanged(object sender, CurrentCellValueChangedEventArgs e) {
            if (e.Column.MappingName == "ExchangeDone") {
                // Bug 1 fix: instead of Invalidate() (which nuked the
                // current plan), invoke the unified progress pipeline so
                // the in-memory route plan is reconciled, parley is
                // re-derived, and the auto-route-plan.json is updated
                // atomically. Both the Planner CK and the map double-click
                // call the same code path.
                Barter myBarter = (Barter)e.Record;
                string rowId = myBarter is null
                    ? string.Empty
                    : myBarter.PlannerRowId;
                bool completed = myBarter?.ExchangeDone ?? false;
                App.myRouteCoordinator?.ApplyBarterCompletionProgress(
                    BuildCurrentAutomaticRouteRequest(
                        ResolveSelectedOptimizationProfile()),
                    App.myPVM.BarterCollection,
                    rowId,
                    completed);

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
            MenuItem deleteItem = new MenuItem { Header = Localization.LanguageService.Instance.Localize("str.Msg.Planner.DeleteRowMenu") };
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
                Localization.LanguageService.Instance.Localize(
                    "str.Msg.Planner.DeleteRow",
                    barter.Item1NameDisplay, barter.Item2NameDisplay),
                Localization.LanguageService.Instance.Localize("str.Msg.Confirmation.Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) {
                return;
            }

            App.myPVM.BarterCollection.Remove(barter);
            App.myRouteCoordinator?.Invalidate("planner-delete");
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
            if (App.myStorageVM?.StorageCollection is null || App.myPVM?.BarterCollection is null) {
                return;
            }

            var totals = App.myStorageVM.StorageCollection
                .Where(item => !string.IsNullOrWhiteSpace(item.ItemID))
                .GroupBy(item => item.ItemID, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(item =>
                    (long)item.StorageVeliaQuantity_Velia + item.StorageVeliaQuantity_Iliya
                    + item.StorageVeliaQuantity_Epheria + item.StorageVeliaQuantity_Ancado), StringComparer.Ordinal);

            foreach (Barter barter in App.myPVM.BarterCollection.Where(barter => barter.ExchangeQuantity > 0)) {
                string item1Id = barter.Item1?.ItemID ?? string.Empty;
                string item2Id = barter.Item2?.ItemID ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(item1Id)) {
                    totals.TryGetValue(item1Id, out long current);
                    totals[item1Id] = current - (long)barter.ExchangeQuantity * barter.Item1Number;
                }
                if (!string.IsNullOrWhiteSpace(item2Id) && item2Id != "10") {
                    totals.TryGetValue(item2Id, out long current);
                    totals[item2Id] = current + (long)barter.ExchangeQuantity * barter.Item2Number;
                }
            }

            foreach (Barter consumer in App.myPVM.BarterCollection) {
                string itemId = consumer.Item1?.ItemID ?? string.Empty;
                if (totals.TryGetValue(itemId, out long total)) {
                    consumer.InvQuantityChange = total > int.MaxValue
                        ? int.MaxValue
                        : total < int.MinValue ? int.MinValue : (int)total;
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
            MessageBoxResult result = MessageBox.Show(
                Localization.LanguageService.Instance.Localize("str.Msg.Planner.DoneConfirm"),
                Localization.LanguageService.Instance.Localize("str.Msg.Confirmation.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) {
                return;
            }

            if (App.myStorageVM == null || App.myStorageVM.StorageCollection == null || App.myPVM == null) {
                return;
            }

            int defaultWarehouse = App.myStorageManagement?.ComboBoxAdv_DefaultStorage.SelectedIndex ?? 1;
            var currentInventory = App.myStorageVM.StorageCollection.Select(item =>
                new PlannerWarehouseInventory(
                    item.ItemID,
                    item.StorageVeliaQuantity_Velia,
                    item.StorageVeliaQuantity_Iliya,
                    item.StorageVeliaQuantity_Epheria,
                    item.StorageVeliaQuantity_Ancado)).ToArray();
            var exchanges = App.myPVM.BarterCollection
                .Where(barter => barter.ExchangeQuantity > 0)
                .Select((barter, index) => new PlannerInventoryExchange(
                    index.ToString(CultureInfo.InvariantCulture),
                    barter.Item1?.ItemID ?? string.Empty,
                    barter.Item1Number,
                    barter.Item2?.ItemID ?? string.Empty,
                    barter.Item2Number,
                    barter.ExchangeQuantity)).ToArray();

            var reconciliation = new PlannerInventoryReconciler().Reconcile(
                currentInventory,
                exchanges,
                defaultWarehouse,
                new HashSet<string>(StringComparer.Ordinal) { "10" });

            if (!reconciliation.Success) {
                PlannerInventoryError firstError = reconciliation.Errors.First();
                string itemName = App.myStorageVM.StorageCollection
                    .FirstOrDefault(item => item.ItemID == firstError.ItemId)?.ItemNameDisplay
                    ?? firstError.ItemId;
                string reason = firstError.Code switch {
                    "INSUFFICIENT_STOCK" => "仓库库存不足",
                    "MISSING_STORAGE_ITEM" => "仓库物品列表缺少该物品",
                    "DUPLICATE_ITEM" => "仓库物品重复",
                    _ => "库存数据无效"
                };
                App.myCFun.Log($"无法完成库存结算：{itemName}（{reason}）。库存和计划均未修改。", Brushes.Red);
                return;
            }

            using (App.myStorageVM.SuppressAutoSave(saveOnDispose: false)) {
                foreach (Items item in App.myStorageVM.StorageCollection) {
                    if (!reconciliation.Inventory.TryGetValue(item.ItemID, out PlannerWarehouseInventory? updated)) {
                        continue;
                    }
                    item.StorageVeliaQuantity_Velia = updated.Velia;
                    item.StorageVeliaQuantity_Iliya = updated.Iliya;
                    item.StorageVeliaQuantity_Epheria = updated.Epheria;
                    item.StorageVeliaQuantity_Ancado = updated.Ancado;
                }
            }

            if (!App.myStorageVM.TrySaveData()) {
                using (App.myStorageVM.SuppressAutoSave(saveOnDispose: false)) {
                    foreach (Items item in App.myStorageVM.StorageCollection) {
                        PlannerWarehouseInventory original = currentInventory.First(snapshot => snapshot.ItemId == item.ItemID);
                        item.StorageVeliaQuantity_Velia = original.Velia;
                        item.StorageVeliaQuantity_Iliya = original.Iliya;
                        item.StorageVeliaQuantity_Epheria = original.Epheria;
                        item.StorageVeliaQuantity_Ancado = original.Ancado;
                    }
                }
                App.myCFun.Log("仓库数据保存失败，库存和计划均未修改。", Brushes.Red);
                return;
            }

            foreach (Barter barter in App.myPVM.BarterCollection) {
                if (reconciliation.Inventory.TryGetValue(barter.Item1?.ItemID ?? string.Empty, out PlannerWarehouseInventory? updated)) {
                    barter.InvQuantityChange = updated.Total;
                }
            }

            App.myRouteCoordinator?.Invalidate("planner-done");
            App.listBarterPlanner.Clear();
            DataGrid_Planner.BeginInit();
            App.myPVM.BarterCollection.Clear();
            DataGrid_Planner.EndInit();
            SaveData();
        }



        private void ButtonAdv_New_Click(object sender, RoutedEventArgs e) {
            App.myRouteCoordinator?.Invalidate("planner-new");
            DataGrid_Planner.BeginInit();
            if (App.myPVM != null) {
                App.myPVM.BarterCollection.Clear();
            }

            DataGrid_Planner.SortColumnDescriptions.Clear();
            DataGrid_Planner.EndInit();
        }

        private void ButtonAdv_Clean_Click(object sender, RoutedEventArgs e) {
            MessageBoxResult result = MessageBox.Show(
                Localization.LanguageService.Instance.Localize("str.Msg.Planner.CleanConfirm"),
                Localization.LanguageService.Instance.Localize("str.Msg.Confirmation.Title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) {
                App.myRouteCoordinator?.Invalidate("planner-clean");
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

        // Phase 6 (i18n) / Task 6: Auto Plan click handler. Builds snapshots from the
        // live collection without mutating it, delegates to the immutable planner via
        // the adapter, and applies the result in one BeginInit/EndInit batch so the
        // grid only re-renders once. On planner failure (ApplySet is null) the
        // multipliers stay byte-for-byte unchanged — the failure path never enters
        // BeginInit and never writes a multiplier.
        private async void ButtonAdv_AutoPlan_Click(object sender, RoutedEventArgs e) {
            var svc = Localization.LanguageService.Instance;
            ButtonAdv_AutoPlan.IsEnabled = false;
            try {

            // End any in-progress edit so the just-typed value makes it into the
            // snapshot (otherwise we'd plan against a stale ExchangeQuantity).
            DataGrid_Planner.SelectionController?.CurrentCellManager?.EndEdit();

            // ---- Plan-input / search phase: any failure here is a
            // legitimate "invalid input" or "search budget exhausted"
            // and should be logged with that code. The catch below
            // also catches map-render and save errors, which we
            // classify explicitly instead of bundling them under
            // InvalidInput.  See the four catch arms at the end of
            // this method.

            if (App.myPVM?.BarterCollection == null || App.myPVM.BarterCollection.Count == 0) {
                App.myCFun.Log(svc.Localize("str.Msg.Planner.AutoPlan.NoRows"), Brushes.Orange);
                return;
            }

            var liveRows = App.myPVM.BarterCollection.ToList();

            var snapshots = new List<PlannerRowSnapshot>(liveRows.Count);
            for (int i = 0; i < liveRows.Count; i++) {
                var b = liveRows[i];
                if (b == null || b.Item1 == null || b.Item2 == null) {
                    App.myCFun.Log(svc.Localize("str.Msg.Planner.AutoPlan.Invalid", "row " + i), Brushes.Red);
                    return;
                }

                if (!int.TryParse(b.Item1.ItemLV, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lv1)
                    || !int.TryParse(b.Item2.ItemLV, NumberStyles.Integer, CultureInfo.InvariantCulture, out lv1)
                    && b.Item2.ItemLV != "-1") {
                    // Item2 may legally be "-1" for terminal routes; everything else
                    // must parse as an int or we cannot index the planner's level
                    // rankers correctly.
                    if (b.Item2.ItemLV != "-1") {
                        App.myCFun.Log(svc.Localize("str.Msg.Planner.AutoPlan.Invalid", "row " + i), Brushes.Red);
                        return;
                    }
                    lv1 = -1;
                }

                bool producesCrowCoin = b.Item2.ItemID == AutoPlanningRoute.CrowCoinItemId;
                int parley = GetEffectiveParley(b);

                // AutoPlanningRoute's Item1Id/Item2Id and the inventory dictionary MUST
                // use the same key field, otherwise every item appears to have 0 stock
                // and the planner silently falls back to "no feasible candidate".
                // Phase 6 review caught this — ItemID is the canonical, locale-
                // independent key both sides agree on.
                var route = new AutoPlanningRoute(
                    RowId: b.PlannerRowId,
                    Group: b.BarterGroup,
                    Item1Id: b.Item1.ItemID,
                    Item1Level: int.TryParse(b.Item1.ItemLV, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLv1) ? parsedLv1 : 0,
                    Item1Number: b.Item1Number,
                    Item2Id: b.Item2.ItemID,
                    Item2Level: int.TryParse(b.Item2.ItemLV, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLv2) ? parsedLv2 : -1,
                    Item2Number: b.Item2Number,
                    ProducesCrowCoin: producesCrowCoin,
                    Parley: parley,
                    Remaining: b.IslandRemaining);

                snapshots.Add(new PlannerRowSnapshot(
                    RowId: route.RowId,
                    ExchangeDone: b.ExchangeDone,
                    ExistingMultiplier: b.ExchangeQuantity,
                    Route: route));
            }

            // Inventory: combine the four storage cities per item, keyed by ItemID so it
            // matches the route's Item1Id/Item2Id. Mixing keys here (e.g. ItemName)
            // makes every item appear to have 0 stock — the planner can't tell that
            // "Crow Coin" in storage corresponds to ItemID "1" in routes, and silently
            // fails every reverse-supply chain.
            var inventory = new Dictionary<string, int>(StringComparer.Ordinal);
            if (App.myStorageVM?.StorageCollection != null) {
                foreach (var item in App.myStorageVM.StorageCollection) {
                    if (item == null || string.IsNullOrEmpty(item.ItemID)) continue;
                    inventory[item.ItemID] = item.StorageVeliaQuantity_Velia
                                           + item.StorageVeliaQuantity_Iliya
                                           + item.StorageVeliaQuantity_Epheria
                                           + item.StorageVeliaQuantity_Ancado;
                }
            }

            var strategyTag = (ComboBoxAdv_AutoPlanningStrategy?.SelectedItem as FrameworkElement)?.Tag as string
                ?? "ProfitFirst";
            if (!Enum.TryParse<AutoPlanningStrategy>(strategyTag, out var strategy)) {
                strategy = AutoPlanningStrategy.ProfitFirst;
            }

            int lv5Target = ComboBox_LV5Max != null && ComboBox_LV5Max.SelectedIndex >= 0
                ? ComboBox_LV5Max.SelectedIndex : 0;
            int lv6Target = ComboBox_LV6Max != null && ComboBox_LV6Max.SelectedIndex >= 0
                ? ComboBox_LV6Max.SelectedIndex : 0;

            int extraLT = Convert.ToInt32(Math.Round(
                App.myCargoProperty.ExtraLT, MidpointRounding.AwayFromZero));
            int totalLT = Convert.ToInt32(Math.Round(
                App.myCargoProperty.TotalLT, MidpointRounding.AwayFromZero));
            var adapter = new PlannerAutoPlanningAdapter();
            var calculation = adapter.Calculate(
                snapshots, inventory, strategy, lv5Target, lv6Target, 1_000_000);

            if (calculation.ApplySet is null) {
                var diag = calculation.Diagnostics.FirstOrDefault();
                string code = diag?.Code ?? "unknown";
                string arg = diag?.RowId ?? code;
                string key = code switch {
                    "reserve-no-producer"     => "str.Msg.Planner.AutoPlan.ReserveNoProducer",
                    "reserve-budget-exceeded" => "str.Msg.Planner.AutoPlan.ReserveBudgetExceeded",
                    "reserve-unreachable"     => "str.Msg.Planner.AutoPlan.ReserveUnreachable",
                    _                         => "str.Msg.Planner.AutoPlan.Invalid",
                };
                App.myCFun.Log(svc.Localize(key, arg), Brushes.Red);
                return;
            }
            bool manualSelection = strategy == AutoPlanningStrategy.ManualSelection;
            if (manualSelection && !liveRows.Any(row =>
                !row.ExchangeDone && row.ExchangeQuantity > 0)) {
                App.myCFun.Log(
                    svc.Localize("str.Msg.Planner.AutoPlan.ManualNoSelection"),
                    Brushes.Orange);
                return;
            }

            var routeRows = liveRows.Select(b => new PlannerRouteSnapshot(
                RowId: b.PlannerRowId,
                ExchangeDone: b.ExchangeDone,
                ExchangeQuantity: calculation.ApplySet.Multipliers.GetValueOrDefault(
                    b.PlannerRowId, b.ExchangeQuantity),
                IslandId: b.IsLandName,
                Item1Id: b.Item1.ItemID,
                Item1DisplayName: b.Item1NameDisplay,
                Item1Level: int.TryParse(b.Item1.ItemLV, NumberStyles.Integer, CultureInfo.InvariantCulture, out int item1Level) ? item1Level : 0,
                Item1Number: b.Item1Number,
                Item2Id: b.Item2.ItemID,
                Item2DisplayName: b.Item2NameDisplay,
                Item2Level: int.TryParse(b.Item2.ItemLV, NumberStyles.Integer, CultureInfo.InvariantCulture, out int item2Level) ? item2Level : 0,
                Item2Number: b.Item2Number)).ToArray();
            var storageRows = App.myStorageVM.StorageCollection.Select(item => new StorageItemSnapshot(
                item.ItemID,
                int.TryParse(item.ItemLV, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) ? level : 0,
                item.StorageVeliaQuantity_Velia,
                item.StorageVeliaQuantity_Iliya,
                item.StorageVeliaQuantity_Epheria,
                item.StorageVeliaQuantity_Ancado)).ToArray();
            var islandRows = App.listIslands.Where(island => island.HasNavigationCoordinates)
                .Select(island => new IslandRouteSnapshot(
                    island.IslandsName,
                    new RoutePoint(island.NavigationX!.Value, island.NavigationY!.Value)))
                .ToArray();
            var cargo = new CargoCapacitySnapshot(extraLT, totalLT);
            var profile = ResolveSelectedOptimizationProfile();
            var request = AutomaticRoutePlanningAdapter.BuildRequest(
                routeRows, storageRows, islandRows, cargo,
                new RouteSearchLimits(250_000, profile.MaxLocalEvaluations), profile);
            App.myCFun.Log(svc.Localize("str.Log.AutoRoute.Solving",
                svc.Localize(OptimModeLocalizationKey(profile.Mode))), Brushes.SteelBlue);
            var routePlan = await App.myRouteCoordinator.CalculateAsync(request, profile);
            if (routePlan.Status is not (RoutePlanStatus.Optimal or RoutePlanStatus.BestKnownWithinLimit)) {
                switch (routePlan.Status) {
                    case RoutePlanStatus.Infeasible:
                        App.myCFun.Log(svc.Localize("str.Log.AutoRoute.Infeasible",
                            FormatRouteDiagnostic(routePlan.Diagnostics.FirstOrDefault(), request, svc)), Brushes.Red);
                        break;
                    case RoutePlanStatus.NoFeasibleSolutionWithinLimit:
                        App.myCFun.Log(svc.Localize("str.Log.AutoRoute.NoFeasibleWithinLimit"), Brushes.OrangeRed);
                        break;
                    case RoutePlanStatus.Cancelled:
                        App.myCFun.Log(svc.Localize("str.Log.AutoRoute.Cancelled"), Brushes.Gray);
                        break;
                    default:
                        App.myCFun.Log(svc.Localize("str.Log.AutoRoute.InvalidInput",
                            FormatRouteDiagnostic(routePlan.Diagnostics.FirstOrDefault(), request, svc)), Brushes.Red);
                        break;
                }
                return;
            }

            // Commit the Planner multipliers only after route calculation and replay
            // verification succeeded. A failed new attempt therefore leaves the last
            // saved Planner + automatic route pair intact and restorable.
            if (!manualSelection) {
                DataGrid_Planner.BeginInit();
                try {
                    // Audit round 3: the planner's Multipliers dictionary
                    // is keyed by Barter.PlannerRowId (persistent), not
                    // by collection index. Look each row up by its stable
                    // id so duplicate tuples get their own multiplier.
                    foreach (var b in liveRows) {
                        if (calculation.ApplySet.Multipliers.TryGetValue(b.PlannerRowId, out int multiplier))
                            b.ExchangeQuantity = multiplier;
                    }
                }
                finally {
                    DataGrid_Planner.EndInit();
                }
            }

            if (!App.myRouteCoordinator.PublishGeneratedPlan(request, routePlan, profile.Mode))
                throw new InvalidOperationException("The generated route failed commit verification.");

            UpdateInvChange(-1);
            UpdateParley();
            SaveData();
            UpdateMapControl();
            App.myfmMain?.myShipCargo?.UpdateCurrentLV();

            int selectedRoutes = liveRows.Select((row, index) => new {
                    Row = row,
                    Multiplier = calculation.ApplySet.Multipliers.GetValueOrDefault(
                        row.PlannerRowId),
                })
                .Count(item => !item.Row.ExchangeDone && item.Multiplier > 0);
            string strategyDisplay = svc.Localize(strategy switch {
                AutoPlanningStrategy.CrowCoinFirst => "str.Planner.AutoPlan.CrowCoinFirst",
                AutoPlanningStrategy.RestockFirst => "str.Planner.AutoPlan.RestockFirst",
                AutoPlanningStrategy.ManualSelection => "str.Planner.AutoPlan.ManualSelection",
                _ => "str.Planner.AutoPlan.ProfitFirst",
            });
            App.myCFun.Log(svc.Localize(
                "str.Msg.Planner.AutoPlan.Success",
                strategyDisplay,
                calculation.UsedParley.ToString("N0", CultureInfo.InvariantCulture),
                selectedRoutes.ToString(CultureInfo.InvariantCulture)),
                selectedRoutes > 0 ? Brushes.DarkOliveGreen : Brushes.Orange);
            switch (routePlan.Status) {
                case RoutePlanStatus.Optimal:
                    App.myCFun.Log(svc.Localize("str.Log.AutoRoute.Optimal",
                        routePlan.Routes.Count, routePlan.Objective?.TotalDistance ?? 0), Brushes.DarkOliveGreen);
                    break;
                case RoutePlanStatus.BestKnownWithinLimit:
                    // The new anytime mode publishes the selected profile, the
                    // actual stop reason from the beam, and a "not proven
                    // globally optimal" disclaimer in the same message so the
                    // user understands the difference between BestKnown and
                    // Optimal without needing to read the source.
                    var modeName = svc.Localize(OptimModeLocalizationKey(profile.Mode));
                    var stopReason = routePlan.Diagnostics.FirstOrDefault()?.Detail ?? "";
                    App.myCFun.Log(svc.Localize("str.Log.AutoRoute.BestKnown",
                        modeName,
                        routePlan.Routes.Count,
                        routePlan.Objective?.TotalDistance ?? 0,
                        stopReason,
                        svc.Localize("str.Log.AutoRoute.NotOptimal")), Brushes.Orange);
                    break;
            }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidOperationException
                || exception is NullReferenceException) {
                // Audit round 7: the failure mode is "planner input
                // invalid / search budget exhausted" — the route
                // never reached the publish / save / map-render
                // phases.  Log with the dedicated InvalidInput
                // resource key so the user knows the cause is in
                // their own data, not in a downstream system.
                App.myCFun.Log(svc.Localize("str.Log.AutoRoute.InvalidInput", exception.Message), Brushes.Red);
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException) {
                // The plan was generated and verified; only the
                // save back to disk failed (locked file, missing
                // perms, etc.).  Don't blame the user's input.
                App.myCFun.Log(svc.Localize("str.Log.AutoRoute.SaveFailed", exception.Message), Brushes.Red);
            }
            catch (Exception exception) {
                // Map-render, fingerprint-compute, or any other
                // post-publish exception.  The plan and the
                // automatic-route-plan.json are already in memory
                // and on disk respectively, so the user keeps
                // their work even if the map UI failed to refresh.
                App.myCFun.Log(svc.Localize("str.Log.AutoRoute.MapRenderFailed", exception.Message), Brushes.OrangeRed);
            }
            finally {
                ButtonAdv_AutoPlan.IsEnabled = true;
            }
        }
    }
}
