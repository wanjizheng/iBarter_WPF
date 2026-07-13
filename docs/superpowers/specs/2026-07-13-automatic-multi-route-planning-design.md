# Automatic Multi-Route Planning Design

## Goal

Extend Planner Auto Plan into a complete sailing itinerary:

1. select every unfinished barter whose resulting `Eq.` is greater than zero;
2. plan warehouse pickups, barter stops, route boundaries, and route order;
3. keep the ship within `TotalLT` after every pickup and barter;
4. minimize the number of routes first and total sailing distance second;
5. let the Ship Cargo panel select an individual route while the map can display either one route or all routes;
6. preserve the existing manual map-selection, cargo sorting, and gold dashed-route workflow.

The feature is an addition to Planner Auto Plan. It does not change how the existing three Planner strategies choose barter multipliers.

## Confirmed Domain Rules

### Warehouses

The first implementation supports every storage location already represented by `StorageManager`. The currently relevant case has two populated warehouse islands: LV1-LV5 goods and LV6 goods can be stored on different islands.

A route may visit a different warehouse as an intermediate pickup. For example, this is one route:

```text
Iliya pickup -> barter A -> Velia pickup -> barter B -> finish at Velia
```

Within one route, a warehouse can be used as a pickup stop at most once. Arriving again at a warehouse already visited by the current route closes the route. The solver may also deliberately finish a route at any warehouse without first using it as an intermediate pickup.

When a route finishes, all remaining ship cargo is unloaded into the ending warehouse. Those items become warehouse inventory available to later routes. The next route starts at that same warehouse; the solver must not add an invisible repositioning jump.

### Cargo and exchanges

The solver treats a selected Planner row as one indivisible barter task. Version 1 does not split one row's `ExchangeQuantity` across multiple routes.

For every barter step, the ship must contain:

```text
ExchangeQuantity * Item1Number
```

units of the input item. The step removes those units and adds:

```text
ExchangeQuantity * Item2Number
```

units of the output item.

If one selected barter produces an item consumed by another selected barter, the producer must execute first unless the required consumer quantity is already available on the ship or in a reachable warehouse. Item IDs, not localized names, are the authoritative dependency and inventory keys.

### Weight

Every state transition must satisfy:

```text
ExtraLT + sum(onBoardQuantity[item] * unitWeight[item]) <= TotalLT
```

The check runs after every warehouse pickup and every barter. `InitialLT` and final `CurrentLT` alone are not sufficient because an intermediate step can have the highest load.

The selected route exposes:

- `InitialLT`: load after its first pickup;
- `CurrentLT`: load after its final barter and before its finishing unload;
- `PeakLT`: the maximum load observed anywhere in the route.

All three values include `ExtraLT`, and none may exceed `TotalLT`. `PeakLT` is a new route metric; the existing manual-cargo meanings of `InitialLT` and `CurrentLT` remain unchanged.

## Terminology and Route Boundaries

A **route** is one selectable itinerary in the Ship Cargo dropdown. It starts at a warehouse, may contain barter stops and at most one pickup visit to each distinct warehouse, and finishes at a warehouse.

A **route step** is one of:

- `WarehousePickupStep`: visit a warehouse and load explicitly listed items;
- `BarterStep`: execute one selected Planner barter;
- `WarehouseUnloadStep`: finish the route and unload all remaining cargo.

The unload step may share an island with the most recent pickup step but remains explicit in the model so inventory transitions and route boundaries are unambiguous. Zero-distance visual legs are not drawn.

## Optimization Model

This is a single-ship, multi-warehouse pickup-and-delivery problem with intermediate replenishment, precedence, changing inventory, and dynamic capacity. It is not a standard capacitated vehicle-routing problem: warehouses are transit-capable replenishment nodes, and barter steps both consume and produce goods.

Relevant background:

- Google OR-Tools pickup and delivery constraints: <https://developers.google.com/optimization/routing/pickup_delivery>
- Multi-depot routing with inter-depot replenishment: <https://doi.org/10.1016/j.ejor.2005.08.015>
- Vehicle routing with intermediate replenishment facilities: <https://doi.org/10.1287/ijoc.1070.0230>
- Exact branch-and-price work for intermediate replenishment: <https://doi.org/10.1016/j.cor.2025.107084>

Version 1 will not add OR-Tools. Its standard routing model does not directly express iBarter's inventory-producing barter transitions and dynamic warehouse unloading, while a CP formulation would introduce a large native dependency and still require extensive custom constraints.

