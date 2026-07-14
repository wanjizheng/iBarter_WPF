using iBarter.Localization;
using iBarter.Routing;

namespace iBarter.ViewModel;

public abstract class AutomaticRouteStepViewModel {
    public string Title { get; }
    public string Detail { get; }
    public string LoadText { get; }
    public string IslandId { get; }

    protected AutomaticRouteStepViewModel(string title, string detail, string loadText, string islandId) {
        Title = title;
        Detail = detail;
        LoadText = loadText;
        IslandId = islandId;
    }

    internal static string FormatLoad(RouteLoadSnapshot load) =>
        LanguageService.Instance.Localize(
            "str.ShipCargo.AutoRoute.LoadFormat",
            load.TotalWithExtraLT,
            load.PeakTotalLT);
}

public sealed class WarehouseRouteStepViewModel : AutomaticRouteStepViewModel {
    public string WarehouseId { get; }
    public bool IsUnload { get; }
    public IReadOnlyList<WarehouseRouteItemViewModel> Items { get; }

    public WarehouseRouteStepViewModel(
        string warehouseId,
        string islandId,
        string islandDisplayName,
        bool isUnload,
        IReadOnlyList<RouteItemQuantity> items,
        IReadOnlyDictionary<string, RouteItem> itemLookup,
        RouteLoadSnapshot load)
        : base(
            LanguageService.Instance.Localize(isUnload
                ? "str.ShipCargo.AutoRoute.Unload"
                : "str.ShipCargo.AutoRoute.Pickup", islandDisplayName),
            string.Empty,
            FormatLoad(load),
            islandId) {
        WarehouseId = warehouseId;
        IsUnload = isUnload;
        Items = items.Select(x => new WarehouseRouteItemViewModel(
            x.ItemId,
            itemLookup.TryGetValue(x.ItemId, out var item)
                ? item.DisplayName
                : LanguageService.Instance.Localize("str.ShipCargo.AutoRoute.UnknownItem"),
            x.Quantity,
            Icon(x.ItemId))).ToArray();
    }

    private static string Icon(string itemId) =>
        AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + itemId + ".bmp";
}

public sealed record WarehouseRouteItemViewModel(
    string ItemId,
    string DisplayName,
    int Quantity,
    string Icon);

public sealed class BarterRouteStepViewModel : AutomaticRouteStepViewModel {
    public string RowId { get; }
    public string Item1Icon { get; }
    public string Item2Icon { get; }

    public BarterRouteStepViewModel(
        BarterStep step,
        string islandDisplayName,
        IReadOnlyDictionary<string, RouteItem> items)
        : base(
            LanguageService.Instance.Localize("str.ShipCargo.AutoRoute.Barter", islandDisplayName),
            $"{Display(items, step.Consumed.ItemId)} × {step.Consumed.Quantity} → " +
            $"{Display(items, step.Produced.ItemId)} × {step.Produced.Quantity}",
            FormatLoad(step.Load),
            step.IslandId) {
        RowId = step.RowId;
        Item1Icon = Icon(step.Consumed.ItemId);
        Item2Icon = Icon(step.Produced.ItemId);
    }

    private static string Display(IReadOnlyDictionary<string, RouteItem> items, string itemId) =>
        items.TryGetValue(itemId, out var item) ? item.DisplayName : itemId;

    private static string Icon(string itemId) =>
        AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + itemId + ".bmp";
}

public sealed record RouteSelectionOption(int? RouteNumber, string DisplayName, bool IsAll);
