using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace iBarter.Routing;

public static class RoutePlanFingerprint {
    private const string AlgorithmVersion = "route-planner-v4-west-coast-corridor";

    public static string Compute(AutomaticRoutePlanningRequest request) {
        var builder = new StringBuilder(2048);
        Add(builder, AlgorithmVersion);
        Add(builder, request.ConfigurationVersion);
        Add(builder, request.ExtraLT);
        Add(builder, request.TotalLT);

        // Keep legacy persisted routes valid when there is no carried cargo.
        // Once carried cargo exists it must participate in the fingerprint,
        // because it materially changes what the first route may execute.
        if (request.InitialOnBoard.Count > 0) {
            Add(builder, "initial-onboard");
            foreach (var pair in request.InitialOnBoard.OrderBy(x => x.Key, StringComparer.Ordinal)) {
                Add(builder, pair.Key);
                Add(builder, pair.Value);
            }
            Add(builder, "initial-onboard-end");
        }

        foreach (var task in request.Tasks.OrderBy(x => x.RowId, StringComparer.Ordinal)) {
            Add(builder, task.RowId);
            Add(builder, task.IslandId);
            Add(builder, task.Point.X);
            Add(builder, task.Point.Y);
            Add(builder, task.Item1Id);
            Add(builder, task.InputQuantity);
            Add(builder, task.Item2Id);
            Add(builder, task.OutputQuantity);
        }

        foreach (var warehouse in request.Warehouses.OrderBy(x => x.WarehouseId, StringComparer.Ordinal)) {
            Add(builder, warehouse.WarehouseId);
            Add(builder, warehouse.IslandId);
            Add(builder, warehouse.Point.X);
            Add(builder, warehouse.Point.Y);
            foreach (var pair in warehouse.Inventory.OrderBy(x => x.Key, StringComparer.Ordinal)) {
                Add(builder, pair.Key);
                Add(builder, pair.Value);
            }
            Add(builder, "warehouse-end");
        }

        foreach (var pair in request.Items.OrderBy(x => x.Key, StringComparer.Ordinal)) {
            Add(builder, pair.Key);
            Add(builder, pair.Value.Level);
            Add(builder, pair.Value.UnitWeight);
        }
        Add(builder, request.Limits.MaxExpandedStates);
        Add(builder, request.Limits.MaxLocalMoves);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void Add(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');

    private static void Add(StringBuilder builder, int value) =>
        Add(builder, value.ToString(CultureInfo.InvariantCulture));

    private static void Add(StringBuilder builder, double value) =>
        Add(builder, value.ToString("R", CultureInfo.InvariantCulture));
}
