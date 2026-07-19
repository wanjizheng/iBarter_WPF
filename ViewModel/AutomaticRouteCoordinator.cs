using iBarter.Model;
using iBarter.Routing;
using Syncfusion.Windows.Shared;
using System.ComponentModel;
using System.IO;

namespace iBarter.ViewModel;

public enum CargoMode { Manual, AutomaticRoute }

public sealed class AutomaticRouteCoordinator : NotificationObject, IDisposable {
    private static string PersistencePath => AutomaticRoutePlanStorage.RuntimeResourcesPath;
    private readonly object gate = new();
    private readonly AutomaticRoutePlanner planner = new();
    private readonly StorageViewModel storageViewModel;
    private readonly CargoProperty cargoProperty;
    private CancellationTokenSource? cancellation;
    private long requestId;
    private string? activeFingerprint;
    private CargoMode mode = CargoMode.Manual;
    private RouteOptimizationMode selectedOptimizationMode = RouteOptimizationMode.Balanced;
    private RoutePlan? currentPlan;
    // Mode that the currently published plan was generated under. Tracked
    // separately from `selectedOptimizationMode` (which mirrors the live
    // ComboBox) so that subsequent interactive saves do not silently change
    // to whatever the user most recently picked in the dropdown.
    private RouteOptimizationMode currentPlanMode = RouteOptimizationMode.Balanced;
    private int? selectedRouteNumber;
    private bool showAllRoutes;
    private bool showRouteGuides = true;
    private int? focusedRouteNumber;
    private string? selectedBarterRowId;
    private string? focusedFromIslandId;
    private string? focusedToIslandId;
    private DateTime focusPulseUntilUtc;
    private long focusRevision;
    private HashSet<string> completedBarterRowIds = new(StringComparer.Ordinal);
    private IReadOnlyList<AutomaticRouteStepViewModel> visibleAutomaticSteps = [];
    private IReadOnlyList<RouteSelectionOption> routeOptions = [];

    public AutomaticRouteCoordinator(
        StorageViewModel storageViewModel,
        CargoProperty cargoProperty,
        ShipCargoViewModel cargoViewModel) {
        this.storageViewModel = storageViewModel;
        this.cargoProperty = cargoProperty;
        storageViewModel.StorageChanged += StorageChanged;
        cargoProperty.PropertyChanged += CargoPropertyChanged;
        cargoViewModel.AttachRouteCoordinator(this);
    }

    public CargoMode Mode => mode;
    public RouteOptimizationMode SelectedOptimizationMode => selectedOptimizationMode;
    public RoutePlan? CurrentPlan => currentPlan;
    public int? SelectedRouteNumber => selectedRouteNumber;
    public bool ShowAllRoutes => showAllRoutes;
    public bool ShowRouteGuides => showRouteGuides;
    public string? SelectedBarterRowId => selectedBarterRowId;
    public IReadOnlySet<string> CompletedBarterRowIds => completedBarterRowIds;
    public int? FocusedRouteNumber => focusedRouteNumber;
    public string? FocusedFromIslandId => focusedFromIslandId;
    public string? FocusedToIslandId => focusedToIslandId;
    public long FocusRevision => focusRevision;
    public IReadOnlyList<AutomaticRouteStepViewModel> VisibleAutomaticSteps => visibleAutomaticSteps;
    public IReadOnlyList<RouteSelectionOption> RouteOptions => routeOptions;
    public event EventHandler? RouteDisplayChanged;

    public async Task<RoutePlan> CalculateAsync(AutomaticRoutePlanningRequest request) {
        return await CalculateAsync(request, RouteOptimizationProfile.For(selectedOptimizationMode));
    }

