using Syncfusion.Windows.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using iBarter.Navigation;
using iBarter.Routing;

namespace iBarter.ViewModel {
    public class ShipCargoViewModel : NotificationObject {
        private AutomaticRouteCoordinator? routeCoordinator;
        private IReadOnlyList<BarterRouteStepViewModel> manualSteps = [];
        public ShipCargoViewModel() {
            CargoDetails = new ObservableCollection<Barter>();
            CargoDetails.CollectionChanged += CargoDetails_CollectionChanged;
        }

        private void CargoDetails_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
            if (App.myfmMain != null) {
                //SaveData();
            }
        }

        private ObservableCollection<Barter> _cargodetails;

        public ObservableCollection<Barter> CargoDetails {
            get { return _cargodetails; }
            set { _cargodetails = value; }
        }

        public CargoMode Mode => routeCoordinator?.Mode ?? CargoMode.Manual;
        public IReadOnlyList<AutomaticRouteStepViewModel> AutomaticSteps =>
            routeCoordinator?.VisibleAutomaticSteps ?? [];
        public IReadOnlyList<BarterRouteStepViewModel> ManualSteps => manualSteps;
        public IReadOnlyList<RouteSelectionOption> RouteOptions => routeCoordinator?.RouteOptions ?? [];

        public ManualCargoProjection RefreshManualSteps(int extraLT) {
            var barters = CargoDetails.ToArray();
            var inputs = barters.Select((barter, index) => new ManualCargoStepInput(
                $"manual:{index}:{barter.IsLandName}",
                barter.IsLandName,
                barter.Item1.ItemID,
                barter.ExchangeQuantity * barter.Item1Number,
                GetWeightFromLevel(barter.Item1.ItemLV),
                barter.Item2.ItemID,
                barter.ExchangeQuantity * barter.Item2Number,
                GetWeightFromLevel(barter.Item2.ItemLV))).ToArray();
            var projection = ManualCargoProjector.Project(inputs, Math.Max(0, extraLT));
            manualSteps = projection.Steps.Select((projected, index) => {
                var barter = barters[index];
                var routeStep = new BarterStep(
                    projected.Step.RowId,
                    projected.Step.IslandId,
                    new RouteItemQuantity(projected.Step.Item1Id, projected.Step.InputQuantity),
                    new RouteItemQuantity(projected.Step.Item2Id, projected.Step.OutputQuantity),
                    projected.Load);
                var items = new Dictionary<string, RouteItem>(StringComparer.Ordinal) {
                    [projected.Step.Item1Id] = new(
                        projected.Step.Item1Id, barter.Item1NameDisplay, 0, projected.Step.Item1UnitWeight),
                    [projected.Step.Item2Id] = new(
                        projected.Step.Item2Id, barter.Item2NameDisplay, 0, projected.Step.Item2UnitWeight),
                };
                return new BarterRouteStepViewModel(
                    routeStep, barter.IsLandNameDisplay, items, barter);
            }).ToArray();
            RaisePropertyChanged(nameof(ManualSteps));
            return projection;
        }

        private static int GetWeightFromLevel(string level) =>
            int.TryParse(level, out int parsed) ? CargoWeightTable.GetWeightForLevel(parsed) : 0;

        public void AttachRouteCoordinator(AutomaticRouteCoordinator coordinator) {
            if (routeCoordinator is not null)
                routeCoordinator.PropertyChanged -= RouteCoordinator_PropertyChanged;
            routeCoordinator = coordinator;
            routeCoordinator.PropertyChanged += RouteCoordinator_PropertyChanged;
            RaisePropertyChanged(nameof(Mode));
            RaisePropertyChanged(nameof(AutomaticSteps));
            RaisePropertyChanged(nameof(RouteOptions));
        }

