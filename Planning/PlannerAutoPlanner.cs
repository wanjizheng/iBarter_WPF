namespace iBarter.Planning;

/// <summary>
/// Deterministic, UI-independent planner for the iBarter trade routes.
/// All inputs are immutable; the public <see cref="Plan"/> method never
/// mutates its <see cref="AutoPlanningRequest"/>. Strategy implementations
/// must produce identical multipliers for identical inputs.
/// </summary>
public sealed class PlannerAutoPlanner {
    private const int DefaultParleyBudget = 1_000_000;

    public AutoPlanningResult Plan(AutoPlanningRequest request) {
        if (request is null) {
            return Failure(new AutoPlanningDiagnostic("null-request"));
        }

        if (request.Routes is null) {
            return Failure(new AutoPlanningDiagnostic("null-routes"));
        }

        if (request.CurrentInventory is null) {
            return Failure(new AutoPlanningDiagnostic("null-inventory"));
        }

        if (request.ParleyBudget < 0 || request.ParleyBudget > DefaultParleyBudget) {
            return Failure(new AutoPlanningDiagnostic("invalid-budget"));
        }

        if (request.Lv5Target < 0 || request.Lv6Target < 0) {
            return Failure(new AutoPlanningDiagnostic("invalid-target"));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in request.Routes) {
            if (string.IsNullOrWhiteSpace(r.RowId)) {
                return Failure(new AutoPlanningDiagnostic("blank-row-id"));
            }

            if (!seen.Add(r.RowId)) {
                return Failure(new AutoPlanningDiagnostic("duplicate-row-id", r.RowId));
            }

            if (r.Item1Number <= 0 || r.Item2Number <= 0) {
                return Failure(new AutoPlanningDiagnostic("invalid-quantity", r.RowId));
            }

            if (r.Parley < 0 || r.Remaining < 0) {
                return Failure(new AutoPlanningDiagnostic("invalid-parley-or-remaining", r.RowId));
            }
        }

        if (request.CurrentInventory.Values.Any(v => v < 0)) {
            return Failure(new AutoPlanningDiagnostic("negative-inventory"));
        }

        var routesById = request.Routes.ToDictionary(r => r.RowId, StringComparer.Ordinal);

        // A "target" is a route whose Item2 is not consumed by any other route in the
        // request (regardless of group). These are the rows the user actually wants to
        // drive; everything else is reached by walking the chain backward via the
        // reverse-supply bundle. Cross-group consumption is enough to disqualify — the
        // bundle builder never crosses groups, so a producer pulled in by a downstream
        // route in another group would have no in-group consumer for its own output.
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in request.Routes) {
            foreach (var other in request.Routes) {
                if (other.RowId != r.RowId && other.Item1Id == r.Item2Id) {
                    consumed.Add(r.Item2Id);
                }
            }
        }

        var targets = request.Routes
            .Where(r => !consumed.Contains(r.Item2Id))
            .OrderBy(r => r.RowId, StringComparer.Ordinal)
            .ToList();

        var committed = new Dictionary<string, int>(StringComparer.Ordinal);
        int committedParley = 0;
        var diagnostics = new List<AutoPlanningDiagnostic>();

        foreach (var target in targets) {
            int targetIncrement = target.Remaining;
            if (targetIncrement <= 0) {
                continue;
            }

            // Working inventory for the bundle reflects everything committed so far.
            var workingInventory = ApplyMultipliers(request.CurrentInventory, routesById, committed);

            if (TryBuildBundle(
                    target.RowId, targetIncrement,
                    routesById,
                    workingInventory,
                    committed,
                    request.ParleyBudget - committedParley,
                    out var bundle,
                    diagnostics)) {

                foreach (var (rowId, finalMul) in bundle.Multipliers) {
                    int existing = committed.GetValueOrDefault(rowId);
                    if (finalMul > existing) {
                        committed[rowId] = finalMul;
                    }
                }

                committedParley += bundle.AdditionalParley;
            }
        }

        // Every requested row gets a slot in the result map (zero when no bundle touched it).
        foreach (var r in request.Routes) {
            if (!committed.ContainsKey(r.RowId)) {
                committed[r.RowId] = 0;
            }
        }

