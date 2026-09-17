# Planner Auto-Planning Design

## Scope

This change replaces manual multiplier selection in the Planner with an automatic planner. It is the first stage of a larger workflow; Ship Cargo behavior is explicitly out of scope.

The Planner toolbar gains:

- a strategy dropdown with Crow Coin First, Profit First, and Restock First;
- an Auto Plan button that recalculates the `ExchangeQuantity` (倍率 / Eq.) column.

The implementation must preserve unrelated user changes already present in the dirty worktree.

## Confirmed Game and Product Rules

Each barter consumes the route's parley cost per exchange. The plan must never use more than 1,000,000 parley. Higher-tier goods are preferred for silver income. Official background references:

- https://www.naeu.playblackdesert.com/fr-fr/Wiki?wikiNo=325
- https://www.console.playblackdesert.com/News/Notice/Detail?boardNo=7230&countryType=en-US

The application data is authoritative for route levels, item quantities, remaining exchanges, and parley costs. The algorithm must not hard-code game prices or assume every route exchanges one item for one item.

Completed (`ExchangeDone` / CK) rows remain unchanged and do not participate in automatic planning. Before calculating, all unfinished rows are treated as having `ExchangeQuantity = 0`. The update is committed to the UI only after a complete valid result has been produced.

## Architecture

Implement the optimization logic as a UI-independent C# service. The WPF control supplies immutable planning inputs and applies the returned multipliers. The service must not reference `PlannerControl`, Syncfusion controls, `App` singletons, storage windows, or message boxes.

Suggested boundaries:

- `AutoPlanningStrategy`: the three strategy values.
- `AutoPlanningRequest`: unfinished routes, current item inventories, LV5/LV6 targets, and the 1,000,000 parley budget.
- `AutoPlanningRoute`: stable row identity, group, input/output item identity and level, per-exchange input/output quantities, parley, and remaining exchange count.
- `AutoPlanningResult`: multiplier by row identity, total parley, projected inventory by item, and diagnostics when no feasible addition exists.
- `PlannerAutoPlanner`: validates input, constructs the route graph, selects atomic exchange bundles, and returns a complete result without mutating the request.

The Planner click handler gathers the request, calls the service, then applies the result in one UI operation. After applying it, the existing inventory-change calculation, parley display, persistence, map refresh, and any necessary cargo-derived display refresh run once.

## Route Graph and Inventory Conservation

Routes are linked only within the same `BarterGroup`. A route producing item B can supply a route consuming item B only when both routes share the same group. Item identity must use the application's stable internal identity where available; display/localized names must not be used as the primary key.

Projected inventory is calculated by quantity, not by equal multipliers:

`projected(item) = current(item) + sum(multiplier * Item2Number) - sum(multiplier * Item1Number)`

When a target exchange would make an input item's projected inventory negative, recursively add an upstream producer from the same group. The required upstream multiplier is the ceiling of the quantity deficit divided by that producer's per-exchange output. Continue backward no farther than the LV4-to-LV5 route.

Example: five downstream exchanges consume five units of B. If the upstream route produces two B per exchange and no B is in stock, the upstream route needs three exchanges, not five.

An atomic exchange bundle consists of a proposed target increment plus every upstream increment needed to keep all affected inventories non-negative. A bundle is rejected in full if:

- an affected multiplier would exceed that route's `IslandRemaining`;
- a required upstream producer cannot be found in the same group;
- recursive expansion still leaves a negative inventory;
- the chain is cyclic or malformed;
- adding the complete bundle would exceed the parley budget.

Multiple consumers and producers of the same item must be evaluated against the shared projected inventory. Planning must be deterministic: identical input and strategy must produce identical multipliers. Stable row identity is the final tie-breaker.

## Optimization Method

Use constrained greedy selection with bounded local improvement.

Each candidate is evaluated as a complete atomic exchange bundle. The strategy score determines which feasible candidate is added next. Near the budget limit, bounded local replacement may remove one or a small number of lower-ranked bundles and insert a better combination that uses more of the remaining parley without violating strategy priority, route limits, or inventory conservation.

The planner should approach 1,000,000 parley where useful, but strategy priority ranks above merely spending more parley. It must never exceed the budget. Exhaustive integer programming or a third-party solver is not required.

## Strategy Rules

### Crow Coin First

Only routes whose output item is internally identified as Crow Coin participate in the first phase. Do not depend solely on the localized display text.

Rank Crow Coin candidates by:

1. greater Crow Coin output per target exchange;
2. when equal, greater Crow Coin output divided by the parley cost of the complete required bundle;
3. lower complete-bundle parley cost;
4. stable row identity.

Increment feasible Crow Coin targets until their remaining counts are exhausted or no further complete bundle fits. If all Crow Coin targets plus their supply chains cannot fit within 1,000,000, keep the best candidates under the ranking above rather than exceeding the budget.