    public async Task<RoutePlan> CalculateAsync(
        AutomaticRoutePlanningRequest request,
        RouteOptimizationProfile profile) {
        CancellationTokenSource ownCancellation;
        long ownRequestId;
        string fingerprint = RoutePlanFingerprint.Compute(request);
        lock (gate) {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = ownCancellation = new CancellationTokenSource();
            ownRequestId = ++requestId;
            activeFingerprint = fingerprint;
        }

        RoutePlan plan = await Task.Run(
            () => planner.Plan(request, profile, ownCancellation.Token), ownCancellation.Token)
            .ContinueWith(task => task.IsCanceled
                    ? new RoutePlan(RoutePlanStatus.Cancelled, [], null, [], fingerprint)
                    : task.GetAwaiter().GetResult(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        lock (gate) {
            if (ownRequestId != requestId || activeFingerprint != fingerprint)
                return new RoutePlan(RoutePlanStatus.Cancelled, [], null, [], fingerprint);
        }

        if (plan.Status is RoutePlanStatus.Optimal or RoutePlanStatus.BestKnownWithinLimit) {
            var verification = RoutePlanVerifier.Verify(request, plan);
            if (!verification.Success) {
                plan = new RoutePlan(RoutePlanStatus.InvalidInput, [], null,
                    [verification.Diagnostic!], fingerprint);
            }
            else {
                plan = verification.VerifiedPlan!;
            }
        }
        return plan;
    }

    public void SetOptimizationMode(RouteOptimizationMode mode) {
        if (mode == selectedOptimizationMode) return;
        selectedOptimizationMode = mode;
        Invalidate("optimization-mode");
    }

    public async Task<RoutePlan> GenerateAsync(AutomaticRoutePlanningRequest request) {
        // Legacy single-arg overload. Caller did not pass a profile, so we
        // fall back to whatever the ComboBox currently shows. The caller in
        // PlannerControl.xaml.cs now passes profile.Mode explicitly via the
        // 3-arg PublishGeneratedPlan; this overload remains for any future
        // test or non-UI code path that builds a request without a profile.
        var plan = await CalculateAsync(request);
        PublishGeneratedPlan(request, plan, selectedOptimizationMode);
        return plan;
    }

    public bool PublishGeneratedPlan(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan,
        RouteOptimizationMode generationMode) {
        if (plan.Status is not (RoutePlanStatus.Optimal or RoutePlanStatus.BestKnownWithinLimit))
            return false;
        if (!StringComparer.Ordinal.Equals(plan.InputFingerprint, RoutePlanFingerprint.Compute(request)))
            return false;
        var verification = RoutePlanVerifier.Verify(request, plan);
        if (!verification.Success || verification.VerifiedPlan is null) return false;
        currentPlanMode = generationMode;

        // Bug 2 fix (round 2): even a verifier-approved plan can carry a
        // phantom pickup that no remaining BarterStep actually consumes.
        // Re-run the normalization pass before publishing so the user
        // never sees the "pickup-then-unload the same item at the same
        // warehouse" round trip that the bug report called out. The
        // normalizer now requires the request and recomputes LT through
        // RouteReplay; we then re-verify the normalized plan and refuse
        // to publish if the post-normalize plan fails verification
        // (defensive — the original plan already passed, but LT changes
        // could in principle introduce a regression).
        var normalized = RouteCargoNormalizer.Normalize(request, verification.VerifiedPlan);
        var finalVerification = RoutePlanVerifier.Verify(request, normalized);
        var publishable = finalVerification.Success && finalVerification.VerifiedPlan is not null
            ? finalVerification.VerifiedPlan
            : verification.VerifiedPlan;

        // Round #3: defense-in-depth against the 800061-style cross-route
        // cargo leak. If the normalizer could not strip the
        // pickup-x-no-barter-unload-x-at-same-warehouse round-trip from
        // every route, refuse to publish rather than write a known-bad
        // plan to automatic-route-plan.json.
        if (RoutePlanVerifier.TryDetectRouteRedundantCargoRoundTrip(publishable!, out var leakDetail)) {
            App.myCFun?.Log(
                $"[AutoRoute] refused to publish: {leakDetail}",
                System.Windows.Media.Brushes.OrangeRed);
            return false;
        }

        Publish(publishable, preferredShowRouteGuides: showRouteGuides);
        SaveCurrentPlan();
        return true;
    }

    /// <summary>
    /// Loads the persisted plan (if any) and, on a fingerprint match, publishes
    /// it as the active route. Returns the rich load result so the caller can
    /// surface the precise reason when restore fails (mismatch / corrupt /
    /// unsupported schema / progress-incompatible).
    /// </summary>
    public RoutePlanLoadResult TryRestore(AutomaticRoutePlanningRequest request) {
        string fingerprint = RoutePlanFingerprint.Compute(request);
        var exact = RoutePlanPersistence.TryLoad(PersistencePath, fingerprint);
        if (exact.Status == RoutePlanLoadStatus.Loaded && exact.Snapshot is not null) {
            var verification = RoutePlanVerifier.Verify(request, exact.Snapshot.Plan);
            if (!verification.Success || verification.VerifiedPlan is null) {
                return exact with { Status = RoutePlanLoadStatus.FingerprintMismatch };
            }
            currentPlanMode = exact.SavedOptimizationMode ?? RouteOptimizationMode.Balanced;
            Publish(
                verification.VerifiedPlan,
                exact.Snapshot.SelectedRouteNumber,
                exact.Snapshot.ShowAll,
                exact.Snapshot.SelectedBarterRowId,
                exact.Snapshot.ShowRouteGuides);
            return exact;
        }

        // Direct fingerprint match failed. Try the progress-restore path so a
        // partially completed plan can still be restored when the user has
        // ticked off some barters since the file was saved.
        if (RoutePlanPersistence.TryLoadAfterProgress(
                PersistencePath, request, completedBarterRowIds, out var progressed)
            && progressed is not null) {
            currentPlanMode = exact.SavedOptimizationMode ?? currentPlanMode;
            Publish(
                progressed.Plan,
                progressed.SelectedRouteNumber,
                progressed.ShowAll,
                progressed.SelectedBarterRowId,
                progressed.ShowRouteGuides);
            return exact with {
                Status = RoutePlanLoadStatus.Loaded,
                Snapshot = progressed,
            };
        }
        return exact;
    }

    public void SelectRoute(int routeNumber) {
        if (currentPlan?.Routes.All(x => x.Number != routeNumber) != false) return;
        mode = CargoMode.AutomaticRoute;
        selectedRouteNumber = routeNumber;
        showAllRoutes = false;
        UpdateVisibleRoute();
        SelectPreferredOrFirstBarter(routeNumber, null);
        NotifyDisplayChanged();
        SaveCurrentPlan();
    }

    public void SelectAll() {
        if (currentPlan?.Routes.Count > 0 != true) return;
        mode = CargoMode.AutomaticRoute;
        showAllRoutes = true;
        RaisePropertyChanged(nameof(ShowAllRoutes));
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
        SaveCurrentPlan();
    }

    public void SetShowRouteGuides(bool show) {
        if (showRouteGuides == show) return;
        showRouteGuides = show;
        RaisePropertyChanged(nameof(ShowRouteGuides));
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
        SaveCurrentPlan();
    }

    public void RefreshLocalization() {
        if (currentPlan is null) return;
        routeOptions = BuildRouteOptions(currentPlan);
        UpdateVisibleRoute();
        RaisePropertyChanged(nameof(RouteOptions));
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ActivateManual() {
        mode = CargoMode.Manual;
        showAllRoutes = false;
        ClearFocus();
        RaisePropertyChanged(nameof(Mode));
        RaisePropertyChanged(nameof(ShowAllRoutes));
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshCompletedBarters(IReadOnlyList<Barter> plannerBarters) {
        // Audit round 3: completed-set is keyed by Barter.PlannerRowId,
        // the persistent stable id stored in myPlan_Data.json. The
        // legacy index-based derivation is removed.
        var refreshed = plannerBarters
            .Where(x => x.ExchangeDone)
            .Select(x => x.PlannerRowId)
            .ToHashSet(StringComparer.Ordinal);
        if (completedBarterRowIds.SetEquals(refreshed)) return;

        completedBarterRowIds = refreshed;
        UpdateVisibleRoute();
        SelectPreferredOrFirstBarter(selectedRouteNumber, selectedBarterRowId);
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Single source of truth for "the user finished a barter" — invoked by
    /// both the Planner CK checkbox and the map's real <c>BarterStep</c>
    /// double-click. Progress is projected over the immutable verified plan;
    /// the plan snapshot and Planner completion state are then persisted by
    /// their respective existing stores.
    /// <para>Bug 1 root cause: the CK path used to call
    /// <c>Invalidate("planner-check")</c>, which set <c>currentPlan = null</c>
    /// and dropped every auto route on the floor.  Bug 4 root cause: the CK
    /// path used to call a UI-only <c>displayedParley -= ...</c> that drifted
    /// under repeated events and reloads.  Both are replaced by this method,
    /// which derives the remaining parley and the post-progress route plan
    /// from the current authoritative Planner/Routing state.</para>
    /// </summary>
    /// <returns>True when at least one route is still live; false when the
    /// plan is fully consumed by the new completion.</returns>
    public bool ApplyBarterCompletionProgress(
        AutomaticRoutePlanningRequest? request,
        IReadOnlyList<Barter> plannerBarters,
        string rowId,
        bool completed) {
        if (string.IsNullOrWhiteSpace(rowId)) return currentPlan is not null;

        // Step 1: rebuild the authoritative completed set so any indirect
        // mutation (row reorder, edit, undo) is observed before we touch the
        // plan. Audit round 3: the identity is Barter.PlannerRowId
        // (persistent), not index/business-key derived.
        var refreshed = plannerBarters
            .Where(x => x.ExchangeDone)
            .Select(x => x.PlannerRowId)
            .ToHashSet(StringComparer.Ordinal);
        // Honor the explicit toggle requested by the caller even if the
        // grid hasn't propagated it yet. The caller MUST pass the same
        // PlannerRowId the planner used to build the row.
        if (completed) refreshed.Add(rowId);
        else refreshed.Remove(rowId);
        completedBarterRowIds = refreshed;

        // Step 2: re-project the in-memory plan against the new completed
        // set.  If no plan exists yet, the user just hasn't generated one —
        // nothing else to do.
        if (currentPlan is null) {
            UpdateVisibleRoute();
            RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        // Step 3: keep the originally verified plan immutable and project
        // execution progress through completedBarterRowIds.  A CK is not a
        // new route-planning request: rebuilding a smaller plan and passing
        // it to the full verifier uses a different task set/fingerprint and
        // incorrectly rejects valid execution progress.  The render, cargo
        // list and restart-compatibility paths already consume this same
        // completed-row overlay.
        var remainingRoutes = currentPlan.Routes
            .Where(route => route.Steps.OfType<BarterStep>()
                .Any(step => !completedBarterRowIds.Contains(step.RowId)))
            .ToArray();
        if (!showAllRoutes
            && (selectedRouteNumber is null
                || !remainingRoutes.Any(route => route.Number == selectedRouteNumber))) {
            selectedRouteNumber = remainingRoutes.FirstOrDefault()?.Number;
        }

        UpdateVisibleRoute();
        SelectPreferredOrFirstBarter(selectedRouteNumber, selectedBarterRowId);
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
        // Persist the known-good base plan. Planner CK state is stored in
        // myPlan_Data.json; TryLoadAfterProgress combines the two on restart.
        SaveCurrentPlan();

        if (completed) {
            if (remainingRoutes.Length == 0) {
                App.myCFun?.Log(
                    "[AutoRoute] 所有自动路线均已完成。",
                    System.Windows.Media.Brushes.DarkOliveGreen);
            }
            else {
                App.myCFun?.Log(
                    "[AutoRoute] 已应用路线进度：完成 1 个交换，剩余 " +
                    remainingRoutes.Length + " 条路线。",
                    System.Windows.Media.Brushes.DarkOliveGreen);
            }
        }
        return remainingRoutes.Length > 0;
    }

    public void Invalidate(string reason) {
        lock (gate) {
            cancellation?.Cancel();
            requestId++;
            activeFingerprint = null;
        }
        currentPlan = null;
        currentPlanMode = RouteOptimizationMode.Balanced;
        selectedRouteNumber = null;
        showAllRoutes = false;
        visibleAutomaticSteps = [];
        routeOptions = [];
        mode = CargoMode.Manual;
        ClearFocus();
        NotifyAll();
    }

    public void FocusBarterStep(string rowId) {
        if (currentPlan is null || selectedRouteNumber is not int routeNumber) return;
        var route = currentPlan.Routes.FirstOrDefault(candidate => candidate.Number == routeNumber);
        if (route is null) return;
        var segment = RouteProgressFilter.FindVisibleBarterSegment(
            route.Steps, completedBarterRowIds, rowId);
        SetFocusedBarter(routeNumber, rowId, segment);
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
        SaveCurrentPlan();
    }

    public bool FocusRouteSegment(
        int routeNumber,
        string fromIslandId,
        string toIslandId) {
        var route = currentPlan?.Routes.FirstOrDefault(
            candidate => candidate.Number == routeNumber);
        if (route is null) return false;
        string? rowId = RouteProgressFilter.FindVisibleBarterRowForSegment(
            route.Steps,
            completedBarterRowIds,
            fromIslandId,
            toIslandId);
        if (rowId is null) return false;

        mode = CargoMode.AutomaticRoute;
        selectedRouteNumber = routeNumber;
        showAllRoutes = false;
        UpdateVisibleRoute();
        SetFocusedBarter(
            routeNumber,
            rowId,
            new RouteFocusSegment(fromIslandId, toIslandId));
        NotifyAll();
        SaveCurrentPlan();
        return true;
    }

    public bool IsFocusedSegment(int routeNumber, string fromIslandId, string toIslandId) =>
        focusedRouteNumber == routeNumber
        && StringComparer.Ordinal.Equals(focusedFromIslandId, fromIslandId)
        && StringComparer.Ordinal.Equals(focusedToIslandId, toIslandId);

    public bool IsFocusedMarker(int routeNumber, string islandId) =>
        focusedRouteNumber == routeNumber
        && (StringComparer.Ordinal.Equals(focusedFromIslandId, islandId)
            || StringComparer.Ordinal.Equals(focusedToIslandId, islandId));

    public bool IsFocusPulseActive => DateTime.UtcNow < focusPulseUntilUtc;

    public RouteRenderSnapshot GetRenderSnapshot(IReadOnlyList<Barter> manualCargo) {
        if (mode == CargoMode.Manual || currentPlan is null) {
            var islandIds = new List<string>();
            var start = ShipCargoViewModel.ResolveStartIslandFromCargo(manualCargo);
            if (start is not null) islandIds.Add(start.IslandsName);
            islandIds.AddRange(manualCargo.Select(x => x.IsLandName));
            return RouteRenderSnapshotFactory.CreateManual(islandIds);
        }
        return RouteRenderSnapshotFactory.CreateAutomatic(
            currentPlan, selectedRouteNumber, showAllRoutes, completedBarterRowIds);
    }

    public void Dispose() {
        storageViewModel.StorageChanged -= StorageChanged;
        cargoProperty.PropertyChanged -= CargoPropertyChanged;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void Publish(
        RoutePlan plan,
        int? preferredRouteNumber = null,
        bool preferredShowAll = false,
        string? preferredBarterRowId = null,
        bool preferredShowRouteGuides = true) {
        currentPlan = plan;
        ClearFocus();
        showRouteGuides = preferredShowRouteGuides;
        bool hasUsableRoutes = plan.Status is RoutePlanStatus.Optimal or RoutePlanStatus.BestKnownWithinLimit
            && plan.Routes.Count > 0;
        mode = hasUsableRoutes ? CargoMode.AutomaticRoute : CargoMode.Manual;
        showAllRoutes = hasUsableRoutes && preferredShowAll;
        routeOptions = hasUsableRoutes ? BuildRouteOptions(plan) : [];
        selectedRouteNumber = hasUsableRoutes
            ? plan.Routes.Any(x => x.Number == preferredRouteNumber)
                ? preferredRouteNumber
                : plan.Routes[0].Number
            : null;
        UpdateVisibleRoute();
        SelectPreferredOrFirstBarter(selectedRouteNumber, preferredBarterRowId);
        NotifyAll();
    }

    private static IReadOnlyList<RouteSelectionOption> BuildRouteOptions(RoutePlan plan) =>
        plan.Routes.Count == 0
            ? []
            : new[] { new RouteSelectionOption(null,
                    Localization.LanguageService.Instance.Localize("str.ShipCargo.AutoRoute.All"), true) }
                .Concat(plan.Routes.Select(x => new RouteSelectionOption(
                    x.Number,
                    Localization.LanguageService.Instance.Localize("str.ShipCargo.AutoRoute.RouteFormat", x.Number),
                    false))).ToArray();

    private void UpdateVisibleRoute() {
        var route = currentPlan?.Routes.FirstOrDefault(x => x.Number == selectedRouteNumber);
        visibleAutomaticSteps = route is null
            ? []
            : RouteProgressFilter.ExcludeCompletedBarters(route.Steps, completedBarterRowIds)
                .Where(step => step is not WarehouseUnloadStep { Items.Count: 0 })
                .Select(ToViewModel)
                .ToArray();
        if (route is not null) {
            cargoProperty.InitialLT = route.InitialLT;
            cargoProperty.CurrentLT = route.CurrentLT;
            cargoProperty.PeakLT = route.PeakLT;
        }
        RaisePropertyChanged(nameof(SelectedRouteNumber));
        RaisePropertyChanged(nameof(ShowAllRoutes));
        RaisePropertyChanged(nameof(VisibleAutomaticSteps));
    }

    private AutomaticRouteStepViewModel ToViewModel(RouteStep step) => step switch {
        WarehousePickupStep pickup => new WarehouseRouteStepViewModel(
            pickup.WarehouseId, pickup.IslandId, ResolveIslandDisplayName(pickup.IslandId), false,
            pickup.Items, BuildItemLookup(pickup.Items), pickup.Load),
        WarehouseUnloadStep unload => new WarehouseRouteStepViewModel(
            unload.WarehouseId, unload.IslandId, ResolveIslandDisplayName(unload.IslandId), true,
            unload.Items, BuildItemLookup(unload.Items), unload.Load),
        BarterStep barter => new BarterRouteStepViewModel(
            barter, ResolveIslandDisplayName(barter.IslandId), currentPlan is null
            ? new Dictionary<string, RouteItem>()
            : BuildItemLookup(barter)),
        _ => throw new InvalidOperationException($"Unknown route step {step.GetType().Name}"),
    };

    private IReadOnlyDictionary<string, RouteItem> BuildItemLookup(BarterStep step) {
        var result = new Dictionary<string, RouteItem>(StringComparer.Ordinal);
        foreach (string id in new[] { step.Consumed.ItemId, step.Produced.ItemId })
            result[id] = new RouteItem(id, ResolveItemDisplayName(id), 0, 0);
        return result;
    }

    private IReadOnlyDictionary<string, RouteItem> BuildItemLookup(IEnumerable<RouteItemQuantity> quantities) =>
        quantities.Select(x => x.ItemId).Distinct(StringComparer.Ordinal).ToDictionary(
            id => id,
            id => new RouteItem(id, ResolveItemDisplayName(id), 0, 0),
            StringComparer.Ordinal);

    private static string ResolveItemDisplayName(string itemId) =>
        App.listItems?.FirstOrDefault(x => x.ItemID == itemId)?.ItemNameDisplay
        ?? Localization.LanguageService.Instance.Localize("str.ShipCargo.AutoRoute.UnknownItem");

    private static string ResolveIslandDisplayName(string islandId) =>
        App.listIslands?.FirstOrDefault(x => x.IslandsName == islandId)?.IslandsNameDisplay
        ?? islandId;

    /// <summary>
    /// Flushes the currently active plan to disk. Called from any user
    /// interaction that mutates the persisted snapshot (publish, select,
    /// focus). Also invoked from <c>MainWindow_Closing</c> as a final
    /// safety net so that a crash or forced shutdown does not lose the
    /// most recently displayed route.
    /// No-op when there is no active plan or when the UI is in manual
    /// cargo mode (the user is editing the cargo list directly and the
    /// plan reflects nothing).
    /// </summary>
    public void SaveCurrentPlan() {
        if (currentPlan is null || mode != CargoMode.AutomaticRoute) return;
        try {
            RoutePlanPersistence.Save(
                PersistencePath,
                currentPlan,
                selectedRouteNumber,
                showAllRoutes,
                selectedBarterRowId,
                currentPlanMode,
                showRouteGuides: showRouteGuides);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            App.myCFun?.Log(ex.Message, System.Windows.Media.Brushes.OrangeRed);
        }
    }

    private void NotifyAll() {
        RaisePropertyChanged(nameof(Mode));
        RaisePropertyChanged(nameof(CurrentPlan));
        RaisePropertyChanged(nameof(SelectedRouteNumber));
        RaisePropertyChanged(nameof(ShowAllRoutes));
        RaisePropertyChanged(nameof(ShowRouteGuides));
        RaisePropertyChanged(nameof(VisibleAutomaticSteps));
        RaisePropertyChanged(nameof(RouteOptions));
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyDisplayChanged() {
        RaisePropertyChanged(nameof(Mode));
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearFocus() {
        focusedRouteNumber = null;
        selectedBarterRowId = null;
        focusedFromIslandId = null;
        focusedToIslandId = null;
        focusPulseUntilUtc = DateTime.MinValue;
        focusRevision++;
    }

    private void SelectPreferredOrFirstBarter(
        int? routeNumber,
        string? preferredRowId) {
        var route = currentPlan?.Routes.FirstOrDefault(
            candidate => candidate.Number == routeNumber);
        if (route is null) {
            ClearFocus();
            return;
        }
        var visibleBarters = RouteProgressFilter.RemainingMapSteps(
                route.Steps, completedBarterRowIds)
            .OfType<BarterStep>()
            .ToArray();
        var selected = visibleBarters.FirstOrDefault(
                step => StringComparer.Ordinal.Equals(step.RowId, preferredRowId))
            ?? visibleBarters.FirstOrDefault();
        if (selected is null) {
            ClearFocus();
            return;
        }
        SetFocusedBarter(
            route.Number,
            selected.RowId,
            RouteProgressFilter.FindVisibleBarterSegment(
                route.Steps, completedBarterRowIds, selected.RowId));
    }

    private void SetFocusedBarter(
        int routeNumber,
        string rowId,
        RouteFocusSegment? segment) {
        focusedRouteNumber = routeNumber;
        selectedBarterRowId = rowId;
        focusedFromIslandId = segment?.FromIslandId;
        focusedToIslandId = segment?.ToIslandId;
        focusPulseUntilUtc = DateTime.UtcNow.AddSeconds(1.8);
        focusRevision++;
        RaisePropertyChanged(nameof(SelectedBarterRowId));
    }

    private void StorageChanged(object? sender, EventArgs e) => Invalidate("storage");

    private void CargoPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(CargoProperty.ExtraLT) or nameof(CargoProperty.TotalLT))
            Invalidate(e.PropertyName);
    }
}
