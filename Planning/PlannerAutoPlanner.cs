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

        // Final-projected-inventory reserve check. Under the final-reserve
        // semantics, initial warehouse stock is freely spendable inside a
        // plan even when the item is reserved, as long as some producer
        // increment (direct or support) eventually replenishes it to the
        // LV5/LV6 target. If the strategy phase left a reserved item below
        // target and no legal producer is available, we record a
        // reserve-no-producer diagnostic so the plan fails atomically.
        CheckFinalProjectedReserves(request, routesById, committed,
            ref committedParley, diagnostics);

        // All-or-nothing: any reserve-* diagnostic means the reserve constraint
        // could not be satisfied. Mark the result failed so the adapter returns
        // ApplySet=null and no live Eq. multiplier is touched.
        bool success = !diagnostics.Any(d => d.Code.StartsWith("reserve-"));
        var finalInventory = success
            ? ApplyMultipliers(request.CurrentInventory, routesById, committed)
            : new Dictionary<string, int>(request.CurrentInventory, StringComparer.Ordinal);
        return new AutoPlanningResult(success, committed, finalInventory, committedParley, diagnostics);
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

        // Plan-relevant reserve (no Universal Reserve Phase): every LV5/LV6
        // reserve is handled atomically inside TryAddRoute / TryBuildBundle
        // when a selected bundle consumes the item as Item1. If a consumed
        // reserve cannot be replenished, TryBuildBundle emits a reserve-*
        // diagnostic and GreedyFillIncrements rolls back any committed
        // progress so the plan can fail atomically.

        // Phase 1: Crow-Coin routes identified by Item2Id (locale-independent).
        // Higher coin output wins; on ties, lower full-bundle parley.
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => r.Item2Id == AutoPlanningRoute.CrowCoinItemId,
            ranker: CompareCrowCoin);

        // Phase 2: remainder budget, restricted to LV4-LV6 input so LV1-LV3
        // routes are never direct Crow Coin remainder targets.
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => r.Item2Id != AutoPlanningRoute.CrowCoinItemId
                         && r.Item1Level >= 4 && r.Item1Level <= 6,
            ranker: CompareRemainder);
    }

    private static void PlanProfitFirst(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {

        // Plan-relevant reserve (no Universal Reserve Phase). Same note as
        // PlanCrowCoinFirst: reserve handling is inside the bundle build.
        // Crow coin is excluded. Direct targets are restricted to LV4-LV6
        // input (LV6→LV7, LV5→LV6, LV4→LV5). LV1-LV3 routes never appear as
        // Profit First candidates — they only show up as upstream supply
        // inside an atomic bundle.
        GreedyFillIncrements(
            request, routesById, committed, ref committedParley, diagnostics,
            filter: r => r.Item2Id != AutoPlanningRoute.CrowCoinItemId
                         && r.Item1Level >= 4 && r.Item1Level <= 6,
            ranker: CompareProfit);
    }

    private static void PlanRestockFirst(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {

        // Phase 1: LV5/LV6 capped restock by largest deficit ratio. Only
        // routes whose produced LV5/LV6 stock is below target are
        // candidates; reserve is satisfied by TryAddRoute's same-group
        // producer pull-in inside each atomic bundle.
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
        if (r.Item2Id == AutoPlanningRoute.CrowCoinItemId) return false;
        if (r.Item2Level != 5 && r.Item2Level != 6) return false;
        int target = r.Item2Level == 5 ? request.Lv5Target : request.Lv6Target;
        if (target <= 0) return false;
        int projected = ProjectedItemInventory(r.Item2Id, request, committed);
        return projected < target;
    }

    private static bool IsUncappedRestockEligible(AutoPlanningRoute r) {
        if (r.Item2Id == AutoPlanningRoute.CrowCoinItemId) return false;
        return r.Item2Level >= 1 && r.Item2Level <= 4;
    }

    private static int GetReserveTarget(string itemId, int itemLevel, AutoPlanningRequest request) {
        if (itemLevel == 5) return request.Lv5Target;
        if (itemLevel == 6) return request.Lv6Target;
        return 0;
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

    // Final-projected-inventory reserve repair. For each LV5/LV6 item whose
    // projected final inventory (initial + planned production - planned
    // consumption) is below its LV5/LV6 target, try to add the minimum legal
    // producer increment that brings it back to target. If no producer is
    // available, record a reserve-no-producer diagnostic. The strategy
    // phase's chain-pull-in already handles the common case; this pass is the
    // final guard for items that the strategy consumed without a chain
    // pull-in (e.g. a direct target whose bundle found enough initial stock
    // to skip the producer fallback). The pass is deterministic, bounded by
    // the number of protected items, and does not turn the planner into an
    // exponential search.
    private static void CheckFinalProjectedReserves(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {
        if (request.Lv5Target <= 0 && request.Lv6Target <= 0) return;
        // Plan-relevant reserve: only items that the plan actually CONSUMES
        // (appear as Item1 in some committed route) are subject to the
        // final projected-inventory check. Items that are only produced
        // but never consumed are not plan-relevant, even if they happen
        // to be LV5/LV6 level.
        var consumed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (rowId, mul) in committed) {
            if (mul <= 0) continue;
            if (!routesById.TryGetValue(rowId, out var r)) continue;
            consumed[r.Item1Id] = consumed.GetValueOrDefault(r.Item1Id) + mul * r.Item1Number;
        }
        var lv5 = new SortedSet<string>(StringComparer.Ordinal);
        var lv6 = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var r in request.Routes) {
            if (r.Item1Level == 5 && consumed.ContainsKey(r.Item1Id)) lv5.Add(r.Item1Id);
            if (r.Item1Level == 6 && consumed.ContainsKey(r.Item1Id)) lv6.Add(r.Item1Id);
        }
        foreach (var itemId in lv5) {
            while (ProjectedItemInventory(itemId, request, committed) < request.Lv5Target) {
                if (!TryAddReserveRepair(request, routesById, committed, ref committedParley,
                        itemId, request.Lv5Target, diagnostics)) return;
            }
        }
        foreach (var itemId in lv6) {
            while (ProjectedItemInventory(itemId, request, committed) < request.Lv6Target) {
                if (!TryAddReserveRepair(request, routesById, committed, ref committedParley,
                        itemId, request.Lv6Target, diagnostics)) return;
            }
        }
    }

    private static bool TryAddReserveRepair(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        string itemId,
        int target,
        List<AutoPlanningDiagnostic> diagnostics) {
        var producers = routesById.Values
            .Where(r => r.Item2Id == itemId && r.Item2Number > 0)
            .OrderBy(r => r.Parley)
            .ThenByDescending(r => r.Item2Number)
            .ThenBy(r => r.RowId, StringComparer.Ordinal)
            .ToList();
        if (producers.Count == 0) {
            diagnostics.Add(new AutoPlanningDiagnostic("reserve-no-producer", itemId));
            return false;
        }
        foreach (var p in producers) {
            int current = committed.TryGetValue(p.RowId, out var c) ? c : 0;
            int headroom = p.Remaining - current;
            if (headroom <= 0) continue;
            int deficit = target - ProjectedItemInventory(itemId, request, committed);
            int add = Math.Max(1, Math.Min(
                CeilingDivide(deficit, p.Item2Number),
                headroom));
            committed[p.RowId] = current + add;
            committedParley = checked(committedParley + add * p.Parley);
            if (committedParley > request.ParleyBudget) {
                committed[p.RowId] = current;
                committedParley = checked(committedParley - add * p.Parley);
                continue;
            }
            return true;
        }
        diagnostics.Add(new AutoPlanningDiagnostic("reserve-no-producer", itemId));
        return false;
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
                .ToList();

            if (candidates.Count == 0) {
                return;
            }

            // Build one +1 bundle per candidate, each with its own working-inventory
            // clone. Per-candidate LOCAL diagnostics keep reserve-* failures
            // inside the probe: a candidate that is not selected does NOT leak
            // its reserve failure into the plan's final diagnostics. Only when
            // this is the strategy's first iteration AND every candidate here
            // fails (i.e. the strategy has no winner at all) is the diagnostic
            // promoted — atomic failure. Once the strategy has committed at
            // least one winner, a later iteration's failed probe is silently
            // abandoned, preserving the prior commits.
            var bundles = new Dictionary<string, Bundle>(StringComparer.Ordinal);
            var reserveFailures = new List<AutoPlanningDiagnostic>();

            foreach (var c in candidates) {
                int currentCommitted = committed.TryGetValue(c.RowId, out var cc) ? cc : 0;
                int absoluteTarget = currentCommitted + 1;

                var attemptInventory = ApplyMultipliers(request.CurrentInventory, routesById, committed);
                int remainingBudget = request.ParleyBudget - committedParley;

                var localDiagnostics = new List<AutoPlanningDiagnostic>();
                if (TryBuildBundle(c.RowId, absoluteTarget, routesById,
                        attemptInventory, committed, remainingBudget,
                        out var bundle, localDiagnostics, request)) {
                    bundles[c.RowId] = bundle;
                }
                else {
                    // A failed probe. The reserve-* entries are only promoted
                    // to the global diagnostics if this iteration ends up with
                    // no viable bundle AND no prior commit landed in this
                    // greedy loop — see atomic-fail branch below.
                    foreach (var d in localDiagnostics) {
                        if (d.Code.StartsWith("reserve-")) {
                            reserveFailures.Add(d);
                        }
                    }
                }
            }

            if (bundles.Count == 0) {
                // No viable bundle in this iteration. Atomic failure only when
                // the strategy has never committed anything: in that case the
                // reserve-constrained route the user intended is the only
                // candidate and it cannot be honored (ApplySet=null).
                // If we have already committed at least one viable bundle in
                // a prior iteration, this iteration's failure is just noise —
                // the strategy's earlier successes stand.
                if (reserveFailures.Count > 0 && committed.Count == 0) {
                    diagnostics.AddRange(reserveFailures);
                    // committed is already empty; nothing to roll back.
                }
                return;
            }

            // Defensive filter: every ranker sorts the full candidate list,
            // but the winner must come from `bundles`. We pre-trim the list
            // to candidates with successful bundles so a misbehaving ranker
            // cannot leak a no-bundle candidate to the top and crash the
            // bundles[winner.RowId] lookup below.
            candidates = candidates.Where(c => bundles.ContainsKey(c.RowId)).ToList();
            if (candidates.Count == 0) {
                return;
            }
            candidates.Sort((a, b) => ranker(a, b, bundles, request, committed));
            var winner = candidates[0];
            var winnerBundle = bundles[winner.RowId];

            // Commit the winner's bundle multiplicities as additions to the master
            // committed map. bundle.Multipliers stores INCREMENTAL contributions
            // (additional exchanges this bundle brings in), so a simple sum is
            // correct — no max() needed. The winner's bundle build succeeded,
            // so its reserve was already satisfied by TryAddRoute's pull-in;
            // no diagnostic promotion is needed here.
            foreach (var (rowId, bundleMul) in winnerBundle.Multipliers) {
                int existing = committed.TryGetValue(rowId, out var cv) ? cv : 0;
                committed[rowId] = existing + bundleMul;
            }

            committedParley += winnerBundle.AdditionalParley;
        }
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

        // Crow Coin remainder phase: prefer HIGHER target tier (LV6→LV7 first,
        // then LV5→LV6, then LV4→LV5). This matches CompareProfit's tier
        // preference. LV1–LV3 are excluded from the candidate filter upstream
        // and never reach this ranker.
        var ba = bundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = bundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        if (ba is null && bb is null) {
            int cmpA = b.Item2Level.CompareTo(a.Item2Level);
            if (cmpA != 0) return cmpA;
            return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
        }
        if (ba is null) return 1;
        if (bb is null) return -1;

        int cmp1 = b.Item2Level.CompareTo(a.Item2Level);
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

        var ba = bundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = bundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        // An unbuildable candidate must NEVER be selected as the winner;
        // otherwise GreedyFillIncrements crashes on bundles[winner.RowId].
        // Mirror CompareProfit / CompareRemainder / CompareCrowCoin: an a-row
        // with no bundle ranks strictly after a b-row with a bundle, and
        // vice versa. Tie-break for two-both-empty pairs uses row id so the
        // selection is deterministic.
        if (ba is null && bb is null) {
            return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
        }
        if (ba is null) return 1;
        if (bb is null) return -1;

        int targetA = a.Item2Level == 5 ? request.Lv5Target : request.Lv6Target;
        int targetB = b.Item2Level == 5 ? request.Lv5Target : request.Lv6Target;
        int projA = ProjectedItemInventory(a.Item2Id, request, committed);
        int projB = ProjectedItemInventory(b.Item2Id, request, committed);
        int deficitA = targetA - projA;
        int deficitB = targetB - projB;

        int cmp = unchecked(deficitB * targetA).CompareTo(unchecked(deficitA * targetB));
        if (cmp != 0) return cmp;
        cmp = ba.AdditionalParley.CompareTo(bb.AdditionalParley);
        if (cmp != 0) return cmp;
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    private static int CompareUncapped(
        AutoPlanningRoute a, AutoPlanningRoute b,
        IReadOnlyDictionary<string, Bundle> bundlesByRow,
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, int> committed) {

        var ba = bundlesByRow.TryGetValue(a.RowId, out var bna) ? bna : null;
        var bb = bundlesByRow.TryGetValue(b.RowId, out var bnb) ? bnb : null;
        // Same null-bundle contract as CompareCapped: a winner must always
        // have a usable bundle. Without this guard, CompareUncapped ranks by
        // projected inventory and the lowest-stock candidate (often the one
        // whose bundle failed because its input is missing) ends up first,
        // then crashes the greedy loop.
        if (ba is null && bb is null) {
            return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
        }
        if (ba is null) return 1;
        if (bb is null) return -1;

        int projA = ProjectedItemInventory(a.Item2Id, request, committed);
        int projB = ProjectedItemInventory(b.Item2Id, request, committed);

        int cmp = projA.CompareTo(projB);
        if (cmp != 0) return cmp;
        cmp = a.Item2Level.CompareTo(b.Item2Level);
        if (cmp != 0) return cmp;
        cmp = ba.AdditionalParley.CompareTo(bb.AdditionalParley);
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
        List<AutoPlanningDiagnostic> diagnostics,
        AutoPlanningRequest request) {

        bundle = new Bundle();
        var stack = new HashSet<(int, string)>();
        var carryOverInventory = request.CarryOverInventory is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(
                request.CarryOverInventory, StringComparer.Ordinal);
        // Plan-relevant reserve: a flag that tracks whether any producer pull-in
        // inside this bundle was triggered by an LV5/LV6 reserve deficit. When
        // set, TryBuildBundle's budget-exceeded classification upgrades to
        // reserve-budget-exceeded, and TryAddRoute's failed producer pull-in
        // emits reserve-no-producer instead of plain "no-producer".
        bool reserveContext = false;

        bool ok = TryAddRoute(
            targetRowId, absoluteTarget, routesById, stack,
            workingInventory, carryOverInventory, committedMultipliers, bundle.Multipliers,
            diagnostics, request, ref reserveContext);

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
            // Reserve-pull-in expanded the bundle past the budget — surface a
            // reserve-budget-exceeded so the caller can fail the plan
            // atomically without losing the lower-level budget-exceeded context.
            diagnostics.Add(new AutoPlanningDiagnostic(
                reserveContext ? "reserve-budget-exceeded" : "budget-exceeded",
                targetRowId));
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
        Dictionary<string, int> carryOverInventory,
        IReadOnlyDictionary<string, int> committedMultipliers,
        Dictionary<string, int> bundleMultipliers,
        List<AutoPlanningDiagnostic> diagnostics,
        AutoPlanningRequest request,
        ref bool reserveContext) {

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
            // Reserve-aware deficit: a reserved item's effective available stock
            // is whatever is above its reserve target. Anything at or below the
            // target may not be consumed. Plan-relevant semantics — the deduction
            // only fires for items the bundle is actually consuming; items the
            // plan never touches are not protected here.
            int reserve = GetReserveTarget(route.Item1Id, route.Item1Level, request);
            bool isReserveContext = reserve > 0
                && (route.Item1Level == 5 || route.Item1Level == 6);
            // Completed exchanges leave physical cargo on the ship. Unlike
            // warehouse stock, that carry-over cargo is intentionally
            // spendable even when it equals the LV5/LV6 reserve target.
            // Planned producer output remains ordinary inventory, so it still
            // replenishes the configured reserve in the usual way.
            int carried = carryOverInventory.TryGetValue(route.Item1Id, out var carriedValue)
                ? Math.Clamp(carriedValue, 0, available)
                : 0;
            // Final-projected-inventory reserve semantics: initial warehouse
            // stock is freely spendable inside a plan, even when the item is
            // reserved, as long as the post-strategy repair pass can bring
            // the projected final inventory back to the LV5/LV6 target.
            // Previously the formula subtracted `reserve` from the initial
            // stock, which forced the chain to produce-before-consume and
            // made the route order depend on whether the producer ran first.
            int effectiveAvailable = available;
            int deficit = Math.Max(0, demand - effectiveAvailable);

            if (deficit > 0) {
                if (isReserveContext) reserveContext = true;
                var producers = FindProducers(route.Group, route.Item1Id, routesById);
                if (producers.Count == 0) {
                    diagnostics.Add(new AutoPlanningDiagnostic(
                        isReserveContext ? "reserve-no-producer" : "no-producer",
                        rowId));
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
                        var attemptCarryOverInventory = new Dictionary<string, int>(
                            carryOverInventory, StringComparer.Ordinal);
                        var attemptBundleMultipliers = new Dictionary<string, int>(bundleMultipliers, StringComparer.Ordinal);
                        var attemptStack = new HashSet<(int, string)>(stack);

                        if (TryAddRoute(
                                producer.RowId, producerAbsoluteTarget, routesById,
                                attemptStack, attemptInventory, attemptCarryOverInventory,
                                committedMultipliers,
                                attemptBundleMultipliers, diagnostics, request,
                                ref reserveContext)) {
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
                            carryOverInventory.Clear();
                            foreach (var kv in attemptCarryOverInventory)
                                carryOverInventory[kv.Key] = kv.Value;
                            deficit = checked(deficit - producerAdd * producer.Item2Number);
                            anyProgress = true;
                            break;
                        }
                    }

                    if (!anyProgress) {
                        diagnostics.Add(new AutoPlanningDiagnostic(
                            isReserveContext ? "reserve-no-producer" : "no-producer",
                            rowId));
                        return false;
                    }
                }
            }

            bundleMultipliers[rowId] = existingInBundle + additionalIncrement;

            int newItem1 = (workingInventory.TryGetValue(route.Item1Id, out var i1) ? i1 : 0) - demand;
            workingInventory[route.Item1Id] = newItem1;
            if (carried > 0) {
                carryOverInventory[route.Item1Id] = Math.Max(0, carried - demand);
            }
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
