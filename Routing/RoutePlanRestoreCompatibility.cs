using System.Text;

namespace iBarter.Routing;

public static class RoutePlanRestoreCompatibility {
    /// <summary>
    /// Audit round 3: when a persisted <c>automatic-route-plan.json</c>
    /// still carries the legacy (Island, Item1, Item2) business-key
    /// RowIds from before PlannerRowId was introduced, the direct
    /// match against <see cref="AutomaticRoutePlanningRequest.Tasks"/>
    /// will miss. This shim performs a one-shot legacy migration:
    ///   * a saved RowId that already matches a PlannerRowId wins
    ///     immediately (no migration);
    ///   * a saved (Island, Item1, Item2) tuple that matches
    ///     <strong>exactly one</strong> current task is migrated to
    ///     that task's PlannerRowId;
    ///   * a saved tuple that matches multiple current tasks is
    ///     treated as ambiguous and the whole plan is reported
    ///     incompatible, so the user knows to regenerate.
    /// </summary>
    public sealed record LegacyRowIdMigrationResult(
        bool IsCompatible,
        IReadOnlyDictionary<string, string> SavedRowIdToCurrentRowId);

    public static LegacyRowIdMigrationResult TryMigrateLegacyRowIds(
        RoutePlan persistedPlan,
        AutomaticRoutePlanningRequest currentRequest,
        out IReadOnlyDictionary<(string, string, string), List<string>>? ambiguousTuples) {
        ambiguousTuples = null;
        var savedBarters = persistedPlan.Routes
            .SelectMany(route => route.Steps.OfType<BarterStep>())
            .ToArray();
        var currentTasks = currentRequest.Tasks.ToArray();
        if (savedBarters.Length == 0 || currentTasks.Length == 0) {
            return new LegacyRowIdMigrationResult(true, new Dictionary<string, string>());
        }
        var currentByRow = currentTasks.ToDictionary(task => task.RowId, StringComparer.Ordinal);

        // Group current tasks by (Island, Item1Id, Item2Id) to detect
        // ambiguous legacy matches.
        var currentByTuple = new Dictionary<(string, string, string), List<RouteBarterTask>>();
        foreach (var task in currentTasks) {
            var key = (task.IslandId, task.Item1Id, task.Item2Id);
            if (!currentByTuple.TryGetValue(key, out var list)) {
                list = new List<RouteBarterTask>();
                currentByTuple[key] = list;
            }
            list.Add(task);
        }

        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new Dictionary<(string, string, string), List<string>>();
        foreach (var saved in savedBarters) {
            if (currentByRow.ContainsKey(saved.RowId)) {
                mapping[saved.RowId] = saved.RowId;
                continue;
            }
            // Try legacy tuple match: "{Island}:{Item1}:{Item2}" and
            // "INVALID:{index}" forms (and any string without a Guid
            // prefix) all map through this branch.
            var tuple = ParseLegacyTuple(saved.RowId);
            if (tuple is null) continue;
            if (!currentByTuple.TryGetValue(tuple.Value, out var candidates)) continue;
            if (candidates.Count > 1) {
                ambiguous.TryGetValue(tuple.Value, out var seenList);
                ambiguous[tuple.Value] = (seenList ?? new List<string>())
                    .Concat(candidates.Select(c => c.RowId))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                continue;
            }
            mapping[saved.RowId] = candidates[0].RowId;
        }
        if (ambiguous.Count > 0) {
            ambiguousTuples = ambiguous;
            return new LegacyRowIdMigrationResult(false, mapping);
        }
        return new LegacyRowIdMigrationResult(true, mapping);
    }

    private static (string IslandId, string Item1Id, string Item2Id)? ParseLegacyTuple(string rowId) {
        // Accept "{Island}:{Item1}:{Item2}" or "{index}:{Island}:{Item1}:{Item2}".
        // Reject anything that already looks like a PlannerRowId
        // (starts with "br-").
        if (string.IsNullOrEmpty(rowId) || rowId.StartsWith("br-", StringComparison.Ordinal))
            return null;
        var parts = rowId.Split(':');
        if (parts.Length == 3)
            return (parts[0], parts[1], parts[2]);
        if (parts.Length == 4)
            return (parts[1], parts[2], parts[3]);
        return null;
    }

