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

        // Plan-relevant reserve: every proposed LV5/LV6 consumer is hardened
        // against final inventory before it can be committed.  This preserves
        // valid earlier work while rejecting only the over-consuming candidate.

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

        // Plan-relevant reserve is enforced per candidate by
        // GreedyFillIncrements before ranking and commit.
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
        // routes whose produced LV5/LV6 stock is below target are candidates;
        // consuming a protected item is then checked by per-candidate reserve
        // hardening before it can enter the plan.
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
        if (AutoPlanningRoute.IsVendorOnlyOceanReward(r.Item2Id)) return false;
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

    // Final-projected-inventory reserve repair. Runs as a fixed-point
    // fixed-point loop: at each iteration, collect every LV5/LV6 item whose
    // projected final inventory is below the LV5/LV6 target AND that the
    // plan actually consumes (appears as Item1 in some committed route),
    // then for each deficit try to add a same-group producer by calling
    // the existing atomic TryBuildBundle. The bundle's full upstream
    // chain is verified inside TryBuildBundle, so adding the producer
    // can never introduce negative inventory or an unbacked producer.
    //
    // The loop terminates when:
    //   - all reserves are met (success),
    //   - a full pass makes no progress (no-progress diagnostic),
    //   - or a safety iteration limit is reached.
    //
    // Every attempt snapshots committed + committedParley so a failed
    // attempt is rolled back atomically; partial repair increments never
    // leak into the result. A failure records a precise diagnostic
    // (reserve-no-producer / reserve-budget-exceeded /
    // reserve-upstream-unavailable / reserve-remaining-exhausted) so
    // the UI log is truthful.
    private static void CheckFinalProjectedReserves(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {
        if (request.Lv5Target <= 0 && request.Lv6Target <= 0) return;

        // Atomic repair: snapshot the entire committed + parley state
        // before any repair attempt. If the repair loop ends without
        // resolving every deficit, roll back to the snapshot so a
        // partial repair never leaks into the result.
        var snapshotCommitted = new Dictionary<string, int>(committed, StringComparer.Ordinal);
        int snapshotParley = committedParley;
        int snapshotDiagnosticCount = diagnostics.Count;

        if (!TryResolveReserveDeficits(request, routesById, committed,
                ref committedParley, diagnostics)) {
            // Roll back the entire repair: no partial producers, no
            // partial parley, no partial diagnostics. The caller (Plan)
            // sees committed == pre-repair committed and parley ==
            // pre-repair parley, and emits a single reserve-no-producer
            // diagnostic per unresolved deficit.
            committed.Clear();
            foreach (var kv in snapshotCommitted) committed[kv.Key] = kv.Value;
            committedParley = snapshotParley;
            while (diagnostics.Count > snapshotDiagnosticCount)
                diagnostics.RemoveAt(diagnostics.Count - 1);
            var remaining = CollectReserveDeficits(request, routesById, committed);
            foreach (var deficit in remaining.OrderBy(d => d.ItemId, StringComparer.Ordinal)) {
                if (deficit.MissingQuantity > 0 && !HasReserveDiagnostic(diagnostics, deficit.ItemId)) {
                    diagnostics.Add(new AutoPlanningDiagnostic(
                        deficit.FailureCode, deficit.ItemId));
                }
            }
        }
    }

    // Resolves final LV5/LV6 deficits against the supplied state.  This is
    // deliberately reusable: normal planning invokes it as a final safety net,
    // while GreedyFillIncrements invokes it on a private candidate probe before
    // allowing that candidate to become part of the real plan.
    private static bool TryResolveReserveDeficits(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        List<AutoPlanningDiagnostic> diagnostics) {
        if (request.Lv5Target <= 0 && request.Lv6Target <= 0) return true;

        int maxIterations = Math.Max(8, request.Routes.Count * 2);
        for (int iter = 0; iter < maxIterations; iter++) {
            var deficits = CollectReserveDeficits(request, routesById, committed,
                diagnostics);
            if (deficits.Count == 0) return true;

            bool anyProgress = false;
            foreach (var deficit in deficits.OrderBy(d => d.ItemId, StringComparer.Ordinal)) {
                if (deficit.MissingQuantity <= 0) continue;
                if (TryRepairDeficit(request, routesById, committed, ref committedParley,
                        deficit, diagnostics)) {
                    anyProgress = true;
                }
            }
            if (!anyProgress) return false;
        }
        return false;
    }

    // Candidate reserve hardening is the crucial distinction between a final
    // inventory target and a late, lossy repair attempt.  We first combine the
    // proposed incremental bundle with the already committed plan, then prove
    // that every LV5/LV6 final reserve remains satisfiable.  The probe owns all
    // mutable state; rejected candidates cannot alter parley, multipliers or
    // diagnostics of another candidate.
    private static Bundle? TryHardenCandidateAgainstReserve(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        IReadOnlyDictionary<string, int> committed,
        int committedParley,
        Bundle candidateBundle,
        List<AutoPlanningDiagnostic> localDiagnostics) {
        if (request.Lv5Target <= 0 && request.Lv6Target <= 0) {
            return candidateBundle;
        }

        var probeCommitted = new Dictionary<string, int>(committed, StringComparer.Ordinal);
        foreach (var (rowId, increment) in candidateBundle.Multipliers) {
            int existing = probeCommitted.TryGetValue(rowId, out var value) ? value : 0;
            probeCommitted[rowId] = checked(existing + increment);
        }
        int probeParley = checked(committedParley + candidateBundle.AdditionalParley);

        if (!TryResolveReserveDeficits(request, routesById, probeCommitted,
                ref probeParley, localDiagnostics)) {
            // TryRepairDeficit deliberately keeps failed internal producer
            // diagnostics local.  A rejected top-level candidate still needs a
            // precise reason so GreedyFillIncrements can report an atomic
            // reserve failure when there is no alternative candidate at all.
            foreach (var deficit in CollectReserveDeficits(request, routesById, probeCommitted)) {
                if (deficit.MissingQuantity > 0 && !HasReserveDiagnostic(localDiagnostics, deficit.ItemId)) {
                    localDiagnostics.Add(new AutoPlanningDiagnostic(
                        deficit.FailureCode, deficit.ItemId));
                }
            }
            return null;
        }

        // Convert the final probe totals back into INCREMENTAL bundle values.
        // Writing probe totals directly would double-count prior committed
        // exchanges when the winner is merged into the master map.
        var hardened = new Bundle {
            AdditionalParley = checked(probeParley - committedParley)
        };
        foreach (var (rowId, probeValue) in probeCommitted) {
            int prior = committed.TryGetValue(rowId, out var value) ? value : 0;
            int increment = probeValue - prior;
            if (increment > 0) {
                hardened.Multipliers[rowId] = increment;
            }
        }
        return hardened;
    }

    // A single LV5/LV6 reserve deficit is a GLOBAL final-inventory
    // constraint on the item, so a deficit carries the set of ALL consumer
    // groups whose committed routes spend the item. The repair phase may
    // satisfy the deficit using producers from any of these groups (never
    // from an unrelated group that does not consume the item).
    //
    // Level: the item's Item1Level across all consumers (must agree; if
    // routes declare the same ItemId at different levels, that is an input
    // validation error and surfaces as a diagnostic).
    private readonly record struct ReserveDeficit(
        string ItemId,
        int ItemLevel,
        IReadOnlyList<int> ConsumerGroups,
        int Target,
        int ProjectedFinal,
        int MissingQuantity,
        string FailureCode);

    // Collect every LV5/LV6 item whose projected final inventory is
    // below the LV5/LV6 target AND that the plan actually consumes.
    // "Actually consumes" = at least one committed route has the item
    // as its Item1Id. Each item produces exactly ONE deficit, carrying
    // the complete set of consumer groups. Inconsistent Item1Level
    // declarations across consumers for the same ItemId surface as an
    // explicit diagnostic and produce no deficit (so the strategy's own
    // failure path takes over).
    private static List<ReserveDeficit> CollectReserveDeficits(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        IReadOnlyDictionary<string, int> committed,
        List<AutoPlanningDiagnostic>? diagnostics = null) {
        var deficits = new List<ReserveDeficit>();
        if (request.Lv5Target <= 0 && request.Lv6Target <= 0) return deficits;
        // First pass: aggregate by item id — every consumer group that
        // commits the item, and the (single) Item1Level for the item.
        var consumerInfo = new Dictionary<string, (SortedSet<int> Groups, int Level, bool Inconsistent)>(
            StringComparer.Ordinal);
        foreach (var r in request.Routes.OrderBy(x => x.RowId, StringComparer.Ordinal)) {
            if (r.Item1Level != 5 && r.Item1Level != 6) continue;
            if (!committed.TryGetValue(r.RowId, out var m) || m <= 0) continue;
            if (consumerInfo.TryGetValue(r.Item1Id, out var existing)) {
                existing.Groups.Add(r.Group);
                if (existing.Level != r.Item1Level) {
                    existing.Inconsistent = true;
                }
                consumerInfo[r.Item1Id] = existing;
            }
            else {
                consumerInfo[r.Item1Id] = (
                    new SortedSet<int>(new[] { r.Group }),
                    r.Item1Level,
                    false);
            }
        }
        foreach (var (itemId, info) in consumerInfo) {
            int target = info.Level == 6 ? request.Lv6Target : request.Lv5Target;
            if (target <= 0) continue;
            int projected = ProjectedItemInventory(itemId, request, committed);
            if (info.Inconsistent) {
                diagnostics?.Add(new AutoPlanningDiagnostic(
                    "reserve-level-inconsistent", itemId));
                continue;
            }
            if (projected < target) {
                deficits.Add(new ReserveDeficit(
                    itemId,
                    info.Level,
                    info.Groups.ToArray(),
                    target,
                    projected,
                    target - projected,
                    "reserve-no-producer"));
            }
        }
        return deficits;
    }

    private static bool HasReserveDiagnostic(
        List<AutoPlanningDiagnostic> diagnostics, string itemId) =>
        diagnostics.Any(d => d.Code.StartsWith("reserve-") && d.RowId == itemId);

    // Try to repair a single deficit by adding a producer from any of the
    // consumer groups (never from an unrelated group). Every candidate
    // bundle is built first via the existing atomic TryBuildBundle, then
    // ranked by:
    //   1. Bundle that fully eliminates the deficit wins over partial.
    //   2. Lower Bundle.AdditionalParley wins.
    //   3. Greater deficit reduction wins.
    //   4. Fewer new routes introduced wins.
    //   5. Lower producer.Group wins.
    //   6. Lower producer.RowId wins.
    // Each candidate attempt uses a per-build working-inventory clone
    // from the shared base attemptInventory (line below), so a failed
    // bundle build cannot leak into the next attempt or into committed.
    private static bool TryRepairDeficit(
        AutoPlanningRequest request,
        IReadOnlyDictionary<string, AutoPlanningRoute> routesById,
        Dictionary<string, int> committed,
        ref int committedParley,
        ReserveDeficit deficit,
        List<AutoPlanningDiagnostic> diagnostics) {
        // Candidate producers: any route that produces the deficit item
        // AND lives in a consumer group, AND has Remaining capacity.
        var consumerGroupSet = new HashSet<int>(deficit.ConsumerGroups);
        var candidates = routesById.Values
            .Where(r => consumerGroupSet.Contains(r.Group)
                && r.Item2Id == deficit.ItemId
                && r.Item2Number > 0
                && r.Remaining > (committed.TryGetValue(r.RowId, out var ca) ? ca : 0))
            .OrderBy(r => r.Group)
            .ThenBy(r => r.Parley)
            .ThenByDescending(r => r.Item2Number)
            .ThenBy(r => r.RowId, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0) {
            // No candidate in any consumer group: genuine reserve-no-producer.
            return false;
        }

        // Shared base inventory: every candidate bundle sees the same
        // starting state, so the comparison is apples-to-apples.
        var attemptInventory = ApplyMultipliers(
            request.CurrentInventory, routesById, committed);
        int remainingBudget = request.ParleyBudget - committedParley;
        int deficitBeforeRepair = deficit.MissingQuantity;

        var built = new List<(AutoPlanningRoute Producer, Bundle Bundle)>();
        foreach (var p in candidates) {
            int already = committed.TryGetValue(p.RowId, out var a) ? a : 0;
            int maxAdd = p.Remaining - already;
            if (maxAdd <= 0) continue;
            int desiredAdd = Math.Min(
                CeilingDivide(deficitBeforeRepair, p.Item2Number),
                maxAdd);
            int absoluteTarget = already + desiredAdd;

            var localDiagnostics = new List<AutoPlanningDiagnostic>();
            if (TryBuildBundle(p.RowId, absoluteTarget, routesById,
                    attemptInventory, committed, remainingBudget,
                    out var bundle, localDiagnostics, request)) {
                built.Add((p, bundle));
            }
        }

        if (built.Count == 0) {
            return false;
        }

        // Rank by full-bundle cost, not by surface producer parley.
        built.Sort((x, y) => CompareRepairCandidates(
            x.Producer, x.Bundle, y.Producer, y.Bundle,
            deficitBeforeRepair, p => p.Item2Number));

        // Commit the winner atomically. TryBuildBundle already mutates
        // only its own working-inventory clone; committed is untouched
        // until we merge here.
        var winner = built[0];
        committedParley = checked(committedParley + winner.Bundle.AdditionalParley);
        foreach (var kv in winner.Bundle.Multipliers) {
            int existing = committed.TryGetValue(kv.Key, out var cv) ? cv : 0;
            committed[kv.Key] = existing + kv.Value;
        }
        return true;
    }

    private static int CompareRepairCandidates(
        AutoPlanningRoute a, Bundle ba,
        AutoPlanningRoute b, Bundle bb,
        int deficitBeforeRepair,
        Func<AutoPlanningRoute, int> yieldOf) {
        // 1. Full-fix wins over partial.
        int reductionA = BundleYield(a, ba, yieldOf);
        int reductionB = BundleYield(b, bb, yieldOf);
        bool fullA = reductionA >= deficitBeforeRepair;
        bool fullB = reductionB >= deficitBeforeRepair;
        if (fullA != fullB) return fullA ? -1 : 1;

        // 2. Lower full-bundle parley wins.
        int cmp = ba.AdditionalParley.CompareTo(bb.AdditionalParley);
        if (cmp != 0) return cmp;

        // 3. Greater deficit reduction wins.
        cmp = reductionB.CompareTo(reductionA);
        if (cmp != 0) return cmp;

        // 4. Fewer new routes introduced wins.
        cmp = ba.Multipliers.Count.CompareTo(bb.Multipliers.Count);
        if (cmp != 0) return cmp;

        // 5. Lower producer group wins.
        cmp = a.Group.CompareTo(b.Group);
        if (cmp != 0) return cmp;

        // 6. Lower producer row id wins.
        return StringComparer.Ordinal.Compare(a.RowId, b.RowId);
    }

    private static int BundleYield(
        AutoPlanningRoute producer, Bundle bundle,
        Func<AutoPlanningRoute, int> yieldOf) {
        int multiplier = bundle.Multipliers.GetValueOrDefault(producer.RowId, 0);
        int perExchange = yieldOf(producer);
        return Math.Max(0, multiplier * perExchange);
    }

    // Classify a failed bundle build into a precise reserve diagnostic.
    // If the bundle already emitted a reserve-* diagnostic with a more
    // specific code, use that. Otherwise classify based on what we
    // observed: every candidate producer was either at remaining cap or
    // exhausted the budget.
    private static void ClassifyBundleFailure(
        List<AutoPlanningDiagnostic> localDiagnostics,
        ReserveDeficit deficit,
        AutoPlanningRoute producer,
        List<AutoPlanningDiagnostic> globalDiagnostics) {
        // The bundle build emits its own diagnostics in localDiagnostics.
        // We deliberately do NOT re-add them to globalDiagnostics here;
        // the caller emits a single consolidated diagnostic per deficit
        // after all candidates are tried. This keeps the log clean and
        // avoids duplicate entries.
    }

    private static Dictionary<string, int> SnapshotCommitted(
        Dictionary<string, int> committed) =>
        new(committed, StringComparer.Ordinal);

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
                    // A route can be locally executable but still leave the
                    // final LV5/LV6 inventory below its configured minimum.
                    // Validate that constraint *before* ranking or committing
                    // the candidate.  This is what caps Grandiha at four
                    // exchanges when 800058 must finish at seven: the fifth
                    // candidate is rejected rather than accepted and repaired
                    // too late after its CK producer is unavailable.
                    var hardened = TryHardenCandidateAgainstReserve(
                        request, routesById, committed, committedParley,
                        bundle, localDiagnostics);
                    if (hardened is not null) {
                        bundles[c.RowId] = hardened;
                    }
                    else {
                        foreach (var d in localDiagnostics) {
                            if (d.Code.StartsWith("reserve-")) {
                                reserveFailures.Add(d);
                            }
                        }
                    }
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
