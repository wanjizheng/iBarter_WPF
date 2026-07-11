namespace iBarter.Planning;

/// <summary>
/// Deterministic, UI-independent planner for the iBarter trade routes.
/// All inputs are immutable; the public <see cref="Plan"/> method never
/// mutates its <see cref="AutoPlanningRequest"/>. Strategy implementations
/// must produce identical multipliers for identical inputs.
///
/// Semantics (per the post-review spec):
/// - "Add one feasible target exchange at a time" means the planner increments
///   the target's multiplier by exactly 1 per iteration. Each increment is an
///   atomic bundle — all upstream reverse-supply exchanges either all land or
///   all fail.
/// - A target may be re-picked across iterations until either its Remaining is
///   exhausted or its next-increment bundle cannot fit in the budget.
/// - Strategy phase 2 (lower-priority routes) only runs after phase 1 stops
///   making progress, then re-ranks from scratch after every successful commit
///   so shared inventory changes are reflected immediately.
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
    // Strategy drivers
    // ------------------------------------------------------------------

    private static void PlanCrowCoinFirst(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {

        // Phase 1: Crow-Coin-producing routes. Ranked by full bundle output-per-parley
        // so a route with a cheap reverse-supply chain beats an expensive direct route.
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => r.ProducesCrowCoin,
            bundleRanker: CompareBundleOutputPerParley);

        // Phase 2: remainder budget, no crow coin output allowed. Lowest input LV first
        // (LV4 → LV5 → LV6 → LV7) — when no crow coin route is feasible we fall back to
        // refilling whatever stock is cheapest to grow.
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => !r.ProducesCrowCoin,
            bundleRanker: CompareRemainderBundle);
    }

    private static void PlanProfitFirst(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {

        // Crow coin is excluded. No leaf-target filter — when the top tier route can't
        // fit any increment, the planner simply degrades to the next tier. Whether an
        // upstream producer is required is decided per-increment by the bundle builder
        // (FindProducers walks in-group producers only).
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => !r.ProducesCrowCoin,
            bundleRanker: CompareProfitBundle);
    }

    private static void PlanRestockFirst(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {

        // Phase 1: LV5/LV6 capped restock. Each increment must improve the winning
        // item's deficit ratio; overshoot is allowed only when a single indivisible
        // exchange crosses the target.
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => IsCappedRestockEligible(r, request, committed),
            bundleRanker: (a, b, ctx) => CompareCappedBundle(a, b, ctx));

        // Phase 2: LV1-LV4 uncapped — spend remaining parley on the lowest-projected
        // inventory item. Tier-1 inputs have no implicit cap so we fill until the
        // route's Remaining is exhausted or the budget runs out.
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => IsUncappedRestockEligible(r),
            bundleRanker: (a, b, ctx) => CompareUncappedBundle(a, b, ctx));
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
            if (route.Item2Id == itemId) {
                baseQty += mul * route.Item2Number;
            }
            if (route.Item1Id == itemId) {
                baseQty -= mul * route.Item1Number;
            }
        }
        return baseQty;
    }

    // ------------------------------------------------------------------
    // Greedy per-increment loop. Each iteration re-ranks candidates from scratch
    // (using freshly-built bundles for the rankers that need bundle cost) so shared
    // inventory is reflected immediately. A target that succeeds once stays in the
    // candidate pool — its next increment will be attempted in a later iteration
    // until either Remaining is reached or its next bundle cannot fit in the budget.
    // ------------------------------------------------------------------

    private delegate int BundleRanker(
        AutoPlanningRoute a,
        AutoPlanningRoute b,
        GreedyContext ctx);

    private sealed class GreedyContext {
        public AutoPlanningRequest Request = null!;
        public IReadOnlyDictionary<string, AutoPlanningRoute> RoutesById = null!;
        public Dictionary<string, int> Committed = null!;
        public int CommittedParley;
        public Dictionary<string, Bundle> BundlesByRow = new(StringComparer.Ordinal);
    }

    private static void GreedyFillIncrements(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics,
        Func<AutoPlanningRoute, bool> filter,
        BundleRanker bundleRanker) {

        var ctx = new GreedyContext {
            Request = request,
            RoutesById = routesById,
            Committed = committed,
            CommittedParley = committedParley,
        };

        while (true) {
            var candidates = request.Routes
                .Where(filter)
                .Where(r => committed.TryGetValue(r.RowId, out var c) ? c < r.Remaining : r.Remaining > 0)
                .ToList();

            if (candidates.Count == 0) {
                committedParley = ctx.CommittedParley;
                return;
            }

            // Build the largest feasible bundle per candidate. Try the maximum useful
            // target first (which is full Remaining for a leaf route, or downstream-
            // demand-bound for an intermediate producer); shrink by 1 until something
            // fits or we hit committed+1. A candidate with no feasible bundle is
            // removed from this iteration's pool — its chain is broken for now and
            // may become feasible later when shared upstream routes have been
            // committed by other iterations.
            ctx.BundlesByRow.Clear();
            foreach (var c in candidates) {
                var workingInventory = ApplyMultipliers(request.CurrentInventory, routesById, committed);
                int remainingBudget = request.ParleyBudget - ctx.CommittedParley;
                int currentCommitted = committed.TryGetValue(c.RowId, out var cc) ? cc : 0;

                Bundle? bundle = null;
                int maxUsefulTarget = ComputeMaxUsefulTarget(c, committed, request, routesById);
                int tryTarget = Math.Min(currentCommitted + c.Remaining, maxUsefulTarget);

                for (int t = tryTarget; t > currentCommitted; t--) {
                    if (TryBuildBundle(c.RowId, t, routesById,
                            workingInventory, committed, remainingBudget,
                            out var candidateBundle, diagnostics)) {
                        bundle = candidateBundle;
                        break;
                    }
                }

                if (bundle is not null) {
                    ctx.BundlesByRow[c.RowId] = bundle;
                }
            }

            if (ctx.BundlesByRow.Count == 0) {
                committedParley = ctx.CommittedParley;
                return;
            }

            candidates.Sort((a, b) => bundleRanker(a, b, ctx));

            var winner = candidates[0];
            var winnerBundle = ctx.BundlesByRow[winner.RowId];

            // Commit the winning bundle into the master state. Merge by max so a
            // producer already at a higher committed value keeps its value; only the
            // additive growth beyond outer committed is charged.
            foreach (var (rowId, bundleMul) in winnerBundle.Multipliers) {
                int existing = committed.TryGetValue(rowId, out var cv) ? cv : 0;
                int merged = Math.Max(existing, existing + bundleMul);
                if (merged > existing) {
                    committed[rowId] = merged;
                }
            }

            ctx.CommittedParley += winnerBundle.AdditionalParley;
        }
    }

    // ------------------------------------------------------------------
    // Demand-aware target computation
    // ------------------------------------------------------------------

    // For each candidate, compute the largest target the rest of the plan still
    // needs. A leaf route (output not consumed by any other route in the request)
    // is always demand-bound by its own Remaining. An intermediate producer is
    // additionally capped by the downstream demand for its Item2 — without this
    // cap, the ranker would keep picking the cheapest intermediate even after its
    // output has been fully consumed, wasting budget on inventory nobody reads.
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

        // The candidate's absolute target must satisfy downstream demand (up to its
        // Remaining). If currentCommitted already exceeds that, we still allow it
        // to stay (subsequent loops won't grow it past maxByRemaining).
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
    // Bundle rankers
    // ------------------------------------------------------------------

    private static int CompareBundleOutputPerParley(
        AutoPlanningRoute a, AutoPlanningRoute b, GreedyContext ctx) {
        // Higher target output / bundle parley wins. Cross-multiply to stay integer.
        // Only ranks routes that successfully built a bundle; missing bundle loses.
        var ba = ctx.BundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = ctx.BundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        if (ba is null && bb is null) return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
        if (ba is null) return 1;
        if (bb is null) return -1;
        int outA = a.Item2Number; // target row contribution; full chain output adds via bundle.Multipliers keys
        int outB = b.Item2Number;
        int cmp = unchecked((long)outB * ba.AdditionalParley).CompareTo(unchecked((long)outA * bb.AdditionalParley));
        if (cmp != 0) return cmp;
        cmp = b.Item2Number.CompareTo(a.Item2Number); // higher coin output first on tie
        if (cmp != 0) return cmp;
        cmp = a.Parley.CompareTo(b.Parley);
        if (cmp != 0) return cmp;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    private static int CompareRemainderBundle(
        AutoPlanningRoute a, AutoPlanningRoute b, GreedyContext ctx) {
        // Lower input LV first (LV4 → LV5 → LV6 → LV7). On tie, prefer lower bundle
        // parley (cheapest to grow), then lower target row parley, then row id.
        var ba = ctx.BundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = ctx.BundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        if (ba is null && bb is null) {
            int cmp = a.Item1Level.CompareTo(b.Item1Level);
            if (cmp != 0) return cmp;
            return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
        }
        if (ba is null) return 1;
        if (bb is null) return -1;
        int c = a.Item1Level.CompareTo(b.Item1Level);
        if (c != 0) return c;
        c = ba.AdditionalParley.CompareTo(bb.AdditionalParley);
        if (c != 0) return c;
        c = a.Parley.CompareTo(b.Parley);
        if (c != 0) return c;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    private static int CompareProfitBundle(
        AutoPlanningRoute a, AutoPlanningRoute b, GreedyContext ctx) {
        // Higher target tier first (LV7 > LV6 > LV5 > LV4). On tie, prefer the bundle
        // with the higher output-per-parley ratio (full chain, not just target row).
        var ba = ctx.BundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = ctx.BundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        if (ba is null && bb is null) {
            int cmp = b.Item2Level.CompareTo(a.Item2Level);
            if (cmp != 0) return cmp;
            return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
        }
        if (ba is null) return 1;
        if (bb is null) return -1;
        int c = b.Item2Level.CompareTo(a.Item2Level);
        if (c != 0) return c;
        long lhsNum = (long)a.Item2Number * bb.AdditionalParley;
        long rhsNum = (long)b.Item2Number * ba.AdditionalParley;
        c = rhsNum.CompareTo(lhsNum);
        if (c != 0) return c;
        c = ba.AdditionalParley.CompareTo(bb.AdditionalParley);
        if (c != 0) return c;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    private static int CompareCappedBundle(
        AutoPlanningRoute a, AutoPlanningRoute b, GreedyContext ctx) {
        // Higher deficit ratio wins (deficitA / targetA vs deficitB / targetB).
        // On tie, prefer lower bundle parley so we don't burn budget on a chain
        // whose marginal gain is identical to a cheaper chain.
        int targetA = a.Item2Level == 5 ? ctx.Request.Lv5Target : ctx.Request.Lv6Target;
        int targetB = b.Item2Level == 5 ? ctx.Request.Lv5Target : ctx.Request.Lv6Target;
        int projA = ProjectedItemInventory(a.Item2Id, ctx.Request, ctx.Committed);
        int projB = ProjectedItemInventory(b.Item2Id, ctx.Request, ctx.Committed);
        int deficitA = targetA - projA;
        int deficitB = targetB - projB;
        int c = unchecked(deficitB * targetA).CompareTo(unchecked(deficitA * targetB));
        if (c != 0) return c;
        var ba = ctx.BundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = ctx.BundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        int pa = ba?.AdditionalParley ?? int.MaxValue;
        int pb = bb?.AdditionalParley ?? int.MaxValue;
        c = pa.CompareTo(pb);
        if (c != 0) return c;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    private static int CompareUncappedBundle(
        AutoPlanningRoute a, AutoPlanningRoute b, GreedyContext ctx) {
        // Lowest projected inventory first; on tie, lower output level, lower bundle
        // parley, lower row id.
        int projA = ProjectedItemInventory(a.Item2Id, ctx.Request, ctx.Committed);
        int projB = ProjectedItemInventory(b.Item2Id, ctx.Request, ctx.Committed);
        int c = projA.CompareTo(projB);
        if (c != 0) return c;
        c = a.Item2Level.CompareTo(b.Item2Level);
        if (c != 0) return c;
        var ba = ctx.BundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = ctx.BundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        int pa = ba?.AdditionalParley ?? int.MaxValue;
        int pb = bb?.AdditionalParley ?? int.MaxValue;
        c = pa.CompareTo(pb);
        if (c != 0) return c;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    // ------------------------------------------------------------------
    // Atomic reverse-supply bundle builder
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

        foreach (var kvp in workingInventory) {
            if (kvp.Value < 0) {
                diagnostics.Add(new AutoPlanningDiagnostic("negative-inventory", targetRowId));
                bundle.Multipliers.Clear();
                return false;
            }
        }

        // Compute the bundle's incremental parley. bundleMultipliers stores each
        // route's INCREMENTAL contribution (the number of new exchanges the bundle
        // adds, not the absolute target). So AdditionalParley is simply the sum of
        // `bundleMul * route.Parley` for every touched route — the outer committed
        // multiplier is irrelevant here because the bundle's additions are already
        // expressed as new exchanges.
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

        // The bundle's running total for this route (set by prior recursive calls in
        // the same bundle) plus the outer committed value tells us how many MORE
        // exchanges we still need to add. Calling with absoluteTarget <= current is a
        // no-op.
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

                foreach (var producer in producers) {
                    int producerAdd = CeilingDivide(deficit, producer.Item2Number);

                    int producerCommitted = committedMultipliers.TryGetValue(producer.RowId, out var pec) ? pec : 0;
                    int producerInBundle = bundleMultipliers.TryGetValue(producer.RowId, out var peb) ? peb : 0;
                    int producerAbsoluteTarget = producerCommitted + producerInBundle + producerAdd;

                    if (!TryAddRoute(
                            producer.RowId, producerAbsoluteTarget, routesById, stack,
                            workingInventory, committedMultipliers, bundleMultipliers,
                            diagnostics)) {
                        return false;
                    }

                    deficit = checked(deficit - producerAdd * producer.Item2Number);
                    if (deficit <= 0) {
                        break;
                    }
                }
            }

            if (deficit > 0) {
                diagnostics.Add(new AutoPlanningDiagnostic("insufficient-inventory", rowId));
                return false;
            }

            // Commit this route's contribution to the bundle. The bundle's running
            // total for this route is committed + bundleMultiplier; downstream merges
            // compute the additional parley from the difference.
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