    public static bool IsCompatibleAfterProgress(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan persistedPlan,
        IReadOnlySet<string> completedBarterRowIds) {
        if (completedBarterRowIds.Count == 0
            || persistedPlan.Status is not (RoutePlanStatus.Optimal
                or RoutePlanStatus.BestKnownWithinLimit)
            || persistedPlan.Routes.Count == 0)
            return false;

        // First try the strict direct match.
        if (IsCompatibleDirect(currentRequest, persistedPlan, completedBarterRowIds))
            return true;

        // Fall back to the legacy migration. A unique (Island, Item1,
        // Item2) match for every saved RowId succeeds; an ambiguous
        // match fails the whole restore so the user is told to
        // regenerate.
        var migration = TryMigrateLegacyRowIds(persistedPlan, currentRequest, out var ambiguous);
        if (ambiguous is { Count: > 0 }) return false;
        if (migration.IsCompatible)
            return IsCompatibleWithMapping(currentRequest, persistedPlan,
                completedBarterRowIds, migration.SavedRowIdToCurrentRowId);
        return false;
    }

    /// <summary>
    /// Audit round 7: identity-only restore.  The persisted
    /// plan's <c>BarterStep.RowId</c> values may be legacy
    /// <c>{Island}:{Item1}:{Item2}</c> strings (or with a leading
    /// index) rather than the new <c>br-*</c> GUIDs.  When no CK
    /// progress is involved (the user has not yet marked any
    /// barter done), we can re-host the plan on the current
    /// request by mapping every saved RowId to a unique current
    /// task, then run the same <see cref="IsCompatibleBody"/>
    /// check on the mapped plan.  Ambiguous matches refuse the
    /// restore; warehouse / LT / mode mismatches still fail.
    /// </summary>
    public sealed record IdentityMigrationResult(
        bool IsCompatible,
        RoutePlan? MappedPlan,
        IReadOnlyDictionary<(string IslandId, string Item1Id, string Item2Id), List<string>>? AmbiguousTuples,
        IReadOnlyList<string>? MismatchComponents,
        string? MigratedSelectedBarterRowId);

