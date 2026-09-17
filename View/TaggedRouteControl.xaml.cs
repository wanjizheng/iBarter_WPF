using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using iBarter.Localization;
using iBarter.Routing;

namespace iBarter.View;

public partial class TaggedRouteControl : UserControl {
    private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "tagged-transport-settings.json");
    private static string SessionPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "tagged-transport-session.json");
    public TaggedTransportSettings Settings { get; private set; } = new();
    public TaggedTransportSession? Session { get; private set; }
    private TaggedTransportSession? invalidatedSeed;
    private bool displayCleared;
    private bool restoreFailed;
    public bool IsDisplayCleared => displayCleared;
    public bool IsEnabledMode => Settings.Enabled;
    public int? SelectedRouteNumber => selectedRoute;
    public bool ShowAllRoutes => selectedRoute is null;
    public (int Route, string From, string To)? FocusedSegment => focusedSegment;
    public RoutePlan? DisplayPlan => displayCleared || Session is null || Session.MapPlanCompleted ? null : cachedDisplaySession == Session ? cachedDisplayPlan : CacheDisplayPlan();
    private TaggedTransportSession? cachedDisplaySession;
    private RoutePlan? cachedDisplayPlan;
    private RoutePlan CacheDisplayPlan() { cachedDisplaySession = Session; return cachedDisplayPlan = TaggedRoutePresentation.Build(Session!); }
    public bool HasUnsettledExecution => Session is { Settled: false }
        && (Session.CompletedSteps > 0 || Session.SettlementBaseline is not null);
    private CancellationTokenSource? cancellation;
    private TaggedTransportSession? cachedSession;
    private TaggedTransportState? cachedCurrent;
    private bool initializing = true;
    private bool isSearching;
    private bool workspaceLoaded;
    private bool externalSearchFeedback;
    private int? selectedRoute;
    private int? selectedStep;
    private bool updatingRoutes;
    private bool updatingGuides;
    private TaggedTransportRoute[] routes = [];
    private (int Route, string From, string To)? focusedSegment;
    public event EventHandler? DisplayChanged;
    internal static string L(string en, string zh) => LanguageService.Instance.Current == AppLanguage.English ? en : ChineseTextNormalizer.ToSimplifiedChinese(zh);
    private TaggedTransportState Current() {
        if (!ReferenceEquals(cachedSession, Session)) {
            cachedCurrent = Session!.Current(); cachedSession = Session;
        }
        return cachedCurrent!;
    }

    public bool ValidateRestoredWorkspace() {
        workspaceLoaded = true;
        if (Session is null) return false;
        var original = Session.WorkspaceInputs ?? Session.SettlementBaseline ?? Session.Request;
        var differences = new List<string>();
        bool valid = App.myCargoProperty is not null && original.ShipLimitLT == App.myCargoProperty.TotalLT
            && original.ShipOccupiedLT == App.myCargoProperty.ExtraLT;
        if (!valid) differences.Add(L("ship capacity / occupied weight", "船舶載重／已佔負重"));
        // The optimizer may choose a different departure warehouse; compare a
        // chosen start only when the user pinned it or supplied initial cargo.
        string? loadingStart = Session.Request.Settings.InitialCargo.Length == 0
            && Session.Plan.Steps.FirstOrDefault()?.Action is { Kind: TaggedActionKind.Transfer or TaggedActionKind.StackAtWarehouse } first
            && TaggedTransportSimulator.IsWarehouse(first.From)
            && first.Location == Session.Request.Settings.StartIsland ? first.Location : null;
        string TransportInputs(TaggedTransportSettings s) => System.Text.Json.JsonSerializer.Serialize(new {
            s.ActiveCharacter, s.HomeWarehouseId,
            // Pinning the very warehouse where an empty voyage already starts
            // adds no travel. Compare that effective start, not the UI checkbox.
            Start = s.StartFromSelectedLocation || s.InitialCargo.Length > 0 ? s.StartIsland : loadingStart ?? "",
            Carriers = s.Carriers.OrderBy(x => x.Id).Select(x => new { x.Id, x.LimitLT, x.OccupiedLT, x.Slots }),
            Ports = s.Ports.Where(x => x.Enabled).OrderBy(x => x.IslandId).Select(x => new { x.IslandId, x.WarehouseId }),
            Cargo = s.InitialCargo.OrderBy(x => x.Container).ThenBy(x => x.ItemId),
            s.ShipSlots, s.AllowOverloadedSailing, s.BarterOutputRatio, s.CharacterReceiveRatio
        });
        bool settingsMatch = TransportInputs(original.Settings) == TransportInputs(Settings);
        if (!settingsMatch) differences.Add(L("TAG departure / cargo / carrier / port settings", "TAG 出發點／攜貨／角色／碼頭設定"));
        valid &= settingsMatch;
        if (!Session.Settled && !Session.MapPlanCompleted && App.myPVM is not null)
            if (!App.myPVM.BarterCollection.Where(x => !x.ExchangeDone && x.ExchangeQuantity > 0)
                .Select(x => x.PlannerRowId).ToHashSet().SetEquals(original.Trades.Select(x => x.RowId))) {
                valid = false; differences.Add(L("selected exchanges", "交換清單"));
            }
        foreach (var t in original.Trades) {
            var row = App.myPVM?.BarterCollection.FirstOrDefault(b => b.PlannerRowId == t.RowId);
            bool rowMatches = row is not null && row.IsLandName == t.IslandId && row.Item1.ItemID == t.InputId && row.Item2.ItemID == t.OutputId
                && row.Item1Number == t.InputPerExchange && row.Item2Number == t.OutputPerExchange && row.ExchangeQuantity == t.Exchanges
                && row.ExchangeDone == (Session.Settled || Session.MapPlanCompleted);
            if (!rowMatches) differences.Add(Island(t.IslandId) + L(" exchange quantity / completion", "交換數量／完成狀態"));
            valid &= rowMatches;
        }
        if (!Session.Settled) foreach (var wh in original.Warehouses) foreach (var item in wh.Value) {
            if (original.Items[item.Key].Level == 0) continue; // user-supplied land materials
            var stock = App.myStorageVM?.StorageCollection.FirstOrDefault(x => x.ItemID == item.Key);
            bool stockMatches = stock is not null && ReadStock(stock, wh.Key) == item.Value;
            if (!stockMatches) differences.Add(Island(wh.Key) + " · " + Item(item.Key) + L(" stock", "庫存"));
            valid &= stockMatches;
        }
        if (valid) {
            if (original.Settings.StartFromSelectedLocation != Settings.StartFromSelectedLocation
                || original.Settings.StartIsland != Settings.StartIsland) {
                // Record the equivalent UI preference separately from the actual
                // execution start. Later map completion must retain this baseline,
                // even after the ship has sailed and now contains exchanged cargo.
                Session = Session with { WorkspaceInputs = original with { Settings = original.Settings with {
                    StartFromSelectedLocation = Settings.StartFromSelectedLocation, StartIsland = Settings.StartIsland } } };
                try { TaggedTransportStorage.Save(SessionPath, Session); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                    App.myCFun?.Log(L("Could not save the restored departure snapshot: ", "無法儲存已還原的出發設定快照：") + error.Message,
                        System.Windows.Media.Brushes.OrangeRed);
                }
            }
            return true;
        }
        invalidatedSeed = Session;
        Session = null; routes = []; selectedRoute = null; selectedStep = null; focusedSegment = null;
        Refresh();
        StatusText.Text = L("Saved TAG route no longer matches Planner, inventory, ship or TAG settings. Generate a new route.", "已保存的 TAG 路線與目前交換、庫存、船重或 TAG 設定不符，已清除顯示，請重新規劃。");
        StatusText.Text += " " + L("Changed: ", "不一致項目：") + string.Join("、", differences.Distinct().Take(5));
        App.myCFun?.Log(StatusText.Text, System.Windows.Media.Brushes.OrangeRed);
        DisplayChanged?.Invoke(this, EventArgs.Empty);
        return false;
    }

    public TaggedRouteControl() {
        InitializeComponent();
        if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this)) return;
        try {
            Settings = TaggedTransportStorage.Load<TaggedTransportSettings>(SettingsPath) ?? new();
            Session = TaggedTransportStorage.Load<TaggedTransportSession>(SessionPath);
            selectedRoute = Session?.SelectedRouteNumber;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException) {
            restoreFailed = true;
            StatusText.Text = L("Could not restore TAG settings / route: ", "無法還原 TAG 設定／路線：") + e.Message;
            App.myCFun?.Log(StatusText.Text, System.Windows.Media.Brushes.OrangeRed);
        }
        EnabledBox.IsChecked = Settings.Enabled;
        string beforeMigration = System.Text.Json.JsonSerializer.Serialize(Settings);
        TaggedPortCatalog.Merge(Settings);
        if (Settings.HomeWarehouseId is null && App.listIslands?.Any(i => i.IslandsName == "Iliya") == true)
            Settings.HomeWarehouseId = "Iliya";
        initializing = false;
        LanguageService.Instance.LanguageChanged += (_, _) => Refresh();
        Loaded += (_, _) => {
            // Persisted settings own the switch, not a restored visual/layout value.
            initializing = true; EnabledBox.IsChecked = Settings.Enabled; initializing = false;
            Refresh();
        };
        Refresh();
        if (beforeMigration != System.Text.Json.JsonSerializer.Serialize(Settings)) TrySaveSettings();
    }

    private void Enabled_Changed(object sender, RoutedEventArgs e) {
        if (initializing) return;
        cancellation?.Cancel();
        bool previous = Settings.Enabled;
        Settings.Enabled = EnabledBox.IsChecked == true;
        try { TaggedTransportStorage.Save(SettingsPath, Settings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            Settings.Enabled = previous;
            initializing = true; EnabledBox.IsChecked = previous; initializing = false;
            StatusText.Text = L("Could not save TAG switch: ", "無法儲存 TAG 開關：") + error.Message;
            App.myCFun?.Log(StatusText.Text, System.Windows.Media.Brushes.OrangeRed);
            return;
        }
        if (Settings.Enabled && workspaceLoaded) ValidateRestoredWorkspace();
        Refresh(); DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) {
        var dialog = new TaggedSettingsWindow(Settings) { Owner = Window.GetWindow(this) };
        dialog.SettingsChanged += updated => {
            TaggedTransportStorage.Save(SettingsPath, updated);
            cancellation?.Cancel();
            Settings = updated;
        };
        dialog.ShowDialog();
        Refresh();
        if (Session is not null) StatusText.Text = L("Settings saved. Existing route keeps its original assumptions; generate a new route to apply changes.",
            "設定已儲存。現有路線保留原始參數；重新產生路線才會套用新設定。");
    }

    private void TrySaveSettings() {
        try { TaggedTransportStorage.Save(SettingsPath, Settings); }
        catch (IOException e) { StatusText.Text = e.Message; }
    }

    public void ClearRouteDisplayForPlanning() {
        // Keep the verified seed and recorded cargo for continuation, but remove
        // every old route from the UI until a new verified plan is published.
        displayCleared = true;
        selectedStep = null; focusedSegment = null;
        Refresh(); DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<TaggedTransportResult> GenerateAsync(TaggedTransportRequest request, bool remainingOnly = false, Func<bool>? stillCurrent = null,
        RouteOptimizationProfile? profile = null, Action<TaggedSearchProgress>? searchProgress = null) {
        TaggedTransportRequest? workspaceInputs = null;
        if (!remainingOnly && Session is { MapPlanCompleted: false, WorkspaceInputs: { } recorded }
            && System.Text.Json.JsonSerializer.Serialize(Settings.InitialCargo) == System.Text.Json.JsonSerializer.Serialize(recorded.Settings.InitialCargo)
            && Settings.StartFromSelectedLocation == recorded.Settings.StartFromSelectedLocation
            && (!Settings.StartFromSelectedLocation || Settings.StartIsland == recorded.Settings.StartIsland)) {
            workspaceInputs = request;
            var actual = Session.Request;
            request = request with {
                Settings = request.Settings with { StartIsland = actual.Settings.StartIsland, StartFromSelectedLocation = true,
                    ActiveCharacter = actual.Settings.ActiveCharacter, InitialCargo = actual.Settings.InitialCargo },
                Warehouses = request.Warehouses.ToDictionary(w => w.Key, w => w.Value.ToDictionary(i => i.Key, i =>
                    recorded.Warehouses.GetValueOrDefault(w.Key)?.GetValueOrDefault(i.Key) == i.Value
                        ? actual.Warehouses.GetValueOrDefault(w.Key)?.GetValueOrDefault(i.Key) ?? i.Value : i.Value))
            };
        }
        var baseline = remainingOnly ? Session?.SettlementBaseline ?? Session?.Request : null;
        string settingsVersion = System.Text.Json.JsonSerializer.Serialize(Settings);
        cancellation?.Cancel(); cancellation?.Dispose();
        var own = cancellation = new();
        var preferredShipPlan = App.myRouteCoordinator?.CurrentPlan;
        var preferredTaggedPlan = !remainingOnly ? ReusableSeed(Session ?? invalidatedSeed, request) : null;
        profile ??= App.myfmMain?.myPlannerControl?.ResolveSelectedOptimizationProfileSafe();
        if (profile is not null) request = request with { Settings = request.Settings with { SearchProfile = profile } };
        bool running = true;
        isSearching = true;
        externalSearchFeedback = searchProgress is not null;
        var updates = new Progress<TaggedSearchProgress>(p => {
            if (!running || own != cancellation) return;
            if (searchProgress is not null) { searchProgress(p); return; }
            StatusText.Text = L("Searching", "正在搜尋") + $" {p.ElapsedSeconds:N0}/{p.BudgetSeconds:N0} " + L("seconds", "秒")
                + $"\n{p.ExpandedStates:N0} " + L("search candidates", "個搜尋候選")
                + (p.BestDistance is double distance ? " · " + Distance(distance) : "");
        });
        CancelButton.Visibility = ActionsPanel.Visibility = externalSearchFeedback ? Visibility.Collapsed : Visibility.Visible;
        NextButton.IsEnabled = UndoButton.IsEnabled = ReplanButton.IsEnabled = SettingsButton.IsEnabled = false;
        SettleButton.IsEnabled = false;
        StatusText.Text = externalSearchFeedback ? L("Search progress is shown below the toolbar.", "搜尋進度顯示在工具欄下方。")
            : L("Optimizing total sailing distance with TAG cargo…", "正在利用 TAG 載貨能力最佳化總航行距離…");
        try {
            var result = await Task.Run(() => new TaggedTransportPlanner().Plan(request, own.Token, preferredShipPlan,
                p => ((IProgress<TaggedSearchProgress>)updates).Report(p), preferredTaggedPlan: preferredTaggedPlan));
            running = false;
            if (own != cancellation) return new(null, L("Superseded by a newer search.", "已開始新的搜尋。"));
            if (!Settings.Enabled || own.IsCancellationRequested && result.Plan is null) return new(null, L("Cancelled.", "已取消。"));
            if (settingsVersion != System.Text.Json.JsonSerializer.Serialize(Settings) || stillCurrent?.Invoke() == false)
                return new(null, L("Inputs changed during search. Generate again.", "搜尋期間資料已變更，請重新產生路線。"));
            if (result.Plan is not null) {
                var session = new TaggedTransportSession(1, result.PlannedRequest ?? request, result.Plan, 0, baseline,
                    WorkspaceInputs: workspaceInputs, SelectedRouteNumber: RetainedRouteSelection(result.PlannedRequest ?? request, result.Plan));
                TaggedTransportStorage.Save(SessionPath, session);
                Session = session;
                displayCleared = false;
                selectedStep = null; focusedSegment = null;
                Refresh(); DisplayChanged?.Invoke(this, EventArgs.Empty);
            }
            else StatusText.Text = result.Message;
            return result;
        }
        finally {
            running = false;
            if (own == cancellation) {
                isSearching = false;
                externalSearchFeedback = false;
                CancelButton.Visibility = ActionsPanel.Visibility = Visibility.Collapsed;
                SettingsButton.IsEnabled = true;
                UpdateButtons();
            }
        }
    }

    private static TaggedTransportPlan? ReusableSeed(TaggedTransportSession? saved, TaggedTransportRequest current) {
        if (saved is null || saved.CompletedSteps != 0 || saved.MapPlanCompleted || saved.Request.Trades.Length != current.Trades.Length) return null;
        var indices = saved.Request.Trades.Select(t => Array.FindIndex(current.Trades, n => n == t)).ToArray();
        if (indices.Any(i => i < 0)) return null;
        // This is only an ordering candidate. The planner reconstructs operations and
        // verifies current stock, cargo, capacities, ports and the home warehouse.
        return saved.Plan with { Steps = saved.Plan.Steps.Select(s => s.Action.Kind == TaggedActionKind.Barter
            ? s with { Action = s.Action with { TradeIndex = indices[s.Action.TradeIndex] } } : s).ToArray() };
    }

    private void Next_Click(object sender, RoutedEventArgs e) => MoveProgress(1);
    private void Undo_Click(object sender, RoutedEventArgs e) => MoveProgress(-1);
    private void Cancel_Click(object sender, RoutedEventArgs e) => cancellation?.Cancel();
    public void CancelSearch() => cancellation?.Cancel();
    private void Guides_Changed(object sender, RoutedEventArgs e) {
        if (!initializing && !updatingGuides) App.myRouteCoordinator?.SetShowRouteGuides(GuidesBox.IsChecked == true);
    }
    private void MoveProgress(int delta) {
        if (isSearching || Session is null || Session.Settled) return;
        int cursor = TaggedOperationGroups.MoveCursor(Session.Request, Session.Plan, Session.CompletedSteps, delta > 0);
        if (cursor < 0 || cursor > Session.Plan.Steps.Length) return;
        try {
            var updated = Session with { CompletedSteps = cursor };
            TaggedTransportStorage.Save(SessionPath, updated);
            Session = updated; Refresh(); DisplayChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException) { StatusText.Text = error.Message; }
    }
    private async void Replan_Click(object sender, RoutedEventArgs e) {
        if (Session is null) return;
        try {
            var remaining = Session.RemainingRequest();
            if (remaining.Trades.Length == 0) {
                StatusText.Text = L("Finish the remaining unloading steps before starting a new run.", "請先完成剩餘卸貨步驟，再開始新一趟。"); return;
            }
            await GenerateAsync(remaining, remainingOnly: true);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException) { StatusText.Text = error.Message; }
    }
    public RouteRenderSnapshot? RenderSnapshot() {
        if (!Settings.Enabled) return null;
        if (displayCleared || Session is null || Session.MapPlanCompleted) return new(false, true, []);
        var current = Current();
        var visible = routes.Where(r => (selectedRoute is null || r.Number == selectedRoute) && r.End > Session.CompletedSteps).ToArray();
        var future = visible.SelectMany(r => Session.Plan.Steps.Skip(Math.Max(r.Start, Session.CompletedSteps)).Take(r.End - Math.Max(r.Start, Session.CompletedSteps))).ToArray();
        var paths = visible.Select(r => {
            int start = Math.Max(r.Start, Session.CompletedSteps);
            var locations = new[] { start == Session.CompletedSteps ? current.Location : r.StartIsland }
                .Concat(Session.Plan.Steps.Skip(start).Take(r.End - start).Select(s => s.Action.Location)).ToArray();
            return new RouteRenderPath(r.Number, r.Number - 1, locations.Where((x, i) => i == 0 || x != locations[i - 1]).ToArray());
        }).ToArray();
        return new(false, selectedRoute is null, paths,
            future.Where(x => x.Action.Kind == TaggedActionKind.Barter).Select(x => x.Action.Location),
            future.Where(x => x.Action.Kind is TaggedActionKind.Transfer or TaggedActionKind.StackAtWarehouse or TaggedActionKind.Sell).Select(x => x.Action.Location));
    }
    private void RouteSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (updatingRoutes || RouteSelector.SelectedItem is not TaggedRouteOption option) return;
        selectedRoute = option.Number; selectedStep = null; focusedSegment = null;
        Refresh(); SaveRouteSelection(); DisplayChanged?.Invoke(this, EventArgs.Empty);
    }
    private int? RetainedRouteSelection(TaggedTransportRequest request, TaggedTransportPlan plan) => selectedRoute is int number
        ? TaggedTransportRoutes.Build(request, plan).OrderBy(r => Math.Abs(r.Number - number)).FirstOrDefault()?.Number : null;
    private void SaveRouteSelection() {
        if (Session is null || Session.SelectedRouteNumber == selectedRoute) return;
        try {
            var updated = Session with { SelectedRouteNumber = selectedRoute };
            TaggedTransportStorage.Save(SessionPath, updated); Session = updated;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StatusText.Text = error.Message; }
    }
    public bool FocusRouteSegment(int number, string from, string to) {
        if (Session is null) return false;
        var route = routes.FirstOrDefault(r => r.Number == number);
        if (route is null) return false;
        int index = Enumerable.Range(Math.Max(route.Start, Session.CompletedSteps), Math.Max(0, route.End - Math.Max(route.Start, Session.CompletedSteps)))
            .FirstOrDefault(i => Session.Plan.Steps[i].Action is { Kind: TaggedActionKind.Sail } a && a.From == from && a.Location == to, -1);
        if (index < 0) return false;
        index = Enumerable.Range(index, route.End - index).FirstOrDefault(i => Session.Plan.Steps[i].Action.Kind != TaggedActionKind.Sail,
            Enumerable.Range(route.Start, route.End - route.Start).LastOrDefault(i => Session.Plan.Steps[i].Action.Kind != TaggedActionKind.Sail, index));
        selectedRoute = number; selectedStep = index; focusedSegment = (number, from, to);
        Refresh(); SaveRouteSelection(); DisplayChanged?.Invoke(this, EventArgs.Empty); return true;
    }
    public bool IsFocusedSegment(int number, string from, string to) => focusedSegment == (number, from, to);
    public bool IsFocusedMarker(int number, string island) => focusedSegment is { } f && f.Route == number && (f.From == island || f.To == island);
    public void FocusSelectedStep() {
        var card = StepsList.Items.OfType<TaggedStepCard>().FirstOrDefault(x => selectedStep >= x.Index && selectedStep < x.End);
        if (card is null) return;
        StepsList.SelectedItem = card; StepsList.ScrollIntoView(card);
    }
    public async Task CompleteMapStep(int routeNumber, int stepIndex) {
        if (displayCleared || isSearching || Session is null || Session.Settled || Session.MapPlanCompleted) return;
        var route = routes.FirstOrDefault(r => r.Number == routeNumber);
        if (route is null) return;
        int index = route.Start + stepIndex;
        if (index < Session.CompletedSteps || index >= route.End || Session.Plan.Steps[index].Action.Kind != TaggedActionKind.Barter) return;
        var previous = Session;
        var action = previous.Plan.Steps[index].Action;
        var trade = previous.Request.Trades[action.TradeIndex];
        var row = App.myPVM?.BarterCollection.FirstOrDefault(r => r.PlannerRowId == trade.RowId);
        if (App.myfmMain is not null && (row is null || row.ExchangeDone || row.ExchangeQuantity != trade.Exchanges)) return;
        int oldQuantity = row?.ExchangeQuantity ?? 0; bool oldDone = row?.ExchangeDone ?? false;
        string Inputs() => System.Text.Json.JsonSerializer.Serialize(new {
            Settings, Rows = App.myPVM?.BarterCollection.Select(r => new { r.PlannerRowId, r.ExchangeQuantity, r.ExchangeDone,
                r.IsLandName, Input = r.Item1.ItemID, Output = r.Item2.ItemID, r.Item1Number, r.Item2Number }),
            Stock = App.myStorageVM?.StorageCollection.Select(i => new { i.ItemID, i.StorageVeliaQuantity_Velia,
                i.StorageVeliaQuantity_Iliya, i.StorageVeliaQuantity_Epheria, i.StorageVeliaQuantity_Ancado }),
            Limit = App.myCargoProperty?.TotalLT, Occupied = App.myCargoProperty?.ExtraLT });
        string inputVersion = Inputs();
        cancellation?.Cancel(); cancellation?.Dispose();
        var own = cancellation = new CancellationTokenSource();
        isSearching = true; SettingsButton.IsEnabled = false;
        CancelButton.Visibility = ActionsPanel.Visibility = Visibility.Visible;
        StatusText.Text = L("Updating the remaining exchanges…", "正在重新規劃其餘交換…");
        bool saved = false;
        try {
            var remaining = TaggedCompletionReplanner.AtCompletedExchange(previous, index);
            TaggedTransportSession updated;
            if (remaining.Trades.Length == 0) updated = previous with { MapPlanCompleted = true };
            else {
                var quick = remaining with { Settings = remaining.Settings with { SearchProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Quick) } };
                var result = await Task.Run(() => {
                    var seed = TaggedCompletionReplanner.Seed(previous, index, quick, own.Token);
                    // A map check-off is a local continuation, not a full Auto
                    // Plan run. Keep valid remaining operations and route order.
                    if (seed is not null && new TaggedTransportSimulator(quick).Verify(seed, out _, out _))
                        return new TaggedTransportResult(seed, "Remaining route verified.", quick);
                    return new TaggedTransportPlanner().Plan(quick, own.Token, preferredTaggedPlan: seed);
                });
                if (own.IsCancellationRequested || Session?.Plan != previous.Plan || Session.CompletedSteps != previous.CompletedSteps || inputVersion != Inputs()) return;
                if (result.Plan is null || result.PlannedRequest is null) {
                    StatusText.Text = L("Could not verify the remaining cargo and route. Nothing was marked complete. Check initial carried cargo / inventory, or run Auto Plan again.",
                        "無法驗證其餘貨物與路線，未勾選任何交換。請核對初始攜貨／庫存，或使用自動規劃重新搜尋。");
                    return;
                }
                updated = new TaggedTransportSession(1, result.PlannedRequest, result.Plan, 0,
                    WorkspaceInputs: (previous.WorkspaceInputs ?? previous.SettlementBaseline ?? previous.Request) with { Trades = remaining.Trades },
                    SelectedRouteNumber: RetainedRouteSelection(result.PlannedRequest, result.Plan));
            }
            if (own.IsCancellationRequested || Session?.Plan != previous.Plan || Session.CompletedSteps != previous.CompletedSteps || inputVersion != Inputs()) return;
            TaggedTransportStorage.Save(SessionPath, updated);
            saved = true;
            if (row is not null) {
                if (trade.Exchanges == action.Quantity) row.ExchangeDone = true;
                else row.ExchangeQuantity = trade.Exchanges - action.Quantity;
                if (App.myfmMain?.myPlannerControl is { } planner) {
                    planner.RefreshDerivedValuesAfterRouteProgress();
                    if (!planner.TrySaveData()) throw new IOException(L("Could not save exchange completion.", "無法儲存交換完成狀態。"));
                }
            }
            Session = updated; selectedStep = null; focusedSegment = null;
            Refresh(selectNextBarter: true, preferredRoute: routeNumber); DisplayChanged?.Invoke(this, EventArgs.Empty);
            App.myCFun?.Log(L("TAG remaining route distance: ", "TAG 剩餘路線距離：") + Distance(updated.MapPlanCompleted ? 0 : updated.Plan.Distance), System.Windows.Media.Brushes.DarkOliveGreen);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException) {
            if (row is not null) { row.ExchangeQuantity = oldQuantity; row.ExchangeDone = oldDone; }
            if (saved) try { TaggedTransportStorage.Save(SessionPath, previous); } catch (IOException) { }
            StatusText.Text = L("Exchange was not marked complete. Check the recorded carried cargo and warehouse stock. ",
                "未勾選完成，請核對初始攜貨和倉庫庫存。") + error.Message;
        }
        finally {
            if (own == cancellation) {
                isSearching = false; SettingsButton.IsEnabled = true;
                CancelButton.Visibility = ActionsPanel.Visibility = Visibility.Collapsed;
                if (own.IsCancellationRequested) StatusText.Text = L("Cancelled. Previous route retained.", "已取消，保留原路線。");
            }
        }
    }
    private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (updatingRoutes || StepsList.SelectedItem is not TaggedStepCard card || Session is null) return;
        SetStepFocus(card);
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }
    private void SetStepFocus(TaggedStepCard card) {
        selectedStep = card.Index;
        var route = routes.Single(r => r.Number == card.RouteNumber);
        var sail = Session!.Plan.Steps.Skip(route.Start).Take(card.Index - route.Start + 1).LastOrDefault(s => s.Action.Kind == TaggedActionKind.Sail)
            ?? Session.Plan.Steps.Skip(card.Index).Take(route.End - card.Index).FirstOrDefault(s => s.Action.Kind == TaggedActionKind.Sail);
        string location = Session.Plan.Steps[card.Index].Action.Location;
        focusedSegment = sail is null ? (card.RouteNumber, location, location) : (card.RouteNumber, sail.Action.From, sail.Action.Location);
    }
    private void Step_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CopyStepItem(sender, e, false);
    private void Step_MouseRightButtonDown(object sender, MouseButtonEventArgs e) => CopyStepItem(sender, e, true);
    private void CopyStepItem(object sender, MouseButtonEventArgs e, bool output) {
        if (e.ClickCount != 2 || sender is not ListBoxItem { Content: TaggedStepCard card } || card.RowId is null) return;
        ShipCargoControl.CopyAutomaticBarterItem(card.RowId, output);
        e.Handled = true;
    }
    private string Container(string id) {
        if (id == "ship") return L("Ship", "船舱");
        if (TaggedTransportSimulator.IsWarehouse(id)) return Island(id["warehouse:".Length..]) + L(" storage", " 倉庫");
        string name = Session?.Request.Settings.Carriers.FirstOrDefault(x => x.Id == id)?.Name ?? id;
        return name is "Main" or "main" ? L("Main", "主號") : name is "TAG" or "alt" ? L("TAG", "TAG 小號")
            : name == "Main elephant" ? L(name, "主號小象") : name == "TAG elephant" ? L(name, "TAG 小號小象") : name;
    }
    private string Item(string id) {
        string? name = App.listItems?.FirstOrDefault(x => x.ItemID == id)?.ItemNameDisplay;
        if (!string.IsNullOrWhiteSpace(name) && name != id) return name;
        name = Session!.Request.Items.TryGetValue(id, out var item) ? item.DisplayName : null;
        return !string.IsNullOrWhiteSpace(name) && name != id ? name : L("Unrecognized item", "未識別商品") + " (" + id + ")";
    }
    private static string Island(string id) => App.listIslands?.FirstOrDefault(x => x.IslandsName == id)?.IslandsNameDisplay ?? id;
    private static string Distance(double meters) => RouteDistanceDisplay.Tagged(meters);
    private void UpdateButtons() {
        NextButton.IsEnabled = !isSearching && Session is { Settled: false } && Session.CompletedSteps < Session.Plan.Steps.Length;
        UndoButton.IsEnabled = !isSearching && Session is { Settled: false, CompletedSteps: > 0 };
        ReplanButton.IsEnabled = !isSearching && Session is { Settled: false };
        SettleButton.IsEnabled = !isSearching && Session is { Settled: false } && Session.CompletedSteps == Session.Plan.Steps.Length;
    }
    private void Refresh(bool selectNextBarter = false, int? preferredRoute = null) {
        EnabledBox.Content = L("TAG-assisted barter", "啟用 TAG 輔助換貨");
        SettingsButton.Content = L("Settings", "設定");
        NextButton.Content = L("Confirm next action", "確認完成下一步");
        UndoButton.Content = L("Undo last step", "撤銷上一步");
        ReplanButton.Content = L("Replan remaining", "重算剩餘路線");
        SettleButton.Content = L("Finish & save inventory", "完成行程並更新庫存");
        UndoButton.ToolTip = L("Undo the last recorded action. This does not move cargo in the game.", "撤回上一個步驟紀錄；不會操作遊戲中的貨物。");
        ReplanButton.ToolTip = L("Continue planning from the cargo and location recorded by completed steps.", "途中想更改走法時，從已完成步驟記錄的位置及貨物重算未完成交換。");
        SettleButton.ToolTip = L("After all unloading is confirmed, save the final warehouse quantities and mark the exchanges complete in Planner.", "確認全部卸貨後，更新軟體的倉庫數量並勾選 Planner 交換完成。");
        ExecutionHelp.Text = L("Double-click a map exchange to complete only that exchange and update the remaining route. Use Auto Plan for a full search. Warehouse inventory stays under the existing inventory controls.",
            "雙擊地圖只完成選中的交換，並更新其餘路線；完整搜尋請使用自動規劃。裝卸提示不必逐步確認，倉庫沿用原有庫存管理。");
        CancelButton.Content = L("Stop & keep best", "停止並保留結果");
        GuidesBox.Content = L("Show route guides", "顯示路線引導");
        updatingGuides = true;
        GuidesBox.IsChecked = App.myRouteCoordinator?.ShowRouteGuides ?? true;
        updatingGuides = false;
        PlanPanel.Visibility = Settings.Enabled ? Visibility.Visible : Visibility.Collapsed;
        ActionsPanel.Visibility = isSearching && !externalSearchFeedback ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = UndoButton.Visibility = ReplanButton.Visibility = SettleButton.Visibility = Visibility.Collapsed;
        UpdateButtons();
        if (!Settings.Enabled) { StatusText.Text = L("Disabled — standard route planning is active.", "未啟用，目前使用原有路線規劃。"); return; }
        if (displayCleared || Session is null || Session.MapPlanCompleted) { StepsList.ItemsSource = null; RouteSelector.ItemsSource = null; RouteCountText.Text = "";
            PlanPanel.Visibility = ActionsPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = L("Set carrier weights and departure, then use Auto Plan. Initial cargo is actual carried cargo, separate from recorded warehouse stock.",
            "先設定角色負重與出發點，再按自動規劃。初始貨物指身上已有的貨物，與倉庫紀錄分開計算。");
            if (Session?.MapPlanCompleted == true) StatusText.Text = L("All exchanges are complete. Use Auto Plan to start a new plan.", "交換已全部完成。可使用自動規劃開始新方案。");
            if (Session is null && restoreFailed && !displayCleared)
                StatusText.Text = L("The saved TAG route failed validation. Generate a new route using the current cargo and capacity rules.",
                    "已保存的 TAG 路線未通過校驗，已清除顯示。請依目前貨物與載重限制重新自動規劃。");
            return; }
        var plan = Session.Plan;
        var operationGroups = TaggedOperationGroups.Build(Session.Request, plan);
        routes = TaggedTransportRoutes.Build(Session.Request, plan);
        if (selectedRoute is int selected && routes.All(r => r.Number != selected))
            selectedRoute = routes.OrderBy(r => Math.Abs(r.Number - selected)).FirstOrDefault()?.Number;
        var routeTitles = routes.ToDictionary(r => r.Number, r => L($"Route {r.Number}", $"路線 {r.Number}")
            + $" · {plan.Steps.Skip(r.Start).Take(r.End - r.Start).Count(s => s.Action.Kind == TaggedActionKind.Barter)} " + L("barter stops", "個交換步驟"));
        updatingRoutes = true;
        RouteCountText.Text = L($"{routes.Length} routes", $"共 {routes.Length} 條路線");
        RouteSelector.ItemsSource = new[] { new TaggedRouteOption(null, L("All routes", "全部路線")) }
            .Concat(routes.Select(r => new TaggedRouteOption(r.Number, routeTitles[r.Number]))).ToArray();
        RouteSelector.SelectedItem = RouteSelector.Items.OfType<TaggedRouteOption>().First(x => x.Number == selectedRoute);
        // Route distance and TAG participation are already written to the
        // log when planning completes.  Keep the narrow cargo pane focused
        // on route selection and actionable steps.
        StatusText.Text = "";
        var routeIndexes = new Dictionary<int, int>();
        var cards = operationGroups.Select(group => {
            int i = group.Start;
            var s = plan.Steps[i];
            var a = s.Action;
            string title = a.Kind switch {
                TaggedActionKind.Sail => L("Sail to ", "航行至 ") + Island(a.Location),
                TaggedActionKind.Switch => Island(a.Location) + " · " + L("Switch to ", "切換至 ") + Container(a.To),
                TaggedActionKind.SummonElephant => Island(a.Location) + " · " + L("Use whistle: summon ", "使用笛子召喚：") + Container(a.To),
                TaggedActionKind.Barter => L("Barter at ", "交換：") + Island(a.Location),
                TaggedActionKind.StackAtWarehouse => L("Prepare elephant stack at ", "在倉庫準備小象疊貨：") + Island(a.Location),
                _ => L("Transfer at ", "轉移：") + Island(a.Location)
            };
            string detail = "";
            if (a.Kind is TaggedActionKind.Transfer or TaggedActionKind.StackAtWarehouse)
                detail = $"{Container(a.From)} → {Container(a.To)}\n{Item(a.ItemId)} × {a.Quantity:N0}";
            if (a.Kind == TaggedActionKind.StackAtWarehouse)
                detail += L("\nRepeat the warehouse / character / elephant stacking workflow before continuing.",
                    "\n先完成倉庫／角色／小象往返疊貨再繼續。");
            if (a.Kind == TaggedActionKind.Barter) {
                var t = Session.Request.Trades[a.TradeIndex];
                detail = $"{Item(t.InputId)} × {t.InputPerExchange * a.Quantity} → {Item(t.OutputId)} × {t.OutputPerExchange * a.Quantity}";
            }
            if (a.Kind == TaggedActionKind.Sail) detail = Island(a.From) + " → " + Island(a.Location)
                + (s.ShipLT > Session.Request.ShipLimitLT ? L(" · overloaded / no sprint", " · 超重／無法衝刺") : L(" · normal sailing", " · 正常航行"));
            var route = routes.Single(r => i >= r.Start && i < r.End);
            var trade = a.Kind == TaggedActionKind.Barter ? Session.Request.Trades[a.TradeIndex] : null;
            var source = trade is null ? null : ShipCargoControl.ResolveAutomaticBarter(trade.RowId);
            if (source is not null && (source.Item1?.ItemID != trade!.InputId || source.Item2?.ItemID != trade.OutputId)) source = null;
            TaggedCargoLine[] lines = [];
            if (group.Kind != "operation") {
                title = (group.Kind == "selling" ? L("Sell at ", "賣出：") : group.Kind == "loading" ? L("Load at ", "裝貨：") : group.Kind == "unloading" ? L("Unload at ", "卸貨：") : group.Kind == "transfer" ? L("Transfer at ", "倒貨：") : L("Cargo handling at ", "裝卸貨：")) + Island(a.Location);
                detail = "";
                var combined = TaggedOperationGroups.Summarize(plan, group, Session.CompletedSteps);
                lines = combined.Select(entry => {
                    var action = entry.Action;
                    string text = action.Kind switch {
                        TaggedActionKind.Switch => L("Switch to ", "切換至 ") + Container(action.To),
                        TaggedActionKind.SummonElephant => L("Summon ", "召喚 ") + Container(action.To),
                        TaggedActionKind.Sell => L("Sell ", "賣出 ") + $"{Item(action.ItemId)} × {action.Quantity:N0}",
                        _ => $"{Container(action.From)} → {Container(action.To)}\n{Item(action.ItemId)} × {action.Quantity:N0}"
                    };
                    if (action.Kind == TaggedActionKind.StackAtWarehouse) text += L(" (storage stacking)", "（倉庫疊貨）");
                    string character = action.Kind == TaggedActionKind.Sell ? ""
                        : action.To is "main" or "alt" ? action.To : action.From is "main" or "alt" ? action.From : "";
                    return new TaggedCargoLine((entry.Done ? "✓ " : "") + text,
                        string.IsNullOrEmpty(action.ItemId) ? null : Icon(action.ItemId), character.Length == 0 ? "" : Container(character));
                }).ToArray();
            }
            var load = plan.Steps[group.End - 1];
            bool complete = group.End <= Session.CompletedSteps;
            int localIndex = routeIndexes.GetValueOrDefault(route.Number) + 1; routeIndexes[route.Number] = localIndex;
            string prefix = complete ? "✓" : "";
            return new TaggedStepCard($"{prefix} {localIndex}. {title}", detail,
                L($"Ship {load.ShipLT:N0} · Main {load.MainLT:N0} · TAG {load.AltLT:N0} LT", $"船 {load.ShipLT:N0} · 主號 {load.MainLT:N0} · TAG {load.AltLT:N0} LT"),
                complete ? 0.5 : 1,
                i, group.End, route.Number, routeTitles[route.Number], trade?.RowId,
                trade is not null ? source?.Item1Icon ?? Icon(trade.InputId) : string.IsNullOrEmpty(a.ItemId) ? null : Icon(a.ItemId),
                trade is not null ? source?.Item2Icon ?? Icon(trade.OutputId) : null, lines);
        }).ToArray();
        var view = new ListCollectionView(cards.Where(c => selectedRoute is null || c.RouteNumber == selectedRoute).ToArray());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TaggedStepCard.RouteTitle)));
        StepsList.ItemsSource = view;
        if (selectNextBarter) {
            var pending = StepsList.Items.OfType<TaggedStepCard>().Where(c => c.RowId is not null && c.End > Session.CompletedSteps).ToArray();
            var nextBarter = pending.FirstOrDefault(c => c.RouteNumber == (selectedRoute ?? preferredRoute)) ?? pending.FirstOrDefault();
            if (nextBarter is not null) SetStepFocus(nextBarter);
        }
        FocusSelectedStep();
        if (selectedStep is null && StepsList.Items.OfType<TaggedStepCard>().FirstOrDefault(c => c.End > Session.CompletedSteps) is { } next)
            StepsList.ScrollIntoView(next);
        updatingRoutes = false;
    }

    private void Settle_Click(object sender, RoutedEventArgs e) {
        if (isSearching || Session is null || Session.Settled) return;
        try {
            var changes = Session.SettlementChanges();
            var stock = App.myStorageVM.StorageCollection.ToDictionary(x => x.ItemID);
            foreach (var c in changes) {
                if (!stock.TryGetValue(c.ItemId, out var item)) throw new InvalidOperationException(L("Missing warehouse item: ", "倉庫缺少商品：") + Item(c.ItemId));
                int value = ReadStock(item, c.WarehouseId);
                // Idempotent recovery if the app stopped between saving the two files.
                if (value != c.Before && value != c.After) throw new InvalidOperationException(L("Warehouse stock changed independently: ", "倉庫已另行修改，未覆寫：") + Item(c.ItemId));
            }
            var original = Session.SettlementBaseline ?? Session.Request;
            var rows = original.Trades.Select(t => (Trade: t, Row: App.myPVM.BarterCollection.FirstOrDefault(x => x.PlannerRowId == t.RowId))).ToArray();
            if (rows.Any(x => x.Row is null || x.Row.Item1.ItemID != x.Trade.InputId || x.Row.Item2.ItemID != x.Trade.OutputId
                || x.Row.Item1Number != x.Trade.InputPerExchange || x.Row.Item2Number != x.Trade.OutputPerExchange
                || x.Row.ExchangeQuantity != x.Trade.Exchanges))
                throw new InvalidOperationException(L("Planner rows changed; this snapshot cannot settle them.", "規劃列已變更，無法使用此快照結算。"));
            using (App.myStorageVM.SuppressAutoSave(saveOnDispose: false))
                foreach (var c in changes) WriteStock(stock[c.ItemId], c.WarehouseId, c.After);
            if (!App.myStorageVM.TrySaveData()) throw new IOException(L("Could not save warehouse settlement; retry.", "無法儲存倉庫結算，請重試。"));
            foreach (var row in rows) row.Row!.ExchangeDone = true;
            App.myfmMain.myPlannerControl.RefreshDerivedValuesAfterRouteProgress();
            if (!App.myfmMain.myPlannerControl.TrySaveData())
                throw new IOException(L("Could not save completed exchanges; retry settlement.", "無法儲存已完成交換，請重試結算。"));
            var settled = Session with { Settled = true };
            TaggedTransportStorage.Save(SessionPath, settled); Session = settled;
            Settings.InitialCargo = []; TrySaveSettings();
            Refresh(); DisplayChanged?.Invoke(this, EventArgs.Empty);
            StatusText.Text = L("Run settled. Warehouse inventory and completed exchanges saved.", "本趟已結算，倉庫庫存與已完成交換已儲存。");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException) { StatusText.Text = error.Message; }
    }
    private static int ReadStock(Items item, string warehouse) => warehouse switch {
        "Velia" => item.StorageVeliaQuantity_Velia, "Iliya" => item.StorageVeliaQuantity_Iliya,
        "Epheria" => item.StorageVeliaQuantity_Epheria, "Ancado" => item.StorageVeliaQuantity_Ancado,
        _ => throw new InvalidOperationException("Unknown warehouse")
    };
    private static void WriteStock(Items item, string warehouse, int value) {
        switch (warehouse) {
            case "Velia": item.StorageVeliaQuantity_Velia = value; break;
            case "Iliya": item.StorageVeliaQuantity_Iliya = value; break;
            case "Epheria": item.StorageVeliaQuantity_Epheria = value; break;
            case "Ancado": item.StorageVeliaQuantity_Ancado = value; break;
            default: throw new InvalidOperationException("Unknown warehouse");
        }
    }
    private static string Icon(string itemId) => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Images", "Items", itemId + ".bmp");
    private sealed record TaggedRouteOption(int? Number, string DisplayName);
    private sealed record TaggedStepCard(string Title, string Detail, string Load, double Opacity,
        int Index, int End, int RouteNumber, string RouteTitle, string? RowId, string? Item1Icon, string? Item2Icon, TaggedCargoLine[] Lines) {
        public bool IsCargoGroup => Lines.Length > 0;
    }
    private sealed record TaggedCargoLine(string Text, string? Icon, string Character = "") {
        private int CharacterIndex => Character.Length == 0 ? -1 : Text.IndexOf(Character, StringComparison.Ordinal);
        public string BeforeCharacter => CharacterIndex < 0 ? Text : Text[..CharacterIndex];
        public string CharacterText => CharacterIndex < 0 ? "" : Character;
        public string AfterCharacter => CharacterIndex < 0 ? "" : Text[(CharacterIndex + Character.Length)..];
    }
}
