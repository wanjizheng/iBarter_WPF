# TAG route selection and LV7 sales

## Behavior

- The TAG session now saves the selected route number. A null selection means All routes. Dropdown selection and map segment focus both persist it; startup, completion and replanning retain the selection (or the nearest remaining route if its number disappeared).
- The session field is outside the request fingerprint. Old saves still replay under their original rules and default to All until a selection is made.
- New TAG plans sell terminal LV7 goods at enabled ports. Sale cards show the localized item name, icon and quantity, without a permanent transfer-to-character instruction. Ordinary planning is unchanged.
- A sale is a planning macro: an item can be withdrawn and sold individually, reusing a character slot. Ship sales require legal receiving headroom and a free slot; an alternate character is used through explicit switches when necessary. Already-carried character goods can be sold directly. LV6 and goods required by a remaining exchange are excluded.
- Sales have an independent quantity ledger and reduce the source inventory. They do not add warehouse stock. Old LV7 transfer actions remain legal for save verification, while new compiler/search actions use sales. Saved incumbents are replayed with sales; when both characters are full, already-planned local unloads of other goods may move ahead of a sale to free receiving space. Every resulting action is replay-validated.
- Completing a map exchange does not record a subsequent sale as already performed. The exchange-only compiler still stops at the selected barter.

## Verification

- AutomaticRoutePlanningTests: 408 passed, zero failures.
- WPF app and smoke runner build succeeded (existing repository warnings remain).
- WPF smoke: specific route and All survive reopening; map-focused selection persists; split exchange completion and next-exchange highlight still work. Sale card icon and quantity rendered and visually inspected.
- Read-only audit of `D:\Games\iBarter\Resources\tagged-transport-session.json`: 20 LV7 goods across 5 sale steps, zero LV7 transfers to storage/characters, all remaining exchanges and inventory conserved. Result distance 38.14 km for the snapshot read during this audit.
- Test output is isolated under `Tools/TaggedTransportUiSmoke/bin/lv7-selection-fix/`. The installed executable and runtime Resources were not modified. These are simulator/WPF checks, not live-game sale validation.

Rebuild the app, select the desired route once, and run Auto Plan to apply the new LV7 handling to an old saved route.