    public static IdentityMigrationResult TryMigrateIdentityOnly(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan persistedPlan,
        string? selectedBarterRowId = null) {
        // Sanity: status + non-empty.
        if (persistedPlan.Status is not (RoutePlanStatus.Optimal
                or RoutePlanStatus.BestKnownWithinLimit)
            || persistedPlan.Routes.Count == 0) {
            return new IdentityMigrationResult(
                false, null, null,
                new[] { "saved plan status or empty routes" },
                null);
        }

        var savedBarters = persistedPlan.Routes
            .SelectMany(route => route.Steps.OfType<BarterStep>())
            .ToList();
        var currentTasks = currentRequest.Tasks.ToArray();
        var currentTasksByRow = currentTasks.ToDictionary(
            t => t.RowId, StringComparer.Ordinal);

        // Audit round 8: EVERY saved BarterStep must map to a
        // current task. Unmapped steps mean a route was deleted
        // or a RowId was renamed — the whole plan is unsafe to
        // restore. The v1 path silently `continue`d past unmapped
        // steps which let invalid plans appear "compatible".
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new Dictionary<(string, string, string), List<string>>();
        var errors = new List<string>();
        foreach (var saved in savedBarters) {
            if (currentTasksByRow.ContainsKey(saved.RowId)) {
                mapping[saved.RowId] = saved.RowId;
                continue;
            }
            // No direct match — try legacy business-tuple.
            var tuple = ParseLegacyTuple(saved.RowId);
            if (tuple is null) {
                // RowId is neither current nor legacy; treat as
                // an unrecoverable mismatch.
                errors.Add(
                    $"saved BarterStep rowId '{saved.RowId}' " +
                    $"is neither a current task nor a legacy " +
                    $"(Island,Item1,Item2) tuple");
                continue;
            }
            // Find all current tasks whose business tuple matches
            // the saved step.  The audit requires that quantity
            // changes be rejected, so we also check that the
            // saved step's Consumed/Produced quantities match the
            // candidate task's Input/Output quantities.  The
            // legacy RowId does not carry quantities, but the
            // saved BarterStep itself does (consumed/produced).
            var key = tuple.Value;
            var savedConsumed = saved.Consumed.Quantity;
            var savedProduced = saved.Produced.Quantity;
            var candidates = currentTasks
                .Where(t => t.IslandId == key.IslandId
                    && t.Item1Id == key.Item1Id
                    && t.Item2Id == key.Item2Id
                    && t.InputQuantity == savedConsumed
                    && t.OutputQuantity == savedProduced)
                .ToList();
            if (candidates.Count == 0) {
                // Tuple doesn't match any current task.  Either
                // the item was removed, the quantities changed, or
                // the island was renamed.
                errors.Add(
                    $"saved BarterStep (Island={key.IslandId} " +
                    $"Item1={key.Item1Id}×{savedConsumed} " +
                    $"Item2={key.Item2Id}×{savedProduced}) " +
                    $"has no current-task match");
                continue;
            }
            if (candidates.Count > 1) {
                if (!ambiguous.TryGetValue(key, out var seenList)) seenList = new List<string>();
                ambiguous[key] = seenList
                    .Concat(candidates.Select(c => c.RowId))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                continue;
            }
            mapping[saved.RowId] = candidates[0].RowId;
        }
        if (ambiguous.Count > 0) {
            return new IdentityMigrationResult(
                false, null, ambiguous, null, null);
        }
        if (errors.Count > 0) {
            return new IdentityMigrationResult(
                false, null, null, errors, null);
        }

        // Apply the mapping: re-host each saved BarterStep on the
        // current task's RowId. Steps that already matched keep
        // their identity. The remapped plan then has the new
        // task catalog's PlannerRowIds.
        var remapped = RemapPersistedPlan(persistedPlan, mapping);
        // Update the InputFingerprint to the current request's
        // fingerprint so the post-restore check (the verifier) and
        // any future direct match all use the same key. The
        // v1 path left the saved fingerprint in place, which
        // caused repeated FingerprintMismatch on the next reload.
        var updated = new RoutePlan(
            remapped.Status, remapped.Routes, remapped.Objective,
            remapped.Diagnostics, RoutePlanFingerprint.Compute(currentRequest));
        // Migrate SelectedBarterRowId through the same mapping.
        string? migratedSelected = null;
        if (selectedBarterRowId is not null
            && mapping.TryGetValue(selectedBarterRowId, out var mappedSelected)) {
            migratedSelected = mappedSelected;
        }
        return new IdentityMigrationResult(true, updated, null, null, migratedSelected);
    }

    private static RoutePlan RemapPersistedPlan(
        RoutePlan source,
        IReadOnlyDictionary<string, string> savedToCurrent) {
        var routes = new List<PlannedRoute>(source.Routes.Count);
        foreach (var route in source.Routes) {
            var newSteps = new List<RouteStep>(route.Steps.Count);
            foreach (var step in route.Steps) {
                if (step is BarterStep barter
                    && savedToCurrent.TryGetValue(barter.RowId, out var mapped)
                    && mapped != barter.RowId) {
                    newSteps.Add(new BarterStep(
                        mapped, barter.IslandId, barter.Consumed, barter.Produced,
                        barter.Load));
                }
                else {
                    newSteps.Add(step);
                }
            }
            routes.Add(new PlannedRoute(
                route.Number, route.StartWarehouseId, route.EndWarehouseId,
                newSteps, route.Distance, route.InitialLT, route.CurrentLT, route.PeakLT));
        }
        return new RoutePlan(source.Status, routes, source.Objective, source.Diagnostics,
            source.InputFingerprint);
    }

