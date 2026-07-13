using iBarter.Model;
using iBarter.Routing;
using Syncfusion.Windows.Shared;
using System.ComponentModel;

namespace iBarter.ViewModel;

public enum CargoMode { Manual, AutomaticRoute }

public sealed class AutomaticRouteCoordinator : NotificationObject, IDisposable {
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

    public async Task<RoutePlan> GenerateAsync(AutomaticRoutePlanningRequest request) {
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
        Publish(plan);
        return plan;
    }

    public void SelectRoute(int routeNumber) {
        if (currentPlan?.Routes.All(x => x.Number != routeNumber) != false) return;
        selectedRouteNumber = routeNumber;
        showAllRoutes = false;
        UpdateVisibleRoute();
        NotifyDisplayChanged();
    }

    public void SelectAll() {
        if (currentPlan?.Routes.Count > 0 != true) return;
        showAllRoutes = true;
        RaisePropertyChanged(nameof(ShowAllRoutes));
        RouteDisplayChanged?.Invoke(this, EventArgs.Empty);
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
        RaisePropertyChanged(nameof(Mode));
        RaisePropertyChanged(nameof(ShowAllRoutes));
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
        NotifyAll();
    }

    public RouteRenderSnapshot GetRenderSnapshot(IReadOnlyList<Barter> manualCargo) {
        if (mode == CargoMode.Manual || currentPlan is null) {
            var islandIds = new List<string>();
            var start = ShipCargoViewModel.ResolveStartIslandFromCargo(manualCargo);
            if (start is not null) islandIds.Add(start.IslandsName);
            islandIds.AddRange(manualCargo.Select(x => x.IsLandName));
            return RouteRenderSnapshotFactory.CreateManual(islandIds);
        }
        return RouteRenderSnapshotFactory.CreateAutomatic(currentPlan, selectedRouteNumber, showAllRoutes);
    }

    public void Dispose() {
        storageViewModel.StorageChanged -= StorageChanged;
        cargoProperty.PropertyChanged -= CargoPropertyChanged;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void Publish(RoutePlan plan) {
        currentPlan = plan;
        bool hasUsableRoutes = plan.Status is RoutePlanStatus.Optimal or RoutePlanStatus.BestKnownWithinLimit
            && plan.Routes.Count > 0;
        mode = hasUsableRoutes ? CargoMode.AutomaticRoute : CargoMode.Manual;
        showAllRoutes = false;
        routeOptions = hasUsableRoutes ? BuildRouteOptions(plan) : [];
        selectedRouteNumber = hasUsableRoutes ? plan.Routes[0].Number : null;
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
        visibleAutomaticSteps = route is null ? [] : route.Steps.Select(ToViewModel).ToArray();
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
            pickup.WarehouseId, pickup.IslandId, false, pickup.Items, pickup.Load),
        WarehouseUnloadStep unload => new WarehouseRouteStepViewModel(
            unload.WarehouseId, unload.IslandId, true, unload.Items, unload.Load),
        BarterStep barter => new BarterRouteStepViewModel(barter, currentPlan is null
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

    private static string ResolveItemDisplayName(string itemId) =>
        App.listItems?.FirstOrDefault(x => x.ItemID == itemId)?.ItemNameDisplay ?? itemId;

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

    private void StorageChanged(object? sender, EventArgs e) => Invalidate("storage");

    private void CargoPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName is nameof(CargoProperty.ExtraLT) or nameof(CargoProperty.TotalLT))
            Invalidate(e.PropertyName);
    }
}
