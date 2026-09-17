# TAG out-of-order completion, home cargo and ports

## Reproduction and changes

Read the 30-exchange, 93-operation saved session in `D:\Games\iBarter\Resources` without modifying runtime files.

- The old completion path skipped earlier barters but replayed their cargo handling unchanged. A later exchange could therefore fail weight/stock checks even when a different loading order was feasible. On failure, completion now compiles the selected exchange first with independently checked loading/port handling, without marking any other exchange complete or inventing input goods. All five route-4 exchanges (Rameda, Haemo, Lema, Olvia, Epheria) pass reconciliation and continuation verification.
- Added a persisted home warehouse setting. Existing live settings without one default to Iliya; this changes the assumptions and invalidates an incompatible displayed session. Empty string remains an explicit legacy unrestricted setting for old regression fixtures. Finished goods stay on characters/ship at other ports; cargo and slots remain continuous between voyages. Final goods return to the selected home warehouse, and verification rejects a net increase of goods left in other warehouses.
- Preloading includes later segments with no independent warehouse loading, so an incidental Epheria stop does not discard the supplies for the next voyage. Current-data replay transfers all five units of item 800059 to characters at Epheria and unloads them at Iliya.
- Invalidated routes remain hidden, but an identical exchange set may supply a remapped ordering candidate for search. Every candidate is replayed/recompiled under current stock, capacities, ports and home destination before acceptance. Changed exchange identities/quantities cannot reuse this candidate.
- Port NPC names now use the Chinese game names in Chinese mode and update live. The execution help paragraph is hidden.
- Append missing port entries without overwriting existing on/off choices. Added Nampo, Byukgye, Cheongsa, Ancado, Oquilla, Outpost and the existing Crow's Nest destination. New candidates are off initially; personal-wharf identity does not automatically grant warehouse access. Only Ancado links to the already-modelled Ancado inventory. Navigation points are loaded from the supplemental source catalog even with older runtime CSVs.

## Port sources

NPC names, roles and map positions were checked on 2026-09-14 using the public English and Taiwan NPC catalogs and individual records:

- [Yooan / 尤安, Nampo](https://bdocodex.com/tw/npc/47209/1/)
- [Darirong / 橋龍, Byukgye](https://bdocodex.com/tw/npc/47332/1/)
- [Gangman / 江萬, Cheongsa](https://bdocodex.com/tw/npc/47772/1/)
- [Gurong / 具龍, Haemo](https://bdocodex.com/tw/npc/47309/1/)
- [Sungoo / 善九, Dallae](https://bdocodex.com/tw/npc/47270/1/)
- [Samia](https://bdocodex.com/us/npc/45301/1/), [Ravikel](https://bdocodex.com/us/npc/49579/1/), [Flanche](https://bdocodex.com/us/npc/49558/1/), [Anax](https://bdocodex.com/us/npc/50810/1/).

Coordinates use the repository's existing calibrated transform, `worldX = 25 × codexX - 1714970`, `worldY = -25 × codexY + 1805480`. They are estimates from catalog positions, not newly measured in-game docking points. Nampo personal-wharf access is distinct from any warehouse integration; the latter is not added here.

## Validation

- Routing: 394 tests passed, including out-of-order overweight handling and foreign-port character storage followed by home delivery.
- WPF: actual route-4 map completion preserves all other quantities, removes the selected marker, verifies the continuation and restores it; settings, localized NPCs, hidden help, route switching and toolbar feedback pass in isolated output.
- Current-data home replay: 165.22 km seed with two Haemo transfers; short search finds 149.25 km and finishes at Iliya. The shorter candidate does not need to transfer at Haemo. Enabling a port permits handling there; it does not force a stop. A fresh short search without the existing order found 188.63 km, which is why verified incumbent reuse matters. None of these bounded results proves global optimality.
- Build: no errors; full WPF compilation retains existing warnings.
- Navigation suite: 87/89 passed, including the updated port display-group coverage. Two existing failures remain: `Barter_location_catalog_uses_unique_finite_npc_destinations` expects 68 while both HEAD and the untouched CSV contain 69 rows; `Trusted_affine_fit_excludes_barterer_destinations` conflicts with the untouched mapping implementation's fallback behavior. Their source/test files have no diff from HEAD and the latter uses synthetic Main-region names unrelated to the new port groups.
- Outputs live under `Tools/TaggedTransportUiSmoke/bin/final-fixes/`. No deployment, commit or push; installed files and user resource data were not overwritten.
