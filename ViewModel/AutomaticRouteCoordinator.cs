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
    private RoutePlan? currentPlan;
    private int? selectedRouteNumber;
    private bool showAllRoutes;
    private int? focusedRouteNumber;
    private string? focusedFromIslandId;
    private string? focusedToIslandId;
    private DateTime focusPulseUntilUtc;
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
    public RoutePlan? CurrentPlan => currentPlan;
    public int? SelectedRouteNumber => selectedRouteNumber;
    public bool ShowAllRoutes => showAllRoutes;
    public IReadOnlyList<AutomaticRouteStepViewModel> VisibleAutomaticSteps => visibleAutomaticSteps;
    public IReadOnlyList<RouteSelectionOption> RouteOptions => routeOptions;
    public event EventHandler? RouteDisplayChanged;

    public async Task<RoutePlan> CalculateAsync(AutomaticRoutePlanningRequest request) {
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
            () => planner.Plan(request, ownCancellation.Token), ownCancellation.Token)
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

    public async Task<RoutePlan> GenerateAsync(AutomaticRoutePlanningRequest request) {
        var plan = await CalculateAsync(request);
        PublishGeneratedPlan(request, plan);
        return plan;
    }

    public bool PublishGeneratedPlan(AutomaticRoutePlanningRequest request, RoutePlan plan) {
        if (plan.Status is not (RoutePlanStatus.Optimal or RoutePlanStatus.BestKnownWithinLimit))
            return false;
        if (!StringComparer.Ordinal.Equals(plan.InputFingerprint, RoutePlanFingerprint.Compute(request)))
            return false;
        var verification = RoutePlanVerifier.Verify(request, plan);
        if (!verification.Success || verification.VerifiedPlan is null) return false;
        Publish(verification.VerifiedPlan);
        SaveCurrentPlan();
        return true;
    }

    public bool TryRestore(AutomaticRoutePlanningRequest request) {
        string fingerprint = RoutePlanFingerprint.Compute(request);
        if (!RoutePlanPersistence.TryLoad(PersistencePath, fingerprint, out var persisted)
            || persisted is null)
            return false;
        var verification = RoutePlanVerifier.Verify(request, persisted.Plan);
        if (!verification.Success || verification.VerifiedPlan is null) return false;
        Publish(
            verification.VerifiedPlan,
            persisted.SelectedRouteNumber,
            persisted.ShowAll);
        return true;
    }

    public void SelectRoute(int routeNumber) {
        if (currentPlan?.Routes.All(x => x.Number != routeNumber) != false) return;
        mode = CargoMode.AutomaticRoute;
        selectedRouteNumber = routeNumber;
        showAllRoutes = false;
        ClearFocus();
        UpdateVisibleRoute();
        NotifyDisplayChanged();
        SaveCurrentPlan();
    }

    public void SelectAll() {
        if (currentPlan?.Routes.Count > 0 != true) return;
        mode = CargoMode.AutomaticRoute;
        showAllRoutes = true;
        ClearFocus();
        RaisePropertyChanged(nameof(ShowAllRoutes));
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
        var refreshed = plannerBarters.Select((barter, index) => (barter, index))
            .Where(x => x.barter.ExchangeDone)
            .Select(x => RoutePlannerRowIdentity.Create(
                x.index,
                x.barter.IsLandName,
                x.barter.Item1.ItemID,
                x.barter.Item2.ItemID))
            .ToHashSet(StringComparer.Ordinal);
        if (completedBarterRowIds.SetEquals(refreshed)) return;

        completedBarterRowIds = refreshed;
        ClearFocus();
        UpdateVisibleRoute();
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Invalidate(string reason) {
        lock (gate) {
            cancellation?.Cancel();
            requestId++;
            activeFingerprint = null;
        }
        currentPlan = null;
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
        if (segment is null) return;
        focusedRouteNumber = routeNumber;
        focusedFromIslandId = segment.FromIslandId;
        focusedToIslandId = segment.ToIslandId;
        focusPulseUntilUtc = DateTime.UtcNow.AddSeconds(1.8);
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsFocusedSegment(int routeNumber, string fromIslandId, string toIslandId) =>
        focusedRouteNumber == routeNumber
        && StringComparer.Ordinal.Equals(focusedFromIslandId, fromIslandId)
        && StringComparer.Ordinal.Equals(focusedToIslandId, toIslandId);

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
        bool preferredShowAll = false) {
        currentPlan = plan;
        ClearFocus();
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

    private void SaveCurrentPlan() {
        if (currentPlan is null || mode != CargoMode.AutomaticRoute) return;
        try {
            RoutePlanPersistence.Save(
                PersistencePath, currentPlan, selectedRouteNumber, showAllRoutes);
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
        focusedFromIslandId = null;
        focusedToIslandId = null;
        focusPulseUntilUtc = DateTime.MinValue;
    }

    private void StorageChanged(object? sender, EventArgs e) => Invalidate("storage");

    private void CargoPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(CargoProperty.ExtraLT) or nameof(CargoProperty.TotalLT))
            Invalidate(e.PropertyName);
    }
}