    /// <summary>
    /// Internal consistency: warehouses must still exist; LT
    /// invariants must hold; legacy migration must already have
    /// re-id'd any business-tuple RowIds (so we don't re-check
    /// the identity mismatch path here — that lives in the public
    /// IsCompatibleBody).
    /// </summary>
    private static bool IsRestoredPlanInternallyConsistent(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan plan,
        out IReadOnlyList<string>? mismatchComponents) {
        var warehouses = currentRequest.Warehouses.ToDictionary(
            w => w.WarehouseId, StringComparer.Ordinal);
        var errors = new List<string>();
        int expectedRouteNumber = 1;
        foreach (var route in plan.Routes) {
            if (route.Number != expectedRouteNumber++) {
                errors.Add($"route number gap at {route.Number}");
                continue;
            }
            foreach (var step in route.Steps) {
                if (step.Load.TotalWithExtraLT - step.Load.CargoLT != currentRequest.ExtraLT
                    || step.Load.TotalWithExtraLT > currentRequest.TotalLT) {
                    errors.Add(
                        $"step {step.GetType().Name} island={step.IslandId} " +
                        $"LT out of range (Total={step.Load.TotalWithExtraLT} " +
                        $"Extra={currentRequest.ExtraLT} Cargo={step.Load.CargoLT})");
                    continue;
                }
                if (step is WarehousePickupStep pickup
                    && (!warehouses.TryGetValue(pickup.WarehouseId, out var pw)
                        || !StringComparer.Ordinal.Equals(pw.IslandId, pickup.IslandId))) {
                    errors.Add(
                        $"pickup warehouse {pickup.WarehouseId} " +
                        $"on island {pickup.IslandId} not in current request");
                }
                if (step is WarehouseUnloadStep unload
                    && (!warehouses.TryGetValue(unload.WarehouseId, out var uw)
                        || !StringComparer.Ordinal.Equals(uw.IslandId, unload.IslandId))) {
                    errors.Add(
                        $"unload warehouse {unload.WarehouseId} " +
                        $"on island {unload.IslandId} not in current request");
                }
            }
        }
        if (errors.Count > 0) {
            mismatchComponents = errors;
            return false;
        }
        mismatchComponents = null;
        return true;
    }

    private static bool IsCompatibleDirect(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan persistedPlan,
        IReadOnlySet<string> completedBarterRowIds) {
        var savedBarters = persistedPlan.Routes
            .SelectMany(route => route.Steps.OfType<BarterStep>())
            .ToArray();
        if (savedBarters.Length == 0
            || savedBarters.Select(step => step.RowId).Distinct(StringComparer.Ordinal).Count()
                != savedBarters.Length)
            return false;

        var savedByRow = savedBarters.ToDictionary(step => step.RowId, StringComparer.Ordinal);
        var currentTasks = currentRequest.Tasks.ToArray();
        if (currentTasks.Select(task => task.RowId).Distinct(StringComparer.Ordinal).Count()
                != currentTasks.Length
            || currentTasks.Any(task => !savedByRow.ContainsKey(task.RowId)))
            return false;

        var currentByRow = currentTasks.ToDictionary(task => task.RowId, StringComparer.Ordinal);
        if (savedBarters.Any(step => !currentByRow.ContainsKey(step.RowId)
                && !RouteTaskIdentity.IsCompleted(step.RowId, completedBarterRowIds))
            || !savedBarters.Any(step =>
                RouteTaskIdentity.IsCompleted(step.RowId, completedBarterRowIds)))
            return false;

        return IsCompatibleBody(currentRequest, persistedPlan, currentByRow, completedBarterRowIds,
            identity: step => step.RowId);
    }

    private static bool IsCompatibleWithMapping(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan persistedPlan,
        IReadOnlySet<string> completedBarterRowIds,
        IReadOnlyDictionary<string, string> savedToCurrent) {
        var savedBarters = persistedPlan.Routes
            .SelectMany(route => route.Steps.OfType<BarterStep>())
            .ToArray();
        if (savedBarters.Length == 0) return false;
        var currentTasks = currentRequest.Tasks.ToArray();
        var currentByRow = currentTasks.ToDictionary(task => task.RowId, StringComparer.Ordinal);
        foreach (var saved in savedBarters) {
            if (!savedToCurrent.TryGetValue(saved.RowId, out var mapped))
                return false;
            if (!currentByRow.ContainsKey(mapped)
                && !RouteTaskIdentity.IsCompleted(mapped, completedBarterRowIds))
                return false;
        }
        if (!savedBarters.Any(step => RouteTaskIdentity.IsCompleted(
                savedToCurrent.GetValueOrDefault(step.RowId, ""), completedBarterRowIds)))
            return false;
        return IsCompatibleBody(currentRequest, persistedPlan, currentByRow, completedBarterRowIds,
            savedToCurrent);
    }

