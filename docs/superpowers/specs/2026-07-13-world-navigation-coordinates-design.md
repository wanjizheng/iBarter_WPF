# BDO Barter World Navigation Coordinates Design

## Goal

Replace the current synthetic Y-offset distance model with a navigation coordinate system that preserves the approximate real-world direction and straight-line sailing distance between barter locations. Reproject the 21 locations drawn in inset or edge regions so their on-screen relative positions also reflect the same geography.

The route solver must no longer infer sailing geometry from the composed display map. The display map contains two inset boxes and two bottom-edge city markers, so its pixel coordinates are intentionally not a continuous world map.

## Current Problem

`ShipCargoViewModel.CentroidForDistance()` currently starts with a display centroid and adds a per-island Y offset. Twelve Margoria points, two Land of the Morning Light points, five Valencia points, Grándiha, and Starry Midnight Port do not form one geographical group, so a common downward offset cannot reconstruct their real directions.

For example, Rickun and Shipwrecked Cox Pirate Ship both receive `+4`, while Starry Midnight Port receives `+2`. This moves Rickun and Cox past Midnight in the synthetic coordinate system and reverses which one is closer:

- Synthetic coordinates: Midnight → Rickun is approximately `1.300`; Midnight → Cox is approximately `1.354`.
- Calibrated world coordinates: Midnight → Rickun is approximately `1,361,000`; Midnight → Cox is approximately `1,182,000` world units.

The greedy solver therefore behaves consistently with its input metric, but the metric is geographically wrong.

## Authoritative Coordinate Model

Add two navigation-only fields to the island data:

```text
NavigationX,NavigationY
```

The fields have these responsibilities:

- `Left`, `Top`, `Right`, and `Bottom` remain display-only coordinates.
- `NavigationX` and `NavigationY` are the sole coordinates used for route distance.
- Display projection must never modify navigation coordinates.
- Route preferences or chain constraints must never be encoded by moving a coordinate.

All ordinary and special destinations must be represented in the same world coordinate system. Assigning navigation coordinates only to inset locations would leave ordinary-to-inset distances incomparable.

### Data Sources and Calibration

Use the following source priority:

1. Published game-world node coordinates from the open Something Lovely BDO map dataset.
2. BDO Codex NPC or node map coordinates, converted into game-world coordinates through a robust common-node calibration.
3. A calibrated conversion from the existing main-map display coordinates only when no direct node or NPC coordinate is available.

The BDO Codex-to-world conversion was established by matching 760 map nodes and rejecting inconsistent or obsolete outliers. A robust fit over 560 inliers produced the approximate transform:

```text
worldX =  25 × codexX - 1,714,970
worldY = -25 × codexY + 1,805,480
```

The inlier median residual was approximately 47 world units, negligible compared with million-unit intercontinental legs. The implementation should retain source notes or a reproducible import/calibration utility so values can be refreshed after future map changes.

Relevant references:

- BDO Codex NPC location pages, including Rickun: <https://bdocodex.com/us/npc/50819/>
- Something Lovely map source and node dataset: <https://github.com/fffam/blackdesert-somethinglovely-map>
- Margoria barter location reference: <https://www.naeu.playblackdesert.com/es-eS/Forum/ForumTopic/Detail?_bigPageNo=1&_categoryNo=13&_forumListType=0&_orderType=False&_pageNo=1&_pageSize=25&_processStatus=99&_searchDay=0&_searchType=0&_sortType=6&_topicNo=2315&_topicNo=73627>
- April 2026 barter update: <https://www.sa.playblackdesert.com/es-mx/News/Detail?countryType=es-Mx&groupContentNo=7849>

The April 2026 Margoria structure changes moved or removed local collision objects around barter structures. They do not establish that the barter destinations moved to different regions, so current NPC map markers remain the navigation reference.

## Special Display Regions

Twenty-one locations need display coordinates that are independent of navigation coordinates.

### Left Inset: 14 Locations

```text
Dallae
Haemo
Unfinished
Pakio
Lantinia
Carrack
Wandering
Haran
Crow
Cholace
Ancient
Rickun
Marine
Cox_Pirate
```

The group contains the twelve Margoria floating barter destinations plus Dallae and Haemo in the distant Land of the Morning Light region.

### Right Inset: 5 Locations

```text
Hakoven
Derko
Arehaza
Kashuma
Halmad
```

### Bottom Edge: 2 Locations

```text
Grandiha
Midnight
```

The code must define these groups explicitly. It must not infer group membership from conditions such as `Top < 0.21`, from `InitTempGrid()`, or from whether a marker happens to be displayed by default.

## Inset Projection

Each inset projects its members from navigation space into a fixed display rectangle. Use a similarity transform with one uniform scale for both axes:

