namespace iBarter.Planning;

/// <summary>
/// Deterministic, UI-independent planner for the iBarter trade routes.
/// All inputs are immutable; the public <see cref="Plan"/> method never
/// mutates its <see cref="AutoPlanningRequest"/>. Strategy implementations
/// must produce identical multipliers for identical inputs.
///
/// Semantics (per the design spec):
/// - Each iteration increments exactly one target route's multiplier by 1.
///   The 1-exchange bundle (and any required upstream reverse-supply chain)
///   is atomic — all-or-nothing.
/// - A target may be re-picked across iterations until either its Remaining
///   is exhausted or its next 1-exchange bundle cannot fit in the budget.
/// - After every successful commit the ranker re-runs from scratch with the
///   updated inventory so shared producers are re-ranked correctly.
/// - Each bundle build clones the working inventory, so a failed attempt
///   never pollutes the next attempt's view of the world.
/// - Each producer-attempt inside a bundle also clones state, so a failed
///   producer attempt does not block the planner from trying the next one
///   (or combining multiple producers to satisfy the deficit).
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

        var committed = new Dictionary<string, int>(StringComparer.Ordinal);
        int committedParley = 0;
        var diagnostics = new List<AutoPlanningDiagnostic>();

        switch (request.Strategy) {
            case AutoPlanningStrategy.CrowCoinFirst:
                PlanCrowCoinFirst(request, routesById, committed, ref committedParley, diagnostics);
                break;
            case AutoPlanningStrategy.ProfitFirst:
                PlanProfitFirst(request, routesById, committed, ref committedParley, diagnostics);
                break;
            case AutoPlanningStrategy.RestockFirst:
                PlanRestockFirst(request, routesById, committed, ref committedParley, diagnostics);
                break;
        }

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
    // Strategy drivers
    // ------------------------------------------------------------------

    private static void PlanCrowCoinFirst(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {

        // Phase 1: Crow-Coin routes. Higher coin output wins; on ties, lower
        // full-bundle parley (cheap chain beats expensive chain).
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => r.ProducesCrowCoin,
            ranker: CompareCrowCoin);

        // Phase 2: remainder budget, no crow coin output allowed. Lowest input LV
        // first (LV4 → LV5 → LV6 → LV7).
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => !r.ProducesCrowCoin,
            ranker: CompareRemainder);
    }

    private static void PlanProfitFirst(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {

        // Crow coin is excluded. Strict tier-desc ranking — no leaf filter.
        // When the top tier can't fit any increment, the planner degrades to
        // the next tier.
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => !r.ProducesCrowCoin,
            ranker: CompareProfit);
    }

    private static void PlanRestockFirst(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {

        // Phase 1: LV5/LV6 capped restock by largest deficit ratio.
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => IsCappedRestockEligible(r, request, committed),
            ranker: CompareCapped);

        // Phase 2: LV1-LV4 uncapped, lowest projected inventory first.
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => IsUncappedRestockEligible(r),
            ranker: CompareUncapped);
    }

    private static bool IsCappedRestockEligible(
        AutoPlanningRoute r,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, int> committed) {
        if (r.ProducesCrowCoin) return false;
        if (r.Item2Level != 5 && r.Item2Level != 6) return false;
        int target = r.Item2Level == 5 ? request.Lv5Target : request.Lv6Target;
        if (target <= 0) return false;
        int projected = ProjectedItemInventory(r.Item2Id, request, committed);
        return projected < target;
    }

    private static bool IsUncappedRestockEligible(AutoPlanningRoute r) {
        if (r.ProducesCrowCoin) return false;
        return r.Item2Level >= 1 && r.Item2Level <= 4;
    }

    private static int ProjectedItemInventory(
        string itemId,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, int> committed) {
        int baseQty = request.CurrentInventory.TryGetValue(itemId, out var q) ? q : 0;
        foreach (var (rowId, mul) in committed) {
            if (mul <= 0) continue;
            var route = request.Routes.FirstOrDefault(r => r.RowId == rowId);
            if (route is null) continue;
            if (route.Item2Id == itemId) baseQty += mul * route.Item2Number;
            if (route.Item1Id == itemId) baseQty -= mul * route.Item1Number;
        }
        return baseQty;
    }

    // ------------------------------------------------------------------
    // Greedy per-increment loop. Each iteration:
    //   1. Filters candidates (committed < remaining AND downstream demand > 0).
    //   2. Builds a +1 atomic bundle per candidate with its own cloned inventory.
    //   3. Sorts by the strategy's ranker using the freshly-built bundles.
    //   4. Commits the winner and re-loops.
    // ------------------------------------------------------------------

    private delegate int BundleRanker(
        AutoPlanningRoute a,
        AutoPlanningRoute b,
        IReadOnlyDictionary<string, Bundle> bundlesByRow,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, int> committed);

    private static void GreedyFillIncrements(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics,
        Func<AutoPlanningRoute, bool> filter,
        BundleRanker ranker) {

        while (true) {
            var candidates = request.Routes
                .Where(filter)
                .Where(r => committed.TryGetValue(r.RowId, out var c) ? c < r.Remaining : r.Remaining > 0)
                .Where(r => {
                    int cur = committed.TryGetValue(r.RowId, out var cc) ? cc : 0;
                    // Skip intermediate routes whose downstream demand is already
                    // saturated by committed producers — picking them as targets
                    // would burn budget on inventory nobody reads.
                    return ComputeMaxUsefulTarget(r, committed, request, routesById) > cur;
                })
                .ToList();

            if (candidates.Count == 0) {
                return;
            }

            // Build one +1 bundle per candidate, each with its own working-inventory
            // clone. A failed build attempt never pollutes the next attempt.
            var bundles = new Dictionary<string, Bundle>(StringComparer.Ordinal);
            foreach (var c in candidates) {
                int currentCommitted = committed.TryGetValue(c.RowId, out var cc) ? cc : 0;
                int absoluteTarget = currentCommitted + 1;

                var attemptInventory = ApplyMultipliers(request.CurrentInventory, routesById, committed);
                int remainingBudget = request.ParleyBudget - committedParley;

                if (TryBuildBundle(c.RowId, absoluteTarget, routesById,
                        attemptInventory, committed, remainingBudget,
                        out var bundle, diagnostics)) {
                    bundles[c.RowId] = bundle;
                }
            }

            if (bundles.Count == 0) {
                return;
            }

            candidates.Sort((a, b) => ranker(a, b, bundles, request, committed));

            var winner = candidates[0];
            var winnerBundle = bundles[winner.RowId];

            // Commit the winner's bundle multiplicities as additions to the master
            // committed map. bundle.Multipliers stores INCREMENTAL contributions
            // (additional exchanges this bundle brings in), so a simple sum is
            // correct — no max() needed.
            foreach (var (rowId, bundleMul) in winnerBundle.Multipliers) {
                int existing = committed.TryGetValue(rowId, out var cv) ? cv : 0;
                committed[rowId] = existing + bundleMul;
            }

            committedParley += winnerBundle.AdditionalParley;
        }
    }

    // ------------------------------------------------------------------
    // Demand-aware target computation
    // ------------------------------------------------------------------

    private static int ComputeMaxUsefulTarget(
        AutoPlanningRoute candidate,
        IReadOnlyDictionary<string, int> committed,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById) {

        int currentCommitted = committed.TryGetValue(candidate.RowId, out var cc) ? cc : 0;
        int maxByRemaining = candidate.Remaining;

        bool isLeaf = !IsConsumedByAny(candidate.Item2Id, candidate.RowId, request.Routes);
        if (isLeaf) {
            return maxByRemaining;
        }

        int downstreamDemand = SumDownstreamDemand(candidate.Item2Id, committed, request, routesById);
        int producerExchangesNeeded = downstreamDemand > 0
            ? CeilingDivide(downstreamDemand, candidate.Item2Number)
            : 0;

        return Math.Min(maxByRemaining, Math.Max(currentCommitted, producerExchangesNeeded));
    }

    private static bool IsConsumedByAny(
        string itemId, string excludeRowId, IReadOnlyList<AutoPlanningRoute> routes) {
        foreach (var r in routes) {
            if (r.RowId == excludeRowId) continue;
            if (r.Item1Id == itemId) return true;
        }
        return false;
    }

    private static int SumDownstreamDemand(
        string itemId,
        IReadOnlyDictionary<string, int> committed,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById) {

        int totalDemand = 0;
        foreach (var consumer in request.Routes) {
            if (consumer.Item1Id != itemId) continue;
            if (consumer.Item1Number <= 0) continue;
            int currentCommitted = committed.TryGetValue(consumer.RowId, out var cc) ? cc : 0;
            int remainingDemand = Math.Max(0, consumer.Remaining - currentCommitted);
            totalDemand += remainingDemand * consumer.Item1Number;
        }
        return totalDemand;
    }

    // ------------------------------------------------------------------
    // Bundle rankers. All rankers operate on bundles built for +1 increment of
    // each candidate; the bundle's effective output for the target is therefore
    // always 1 * Item2Number, so we can score by target row's Item2Number.
    // ------------------------------------------------------------------

    private static int CompareCrowCoin(
        AutoPlanningRoute a, AutoPlanningRoute b,
        IReadOnlyDictionary<string, Bundle> bundlesByRow,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, int> committed) {

        var ba = bundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = bundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        if (ba is null && bb is null) return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
        if (ba is null) return 1;
        if (bb is null) return -1;

        // Spec: higher coin output wins. On ties, lower full-bundle parley wins.
        int cmp = b.Item2Number.CompareTo(a.Item2Number);
        if (cmp != 0) return cmp;
        cmp = ba.AdditionalParley.CompareTo(bb.AdditionalParley);
        if (cmp != 0) return cmp;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    private static int CompareRemainder(
        AutoPlanningRoute a, AutoPlanningRoute b,
        IReadOnlyDictionary<string, Bundle> bundlesByRow,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, int> committed) {

        var ba = bundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = bundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        if (ba is null && bb is null) {
            int cmpA = a.Item1Level.CompareTo(b.Item1Level);
            if (cmpA != 0) return cmpA;
            return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
        }
        if (ba is null) return 1;
        if (bb is null) return -1;

        int cmp1 = a.Item1Level.CompareTo(b.Item1Level);
        if (cmp1 != 0) return cmp1;
        int cmp2 = b.Item2Number.CompareTo(a.Item2Number);
        if (cmp2 != 0) return cmp2;
        int cmp3 = ba.AdditionalParley.CompareTo(bb.AdditionalParley);
        if (cmp3 != 0) return cmp3;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    private static int CompareProfit(
        AutoPlanningRoute a, AutoPlanningRoute b,
        IReadOnlyDictionary<string, Bundle> bundlesByRow,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, int> committed) {

        var ba = bundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = bundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        if (ba is null && bb is null) {
            int cmpA = b.Item2Level.CompareTo(a.Item2Level);
            if (cmpA != 0) return cmpA;
            return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
        }
        if (ba is null) return 1;
        if (bb is null) return -1;

        // Spec: higher target tier wins (LV7 > LV6 > LV5 > LV4). On ties, higher
        // target output, then lower bundle parley, then row id.
        int cmp1 = b.Item2Level.CompareTo(a.Item2Level);
        if (cmp1 != 0) return cmp1;
        int cmp2 = b.Item2Number.CompareTo(a.Item2Number);
        if (cmp2 != 0) return cmp2;
        int cmp3 = ba.AdditionalParley.CompareTo(bb.AdditionalParley);
        if (cmp3 != 0) return cmp3;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    private static int CompareCapped(
        AutoPlanningRoute a, AutoPlanningRoute b,
        IReadOnlyDictionary<string, Bundle> bundlesByRow,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, int> committed) {

        int targetA = a.Item2Level == 5 ? request.Lv5Target : request.Lv6Target;
        int targetB = b.Item2Level == 5 ? request.Lv5Target : request.Lv6Target;
        int projA = ProjectedItemInventory(a.Item2Id, request, committed);
        int projB = ProjectedItemInventory(b.Item2Id, request, committed);
        int deficitA = targetA - projA;
        int deficitB = targetB - projB;

        var ba = bundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = bundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        int pa = ba?.AdditionalParley ?? int.MaxValue;
        int pb = bb?.AdditionalParley ?? int.MaxValue;

        int cmp = unchecked(deficitB * targetA).CompareTo(unchecked(deficitA * targetB));
        if (cmp != 0) return cmp;
        cmp = pa.CompareTo(pb);
        if (cmp != 0) return cmp;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    private static int CompareUncapped(
        AutoPlanningRoute a, AutoPlanningRoute b,
        IReadOnlyDictionary<string, Bundle> bundlesByRow,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, int> committed) {

        int projA = ProjectedItemInventory(a.Item2Id, request, committed);
        int projB = ProjectedItemInventory(b.Item2Id, request, committed);

        var ba = bundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = bundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        int pa = ba?.AdditionalParley ?? int.MaxValue;
        int pb = bb?.AdditionalParley ?? int.MaxValue;

        int cmp = projA.CompareTo(projB);
        if (cmp != 0) return cmp;
        cmp = a.Item2Level.CompareTo(b.Item2Level);
        if (cmp != 0) return cmp;
        cmp = pa.CompareTo(pb);
        if (cmp != 0) return cmp;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    // ------------------------------------------------------------------
    // Atomic reverse-supply bundle builder (single absolute target per call)
    // ------------------------------------------------------------------

    private static bool TryBuildBundle(
        string targetRowId,
        int absoluteTarget,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> workingInventory,
        IReadOnlyDictionary<string, int> committedMultipliers,
        int remainingBudget,
        out Bundle bundle,
        List<AutoPlanningDiagnostic> diagnostics) {

        bundle = new Bundle();
        var stack = new HashSet<(int, string)>();

        bool ok = TryAddRoute(
            targetRowId, absoluteTarget, routesById, stack,
            workingInventory, committedMultipliers, bundle.Multipliers,
            diagnostics);

        if (!ok) {
            bundle.Multipliers.Clear();
            return false;
        }

        foreach (var v in workingInventory.Values) {
            if (v < 0) {
                diagnostics.Add(new AutoPlanningDiagnostic("negative-inventory", targetRowId));
                bundle.Multipliers.Clear();
                return false;
            }
        }

        // bundle.Multipliers holds each route's INCREMENTAL contribution, so the
        // additional parley is simply sum(bundleMul * route.Parley). The outer
        // committed multiplier is irrelevant here because the bundle's
        // additions are already expressed as new exchanges.
        int bundleParley = 0;
        foreach (var (rowId, bundleMul) in bundle.Multipliers) {
            if (bundleMul <= 0) continue;
            if (!routesById.TryGetValue(rowId, out var route)) continue;
            bundleParley = checked(bundleParley + bundleMul * route.Parley);
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
        int absoluteTarget,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        HashSet<(int, string)> stack,
        Dictionary<string, int> workingInventory,
        IReadOnlyDictionary<string, int> committedMultipliers,
        Dictionary<string, int> bundleMultipliers,
        List<AutoPlanningDiagnostic> diagnostics) {

        if (!routesById.TryGetValue(rowId, out var route)) {
            diagnostics.Add(new AutoPlanningDiagnostic("missing-route", rowId));
            return false;
        }

        int existingCommitted = committedMultipliers.TryGetValue(rowId, out var ec) ? ec : 0;
        int existingInBundle = bundleMultipliers.TryGetValue(rowId, out var eb) ? eb : 0;
        int additionalIncrement = absoluteTarget - existingCommitted - existingInBundle;

        if (additionalIncrement <= 0) {
            return true;
        }

        if (absoluteTarget > route.Remaining) {
            diagnostics.Add(new AutoPlanningDiagnostic("exceeds-remaining", rowId));
            return false;
        }

        var key = (route.Group, route.Item1Id);
        if (!stack.Add(key)) {
            diagnostics.Add(new AutoPlanningDiagnostic("cycle", rowId));
            return false;
        }

        try {
            int demand = checked(additionalIncrement * route.Item1Number);
            int available = workingInventory.TryGetValue(route.Item1Id, out var cur) ? cur : 0;
            int deficit = Math.Max(0, demand - available);

            if (deficit > 0) {
                var producers = FindProducers(route.Group, route.Item1Id, routesById);
                if (producers.Count == 0) {
                    diagnostics.Add(new AutoPlanningDiagnostic("no-producer", rowId));
                    return false;
                }

                // Producer fallback: try each producer individually, then combine
                // multiple producers if one alone cannot cover the deficit. Each
                // attempt clones the working state so a failed try does not block
                // the next.
                while (deficit > 0) {
                    bool anyProgress = false;

                    foreach (var producer in producers) {
                        int producerCommitted = committedMultipliers.TryGetValue(producer.RowId, out var pec) ? pec : 0;
                        int producerInBundle = bundleMultipliers.TryGetValue(producer.RowId, out var peb) ? peb : 0;
                        int maxProducerAdd = producer.Remaining - producerCommitted - producerInBundle;
                        if (maxProducerAdd <= 0) continue;

                        int producerAdd = Math.Min(
                            CeilingDivide(deficit, producer.Item2Number),
                            maxProducerAdd);
                        int producerAbsoluteTarget = producerCommitted + producerInBundle + producerAdd;

                        var attemptInventory = new Dictionary<string, int>(workingInventory, StringComparer.Ordinal);
                        var attemptBundleMultipliers = new Dictionary<string, int>(bundleMultipliers, StringComparer.Ordinal);
                        var attemptStack = new HashSet<(int, string)>(stack);

                        if (TryAddRoute(
                                producer.RowId, producerAbsoluteTarget, routesById,
                                attemptStack, attemptInventory, committedMultipliers,
                                attemptBundleMultipliers, diagnostics)) {
                            // Validate the attempt's inventory before committing it.
                            foreach (var v in attemptInventory.Values) {
                                if (v < 0) {
                                    diagnostics.Add(new AutoPlanningDiagnostic("negative-inventory", producer.RowId));
                                    return false;
                                }
                            }
                            bundleMultipliers.Clear();
                            foreach (var kv in attemptBundleMultipliers) bundleMultipliers[kv.Key] = kv.Value;
                            workingInventory.Clear();
                            foreach (var kv in attemptInventory) workingInventory[kv.Key] = kv.Value;
                            deficit = checked(deficit - producerAdd * producer.Item2Number);
                            anyProgress = true;
                            break;
                        }
                    }

                    if (!anyProgress) {
                        diagnostics.Add(new AutoPlanningDiagnostic("no-producer", rowId));
                        return false;
                    }
                }
            }

            bundleMultipliers[rowId] = existingInBundle + additionalIncrement;

            int newItem1 = (workingInventory.TryGetValue(route.Item1Id, out var i1) ? i1 : 0) - demand;
            workingInventory[route.Item1Id] = newItem1;
            int produced = checked(additionalIncrement * route.Item2Number);
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