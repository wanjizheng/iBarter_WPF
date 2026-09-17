# TAG interaction, distance and localization fixes

Map completion now acknowledges only the selected barter segment. It reconstructs the selected trip's required handling, skips other unconfirmed barters, applies the selected barter with the transport simulator, and replans from that island with the resulting inventories. The earlier and later tasks remain pending. Same-row split segments decrement only their own quantity. A verified continuation seed tries the retained, reversed and distance-ordered first-trip jobs; regular TAG search evaluates the candidate. Failed reconciliation, cancellation or changed inputs does not mark a task complete.

The saved workspace inputs are separate from projected cargo. Restart validation compares the unchanged Planner/warehouse/ship/settings inputs; continuing through the normal Auto Plan entry retains the recorded cargo/location. User-edited warehouse quantities take precedence over an unchanged snapshot. Map progress does not write warehouse inventory. The existing inventory controls remain authoritative.

The extra next/undo/replan/settle buttons and the sequential cargo tab are hidden. Legacy sequential session support remains for compatibility. Final map completion persists an empty display without requiring a settlement click. Search cancellation remains available.

Ordinary routing objectives use world centimetres; TAG objectives use metres. Both now display kilometres with two decimals in final logs and search feedback. Internal optimization units are unchanged.

TAG text uses the shared font and rendering resources. Selected-tab emphasis applies only to its header, avoiding inherited semibold Chinese table cells. Departure, port island and warehouse names use the existing localized island catalog while retaining canonical IDs in settings. Open port grids refresh their translated bindings on language change.

Validation: application compilation passed (796 existing warnings, zero errors); 389 route tests passed. WPF smoke covered independent middle completion on the real saved 27-trade plan, split-task completion, last-task clearing/reload, Planner quantity updates, cargo replay, restart validation, continuation through Auto Plan, map labels/pins, hidden action buttons, Chinese typography and live Chinese/English names. Game runtime and the installed D: application were not modified or exercised. Out-of-order completion that cannot be reconciled against recorded cargo/port constraints remains uncommitted and reports the need to correct the recorded inputs.