        var finalInventory = ApplyMultipliers(request.CurrentInventory, routesById, committed);
        return new AutoPlanningResult(true, committed, finalInventory, committedParley, diagnostics);
    }

    private static AutoPlanningResult Failure(params AutoPlanningDiagnostic[] diagnostics) =>
        new(false,
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal),
            0,
            diagnostics);

    // ------------------------------------------------------------------
    // Atomic reverse-supply bundle builder
    // ------------------------------------------------------------------

    private static bool TryBuildBundle(
        string targetRowId,
        int targetIncrement,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> workingInventory,
        IReadOnlyDictionary<string, int> committedMultipliers,
        int remainingBudget,
        out Bundle bundle,
        List<AutoPlanningDiagnostic> diagnostics) {

        bundle = new Bundle();
        var stack = new HashSet<(int, string)>();
        int bundleParley = 0;

        bool ok = TryAddRoute(
            targetRowId, targetIncrement, routesById, stack,
            workingInventory, committedMultipliers, bundle.Multipliers,
            ref bundleParley, diagnostics);

        if (!ok) {
            bundle.Multipliers.Clear();
            return false;
        }

        foreach (var kvp in workingInventory) {
            if (kvp.Value < 0) {
                diagnostics.Add(new AutoPlanningDiagnostic("negative-inventory", targetRowId));
                bundle.Multipliers.Clear();
                return false;
            }
        }

        if (bundleParley > remainingBudget) {
            diagnostics.Add(new AutoPlanningDiagnostic("budget-exceeded", targetRowId));
            bundle.Multipliers.Clear();
            return false;
        }

        bundle.AdditionalParley = bundleParley;
        return true;
    }

    private static bool TryAddRoute(
        string rowId,
        int increment,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        HashSet<(int, string)> stack,
        Dictionary<string, int> workingInventory,
        IReadOnlyDictionary<string, int> committedMultipliers,
        Dictionary<string, int> bundleMultipliers,
        ref int bundleParley,
        List<AutoPlanningDiagnostic> diagnostics) {

        if (!routesById.TryGetValue(rowId, out var route)) {
            diagnostics.Add(new AutoPlanningDiagnostic("missing-route", rowId));
            return false;
        }

        if (increment <= 0) {
            return true;
        }

        if (increment > route.Remaining) {
            diagnostics.Add(new AutoPlanningDiagnostic("exceeds-remaining", rowId));
            return false;
        }

        var key = (route.Group, route.Item1Id);
        if (!stack.Add(key)) {
            diagnostics.Add(new AutoPlanningDiagnostic("cycle", rowId));
            return false;
        }

        try {
            int demand = checked(increment * route.Item1Number);
            int available = workingInventory.TryGetValue(route.Item1Id, out var cur) ? cur : 0;
            int deficit = Math.Max(0, demand - available);

            if (deficit > 0) {
                var producers = FindProducers(route.Group, route.Item1Id, routesById);
                if (producers.Count == 0) {
                    diagnostics.Add(new AutoPlanningDiagnostic("no-producer", rowId));
                    return false;
                }

                foreach (var producer in producers) {
                    int producerIncrement = CeilingDivide(deficit, producer.Item2Number);

                    if (!TryAddRoute(
                            producer.RowId, producerIncrement, routesById, stack,
                            workingInventory, committedMultipliers, bundleMultipliers,
                            ref bundleParley, diagnostics)) {
                        return false;
                    }

                    deficit = checked(deficit - producerIncrement * producer.Item2Number);
                    if (deficit <= 0) {
                        break;
                    }
                }
            }

            if (deficit > 0) {
                diagnostics.Add(new AutoPlanningDiagnostic("insufficient-inventory", rowId));
                return false;
            }

            // Commit this route. The bundle's multiplier is the max of the outer committed
            // value, anything previously added to this same bundle, and the increment the
            // recursion just requested. Additional parley is only charged for the delta
            // beyond the outer committed value, so the same producer appearing in two
            // bundles doesn't pay for itself twice.
            int committedVal = committedMultipliers.TryGetValue(rowId, out var cv) ? cv : 0;
            int bundleVal = bundleMultipliers.TryGetValue(rowId, out var bv) ? bv : 0;
            int targetMul = Math.Max(Math.Max(committedVal, bundleVal), increment);

            bundleMultipliers[rowId] = targetMul;

            int delta = targetMul - committedVal;
            if (delta > 0) {
                bundleParley = checked(bundleParley + delta * route.Parley);
            }

            // Update the working inventory so any subsequent recursion sees the post-exchange
            // values. Apply against `increment` (the requested count) — not `targetMul` — so
            // already-committed upstream contributions aren't double-counted.
            int newItem1 = (workingInventory.TryGetValue(route.Item1Id, out var i1) ? i1 : 0) - demand;
            workingInventory[route.Item1Id] = newItem1;
            int produced = checked(increment * route.Item2Number);
            int newItem2 = (workingInventory.TryGetValue(route.Item2Id, out var i2) ? i2 : 0) + produced;
            workingInventory[route.Item2Id] = newItem2;

            return true;
        }
        finally {
            stack.Remove(key);
        }
    }

    private static int CeilingDivide(int deficit, int output) {
        if (output <= 0) {
            throw new ArgumentOutOfRangeException(nameof(output), "Producer output must be positive.");
        }
        return checked((deficit + output - 1) / output);
    }

    private static List<AutoPlanningRoute> FindProducers(
        int group,
        string itemId,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById) {
        return routesById.Values
            .Where(r => r.Group == group && r.Item2Id == itemId && r.Item2Number > 0)
            .OrderByDescending(r => r.Item2Number)
            .ThenBy(r => r.Parley)
            .ThenBy(r => r.RowId, StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, int> ApplyMultipliers(
        IReadOnlyDictionary<string, int> baseInventory,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        IReadOnlyDictionary<string, int> multipliers) {
        var inv = new Dictionary<string, int>(baseInventory, StringComparer.Ordinal);
        foreach (var kvp in multipliers) {
            if (kvp.Value <= 0 || !routesById.TryGetValue(kvp.Key, out var r)) {
                continue;
            }
            int consumed = checked(kvp.Value * r.Item1Number);
            int produced = checked(kvp.Value * r.Item2Number);
            inv[r.Item1Id] = (inv.TryGetValue(r.Item1Id, out var a) ? a : 0) - consumed;
            inv[r.Item2Id] = (inv.TryGetValue(r.Item2Id, out var b) ? b : 0) + produced;
        }
        return inv;
    }

    private sealed class Bundle {
        public Dictionary<string, int> Multipliers { get; } = new(StringComparer.Ordinal);
        public int AdditionalParley { get; set; }
    }
}