        private void RouteCoordinator_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName is nameof(AutomaticRouteCoordinator.Mode))
                RaisePropertyChanged(nameof(Mode));
            if (e.PropertyName is nameof(AutomaticRouteCoordinator.VisibleAutomaticSteps))
                RaisePropertyChanged(nameof(AutomaticSteps));
            if (e.PropertyName is nameof(AutomaticRouteCoordinator.RouteOptions))
                RaisePropertyChanged(nameof(RouteOptions));
        }

        // Re-orders CargoDetails so barters that belong to a chain appear
        // in the order you execute them. Barters whose Item2 is consumed by
        // another cargo barter (e.g. Aemei barter producing Dragon Statues,
        // which the Oborn barter then consumes) sort to the top; barters
        // that stand alone (no other cargo barter takes their Item2) fall
        // to the end in their original insertion order.
        //
        // The chain edges are derived from the cargo itself, not from the
        // global planner barters: two barters in the cargo are linked when
        // one's Item2 equals another's Item1. The "downstream" of a barter
        // is the set of cargo barters that consume what it produces.
        //
        // For the user's example (Aemei barter A -> DS, Oborn barter DS ->
        // CC): Aemei has a downstream link to Oborn (Oborn consumes DS),
        // Oborn has none. Chain depth(Aemei) = 1, Chain depth(Oborn) = 0,
        // so Aemei sorts above Oborn - matching "first trade 20 dragon
        // statues at Aemei, then use 4 at Oborn".
        //
        // The reorder is in-memory only (no auto-save on the cargo list -
        // see ShipCargoControl.SaveData for the explicit save path), and
        // uses Move() so each Barter instance keeps its identity and the
        // PropertyGrid selection survives the reorder.
        public void SortByBarterChain() {
            var cargo = CargoDetails.ToList();
            if (cargo.Count <= 1) {
                return;
            }

            // Index cargo barters by their Item1 (the consumed item). This
            // lets us answer "which cargo barters consume item X?" in O(1).
            // Multiple barters in the same cargo can theoretically consume
            // the same item (split-chain), so the lookup value is a list.
            var consumers = new Dictionary<string, List<Barter>>(StringComparer.Ordinal);
            foreach (var b in cargo) {
                string consumed = b.Item1?.ItemName;
                if (string.IsNullOrEmpty(consumed)) {
                    continue;
                }
                if (!consumers.TryGetValue(consumed, out var list)) {
                    list = new List<Barter>();
                    consumers[consumed] = list;
                }
                list.Add(b);
            }

            // Memoized DFS. chainDepth(barter) = longest downward path
            // length through the cargo's barter graph. A barter with no
            // downstream (its Item2 is not consumed by any other cargo
            // barter) gets depth 0 and falls to the bottom of the sorted
            // list. The visiting set guards against accidental cycles in
            // the cargo graph (e.g. user-crafted loops) by treating a
            // back-edge as terminating the path at 0.
            var depthCache = new Dictionary<Barter, int>();
            var visiting = new HashSet<Barter>();

            int ChainDepth(Barter b) {
                if (depthCache.TryGetValue(b, out int cached)) {
                    return cached;
                }
                if (visiting.Contains(b)) {
                    return 0;
                }

                visiting.Add(b);
                int max = -1;
                string produced = b.Item2?.ItemName;
                if (!string.IsNullOrEmpty(produced)
                    && consumers.TryGetValue(produced, out var downstream)) {
                    foreach (var child in downstream) {
                        if (ReferenceEquals(child, b)) {
                            continue; // skip self-edge
                        }
                        int d = ChainDepth(child);
                        if (d > max) max = d;
                    }
                }
                visiting.Remove(b);

                int result = max + 1; // leaves: -1 + 1 = 0
                depthCache[b] = result;
                return result;
            }

            var indexed = new List<(Barter Barter, int OriginalIndex, int Depth)>(cargo.Count);
            for (int i = 0; i < cargo.Count; i++) {
                indexed.Add((cargo[i], i, ChainDepth(cargo[i])));
            }

            var sorted = indexed
                .OrderByDescending(x => x.Depth)
                .ThenBy(x => x.OriginalIndex)
                .Select(x => x.Barter)
                .ToList();

            for (int target = 0; target < sorted.Count; target++) {
                int current = CargoDetails.IndexOf(sorted[target]);
                if (current > target) {
                    CargoDetails.Move(current, target);
                }
            }

            App.myCFun.Log(
                Localization.LanguageService.Instance.Localize("str.Log.ShipCargo.Sorted"),
                Brushes.DarkOliveGreen);
        }

        // Solves the cargo as a Sequential Ordering Problem with a twist: the
        // starting island is NOT hardcoded - it is derived from the first
        // barter's input item, on the principle "sail from the warehouse
        // that actually holds the thing you're about to trade". Concretely:
