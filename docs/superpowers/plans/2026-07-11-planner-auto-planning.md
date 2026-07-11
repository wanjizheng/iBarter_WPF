# Planner Auto-Planning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Planner strategy selector and Auto Plan button that atomically fills unfinished barter multipliers under inventory, route-count, group, and 1,000,000-parley constraints.

**Architecture:** Put all optimization rules in a deterministic, UI-independent `iBarter.Planning` service. `PlannerControl` adapts live `Barter` rows into immutable requests, calls the service, and applies a successful result once; tests exercise the service directly and a small orchestration helper without constructing WPF controls.

**Tech Stack:** C# 14 / .NET 10, WPF, Syncfusion WPF controls, xUnit, existing XAML resource dictionaries and Newtonsoft-based Planner persistence.

## Global Constraints

- Implement only Planner auto-planning; Ship Cargo sorting/execution changes are out of scope.
- Preserve every unrelated modification in the dirty worktree; never reset, checkout, or reformat unrelated files.
- Completed (`ExchangeDone`) rows remain unchanged and do not participate; every unfinished multiplier is replaced from a zero baseline.
- Calculate inventory by quantities: `current + sum(multiplier * Item2Number) - sum(multiplier * Item1Number)`.
- Link producers and consumers only inside the same `BarterGroup` and use internal item identity, not localized display names.
- Never exceed `IslandRemaining`, never permit negative projected inventory, and never exceed 1,000,000 parley.
- A target increment and all required upstream increments form an atomic bundle: accept all or none.
- Reverse supply uses ceiling division and stops after the LV4-to-LV5 supply edge for high-tier targets.
- Crow Coin First prioritizes Crow Coin routes, then LV4→LV5, LV5→LV6, LV6→LV7 profit work.
- Profit First excludes Crow Coin and prioritizes LV6→LV7, LV5→LV6, LV4→LV5.
- Restock First first fills LV5/LV6 toward configured per-item targets by deficit ratio, then spends remaining parley on the lowest projected LV1–LV4 inventory; LV1–LV4 have no implicit cap and LV7 is excluded.
- Identical inputs must produce identical multipliers; stable row identity is the final tie-breaker.
- No third-party optimization solver, hard-coded silver prices, travel/weight optimization, OCR changes, storage semantic changes, or automatic completion flags.
- Follow red-green-refactor for every behavior and commit only files belonging to the current task.

---

## File Map

- Create `Planning/AutoPlanningModels.cs`: enums, immutable request/route/result/diagnostic types.
- Create `Planning/PlannerAutoPlanner.cs`: validation, projected inventory, atomic reverse-supply bundles, strategy selection, and bounded local improvement.
- Create `Planning/PlannerAutoPlanningAdapter.cs`: non-WPF orchestration contract for zero-baseline calculation and atomic application.
- Create `Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj`: isolated xUnit test project.
- Create `Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.cs`: service behavior tests and reusable route/request builders.
- Create `Tools/PlannerAutoPlannerTests/PlannerAutoPlanningAdapterTests.cs`: CK/reset/atomic-application boundary tests.
- Modify `iBarter.csproj`: exclude the nested test sources/content from the WPF application build.
- Modify `View/PlannerControl.xaml`: strategy dropdown and Auto Plan button.
- Modify `View/PlannerControl.xaml.cs`: effective-parley extraction, request mapping, click orchestration, one-shot refresh/save, localized result messages.
- Modify `Resources/i18n/Strings.en-US.xaml` and `Resources/i18n/Strings.zh-TW.xaml`: UI labels, options, summaries, and error messages.
- Modify `README.md`: describe the new toolbar controls and exact strategy behavior.

### Task 1: Establish the Pure Planning Contract and Test Harness

**Files:**
- Create: `Planning/AutoPlanningModels.cs`
- Create: `Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj`
- Create: `Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.cs`
- Modify: `iBarter.csproj`