```text
displayX = boxCenterX + (NavigationX - groupCenterX) × scale
displayY = boxCenterY - (NavigationY - groupCenterY) × scale
```

Calculate `scale` as the smaller of the available horizontal and vertical scales after applying a fixed inner padding. This guarantees that all points fit while preserving angles, relative directions, and distance ratios. Center unused space within the inset rather than stretching one axis independently.

The projection process must:

- preserve north/south and east/west ordering;
- use the same scale for X and Y;
- leave enough padding for the 10×10 marker and its connector;
- remain stable when the WPF control is resized;
- avoid using adjusted label positions as island positions.

Grándiha and Starry Midnight Port retain their bottom-edge presentation. Their navigation coordinates remain their real world positions, while route arrows terminate at their display markers.

## Distance and Solver Behavior

`DistanceBetween()` computes Euclidean distance exclusively in navigation space:

```text
sqrt((x2 - x1)^2 + (y2 - y1)^2)
```

Remove `PLACEHOLDER_PUSH`, `DISTANCE_PUSH_Y`, and any distance-time mutation of display centroids.

This change deliberately retains the current precedence-aware greedy nearest-neighbor solver for the first implementation. Chain discovery, prerequisite eligibility, warehouse-derived start selection, fallback behavior, and cargo reordering are outside this change unless a test reveals that coordinate migration broke them.

The corrected six-barter regression route must end with:

```text
Midnight → Cox_Pirate → Rickun
```

This result must emerge from coordinates and prerequisites, not a direction rule or island-name special case.

An exact precedence-constrained Held–Karp solver can be considered separately. Correcting the metric comes first because every solver would produce misleading output when given distorted coordinates.

## Data Loading and Validation

Island loading must treat navigation coordinates as an optional pair during migration and a required pair after the dataset is completed.

Validation rules:

- both values must be present together;
- both values must be finite numbers;
- every routeable island must resolve to navigation coordinates;
- duplicate island records must not disagree on navigation coordinates;
- the left, right, and bottom special groups must contain exactly 14, 5, and 2 entries respectively;
- a missing direct coordinate may fall back to the calibrated main-map conversion and must emit one diagnostic warning;
- no fallback may use `DISTANCE_PUSH_Y` or another arbitrary per-island offset.

Invalid data should not crash map rendering. The affected route calculation should report the unresolved island clearly and use the existing safe route fallback behavior.

## Logging

Per-step route logs should continue to show the selected island and distance. Distances will now be world-coordinate units, so the log label must avoid presenting them as normalized map units or real kilometers unless a verified unit conversion is introduced.

In debug or diagnostic output, include the navigation coordinate source (`world node`, `BDO Codex calibrated`, or `main-map calibrated fallback`) when investigating missing or surprising points.

## Testing

### Coordinate Tests

- Every routeable island has a finite coordinate pair.
- The 21 special locations belong to exactly one explicit display group.
- Rickun is north of Cox_Pirate in the Margoria navigation data.
- Cox_Pirate is closer than Rickun to Starry Midnight Port.
- Right-inset north/south ordering agrees with calibrated world coordinates, including placing Arehaza south of the remote Valencia island chain.
- Ordinary-to-special distances are computed in the same unit system.

### Projection Tests

- All 14 left-inset markers fit inside the padded left rectangle.
- All 5 right-inset markers fit inside the padded right rectangle.
- Projected X and Y use one scale factor within numerical tolerance.
- Pairwise direction signs are preserved by each projection.
- Resizing changes pixel positions proportionally without changing navigation coordinates.

### Route Regression Tests

- The reported six-barter case produces Balvege, Narvo, Grandiha, Midnight, Cox_Pirate, Rickun when its stated prerequisites and Iliya start are used.
- Balvege precedes Midnight and Narvo precedes Grandiha.
- Reversing the starting region or changing eligible candidates does not invoke a hard-coded south-first rule.
- Rickun/Cox ordering is validated from at least one additional start point.
- Existing chain fallback and missing-island behavior remain intact.

## Scope Boundaries

This design improves straight-line geographical distance and direction. It does not yet model:

- coastlines or mandatory obstacle avoidance;
- reefs and shallow-water hazards;
- currents, wind, or ship-specific speed;
- autopath behavior;
- measured travel-time matrices;
- replacement of greedy nearest-neighbor with an exact TSP solver.

Those refinements can be layered on the navigation coordinates later. They must not be mixed into the coordinate migration.

## Success Criteria

The work is complete when:

1. The distance solver no longer reads or mutates display coordinates.
2. All routeable destinations use one calibrated navigation coordinate system.
3. The 21 special markers render in their explicit display regions with undistorted relative geometry.
4. The six-barter regression route visits Cox_Pirate before Rickun after Midnight.
5. Coordinate, projection, and route regression tests pass.
6. No unrelated user changes in the existing dirty worktree are overwritten.