Use the remaining parley for profit in this strict target-tier order:

1. LV4 input to LV5 output;
2. LV5 input to LV6 output;
3. LV6 input to LV7 output.

Each selected target still includes all necessary upstream supply.

### Profit First

Exclude every Crow Coin output route. Prefer targets in this strict order:

1. LV6 input to LV7 output;
2. LV5 input to LV6 output;
3. LV4 input to LV5 output.

Within a tier, prefer the feasible complete bundle that yields more target-tier output, then better output per complete-bundle parley, then lower parley, then stable row identity. Continue until no useful complete bundle fits.

### Restock First

Restocking covers LV1 through LV6 goods in two strict phases. Ignore the LV7 maximum because LV7 goods are intended for sale.

Treat the selected Planner LV5 and LV6 maximum values as the desired inventory per item. For every LV5/LV6 item below its target, calculate:

`deficit ratio = (target - projected inventory) / target`

LV5/LV6 items at or above target are not restock targets. A zero target disables restocking for that tier. Select the LV5/LV6 item with the greatest current deficit ratio, then add the smallest complete bundle that improves it without intentionally raising projected inventory beyond the target. Recalculate deficits after each accepted bundle. Ties prefer larger absolute deficit, then lower complete-bundle parley, then stable item and row identity.

Complete the LV5/LV6 phase before spending parley on LV1-LV4. If no eligible LV5/LV6 bundle remains or fits, use the remaining parley for LV1-LV4.

LV1 through LV4 have no configured target and no implicit inventory cap. Among feasible LV1-LV4 targets, select the item with the lowest current projected inventory. After every accepted bundle, recalculate all projected inventories and select the now-lowest item again. Continue until no useful complete bundle fits. Ties prefer the lower item level, then the lower complete-bundle parley cost, then stable item and row identity.

Upstream production may exceed the exact deficit only when indivisible exchange output makes oversupply unavoidable. Route limits, non-negative inventory, and the parley cap remain mandatory.

## UI and Data Flow

The toolbar exposes localized labels for the button, tooltip, dropdown label, and three options in every currently supported language resource dictionary. The default strategy is Crow Coin First unless an existing settings pattern supports persisting the most recent selection cleanly; persistence is optional and must not enlarge scope.

On Auto Plan click:

1. finish or cancel any active grid edit so the request uses committed values;
2. gather unfinished rows and current storage quantities;
3. validate selected strategy and tier targets;
4. calculate without modifying live `Barter` objects;
5. if successful, set unfinished-row multipliers from the result in one guarded update;
6. recompute `InvQuantityChange` globally;
7. refresh the parley label, save Planner data, and refresh dependent map/cargo displays once;
8. show a localized concise summary including strategy, used parley, and count of selected routes.

CK rows are neither reset nor counted. Existing unfinished multipliers are replaced rather than used as seeds.

If validation or calculation fails, leave every live multiplier unchanged and show a localized actionable message. A valid result containing all zeros is allowed and should explain why no feasible exchange was selected.

## Error Handling

Detect and report, without partial UI mutation:

- no strategy selected;
- no unfinished rows;
- malformed negative quantities, parley, or remaining counts;
- duplicate or missing stable row identity;
- ambiguous/missing upstream path that prevents a required bundle;
- cycles in a group route graph;
- no feasible candidate under the chosen strategy.

Individual infeasible candidates should normally be skipped and recorded in result diagnostics; they should not abort otherwise valid planning.

## Testing

Add service-level unit tests before production implementation. Tests must cover:

- quantity-conserving ceiling calculation for non-1:1 exchanges;
- same-group-only reverse supply;
- three-level reverse chaining through LV4-to-LV5;
- shared inventory across multiple consumers/producers;
- route remaining-count enforcement;
- exact and near-budget acceptance and over-budget rejection;
- atomic rejection with no partial upstream multipliers;
- deterministic tie-breaking;
- CK exclusion and unfinished-row reset semantics at the integration boundary;
- Crow Coin ranking, constrained selection, and profit fallback;
- Profit First excluding Crow Coin and preferring LV7 targets;
- Restock First lowest-inventory ordering for LV1-LV4 with no implicit cap;
- Restock First deficit-ratio ordering for LV5/LV6, zero targets, strict phase ordering, and LV7 exclusion;
- indivisible-output oversupply;
- malformed/cyclic graph diagnostics;
- live Planner state remaining unchanged after failure.

Add a thin UI integration test where practical, or isolate the click orchestration behind testable helper methods if WPF automation is unavailable. Run the complete existing build and relevant tests after implementation.

## Out of Scope

- Ship Cargo sorting, route display, or execution behavior;
- changing scanner/OCR behavior;
- changing storage persistence semantics;
- introducing a third-party optimization solver;
- estimating travel time, ship weight, silver prices, or geographic route efficiency;
- automatically marking exchanges complete.