**Interfaces:**
- Produces: `AutoPlanningStrategy`, `AutoPlanningRoute`, `AutoPlanningRequest`, `AutoPlanningResult`, `AutoPlanningDiagnostic` in namespace `iBarter.Planning`.
- `AutoPlanningRoute` carries `RowId`, `Group`, `Item1Id`, `Item1Level`, `Item1Number`, `Item2Id`, `Item2Level`, `Item2Number`, `ProducesCrowCoin`, `Parley`, and `Remaining`.
- `AutoPlanningRequest` carries routes, inventory, strategy, LV5/LV6 targets, and budget.

- [ ] **Step 1: Exclude the nested test project from the application build**

Add beside the existing `CaptureRectGuardTest` exclusions in `iBarter.csproj`:

```xml
<Compile Remove="Tools\PlannerAutoPlannerTests\**\*.cs" />
<None Remove="Tools\PlannerAutoPlannerTests\**\*" />
<Content Remove="Tools\PlannerAutoPlannerTests\**\*" />
```

- [ ] **Step 2: Create the xUnit project**

Create `Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.1">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <Compile Include="..\..\Planning\AutoPlanningModels.cs" Link="Planning\AutoPlanningModels.cs" />
    <Compile Include="..\..\Planning\PlannerAutoPlanner.cs" Link="Planning\PlannerAutoPlanner.cs" Condition="Exists('..\..\Planning\PlannerAutoPlanner.cs')" />
    <Compile Include="..\..\Planning\PlannerAutoPlanningAdapter.cs" Link="Planning\PlannerAutoPlanningAdapter.cs" Condition="Exists('..\..\Planning\PlannerAutoPlanningAdapter.cs')" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the failing contract test**

Create `Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.cs` with this first test and helper:

```csharp
using iBarter.Planning;

namespace PlannerAutoPlannerTests;

public sealed class PlannerAutoPlannerTests {
    [Fact]
    public void Request_rejects_duplicate_row_ids() {
        var route = Route("r1", 1, "A", 4, 1, "B", 5, 1, 10_000, 5);
        var request = new AutoPlanningRequest(
            [route, route], new Dictionary<string, int> { ["A"] = 10 },
            AutoPlanningStrategy.ProfitFirst, 10, 10, 1_000_000);

        var result = new PlannerAutoPlanner().Plan(request);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "duplicate-row-id");
    }

    private static AutoPlanningRoute Route(
        string id, int group, string item1, int lv1, int n1,
        string item2, int lv2, int n2, int parley, int remaining,
        bool crow = false) =>
        new(id, group, item1, lv1, n1, item2, lv2, n2, crow, parley, remaining);
}
```

- [ ] **Step 4: Run the test and verify RED**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --filter Request_rejects_duplicate_row_ids`

Expected: compilation fails because the planning types and `PlannerAutoPlanner` do not exist.

- [ ] **Step 5: Add immutable model types and the minimum validator**

Create `Planning/AutoPlanningModels.cs`:

```csharp
namespace iBarter.Planning;

public enum AutoPlanningStrategy { CrowCoinFirst, ProfitFirst, RestockFirst }

public sealed record AutoPlanningRoute(
    string RowId, int Group,
    string Item1Id, int Item1Level, int Item1Number,
    string Item2Id, int Item2Level, int Item2Number,
    bool ProducesCrowCoin, int Parley, int Remaining);

public sealed record AutoPlanningRequest(
    IReadOnlyList<AutoPlanningRoute> Routes,
    IReadOnlyDictionary<string, int> CurrentInventory,
    AutoPlanningStrategy Strategy,
    int Lv5Target, int Lv6Target,
    int ParleyBudget = 1_000_000);

public sealed record AutoPlanningDiagnostic(string Code, string RowId = "");

public sealed record AutoPlanningResult(
    bool Success,
    IReadOnlyDictionary<string, int> Multipliers,
    IReadOnlyDictionary<string, int> ProjectedInventory,
    int UsedParley,
    IReadOnlyList<AutoPlanningDiagnostic> Diagnostics);
```