//   1. Look at CargoDetails[0].Item1's per-city storage quantities
        //      (Items.StorageVeliaQuantity_{Velia|Iliya|Epheria|Ancado}).
        //   2. Pick the city with the highest non-zero quantity and map
        //      it to the corresponding barter island (Velia->Olvia,
        //      Iliya->Iliya, Epheria->Epheria, Ancado->Sausan).
        //   3. From that start island, greedily walk to the nearest
        //      unvisited barter whose chain prereqs are satisfied, then
        //      repeat - "second-nearest island -> third-nearest -> ...".
// Falls back to the first barter's own island (or Olvia) if the
        // input item has zero stock at every warehouse, and falls back to
        // SortByBarterChain if anything goes sideways (missing island,
// chain cycle, etc.).
        //
        // Why greedy, not Held-Karp: the user explicitly wants the
        // "pick-the-nearest-next" intuition, and a greedy walk with chain
        // constraints produces a sensible route in O(N^2) without the
        // 2^N memory blowup. Held-Karp is still wired in as a private
        // fallback below if you ever want to compare the two.
        public void SolveOptimalRoute() {
            var cargo = CargoDetails.ToList();
            if (cargo.Count == 0) {
                return;
            }
            if (cargo.Count == 1) {
                App.myCFun.Log(
                    Localization.LanguageService.Instance.Localize("str.Log.ShipCargo.OptimalRoute.Solo"),
                    Brushes.DarkOliveGreen);
                return;
            }

            var startIsland = ResolveStartIslandFromCargo(cargo, verbose: true);
            if (startIsland == null) {
                App.myCFun.Log(
                    Localization.LanguageService.Instance.Localize("str.Log.ShipCargo.OptimalRoute.NoStartIsland"),
                    Brushes.Red);
                return;
            }

            int N = cargo.Count;

            // Build prereq[i] = set of barter indices that must precede i.
            // A barter j is a prereq of i iff cargo[j].Item2 == cargo[i].Item1
            // (j produces what i consumes). This mirrors the chain edges
            // used by SortByBarterChain.
            //
            // Matching is best-effort across two criteria - first by
            // English ItemName (the canonical CSV name), then by ItemID
            // (numeric catalog id) as a fallback. ItemID catches the
            // case where ItemName strings diverge (display vs catalog,
            // zh-TW vs English, hydration mismatches) but the two items
            // are the same thing under the hood.
            var prereqs = new HashSet<int>[N];
            var produces = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var producesById = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < N; i++) {
                prereqs[i] = new HashSet<int>();
                string produced = cargo[i].Item2?.ItemName;
                if (string.IsNullOrEmpty(produced)) {
                    continue;
                }
                if (!produces.TryGetValue(produced, out var list)) {
                    list = new List<int>();
                    produces[produced] = list;
                }
                list.Add(i);

                string? producedId = cargo[i].Item2?.ItemID;
                if (!string.IsNullOrEmpty(producedId)) {
                    if (!producesById.TryGetValue(producedId, out var listById)) {
                        listById = new List<int>();
                        producesById[producedId] = listById;
                    }
                    if (!listById.Contains(i)) {
                        listById.Add(i);
                    }
                }
            }
            for (int i = 0; i < N; i++) {
                string consumed = cargo[i].Item1?.ItemName;
                string consumedId = cargo[i].Item1?.ItemID;
                // Skip chain detection for items the user has flagged as
                // "end-product" / non-chainable (Crow Coin and friends).
                // The solver still visits the barter - it just won't
                // refuse to visit it on the grounds of an otherwise-
                // detected "X produces Crow Coin" edge.
                if (IsExcludedFromChain(consumed, consumedId)) {
                    try {
                        App.myCFun?.Log(
                            $"Optimal Route: skipping chain check for barter [{i}] {cargo[i].IsLandName} (item '{consumed}' is in CHAIN_EXCLUDED_ITEMS - end-product currency)",
                            System.Windows.Media.Brushes.Gray);
                    }
                    catch { }
                    continue;
                }
                bool matched = false;
                if (!string.IsNullOrEmpty(consumed)
                    && produces.TryGetValue(consumed, out var producers)) {
                    foreach (var j in producers) {
                        if (j != i) {
                            prereqs[i].Add(j);
                        }
                    }
                    matched = producers.Count > 0;
                }
                if (!matched) {
                    if (!string.IsNullOrEmpty(consumedId)
                        && producesById.TryGetValue(consumedId, out var producersById)) {
                        foreach (var j in producersById) {
                            if (j != i) {
                                prereqs[i].Add(j);
                            }
                        }
                    }
                }
            }

            // Diagnostic: log the chain edges the solver thinks exist
            // between barters in this cargo. Helps the user verify the
            // chain detection didn't misfire when the route order
            // surprises them. Format: "Midnight (idx 3) -> Balvege (idx 0)".
            for (int i = 0; i < N; i++) {
                if (prereqs[i].Count == 0) continue;
                var sb = new System.Text.StringBuilder();
                sb.Append("chain edges:");
                foreach (var j in prereqs[i]) {
                    var cons = cargo[i].Item1Name;
                    var prod = cargo[j].Item2Name;
                    sb.Append($" {cargo[j].Item2Name}({j})->{cargo[i].Item1Name}({i});");
                }
                try {
                    App.myCFun?.Log("Optimal Route: " + sb.ToString(),
                        System.Windows.Media.Brushes.DimGray);
                }
                catch { }
            }

            // Drop any barters without a resolved island (defensive -
            // shouldn't happen given ResolveCatalogIsland on the Barter
            // setter, but if it does we'd rather bail to chain-sort than
            // crash on a null Island reference below).
            var visitable = new List<int>(N);
            for (int i = 0; i < N; i++) {
                if (cargo[i].IsLand != null) {
                    visitable.Add(i);
                }
            }
            if (visitable.Count == 0) {
                App.myCFun.Log(
                    Localization.LanguageService.Instance.Localize("str.Log.ShipCargo.OptimalRoute.MissingIsland"),
                    Brushes.Yellow);
                SortByBarterChain();
                return;
            }

            // Greedy nearest-neighbor walk from the warehouse-derived
            // start island. Each step picks the unvisited barter whose
            // island is closest to the current position and whose chain
            // prereqs are already satisfied. The "current position"
            // advances to each visited barter's island, so the second
            // step is "nearest to first step's island", the third is
            // "nearest to second step's island", etc.
            //
            // Two-pass loop to keep the algorithm making progress even
            // when chain detection misses an edge. Pass 1 honours
            // prereqs strictly; if pass 1 reaches a state where the
            // remaining barters are all prereq-blocked (the previous
            // behaviour was to abandon the route and append remaining
            // barters in their original cargo order, which produced
            // lines like "x -> Rickun -> Cox_Pirate -> Midnight"
            // purely because of insertion accident), pass 2 falls
            // back to distance-only nearest-neighbor so the route
            // still has a sensible distance profile. The tradeoff is
            // that pass 2 may violate a chain the user actually
            // intended; in that case the second-pass log line tells
            // them why.
            var visited = new bool[N];
            var order = new List<int>(N);
            var currentIsland = startIsland;
            bool fellBackToChainlessNN = false;

            for (int step = 0; step < visitable.Count; step++) {
                double bestDist = double.MaxValue;
                int bestIdx = -1;
                for (int k = 0; k < visitable.Count; k++) {
                    int i = visitable[k];
                    if (visited[i]) {
                        continue;
                    }
                    bool prereqMet = true;
                    foreach (var p in prereqs[i]) {
                        if (!visited[p]) {
                            prereqMet = false;
                            break;
                        }
                    }
                    if (!prereqMet) {
                        continue;
                    }
                    var isl = cargo[i].IsLand;
                    if (isl == null) {
                        continue;
                    }
                    double d = DistanceBetween(currentIsland, isl);
                    if (d < bestDist) {
                        bestDist = d;
                        bestIdx = i;
                    }
                }

                if (bestIdx == -1) {
                    // Pass 1 fell off the cliff: every remaining
                    // unvisited barter has at least one prereq that
                    // hasn't been satisfied. This typically means
                    // chain detection missed an edge (display-name
                    // mismatch between a producer and a consumer,
                    // even after the ItemID fallback) or the user
                    // built a true chain cycle. Either way, refusing
                    // to continue left the cargo listing looking
                    // arbitrary. Fall back to a chain-less nearest
                    // neighbour sweep that ignores prereqs entirely;
                    // the route distance profile will still be
                    // sensible, even if it reorders one or two chain
                    // edges the user may have wanted.
                    fellBackToChainlessNN = true;
                    for (int k = 0; k < visitable.Count; k++) {
                        int i = visitable[k];
                        if (visited[i]) {
                            continue;
                        }
                        var isl = cargo[i].IsLand;
                        if (isl == null) {
                            continue;
                        }
                        double d = DistanceBetween(currentIsland, isl);
                        if (d < bestDist) {
                            bestDist = d;
                            bestIdx = i;
                        }
                    }
                    if (bestIdx == -1) {
                        break;
                    }
                }

                visited[bestIdx] = true;
                order.Add(bestIdx);
                currentIsland = cargo[bestIdx].IsLand;

                // Per-step trace so the user can see exactly which
                // barter the greedy picked and (when chain-aware
                // pass 1 ran) which candidates were skipped due to
                // prereqs. Cheap string concat, only run on button
                // press (not from the timer), so this is fine to leave
                // unconditional.
                try {
                    var pickedBarter = cargo[bestIdx];
                    var pickedName = pickedBarter.IsLandName;
                    // List of skipped (blocked) candidates at this step.
                    var skipped = new List<string>();
                    for (int k = 0; k < visitable.Count; k++) {
                        int i = visitable[k];
                        if (visited[i] && i != bestIdx) continue;
                        bool prereqMet = true;
                        var blocking = new List<int>();
                        foreach (var p in prereqs[i]) {
                            if (!visited[p]) {
                                prereqMet = false;
                                blocking.Add(p);
                            }
                        }
                        if (!prereqMet) {
                            skipped.Add($"idx{i}({cargo[i].IsLandName},prereq-blocked-by:{string.Join(",",blocking)})");
                        }
                    }
                    App.myCFun?.Log(
                        $"Optimal Route step {step + 1}/{visitable.Count}: pick [{bestIdx}] {pickedName} (dist {bestDist:F3})"
                        + (skipped.Count > 0 ? $" [skipped: {string.Join("; ", skipped)}]" : "")
                        + (fellBackToChainlessNN && step > 0 && order.Count > 1 && !prereqs[bestIdx].All(p => visited[p])
                            ? " [chain-less fallback]" : ""),
                        System.Windows.Media.Brushes.DarkCyan);
                }
                catch { }
            }

            // Append anything we couldn't visit (unvisited barters + the
            // rare "null-island" rows we skipped earlier) at the end in
            // their original index order so nothing gets lost.
            for (int i = 0; i < N; i++) {
                if (!visited[i]) {
                    order.Add(i);
                }
            }

            // Apply the route. Move() keeps each Barter instance's
            // identity intact (PropertyGrid selection survives).
            var reordered = order.Select(i => cargo[i]).ToList();
            for (int target = 0; target < reordered.Count; target++) {
                int current = CargoDetails.IndexOf(reordered[target]);
                if (current > target) {
                    CargoDetails.Move(current, target);
                }
            }

            App.myCFun.Log(
                Localization.LanguageService.Instance.Localize(
                    "str.Log.ShipCargo.OptimalRoute.Done",
                    reordered.Count,
                    startIsland.IslandsNameDisplay,
                    fellBackToChainlessNN ? " (chain prereqs were deadlocked; route uses distance-only ordering for the tail)" : ""),
                fellBackToChainlessNN ? Brushes.Yellow : Brushes.DarkOliveGreen);
        }

        // Chain detection blacklist: item names that should NEVER be
        // treated as chain dependencies between barters, even when a
        // cargo barter's Item1 happens to match a producer's Item2.
        //
        // Crow Coin (乌鸦硬币) is the canonical entry: in BDO it's
        // essentially a currency that the player spends on Imperial
        // Cooking / NPC shops / life-skilling exchanges, not a barter
        // input. Letting it participate in chain detection routinely
        // spawned bogus edges like "Seashell Deco -> Crow Coin ->
        // Panacea" that forced greedy NN to put both producers before
        // any actual consumer existed, then complained to the user
        // that the resulting route order was nonsense.
        //
        // Add more entries here as you identify other "end-product"
        // items that should never gate one barter on another. The set
        // is matched case-insensitively against both ItemName and the
        // numeric ItemID string, so a translation drift (zh-TW rename
        // etc.) won't accidentally let an excluded item sneak back in.
        private static readonly HashSet<string> CHAIN_EXCLUDED_ITEMS = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Crow Coin",
        };

        private static bool IsExcludedFromChain(string? itemName, string? itemId) {
            if (!string.IsNullOrEmpty(itemName) && CHAIN_EXCLUDED_ITEMS.Contains(itemName)) {
                return true;
            }
            if (!string.IsNullOrEmpty(itemId) && CHAIN_EXCLUDED_ITEMS.Contains(itemId)) {
                return true;
            }
            return false;
        }

        // Hardcoded mapping of warehouse cities (where items are stored)
        // to barter islands (where items are traded). Iliya, Epheria and
        // Velia match directly; Ancado has no direct pin yet so it falls
        // back to Sausan (the closest southern barter island we have on
        // the map). Easy to revise once Ancado gets its own pin; nothing
        // else in this file changes.
        private static readonly (string CityName, Func<Items, int> Qty, EnumLists.Island Island)[] CITY_TO_ISLAND = {
            ("Velia",   i => i.StorageVeliaQuantity_Velia,   EnumLists.Island.Velia),
            ("Iliya",   i => i.StorageVeliaQuantity_Iliya,   EnumLists.Island.Iliya),
            ("Epheria", i => i.StorageVeliaQuantity_Epheria, EnumLists.Island.Epheria),
            ("Ancado",  i => i.StorageVeliaQuantity_Ancado,  EnumLists.Island.Sausan),
        };

        // Public so MapControl can read the same starting island (its
        // ComputeRoute must prefix the route with the same point so the
        // dashed-line overlay matches the cargo's effective ordering).
        //
        // Decision tree:
        //   1. Try to find the first barter's input item in
        //      StorageCollection (the only place where user-edited
        //      per-city quantities actually live - barter.Item1 is a
        //      catalog instance with zeros).
        //   2. If the item is in storage with a positive quantity at any
        //      city, sail from that city's island.
        //   3. Otherwise (item not yet in storage, or all four warehouses
        //      are 0), default to Iliya. This is the central port and
        //      matches the SelectedIndex=1 default of the existing
        //      ComboBoxAdv_DefaultStorage in StorageManagement, so the
        //      user's intuitive "I keep stuff in Iliya" assumption holds
        //      even before they've explicitly configured the row.
        //   4. Iliya is not in the catalog (very unusual - the
        //      Islands.csv row exists for it): fall back to the first
        //      barter's own island.
        //
        // `verbose` controls whether decision branches are logged.
        // MapControl.ComputeRoute calls this on a 100ms repaint timer;
        // only SolveOptimalRoute (button click) passes verbose=true so
        // the log doesn't get spammed every 100ms with the same
        // "defaulting to Iliya" line.
        public static Islands? ResolveStartIslandFromCargo(IReadOnlyList<Barter>? cargo, bool verbose = false) {
            if (cargo == null || cargo.Count == 0) {
                return null;
            }
            var firstBarter = cargo[0];
            if (firstBarter == null) {
                return null;
            }

            string itemName = firstBarter.Item1?.ItemName
                              ?? firstBarter.Item1Name
                              ?? string.Empty;

            // Look up the first barter's input item in StorageCollection.
            // We don't fall back to barter.Item1 here because that
            // reference is the catalog instance (zero quantities
            // hard-coded), so reading StorageVeliaQuantity_* on it would
            // always return 0 and the loop below would skip every city -
            // making the function look like storage was "checked but
            // empty" when really it was never checked at all.
            //
            // Match strategies, in order:
            //   1. exact ItemName  (handles catalog-hydrated rows)
            //   2. case-insensitive ItemName  (defensive against any
            //      casing drift between catalog and saved-JSON rows)
            //   3. ItemID match  (catalog IDs are stable integers, the
            //      most reliable key when names diverge for any reason)
            Items? storedItem = null;
            string? matchStrategy = null;
            string itemId = firstBarter.Item1?.ItemID
                            ?? string.Empty;
            var storageCollection = App.myStorageVM?.StorageCollection;
            if (storageCollection != null && !string.IsNullOrEmpty(itemName)) {
                foreach (var s in storageCollection) {
                    if (s.ItemName == itemName) {
                        storedItem = s;
                        matchStrategy = "exact name";
                        break;
                    }
                }
                if (storedItem == null) {
                    foreach (var s in storageCollection) {
                        if (string.Equals(s.ItemName, itemName, StringComparison.OrdinalIgnoreCase)) {
                            storedItem = s;
                            matchStrategy = "case-insensitive name";
                            break;
                        }
                    }
                }
                if (storedItem == null && !string.IsNullOrEmpty(itemId)) {
                    foreach (var s in storageCollection) {
                        if (s.ItemID == itemId) {
                            storedItem = s;
                            matchStrategy = "item ID";
                            break;
                        }
                    }
                }
            }

            // Find the city with the highest non-zero stock for this
            // item. Stable tie-break by CITY_TO_ISLAND order so all-zero
            // ties always break the same way (Velia -> Olvia wins by
            // iteration order).
            int bestQty = -1;
            int bestCityIndex = -1;
            bool storageHadData = false;
            if (storedItem != null) {
                storageHadData = true;
                for (int c = 0; c < CITY_TO_ISLAND.Length; c++) {
                    int qty = CITY_TO_ISLAND[c].Qty(storedItem);
                    if (qty > bestQty) {
                        bestQty = qty;
                        bestCityIndex = c;
                    }
                }
            }

            var islands = App.listIslands;
            if (islands == null) {
                return null;
            }

            // Best case: storage has a positive quantity at some city.
            // Sail from that warehouse's island.
            if (storageHadData && bestQty > 0) {
                var targetEnum = CITY_TO_ISLAND[bestCityIndex].Island;
                foreach (var isl in islands) {
                    if (isl.Island == targetEnum) {
                        if (verbose) {
                            TryLogDecision(
                                $"using warehouse city {CITY_TO_ISLAND[bestCityIndex].CityName} (item '{itemName}', qty={bestQty}, matched via {matchStrategy}) -> start at {isl.IslandsNameDisplay}",
                                isFallback: false);
                        }
                        return isl;
                    }
                }
            }

            // Default: Iliya. Chosen because the existing
            // ComboBoxAdv_DefaultStorage in StorageManagement ships with
            // SelectedIndex=1 = Iliya, so users who have not yet
            // configured per-row storage get the same default that the
            // UI already implies.
            foreach (var isl in islands) {
                if (isl.Island == EnumLists.Island.Iliya) {
                    string reason;
                    if (!storageHadData) {
                        reason = storageCollection == null
                            ? "StorageCollection is null at scan time (App.myStorageVM not initialised?)"
                            : $"item '{itemName}' (id={itemId}) is not in StorageCollection";
                        if (storageCollection != null) {
                            string? nearestName = null;
                            foreach (var s in storageCollection) {
                                if (!string.IsNullOrEmpty(itemName)
                                    && !string.IsNullOrEmpty(s.ItemName)
                                    && (s.ItemName.Contains(itemName, StringComparison.OrdinalIgnoreCase)
                                        || itemName.Contains(s.ItemName, StringComparison.OrdinalIgnoreCase))) {
                                    nearestName = s.ItemName;
                                }
                            }
                            if (nearestName != null) {
                                reason += $" - closest name match in storage: '{nearestName}'";
                            }
                        }
                    }
                    else {
                        reason = $"item '{itemName}' is in storage but every warehouse is 0";
                    }
                    if (verbose) {
                        TryLogDecision($"defaulting to Iliya ({reason})", isFallback: true);
                    }
                    return isl;
                }
            }

            // Last resort: the first barter's own island ("you sail
            // there directly, item already in hand"). Only reachable if
            // Iliya was somehow removed from Islands.csv.
            if (firstBarter.IsLand != null) {
                if (verbose) {
                    TryLogDecision(
                        $"defaulting to first barter's island {firstBarter.IsLandName} (Iliya missing from island catalog)",
                        isFallback: true);
                }
                return firstBarter.IsLand;
            }
            return null;
        }

        private static void TryLogDecision(string msg, bool isFallback) {
            try {
                var brush = isFallback
                    ? System.Windows.Media.Brushes.Yellow
                    : System.Windows.Media.Brushes.DarkOliveGreen;
                App.myCFun?.Log("Optimal Route: " + msg, brush);
            }
            catch {
                // Defensive: myCFun / Log might not be wired during very
                // early startup or in design-time XAML rendering.
            }
        }

        // Straight-line sailing distance in calibrated BDO world units.
        // Display coordinates and inset-map placement never affect cost.
        private static double DistanceBetween(Islands a, Islands b) {
            if (ReferenceEquals(a, b)) {
                return 0;
            }
            if (a.Island == b.Island) {
                return 0;
            }
            return IslandNavigationGeometry.Distance(a.NavigationPoint, b.NavigationPoint);
        }
    }
}