### Hard constraints

A candidate is rejected immediately if it:

- leaves any selected barter unfinished;
- withdraws more of an item than a warehouse contains at that time;
- executes a barter without its required input quantity;
- exceeds `TotalLT` after any action;
- violates a required producer/consumer ordering;
- uses an unresolved island or non-finite navigation coordinate;
- revisits a warehouse as a pickup inside the same route;
- creates an invisible transfer between consecutive routes.

No infeasible solution is retained or scored. In particular, an "overweight count" is not an optimization objective.

### Lexicographic objective

Feasible solutions are compared in this strict order:

1. fewer routes;
2. shorter total sailing distance, including every pickup, barter, and final return leg;
3. fewer warehouse pickup stops;
4. lower maximum `PeakLT` across all routes;
5. canonical Planner row ID and warehouse ID ordering as the deterministic final tie-break.

The implementation must compare an objective tuple. It must not approximate the priorities with arbitrary weighted sums.

## Solver Architecture

### Immutable input snapshot

`AutomaticRoutePlanningRequest` contains only immutable data:

- selected barter tasks with stable row IDs and resolved navigation coordinates;
- per-item unit weights;
- per-warehouse item quantities;
- warehouse island coordinates;
- `ExtraLT` and `TotalLT`;
- deterministic search limits.

The WPF layer builds the request on the UI thread. The solver never reads `App`, `ObservableCollection`, controls, or localized display names.

### Shared transition simulator

`RouteStateTransition` is the single authority for pickup, barter, and unload transitions. Both the solver and the post-solve verifier call it. UI cargo calculations must consume its recorded load snapshots rather than reimplementing the arithmetic.

The state contains:

```text
current island
current route index
warehouses visited in current route
completed barter bitset
on-board quantities by ItemID
dynamic quantities by warehouse and ItemID
current load and current-route peak load
route steps
route count, distance, and pickup count
```

The usual plan size is currently around 19 active barter rows, so the completed set fits in a `ulong`. Inputs above 64 tasks must be rejected with a localized diagnostic in version 1 rather than silently overflowing.

### Dependency graph

Before searching, construct a DAG candidate graph from ItemIDs. A cycle is not assumed to be impossible: initial warehouse stock can break an apparent producer cycle. Therefore graph edges constrain a consumer only for the quantity that cannot be satisfied from initial or previously deposited stock.

The preflight phase reports permanently unreachable inputs before route search begins. A diagnostic must name the Planner row, required item, required quantity, and all warehouse quantities examined.

### Anytime exact search

Use a deterministic branch-and-bound/best-first search:

1. create a fast feasible incumbent with precedence-aware nearest-neighbor packing;
2. improve the incumbent with precedence-safe relocate, swap, and 2-opt moves;
3. explore exact states in optimistic objective order;
4. prune states whose lower-bound objective cannot beat the incumbent;
5. cache and dominance-prune equivalent states;
6. finish with either `Optimal` or `BestKnownWithinLimit` status.

Search actions are:

- travel to a warehouse and load a demand-derived bundle;
- travel to and execute an eligible barter;
- travel to a warehouse and finish the current route.

Pickup quantities are not arbitrary integers. Candidate bundles are derived from the unmet inputs of reachable barter subsets up to the next warehouse opportunity. This keeps the action space finite and prevents loading goods that no remaining step can consume.

Useful admissible lower bounds include:

- the minimum number of additional routes implied by remaining required weight and reachable replenishment capacity;
- the distance from the current location to the nearest remaining required stop;
- a minimum-spanning-tree bound over remaining barter islands and a reachable finishing warehouse;
- mandatory producer/consumer connection distances.

For the dominance cache, one state dominates another only when position, completed set, current-route warehouse mask, and route boundary status match, and it is no worse in the objective prefix, current load, on-board usable inventory, and relevant dynamic warehouse inventory. Dominance comparisons must be conservative; an uncertain comparison may reduce performance but must never remove a potentially optimal solution.

### Search limits and truthfulness

The solver runs on a background task with cancellation. Search limits are deterministic counts (expanded states and local-improvement iterations), not wall-clock time alone, so identical inputs remain reproducible across machines. The UI may also enforce a safety timeout and cancel the task.

Results contain:

- `Optimal`: exhaustive proof completed;
- `BestKnownWithinLimit`: a valid incumbent exists but optimality was not proved;
- `Infeasible`: exhaustive search proved no valid solution;
- `NoFeasibleSolutionWithinLimit`: the limit expired without an incumbent;
- `Cancelled` or `InvalidInput`.