Create `Planning/PlannerAutoPlanner.cs` with `Plan` returning failure for duplicate IDs and a zero plan otherwise. Validate null/blank IDs, negative inventory/quantities/parley/remaining/budget, non-positive exchange quantities, and budget above 1,000,000 using explicit diagnostic codes.

- [ ] **Step 6: Run tests and verify GREEN**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj`

Expected: PASS with one test and no warnings from the planning sources.

- [ ] **Step 7: Commit the contract**

```powershell
git add iBarter.csproj Planning/AutoPlanningModels.cs Planning/PlannerAutoPlanner.cs Tools/PlannerAutoPlannerTests
git commit -m "test: define planner auto-planning contract"
```

### Task 2: Implement Quantity-Conserving Atomic Bundles

**Files:**
- Modify: `Planning/PlannerAutoPlanner.cs`
- Modify: `Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.cs`

**Interfaces:**
- Consumes: model types from Task 1.
- Produces: deterministic internal `TryBuildBundle(state, targetRowId, increment, out Bundle)` behavior used by every strategy.

- [ ] **Step 1: Add failing conservation tests**

Add `Reverse_supply_uses_ceiling_quantity_not_equal_multiplier` using group 7 routes `A→2B` and `B→C`, zero B, budget 80,000, and assert downstream 5/upstream 3/projected B 1. Add four separate `[Fact]` methods with these exact fixtures and assertions:

| Test | Fixture | Exact assertion |
|---|---|---|
| `Reverse_supply_never_crosses_group` | `A→B` in group 1, `B→C` in group 2, B inventory 0 | both multipliers are 0 |
| `Three_level_reverse_chain_is_added_atomically` | group 3 routes `A→B`, `B→C`, `C→D`, input inventories B=0/C=0, target `C→D` remaining 2 | all three multipliers are 2 and every projected inventory is non-negative |
| `Failed_bundle_leaves_every_multiplier_unchanged` | same chain but `A→B` remaining 1 and target needs 2 | all three multipliers are 0 |
| `Shared_item_inventory_accounts_for_all_consumers_and_producers` | one `A→2B` producer and two `B→C/D` consumers in one group, B inventory 1 | final B is non-negative and produced B equals or exceeds combined B consumption minus initial B |

Add `PlanProfit` as a test helper that constructs `AutoPlanningRequest` with `ProfitFirst` and target values 10/10.

- [ ] **Step 2: Run the new tests and verify RED**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --filter "Reverse_supply|Three_level|Failed_bundle|Shared_item"`

Expected: assertions fail because planning still returns zero multipliers.

- [ ] **Step 3: Implement projected inventory and atomic reverse supply**

In `PlannerAutoPlanner`, introduce private mutable `PlanningState` and immutable `Bundle`. Build a candidate on cloned multiplier/inventory dictionaries, recursively resolving a deficit only from producers with the same group and `Item2Id == required Item1Id`. Use:

```csharp
private static int CeilingDivide(int deficit, int output) =>
    checked((deficit + output - 1) / output);
```

Order eligible producers by higher output, then lower parley, then `RowId`. Maintain a recursion-stack set of `(group, itemId)` and emit `cycle` if re-entered. Reject the cloned bundle if any route exceeds `Remaining`, any projected item becomes negative, or checked arithmetic overflows. Only merge cloned state after all affected items validate.

