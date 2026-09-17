# Remove Ship Cargo Chain-Sort Button Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the obsolete chain-sort toolbar action without removing the route solver's fallback method.

**Architecture:** Delete the XAML entry point, its unreachable code-behind handler, and its private localization strings. Verify no UI references remain while retaining `ShipCargoViewModel.SortByBarterChain()`.

**Tech Stack:** WPF XAML, C# 13, .NET 10, PowerShell verification.

## Global Constraints

- Preserve `ShipCargoViewModel.SortByBarterChain()`.
- Preserve the separator between Optimal Route and Clean.
- Do not remove `Images/refresh.png`, because asset cleanup is outside this change.
- Preserve unrelated dirty-worktree changes.

---

### Task 1: Remove the Obsolete UI Action

**Files:**
- Modify: `View/ShipCargoControl.xaml:51-61`
- Modify: `View/ShipCargoControl.xaml.cs:311-327`
- Modify: `Resources/i18n/Strings.en-US.xaml`
- Modify: `Resources/i18n/Strings.zh-TW.xaml`

**Interfaces:**
- Consumes: existing `ButtonAdv_OptimalRoute` and `ButtonAdv_Clean` toolbar entries.
- Produces: a two-action toolbar with no chain-sort click entry point.

- [ ] **Step 1: Establish the failing stale-reference check**

Run:

```powershell
rg -n "ButtonAdv_SortByChain|ButtonAdv_SortByChain_Click|str\.ShipCargo\.Btn\.SortByChain" View Resources/i18n
```

Expected: matches in the XAML, code-behind, and both localization dictionaries.

- [ ] **Step 2: Remove the XAML button and code-behind handler**

Delete the complete `ButtonAdv_SortByChain` element and the complete `ButtonAdv_SortByChain_Click` method, including its button-specific explanatory comment. Leave `ButtonAdv_OptimalRoute`, the following separator, and `ButtonAdv_Clean` unchanged.

- [ ] **Step 3: Remove button-only localization entries**

Delete these keys from both dictionaries:

```text
str.ShipCargo.Btn.SortByChain
str.ShipCargo.Btn.SortByChainTip
```

- [ ] **Step 4: Verify stale references are gone and fallback remains**

Run:

```powershell
rg -n "ButtonAdv_SortByChain|ButtonAdv_SortByChain_Click|str\.ShipCargo\.Btn\.SortByChain" View Resources/i18n
rg -n "SortByBarterChain\(" ViewModel/ShipCargoViewModel.cs
```

Expected: the first command has no matches; the second still finds the method and solver fallback call.

- [ ] **Step 5: Run regression verification**

Run:

```powershell
dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj --no-restore
dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --no-restore
dotnet build iBarter.csproj --no-restore
git diff --check
```

Expected: both test suites pass, build has zero errors, and no whitespace errors are reported.
