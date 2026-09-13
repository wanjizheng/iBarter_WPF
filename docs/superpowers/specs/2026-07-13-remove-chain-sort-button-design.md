# Remove Ship Cargo Chain-Sort Button Design

## Goal

Remove the obsolete chain-sort toolbar button identified by the `refresh.png` icon from the ship cargo panel.

## Changes

- Remove `ButtonAdv_SortByChain` from `View/ShipCargoControl.xaml`.
- Remove its now-unreachable `ButtonAdv_SortByChain_Click` handler from `View/ShipCargoControl.xaml.cs`.
- Remove the English and Traditional Chinese localization entries used only by that button.
- Keep `ShipCargoViewModel.SortByBarterChain()` because `SolveOptimalRoute()` still uses it as a defensive fallback.
- Keep the separator between the remaining Optimal Route and Clean buttons.

## Verification

- Search for stale `ButtonAdv_SortByChain`, `ButtonAdv_SortByChain_Click`, and chain-sort localization-key references.
- Build `iBarter.csproj` with zero errors.
- Run the existing navigation and planner test projects.

## Scope

No route-solving behavior, toolbar styling, icon assets, or other ship cargo controls will change.