- [ ] **Step 4: Run focused and full tests**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj`

Expected: all conservation tests PASS; malformed/cyclic input returns diagnostics rather than throwing.

- [ ] **Step 5: Commit atomic bundle behavior**

```powershell
git add Planning/PlannerAutoPlanner.cs Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.cs
git commit -m "feat: build quantity-safe barter bundles"
```

### Task 3: Implement Crow Coin and Profit Strategies

**Files:**
- Modify: `Planning/PlannerAutoPlanner.cs`
- Modify: `Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.cs`

**Interfaces:**
- Consumes: atomic bundle builder from Task 2.
- Produces: `Plan` behavior for `CrowCoinFirst` and `ProfitFirst`.

- [ ] **Step 1: Add failing strategy tests**

Add seven `[Fact]` methods using 1:1 routes, sufficient starting inventory, and `Remaining=1` unless specified:

| Test | Inputs | Exact result |
|---|---|---|
| `Crow_first_maximizes_higher_coin_output_before_efficiency` | coin outputs 190 at 20,000 and 180 at 10,000; budget 20,000 | 190 route=1, 180 route=0 |
| `Crow_first_uses_bundle_efficiency_when_coin_output_ties` | both output 190; complete costs 20,000 and 10,000; budget 10,000 | cheaper route=1 |
| `Crow_first_never_exceeds_budget_when_all_coin_routes_cannot_fit` | three 190-coin routes at 400,000 each | exactly two selected and used parley 800,000 |
| `Crow_first_spends_remainder_in_lv4_then_lv5_then_lv6_input_order` | no feasible coin route; three profit routes cost the full budget individually | only LV4→LV5 selected |
| `Profit_first_excludes_every_crow_output` | feasible crow and LV6→LV7 routes | crow=0, LV7 route=1 |
| `Profit_first_prefers_lv6_to_lv7_over_lower_targets` | one route at each profit tier, each costs full budget | only LV6→LV7 selected |
| `Strategy_ties_are_deterministic_by_row_id` | equal `rA`/`rB` routes supplied once forward and once reversed | both runs select `rA` and return identical dictionaries |

For every test also assert exact projected inventory and used parley derived from its fixture.

- [ ] **Step 2: Run strategy tests and verify RED**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --filter "Crow_first|Profit_first|Strategy_ties"`

Expected: at least the ranking and exclusion assertions fail.

- [ ] **Step 3: Implement candidate scoring and greedy selection**

Represent candidate rank as a comparable tuple. For Crow Coin phase use descending coin output, descending `coinOutput / bundleParley` via cross multiplication (avoid floating point), ascending bundle parley, ascending row ID. For Profit use descending target tier, descending output, descending output/bundle-parley, ascending parley, ascending row ID. Add one feasible target exchange at a time and rebuild scores because shared inventory changes bundle cost.

After no direct candidate fits, perform bounded one-for-one replacement: clone final state, remove one accepted target increment, rebuild inventory from the request baseline, and accept a replacement only if it preserves the strategy's lexicographic objective and increases used parley without exceeding budget. Do not search beyond one removed increment.

- [ ] **Step 4: Run tests and verify GREEN**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj`

Expected: all strategy and conservation tests PASS deterministically across repeated runs.

- [ ] **Step 5: Commit both strategies**

```powershell
git add Planning/PlannerAutoPlanner.cs Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.cs
git commit -m "feat: add crow coin and profit planning strategies"
```

### Task 4: Implement Two-Phase Restock Strategy

**Files:**
- Modify: `Planning/PlannerAutoPlanner.cs`
- Modify: `Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.cs`

**Interfaces:**
- Produces: `RestockFirst` with LV5/LV6 capped phase followed by uncapped LV1–LV4 lowest-inventory phase.

- [ ] **Step 1: Add failing restock tests**

Add seven `[Fact]` methods with these exact scenarios:

| Test | Fixture and assertion |
|---|---|
| `Restock_first_orders_lv5_and_lv6_by_deficit_ratio` | target 10; LV5 stock 1 and LV6 stock 5; one-exchange budget; select LV5 producer only |
| `Restock_zero_target_disables_that_capped_tier` | LV5 target 0 with a feasible LV5 producer; assert its multiplier 0 |
| `Restock_completes_capped_phase_before_lv1_to_lv4` | LV5 below target and LV1 stock 0; one-exchange budget; select LV5 producer only |
| `Restock_second_phase_always_selects_lowest_projected_inventory` | LV1–LV4 stocks 1,2,3,4, four equal-cost producers and two-exchange budget; first item receives both units until tied with second |
| `Restock_lv1_to_lv4_has_no_hidden_cap` | only an LV2 producer with remaining 5 and full budget for five; assert multiplier 5 |
| `Restock_excludes_lv7_targets` | only an LV7 output route; assert multiplier 0 |
| `Restock_allows_only_unavoidable_indivisible_oversupply` | LV5 stock 9, target 10, route output 2; assert multiplier 1 and projected 11 |

- [ ] **Step 2: Run restock tests and verify RED**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --filter Restock_`