Logs and UI text must distinguish "optimal" from "best known".

## Data Model

Add pure models under `Routing` or `Planning/Routes`:

```text
AutomaticRoutePlanningRequest
AutomaticRoutePlanningResult
RoutePlan
PlannedRoute
RouteStep
WarehousePickupStep
BarterStep
WarehouseUnloadStep
RouteLoadSnapshot
RoutePlanStatus
RoutePlanObjective
```

`RouteStep` stores stable IDs and computed quantities. It may carry the source Planner row ID, but it must not require a live `Barter` reference. The WPF adapter resolves IDs back to live rows for display and actions.

`RoutePlan` also stores an input fingerprint covering:

- selected row IDs and all quantities affecting exchanges;
- `ExchangeDone` and `ExchangeQuantity`;
- warehouse quantities by ItemID;
- `ExtraLT` and `TotalLT`;
- island navigation coordinates;
- the solver configuration version.

## Manual and Automatic Cargo Modes

`CargoMode` has two values:

- `Manual`;
- `AutomaticRoute`.

The existing `CargoDetails` collection remains the authoritative manual cargo and continues to be persisted by the current cargo JSON path. Automatic route selection must not overwrite or save over it.

In automatic mode, the Ship Cargo panel binds to the selected route's step view models. Exchange-step commands resolve their source Planner row. Switching back to manual mode restores the existing manual list without reconstruction.

Manual behavior remains unchanged:

- map middle-click adds/removes a barter from manual cargo;
- Optimal Route sorts manual cargo;
- the manual map route remains a single gold dashed line;
- manual cargo save/load continues to use the current files.

Starting a manual add/remove action switches the visible mode to `Manual`. A still-valid generated `RoutePlan` may remain cached so selecting one of its routes switches back to automatic mode. Any input-fingerprint change invalidates and discards it.

## Planner Integration

After Planner Auto Plan successfully applies multipliers:

1. complete the existing Planner refresh and save operations;
2. build the immutable route-planning request from `Eq. > 0 && !CK` rows;
3. cancel any older route-planning operation;
4. run the solver off the UI thread;
5. verify the returned plan by replaying every transition from the original snapshot;
6. publish the plan atomically on the UI thread;
7. switch to `AutomaticRoute` and select Route 1 when a valid plan exists.

A route-planning failure does not roll back the Planner multipliers. It leaves manual cargo untouched, clears any stale automatic overlay, and logs a localized actionable diagnostic.

## Ship Cargo UI

Add a route selector with:

```text
ALL
Route 1
Route 2
...
```

Selecting a concrete route:

- switches to automatic mode;
- shows that route's complete ordered steps;
- updates `InitialLT`, `CurrentLT`, and `PeakLT` from recorded snapshots;
- asks the map to show only that route.

The automatic list uses two visibly distinct templates:

- warehouse row: warehouse name, item names and quantities to load or unload, and load after the action;
- barter row: the existing island/input/output presentation plus load after the exchange.

Warehouse rows cannot be dragged, marked complete, or treated as barters. Barter rows retain applicable copy and navigation actions. Automatic steps are solver-owned and are not manually reorderable; users can return to manual mode for custom ordering.

Selecting `ALL` affects only the map. The cargo panel and LT values remain on the most recently selected concrete route. If no concrete route has been selected yet, Route 1 is the cargo context.

## Map Rendering

The map must stop deriving automatic paths from `CargoDetails`. It receives a route-render snapshot from `RoutePlan` containing ordered island IDs and a stable route color.

- Manual mode: preserve the current gold dashed line and arrows.
- Concrete automatic route: draw that route in its assigned color.
- `ALL`: draw every route simultaneously, each in a distinct deterministic color.
- Warehouse stops participate in the polyline and have a distinct waypoint marker or tooltip.
- Consecutive identical islands collapse visually, but the underlying pickup/unload steps remain intact.
- Overlay elements remain non-hit-testable and are rebuilt safely when the map resizes.

Use a fixed color palette with sufficient contrast against the dark map. If routes outnumber the palette, cycle hues while also varying dash patterns so color is not the only distinction.

## Invalidation and Concurrency

The current `RoutePlan` becomes stale when any fingerprint input changes, including:

