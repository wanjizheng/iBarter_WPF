using System.Diagnostics;

namespace iBarter.Routing;

/// <summary>Persistent search over complete voyages; every accepted candidate is replayed by the cargo simulator.</summary>
internal static class TaggedVoyageSearch {
    private sealed record Voyage(TaggedVoyageJob[] Jobs, string End);
    private sealed record Edit(int Left, int Right, Voyage[] Replacement);

    public static TaggedTransportState Improve(TaggedTransportSimulator sim, TaggedTransportState initial,
        TaggedTransportState incumbent, Stopwatch watch, double seconds, CancellationToken token,
        Action<TaggedSearchProgress>? progress, TaggedSearchDiagnostics? diagnostics, ref int evaluated) {
        var request = sim.Request;
        var best = incumbent;
        var elite = new List<TaggedTransportState> { best };
        var random = new Random(73691);
        double lastProgress = -1, lastImprovement = watch.Elapsed.TotalSeconds;
        int count = evaluated;
        // Keep only compact whole-candidate fingerprints, never millions of cargo states.
        // FIFO eviction bounds memory without restarting the search or losing the incumbent.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fifo = new Queue<string>();
        bool Stopped() => token.IsCancellationRequested || watch.Elapsed.TotalSeconds >= seconds
            || request.Settings.SearchProfile is null && count >= request.Settings.MaxStates
            || request.Settings.SearchProfile?.UsesExtremeConvergence == true
                && watch.Elapsed.TotalSeconds - lastImprovement >= request.Settings.SearchProfile.ExtremeNoImprovementTimeout.TotalSeconds;
        var compiler = new TaggedVoyageCompiler(sim, Stopped);
        var seed = best;
        bool systematic = true;
        while (!Stopped()) {
            var plan = new TaggedTransportPlan("", seed.Steps.ToArray(), seed.Seconds, seed.SailingSeconds,
                seed.Seconds - seed.SailingSeconds, seed.Distance, "");
            var routes = TaggedTransportRoutes.Build(request, plan);
            if (routes.Length == 0) break;
            var voyages = routes.Select(r => new Voyage(plan.Steps.Skip(r.Start).Take(r.End - r.Start)
                .Where(s => s.Action.Kind == TaggedActionKind.Barter).Select(s => new TaggedVoyageJob(s.Action.TradeIndex, s.Action.Quantity)).ToArray(),
                plan.Steps[r.End - 1].Action.Location)).ToArray();
            var prefixes = new Dictionary<int, TaggedTransportState>();
            var replay = initial;
            for (int i = 0; i < plan.Steps.Length; i++) {
                if (routes.Any(r => r.Start == i)) prefixes.TryAdd(i, replay);
                if (!sim.TryApply(replay, plan.Steps[i].Action, out replay, out _)) throw new InvalidOperationException("Invalid search incumbent.");
            }
            bool improved = false;
            string seedKey = string.Join(";", seed.Steps.Select(s => s.Action.ToString()));
            // The seed identity includes inventory history, not just island order.
            string seedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seedKey)));
            IEnumerable<Edit> edits = systematic ? Neighbours(voyages).Concat(FinalWarehouses(voyages, request)) : Perturb(voyages, random, 128);
            foreach (var edit in edits) {
                if (Stopped()) break;
                var prefix = prefixes[routes[edit.Left].Start];
                for (int packing = 0; packing < (request.Settings.AllowOverloadedSailing ? 6 : 3) && !Stopped(); packing++) {
                    string key = seedHash + ":" + edit.Left + ":" + edit.Right + ":" + packing + ":" + string.Join("/",
                        edit.Replacement.Select(v => v.End + ":" + string.Join(",", v.Jobs.Select(j => $"{j.TradeIndex}.{j.Quantity}"))));
                    if (!seen.Add(key)) continue;
                    fifo.Enqueue(key); if (fifo.Count > 50000) seen.Remove(fifo.Dequeue());
                    count++;
                    if (diagnostics is not null) diagnostics.VoyageCandidates++;
                    if (watch.Elapsed.TotalSeconds - lastProgress >= 1) {
                        progress?.Invoke(new(watch.Elapsed.TotalSeconds, seconds, count, best.Distance));
                        lastProgress = watch.Elapsed.TotalSeconds;
                    }
                    TaggedTransportState? candidate = prefix;
                    foreach (var voyage in edit.Replacement) {
                        candidate = compiler.Compile(candidate, voyage.Jobs, voyage.End, packing,
                            prepareAtWarehouse: sim.Port(candidate.Location)?.WarehouseId.Length > 0);
                        if (candidate is null) break;
                    }
                    if (candidate is null || Stopped()) continue;
                    foreach (var step in plan.Steps.Skip(routes[edit.Right].End)) {
                        var action = step.Action;
                        if (action.Kind == TaggedActionKind.Sail) action = action with { From = candidate.Location };
                        if (!sim.TryApply(candidate, action, out candidate, out _)) { candidate = null; break; }
                    }
                    if (candidate is null || !sim.Complete(candidate)) continue;
                    if (diagnostics is not null) diagnostics.CompleteCandidates++;
                    if (TaggedDistanceObjective.Compare(request, candidate, best) < 0) {
                        best = candidate; improved = true; lastImprovement = watch.Elapsed.TotalSeconds;
                        if (diagnostics is not null) diagnostics.BetterCandidates++;
                    }
                    // Keep a few different feasible schedules, including slightly longer ones,
                    // so a later merge need not improve every intermediate plan.
                    if (candidate.Distance <= best.Distance * 1.15 && !elite.Any(e => SameSchedule(e, candidate))) {
                        elite.Add(candidate);
                        elite.Sort((a, b) => TaggedDistanceObjective.Compare(request, a, b));
                        if (elite.Count > 12) elite.RemoveAt(elite.Count - 1);
                    }
                }
            }
            if (improved) { seed = best; systematic = true; }
            else {
                if (request.Settings.SearchProfile?.UsesExtremeSearch != true) break;
                seed = elite[random.Next(elite.Count)]; systematic = false;
            }
        }
        evaluated = count;
        return best;
    }

    private static bool SameSchedule(TaggedTransportState a, TaggedTransportState b) => a.Distance == b.Distance
        && a.Steps.Count == b.Steps.Count && a.Steps.Select(s => s.Action).SequenceEqual(b.Steps.Select(s => s.Action));

    private static IEnumerable<Edit> FinalWarehouses(Voyage[] voyages, TaggedTransportRequest request) {
        for (int left = 0; left < voyages.Length; left++)
            foreach (var port in request.Settings.Ports.Where(p => p.Enabled && p.WarehouseId.Length > 0)) {
                var block = voyages.Skip(left).ToArray();
                block[^1] = block[^1] with { End = port.IslandId };
                yield return new(left, voyages.Length - 1, block);
                yield return new(left, voyages.Length - 1, [new(block.SelectMany(v => v.Jobs).ToArray(), port.IslandId)]);
            }
    }

    private static IEnumerable<Edit> Neighbours(Voyage[] voyages) {
        // Merge full voyages first: exposes the value of preloading later inputs.
        for (int width = 2; width <= voyages.Length; width++)
            for (int left = 0; left + width <= voyages.Length; left++) {
                int right = left + width - 1;
                var jobs = voyages.Skip(left).Take(width).SelectMany(v => v.Jobs).ToArray();
                yield return new(left, right, [new(jobs, voyages[right].End)]);
                // Ordinary seeding may split one exchange row to fit normal ship
                // output LT. Recombine quantities under TAG's own loading rules.
                yield return new(left, right, [new(CombineQuantities(jobs), voyages[right].End)]);
                yield return new(left, right, [new(voyages.Skip(left).Take(width).Reverse().SelectMany(v => v.Jobs).ToArray(), voyages[right].End)]);
            }
        for (int v = 0; v < voyages.Length; v++) {
            yield return new(v, v, [voyages[v]]);
            yield return new(v, v, [voyages[v] with { Jobs = CombineQuantities(voyages[v].Jobs) }]);
            foreach (var jobs in Orders(voyages[v].Jobs)) yield return new(v, v, [voyages[v] with { Jobs = jobs }]);
        }
        for (int left = 0; left < voyages.Length; left++)
            for (int right = left + 1; right < voyages.Length; right++) {
                var block = voyages.Skip(left).Take(right - left + 1).ToArray();
                var swapped = block.ToArray(); (swapped[0], swapped[^1]) = (swapped[^1], swapped[0]);
                swapped[^1] = swapped[^1] with { End = block[^1].End };
                yield return new(left, right, swapped);
                foreach (bool backwards in new[] { false, true }) {
                    int source = backwards ? block.Length - 1 : 0, target = backwards ? 0 : block.Length - 1;
                    if (block[source].Jobs.Length <= 1) continue;
                    for (int i = 0; i < block[source].Jobs.Length; i++)
                        for (int j = 0; j <= block[target].Jobs.Length; j++) {
                            var moved = block.ToArray(); var destination = block[target].Jobs.ToList();
                            destination.Insert(j, block[source].Jobs[i]);
                            moved[source] = moved[source] with { Jobs = block[source].Jobs.Where((_, n) => n != i).ToArray() };
                            moved[target] = moved[target] with { Jobs = destination.ToArray() };
                            yield return new(left, right, moved);
                        }
                }
            }
    }

    private static TaggedVoyageJob[] CombineQuantities(IEnumerable<TaggedVoyageJob> jobs) => jobs
        .GroupBy(j => j.TradeIndex).Select(g => new TaggedVoyageJob(g.Key, g.Sum(j => j.Quantity))).ToArray();

    private static IEnumerable<TaggedVoyageJob[]> Orders(TaggedVoyageJob[] source) {
        for (int i = 0; i < source.Length; i++)
            for (int j = i + 1; j < source.Length; j++) {
                var reversed = source.ToArray(); Array.Reverse(reversed, i, j - i + 1); yield return reversed;
                var moved = source.ToList(); var job = moved[j]; moved.RemoveAt(j); moved.Insert(i, job); yield return moved.ToArray();
                moved = source.ToList(); job = moved[i]; moved.RemoveAt(i); moved.Insert(j, job); yield return moved.ToArray();
            }
    }

    private static IEnumerable<Edit> Perturb(Voyage[] voyages, Random random, int count) {
        for (int attempt = 0; attempt < count; attempt++) {
            int left = random.Next(voyages.Length), right = random.Next(left, voyages.Length);
            var all = voyages.Skip(left).Take(right - left + 1).SelectMany(v => v.Jobs).ToList();
            for (int n = 0; n < 1 + random.Next(4); n++) {
                int a = random.Next(all.Count), b = random.Next(all.Count); var job = all[a]; all.RemoveAt(a); all.Insert(b, job);
            }
            int parts = random.Next(1, Math.Min(all.Count, right - left + 2) + 1);
            var result = new List<Voyage>(); int offset = 0;
            for (int part = 0; part < parts; part++) {
                int take = part == parts - 1 ? all.Count - offset : random.Next(1, all.Count - offset - (parts - part - 1) + 1);
                result.Add(new(all.Skip(offset).Take(take).ToArray(), voyages[Math.Min(right, left + part)].End)); offset += take;
            }
            result[^1] = result[^1] with { End = voyages[right].End };
            yield return new(left, right, result.ToArray());
        }
    }
}