Expected: restock selection assertions fail.

- [ ] **Step 3: Implement the capped phase**

Filter output items at levels 5 and 6. Compute deficit ratio using integer cross multiplication, not `double`; compare `deficitA * targetB` against `deficitB * targetA`. Skip target zero and projected inventory at/above target. Add the smallest feasible atomic bundle that improves the winning item; allow overshoot only when one exchange's indivisible output crosses the target.

- [ ] **Step 4: Implement the uncapped phase**

After the capped queue has no feasible candidate, consider output levels 1–4. Rank ascending projected inventory, ascending output level, ascending bundle parley, then item ID and row ID. Add one target exchange, recompute all inventory, and repeat until no candidate fits. Never include an output-level-7 target.

- [ ] **Step 5: Run all tests and commit**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj`

Expected: all tests PASS.

```powershell
git add Planning/PlannerAutoPlanner.cs Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.cs
git commit -m "feat: add two-phase restock planning"
```

### Task 5: Add Atomic Planner Orchestration

**Files:**
- Create: `Planning/PlannerAutoPlanningAdapter.cs`
- Create: `Tools/PlannerAutoPlannerTests/PlannerAutoPlanningAdapterTests.cs`

**Interfaces:**
- Produces: `PlannerAutoPlanningAdapter.Calculate(IReadOnlyList<PlannerRowSnapshot> rows, IReadOnlyDictionary<string, int> inventory, AutoPlanningStrategy strategy, int lv5Target, int lv6Target, int budget)` and immutable `PlannerApplySet`.
- `PlannerRowSnapshot` contains `RowId`, `ExchangeDone`, existing multiplier, and `AutoPlanningRoute`; CK rows are excluded from the request and returned unchanged.

- [ ] **Step 1: Write failing boundary tests**

Create three complete tests: `Calculate_preserves_ck_multiplier_and_replaces_unfinished_from_zero` supplies a CK row at 3 and unfinished row at 4 and asserts the returned CK value is 3 while the unfinished value equals a fresh zero-baseline plan; `Calculate_returns_no_apply_set_when_service_fails` supplies duplicate row IDs and asserts `ApplySet` is null; `Calculate_all_zero_success_is_still_applicable` supplies one valid but unaffordable route and asserts a non-null apply set with multiplier 0 and a `no-feasible-candidate` diagnostic.

- [ ] **Step 2: Run tests and verify RED**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --filter Calculate_`

Expected: compilation fails because adapter types do not exist.

- [ ] **Step 3: Implement the adapter**

Create records `PlannerRowSnapshot`, `PlannerApplySet`, and `PlannerCalculation`. `Calculate` must build a request only from `ExchangeDone == false`, call `PlannerAutoPlanner.Plan`, return `ApplySet = null` on `Success == false`, and otherwise return a complete dictionary containing every row ID: CK rows retain their snapshot multiplier and unfinished rows use the result multiplier or zero.