- Planner row `Eq.`, `CK`, item, island, remaining count, or exchange quantity;
- StorageManager quantity;
- `ExtraLT` or `TotalLT`;
- planner load/new/clean operations;
- navigation coordinate data or solver configuration version.

Invalidation cancels an in-flight solve, removes automatic overlays, and returns the visible cargo mode to `Manual`. Versioned request IDs prevent a late background result from replacing a newer plan.

The first version does not persist generated route plans. They are derived data and are regenerated by Auto Plan, avoiding stale saved routes after Planner or StorageManager changes.

## Error Handling

Diagnostics must be actionable and localized. Important cases include:

- required item absent from every reachable warehouse and producer;
- one indivisible barter cannot fit under `TotalLT` even with an otherwise empty ship;
- unresolved warehouse or barter island;
- invalid/non-finite navigation coordinates;
- dependency or inventory state proven infeasible;
- search limit reached with or without a valid incumbent;
- post-solve replay mismatch, which is treated as an internal error and never published.

No failure may mutate manual cargo or leave a stale automatic route visible.

## Testing Strategy

Create a pure test project that links only route-planning and navigation source files. WPF controls and global `App` state must not be required.

### Transition tests

- pickup decrements the correct warehouse and increases ship load;
- barter removes inputs, adds outputs, and records load;
- intermediate load peaks are detected even when initial and final loads are safe;
- unload transfers every on-board item into the ending warehouse;
- insufficient stock, missing inputs, and overweight transitions are rejected without mutation.

### Solver tests

- nearby A+B and nearby C+D are grouped instead of cross-pairing when two routes are necessary;
- a route visits Iliya for LV1-LV5 and Velia for LV6 when that is shorter and feasible;
- a producer precedes its consumer;
- initial warehouse stock can satisfy a consumer without forcing an unnecessary producer edge;
- a route ending inventory is available to the next route;
- route count dominates distance, and distance dominates pickup count;
- identical requests return identical plans and statuses;
- exhaustive small fixtures are compared with brute-force enumeration to verify optimality claims;
- state-limit fixtures return a valid `BestKnownWithinLimit` plan and never label it optimal;
- an indivisible overweight barter returns a precise infeasibility diagnostic;
- more than 64 barter tasks returns `InvalidInput`.

### Integration tests

- Planner Auto Plan publishes Route 1 without modifying manual `CargoDetails`;
- changing Eq., CK, storage quantity, or LT invalidates the generated plan;
- stale background results are ignored;
- selecting a route changes the automatic step list and single-route overlay;
- selecting `ALL` changes only the overlay and preserves the last concrete cargo context;
- switching to manual mode restores the saved manual cargo and gold route;
- map route snapshots include warehouse waypoints in solver order.

### Regression verification

- build `iBarter.csproj` with zero errors;
- run Planner Auto Planner tests;
- run Island Navigation tests;
- run the new automatic-route-planning tests;
- manually verify both English and Traditional Chinese UI resources.

## Delivery Stages

Implementation should be reviewable in six independently testable stages:

1. immutable route models and transition/load simulator;
2. dependency preflight and anytime solver;
3. Planner adapter, cancellation, verification, and invalidation;
4. manual/automatic cargo-mode separation and Ship Cargo route-step UI;
5. map route snapshots, single-route rendering, and `ALL` rendering;
6. localization, diagnostics, full regression tests, and documentation.

The solver and simulator must be accepted before UI integration begins. The map and Ship Cargo panel must consume the same published `RoutePlan`; neither may independently infer an automatic route.

## Out of Scope

Version 1 does not include:

- splitting one Planner row across routes;
- currents, wind, collision avoidance, reefs, or autopath travel time;
- automatic in-game input or inventory manipulation;
- persisted automatic route plans;
- arbitrary manual editing of automatic route steps;
- more than 64 selected barter tasks;
- replacing the existing Planner multiplier strategies.

## Success Criteria

The feature is complete when:

1. every published automatic plan replays successfully against its immutable input snapshot;
2. no transition exceeds `TotalLT` or consumes unavailable inventory;
3. warehouse pickups, barter dependencies, unloads, and later-route inventory are modeled explicitly;
4. a completed exact search truthfully reports `Optimal`, while limited searches report `BestKnownWithinLimit`;
5. Route selection updates the automatic cargo steps and one colored map route;
6. `ALL` shows every colored route without changing cargo or LT context;
7. the existing manual cargo, optimal sorting, persistence, and gold dashed route continue to work;
8. all new and existing relevant tests pass without overwriting unrelated dirty-worktree changes.