    private static bool IsCompatibleBody(
        AutomaticRoutePlanningRequest currentRequest,
        RoutePlan persistedPlan,
        IReadOnlyDictionary<string, RouteBarterTask> currentByRow,
        IReadOnlySet<string> completedBarterRowIds,
        IReadOnlyDictionary<string, string>? savedToCurrent = null,
        Func<BarterStep, string>? identity = null) {
        identity ??= step => step.RowId;
        if (!CanReachFirstRemainingBarter(currentRequest, persistedPlan, currentByRow,
                completedBarterRowIds, savedToCurrent, identity))
            return false;
        var warehouses = currentRequest.Warehouses.ToDictionary(
            warehouse => warehouse.WarehouseId, StringComparer.Ordinal);
        int expectedRouteNumber = 1;
        foreach (var route in persistedPlan.Routes) {
            if (route.Number != expectedRouteNumber++) return false;
            foreach (var step in route.Steps) {
                if (step.Load.TotalWithExtraLT - step.Load.CargoLT != currentRequest.ExtraLT
                    || step.Load.TotalWithExtraLT > currentRequest.TotalLT)
                    return false;
                if (step is WarehousePickupStep pickup
                    && (!warehouses.TryGetValue(pickup.WarehouseId, out var pickupWarehouse)
                        || !StringComparer.Ordinal.Equals(pickupWarehouse.IslandId, pickup.IslandId)))
                    return false;
                if (step is WarehouseUnloadStep unload
                    && (!warehouses.TryGetValue(unload.WarehouseId, out var unloadWarehouse)
                        || !StringComparer.Ordinal.Equals(unloadWarehouse.IslandId, unload.IslandId)))
                    return false;
            }
        }
        return true;
    }

    private static bool CanReachFirstRemainingBarter(
        AutomaticRoutePlanningRequest request,
        RoutePlan persistedPlan,
        IReadOnlyDictionary<string, RouteBarterTask> currentByRow,
        IReadOnlySet<string> completedBarterRowIds,
        IReadOnlyDictionary<string, string>? savedToCurrent = null,
        Func<BarterStep, string>? identity = null) {
        identity ??= step => step.RowId;
        foreach (var route in persistedPlan.Routes) {
            if (!route.Steps.OfType<BarterStep>().Any(step => {
                    var key = identity(step);
                    return currentByRow.ContainsKey(savedToCurrent?.GetValueOrDefault(key, key) ?? key);
                }))
                continue;

            var onboard = request.InitialOnBoard.ToDictionary(
                pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var step in route.Steps) {
                switch (step) {
                    case WarehousePickupStep pickup:
                        foreach (var item in pickup.Items)
                            onboard[item.ItemId] = checked(onboard.GetValueOrDefault(item.ItemId) + item.Quantity);
                        break;
                    case WarehouseUnloadStep unload:
                        foreach (var item in unload.Items) {
                            int remaining = onboard.GetValueOrDefault(item.ItemId) - item.Quantity;
                            if (remaining > 0) onboard[item.ItemId] = remaining;
                            else onboard.Remove(item.ItemId);
                        }
                        break;
                    case BarterStep barter: {
                        var stepKey = identity(barter);
                        var mappedKey = savedToCurrent?.GetValueOrDefault(stepKey, stepKey) ?? stepKey;
                        if (currentByRow.ContainsKey(mappedKey)
                            && !RouteTaskIdentity.IsCompleted(mappedKey, completedBarterRowIds))
                            return onboard.GetValueOrDefault(barter.Consumed.ItemId) >= barter.Consumed.Quantity;
                        if (RouteTaskIdentity.IsCompleted(mappedKey, completedBarterRowIds)) {
                            int available = onboard.GetValueOrDefault(barter.Consumed.ItemId);
                            if (available < barter.Consumed.Quantity) return false;
                            int afterConsume = available - barter.Consumed.Quantity;
                            if (afterConsume == 0) onboard.Remove(barter.Consumed.ItemId);
                            else onboard[barter.Consumed.ItemId] = afterConsume;
                            onboard[barter.Produced.ItemId] = checked(
                                onboard.GetValueOrDefault(barter.Produced.ItemId) + barter.Produced.Quantity);
                        }
                        break;
                    }
                }
            }
            return false;
        }
        return true;
    }
}