- [ ] **Step 4: Run all tests and commit**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj`

Expected: all tests PASS.

```powershell
git add Planning/PlannerAutoPlanningAdapter.cs Tools/PlannerAutoPlannerTests/PlannerAutoPlanningAdapterTests.cs
git commit -m "feat: add atomic planner application boundary"
```

### Task 6: Integrate the Toolbar, Localization, and Planner Refresh

**Files:**
- Modify: `View/PlannerControl.xaml`
- Modify: `View/PlannerControl.xaml.cs`
- Modify: `Resources/i18n/Strings.en-US.xaml`
- Modify: `Resources/i18n/Strings.zh-TW.xaml`

**Interfaces:**
- Consumes: `PlannerAutoPlanningAdapter` and planning model types.
- Produces: `ComboBoxAdv_AutoPlanningStrategy`, `ButtonAdv_AutoPlan`, and `ButtonAdv_AutoPlan_Click`.

- [ ] **Step 1: Add localized resource keys**

Add these keys to both dictionaries with native translations:

```text
str.Planner.AutoPlan.Strategy
str.Planner.AutoPlan.CrowCoinFirst
str.Planner.AutoPlan.ProfitFirst
str.Planner.AutoPlan.RestockFirst
str.Planner.Btn.AutoPlan
str.Planner.Btn.AutoPlanTip
str.Msg.Planner.AutoPlan.Success
str.Msg.Planner.AutoPlan.NoRows
str.Msg.Planner.AutoPlan.Invalid
str.Msg.Planner.AutoPlan.NoFeasible
```

Use English text “Strategy”, “Crow Coin First”, “Profit First”, “Restock First”, “Auto Plan”; use Traditional Chinese text “策略”、“烏鴉硬幣優先”、“賺錢優先”、“補充貨源優先”、“自動規劃”. The success format must accept strategy name, used parley, and selected route count.

- [ ] **Step 2: Add the controls to the toolbar**

In `PlannerControl.xaml`, insert after the grouping/clean/done controls and before totals:

```xml
<Separator />
<Label Content="{loc:Localize str.Planner.AutoPlan.Strategy}" VerticalAlignment="Center" />
<syncfusion:ComboBoxAdv x:Name="ComboBoxAdv_AutoPlanningStrategy" SelectedIndex="0" Width="135">
    <syncfusion:ComboBoxItemAdv Content="{loc:Localize str.Planner.AutoPlan.CrowCoinFirst}" Tag="CrowCoinFirst" />
    <syncfusion:ComboBoxItemAdv Content="{loc:Localize str.Planner.AutoPlan.ProfitFirst}" Tag="ProfitFirst" />
    <syncfusion:ComboBoxItemAdv Content="{loc:Localize str.Planner.AutoPlan.RestockFirst}" Tag="RestockFirst" />
</syncfusion:ComboBoxAdv>
<syncfusion:ButtonAdv x:Name="ButtonAdv_AutoPlan"
    Margin="4,0,4,0" SizeMode="Small" FontFamily="Microsoft YaHei"
    Label="{loc:Localize str.Planner.Btn.AutoPlan}"
    ToolTip="{loc:Localize str.Planner.Btn.AutoPlanTip}"
    SmallIcon="/Images/refresh.png" Click="ButtonAdv_AutoPlan_Click" />
```

Reuse the existing icon; do not add a bitmap asset.

- [ ] **Step 3: Extract one effective parley calculation**

Move the current `UsingALT` island switch from `UpdateParley` into `private static int GetEffectiveParley(Barter barter)`. Make `UpdateParley` sum `GetEffectiveParley(barter) * barter.ExchangeQuantity`. The auto-planning adapter must pass the same effective per-exchange value so the displayed total and budget constraint cannot disagree.

- [ ] **Step 4: Implement request mapping and atomic click handling**

In `PlannerControl.xaml.cs`, map each live row to a stable per-plan `RowId` using its zero-based collection index formatted with invariant culture. Use internal `Item1.ItemID`/`Item2.ItemID`; set `ProducesCrowCoin` from the canonical `Item2.ItemName == "Crow Coin"`, not display text. Parse item levels with `int.TryParse` and treat failure as invalid input.

The click handler must:

```csharp
DataGrid_Planner.SelectionController.CurrentCellManager.EndEdit();
var liveRows = App.myPVM.BarterCollection.ToList();
// Build snapshots without changing ExchangeQuantity.
var calculation = adapter.Calculate(snapshots, inventory, selectedStrategy,
    ComboBox_LV5Max.SelectedIndex, ComboBox_LV6Max.SelectedIndex, 1_000_000);
