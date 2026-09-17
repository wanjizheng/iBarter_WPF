using System.IO;
using System.Reflection;
using iBarter.Routing;

// Reproducible counterexample for the supplied 27-trade save. This is an audit
// fixture, not a route generator or a shortcut around the transport simulator.
internal static class TaggedAlgorithmAudit {
    public static int TraceSearch(string path) {
        var saved = TaggedTransportStorage.Load<TaggedTransportSession>(path) ?? throw new Exception("Missing save.");
        var request = saved.Request with { Settings = saved.Request.Settings with {
            SearchProfile = RouteOptimizationProfile.For(RouteOptimizationMode.Custom) with { TotalTarget = TimeSpan.FromSeconds(30) }
        } };
        var diagnostics = new TaggedSearchDiagnostics();
        var result = new TaggedTransportPlanner().Plan(request, diagnostics: diagnostics);
        string json = System.Text.Json.JsonSerializer.Serialize(diagnostics, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "tagged-search-diagnostics.json"), json);
        Console.WriteLine(json);
        Console.WriteLine($"Result distance={result.Plan?.Distance}; ship reference={result.Plan?.ShipOnlyDistance}; {result.Message}");
        return result.Plan is null ? 1 : 0;
    }
    public static int Run(string path) {
        var saved = TaggedTransportStorage.Load<TaggedTransportSession>(path) ?? throw new Exception("Missing save.");
        var firstOperation = saved.Plan.Steps.First(s => s.Action.Kind != TaggedActionKind.Sail);
        var request = saved.Request with { Settings = saved.Request.Settings with { StartIsland = firstOperation.Action.Location } };
        var sim = new TaggedTransportSimulator(request);
        var state = sim.Initial();
        bool trace = false;
        int missingActions = 0, checkedActions = 0;
        var generator = typeof(TaggedTransportPlanner).GetMethod("Actions", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var step in saved.Plan.Steps.SkipWhile(s => s.Action.Kind == TaggedActionKind.Sail)) Apply(step.Action);
        var baseline = Plan(state);
        var routes = TaggedTransportRoutes.Build(request, baseline);
        if (routes.Length != 4) throw new Exception("This counterexample expects the supplied four-route save.");
        var third = routes[2]; var fourth = routes[3];
        var laterLoad = baseline.Steps.Skip(fourth.Start).First(s => s.Action.Kind == TaggedActionKind.Transfer && s.Action.From.StartsWith("warehouse:"));
        state = sim.Initial();
        foreach (var step in baseline.Steps.Take(third.Start)) Apply(step.Action);
        trace = true;
        double initialPreparationScore = Score(state);
        var preparationScores = new List<double> { initialPreparationScore };
        string wh = "warehouse:" + sim.Port(state.Location)!.WarehouseId;
        Apply(new(TaggedActionKind.Switch, state.Location, "main", "alt"));
        preparationScores.Add(Score(state));
        Apply(new(TaggedActionKind.SummonElephant, state.Location, "alt", "alt-elephant"));
        preparationScores.Add(Score(state));
        Apply(new(TaggedActionKind.StackAtWarehouse, state.Location, wh, "alt-elephant", laterLoad.Action.ItemId, laterLoad.Action.Quantity));
        preparationScores.Add(Score(state));
        Apply(new(TaggedActionKind.Transfer, state.Location, "alt-elephant", "alt", laterLoad.Action.ItemId, laterLoad.Action.Quantity));
        preparationScores.Add(Score(state));
        Apply(new(TaggedActionKind.Switch, state.Location, "alt", "main"));
        preparationScores.Add(Score(state));
        int lastTrade = Enumerable.Range(third.Start, third.End - third.Start).Last(i => baseline.Steps[i].Action.Kind == TaggedActionKind.Barter);
        foreach (var step in baseline.Steps.Skip(third.Start).Take(lastTrade - third.Start + 1)) Apply(step.Action);
        Apply(new(TaggedActionKind.Sail, "Lema", state.Location, "Lema"));
        string output = state.Cargo["ship"].First(i => i.Value >= 4 && request.Items[i.Key].UnitWeight == 2000).Key;
        for (int i = 0; i < 2; i++) Apply(new(TaggedActionKind.Transfer, state.Location, "ship", "main", output, 1));
        Apply(new(TaggedActionKind.Switch, state.Location, "main", "alt"));
        Apply(new(TaggedActionKind.Transfer, state.Location, "alt", "ship", laterLoad.Action.ItemId, laterLoad.Action.Quantity));
        for (int i = 0; i < 2; i++) Apply(new(TaggedActionKind.Transfer, state.Location, "ship", "alt", output, 1));
        Apply(new(TaggedActionKind.Switch, state.Location, "alt", "main"));
        foreach (var step in baseline.Steps.Skip(fourth.Start).Where(s => s.Action.Kind is TaggedActionKind.Sail or TaggedActionKind.Barter)) Apply(step.Action);
        wh = "warehouse:" + sim.Port(state.Location)!.WarehouseId;
        Unload("ship"); Unload("main");
        Apply(new(TaggedActionKind.Switch, state.Location, "main", "alt"));
        Unload("alt");
        Apply(new(TaggedActionKind.Switch, state.Location, "alt", "main"));
        var candidate = Plan(state);
        if (!sim.Verify(candidate, out _, out var error)) throw new Exception(error);
        if (candidate.Distance >= baseline.Distance) throw new Exception("Counterexample is not shorter.");
        var reducedRoutes = TaggedTransportRoutes.Build(request, candidate);
        if (reducedRoutes.Length >= routes.Length) throw new Exception("Counterexample does not reduce trips.");
        TaggedTransportStorage.Save(Path.Combine(AppContext.BaseDirectory, "tagged-audit-counterexample.json"), new TaggedTransportSession(1, request, candidate, 0));
        Console.WriteLine($"VERIFIED COUNTEREXAMPLE: {routes.Length} routes / {baseline.Distance:F3} m -> {reducedRoutes.Length} routes / {candidate.Distance:F3} m; saved {baseline.Distance - candidate.Distance:F3} m.");
        Console.WriteLine($"Preload alt {laterLoad.Action.ItemId} x{laterLoad.Action.Quantity} via its elephant; replace Sausan -> Iliya -> Dallae by Sausan -> Lema -> Dallae; Lema transfers {output} x2 to each character and alt input to ship; unload all at Epheria.");
        Console.WriteLine($"All {request.Trades.Length} trade rows complete; every action and inventory replay verified; max sailing load {candidate.Steps.Where(s => s.Action.Kind == TaggedActionKind.Sail).Max(s => s.ShipLT)} / {request.ShipLimitLT} LT.");
        Console.WriteLine($"Action coverage: {checkedActions - missingActions}/{checkedActions} candidate suffix operations are emitted by the current action generator; missing={missingActions}.");
        Console.WriteLine($"Preloading score before/after each of five useful operations: {string.Join(", ", preparationScores.Select(s => s.ToString("F3")))}. No queue preference for the completed preload.");
        return 0;

        void Apply(TaggedAction action) {
            if (action.Kind == TaggedActionKind.Sail) action = action with { From = state.Location, To = action.Location };
            if (trace) {
                var actions = (IEnumerable<TaggedAction>)generator.Invoke(null, [sim, state])!;
                checkedActions++;
                if (!actions.Contains(action)) { missingActions++; Console.WriteLine("Not emitted: " + action); }
            }
            if (!sim.TryApply(state, action, out var next, out var error)) throw new Exception($"{action}: {error}; ship={sim.Weight(state, "ship")}; main={sim.Weight(state, "main")}; alt={sim.Weight(state, "alt")}");
            state = next;
        }
        void Unload(string source) {
            foreach (var item in state.Cargo[source].Where(i => request.Items[i.Key].UnitWeight > 0).ToArray())
                Apply(new(TaggedActionKind.Transfer, state.Location, source, wh, item.Key, item.Value));
        }
        TaggedTransportPlan Plan(TaggedTransportState s) => new(request.Fingerprint(), s.Steps.ToArray(), s.Seconds, s.SailingSeconds,
            s.Seconds - s.SailingSeconds, s.Distance, "VerifiedAuditCandidate");
        double Score(TaggedTransportState s) => s.Distance + 20000 * s.Remaining.Select((n, i) => (double)n / request.Trades[i].Exchanges).Sum();
    }
}