if (calculation.ApplySet is null) { Show localized error; return; }
DataGrid_Planner.BeginInit();
try {
    for (var i = 0; i < liveRows.Count; i++)
        liveRows[i].ExchangeQuantity = calculation.ApplySet.Multipliers[i.ToString(CultureInfo.InvariantCulture)];
}
finally { DataGrid_Planner.EndInit(); }
UpdateInvChange(-1);
UpdateParley();
SaveData();
UpdateMapControl();
App.myfmMain.myShipCargo.UpdateCurrentLV();
```

Call cargo persistence/refresh only if required by the existing manual multiplier-edit path; do not introduce Ship Cargo sorting behavior. Show the localized success summary after refresh. If calculation fails, do not enter `BeginInit` and do not alter a multiplier.

- [ ] **Step 5: Build and manually smoke-test the UI**

Run: `dotnet build iBarter.csproj -c Debug -p:Platform=x86`

Expected: build succeeds with zero errors.

Manual checks:

1. Change language while Planner is open; button, tooltip, label, and all three options update.
2. Set distinct CK and unfinished multipliers; Auto Plan preserves CK and replaces unfinished values.
3. Verify displayed parley equals `sum(effective parley * multiplier)` and is at most 1,000,000.
4. Trigger an invalid/no-feasible plan and verify multipliers remain byte-for-byte unchanged.
5. Restart after Save and verify planned multipliers reload through existing persistence.

- [ ] **Step 6: Commit the WPF integration**

```powershell
git add View/PlannerControl.xaml View/PlannerControl.xaml.cs Resources/i18n/Strings.en-US.xaml Resources/i18n/Strings.zh-TW.xaml
git commit -m "feat: add planner auto-planning controls"
```

### Task 7: Documentation and Final Verification

**Files:**
- Modify: `README.md`
- Verify: all files from Tasks 1–6

**Interfaces:**
- Produces: user-facing behavior documentation and final evidence.

- [ ] **Step 1: Update Planner documentation**

Update the toolbar count and list in `README.md`. Document that Auto Plan overwrites only unfinished Eq. values, preserves CK rows, never exceeds 1,000,000 parley, and summarize the three strategies including the Restock two-phase order.

- [ ] **Step 2: Run formatting and unfinished-code checks**

Run:

```powershell
git diff --check
rg -n "NotImplementedException" Planning Tools/PlannerAutoPlannerTests View/PlannerControl.xaml.cs README.md
```

Expected: `git diff --check` is silent; the search finds no newly introduced unimplemented planning code. Existing unrelated matches must be inspected and left untouched.

- [ ] **Step 3: Run the complete focused test suite**

Run: `dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj -c Debug`

Expected: every test passes with zero failed/skipped tests.

- [ ] **Step 4: Build the application**

Run: `dotnet build iBarter.csproj -c Debug -p:Platform=x86 --no-restore`

Expected: build succeeds with zero errors. Record any pre-existing warnings separately; do not claim they were introduced by this feature without comparing baseline output.

- [ ] **Step 5: Inspect scope and commit documentation**

Run:

```powershell
git status --short
git diff --stat HEAD~1
```

Confirm no OCR, scanner, storage semantics, or Ship Cargo sorting files were changed for this feature. Then:

```powershell
git add README.md
git commit -m "docs: explain planner auto-planning"
```

- [ ] **Step 6: Hand off verification evidence**

Report the test count, build result, manual smoke-test results, commits created, and any skipped UI check with its exact reason. Do not mark the work complete if tests fail, the build fails, parley can exceed 1,000,000, CK rows change, or a failure path partially applies multipliers.
