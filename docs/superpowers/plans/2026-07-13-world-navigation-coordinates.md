# World Navigation Coordinates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace display-coordinate Y pushes with calibrated BDO world coordinates and render all 21 special locations through undistorted inset projections.

**Architecture:** `Islands` owns an immutable-at-runtime navigation coordinate pair loaded from the canonical CSV. A pure `IslandNavigationGeometry` service computes world distances and inset projections, while `ShipCargoViewModel` and `MapControl` consume those separate outputs. A small xUnit project exercises geometry and catalog invariants without booting WPF.

**Tech Stack:** C# 13, .NET 10, WPF, xUnit 2.9.3, CSV resources, PowerShell verification commands.

## Global Constraints

- Preserve every unrelated modification in the existing dirty worktree.
- Route distance uses only `NavigationX` and `NavigationY`; display coordinates never affect solver cost.
- Delete `PLACEHOLDER_PUSH` and `DISTANCE_PUSH_Y`; do not replace them with direction or island-name bonuses.
- The special display groups contain exactly 14 left-inset, 5 right-inset, and 2 bottom-edge locations.
- Inset projection uses one uniform X/Y scale and fixed padding.
- Keep the existing precedence-aware greedy solver and chain discovery behavior.
- Treat computed distance as BDO world units, not kilometers.

---

## File Structure

- Create `Navigation/IslandNavigationGeometry.cs`: pure point, bounds, distance, grouping, validation, and projection logic.
- Modify `Model/Islands.cs`: store navigation coordinates and source metadata.
- Modify `CFunctions.cs`: parse and validate the extended island CSV schema.
- Modify `Model/Barter.cs`: preserve navigation fields when cloning a catalog island.
- Modify `Resources/Islands.csv`: append navigation X, navigation Y, and source columns for every row.
- Modify `ViewModel/ShipCargoViewModel.cs`: consume navigation distance and remove synthetic pushes.
- Modify `View/MapControl.xaml.cs`: use explicit inset projection for special markers and raw display coordinates elsewhere.
- Create `Tools/IslandNavigationTests/IslandNavigationTests.csproj`: isolated xUnit test project.
- Create `Tools/IslandNavigationTests/IslandNavigationGeometryTests.cs`: unit and regression tests.
- Create `Tools/IslandNavigationTests/IslandCatalogTests.cs`: CSV completeness and group-count tests.
- Modify `iBarter.csproj`: exclude the new test sources from the WPF application compile glob.

---

### Task 1: Pure Navigation Geometry and Failing Tests

**Files:**
- Create: `Navigation/IslandNavigationGeometry.cs`
- Create: `Tools/IslandNavigationTests/IslandNavigationTests.csproj`
- Create: `Tools/IslandNavigationTests/IslandNavigationGeometryTests.cs`
- Modify: `iBarter.csproj`

**Interfaces:**
- Produces: `NavigationPoint(double X, double Y)`, `NormalizedPoint(double X, double Y)`, `NormalizedBounds(double Left, double Top, double Right, double Bottom)`, and `IslandNavigationGeometry.Distance/ProjectToInset`.
- Consumes: no application globals or WPF types.

- [ ] **Step 1: Create the test project and exclude it from the WPF glob**

Add this project definition:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
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
    <Compile Include="..\..\Navigation\IslandNavigationGeometry.cs" Link="Navigation\IslandNavigationGeometry.cs" />
  </ItemGroup>
</Project>
```

Add matching `Compile/None/Content Remove="Tools\IslandNavigationTests\**\*"` entries beside the existing tool-project exclusions in `iBarter.csproj`.

- [ ] **Step 2: Write failing geometry tests**

```csharp
[Fact]
public void Distance_uses_world_coordinates() {
    var midnight = new NavigationPoint(-321_664, -598_912);
    var cox = new NavigationPoint(-747_393, 504_292);
    var rickun = new NavigationPoint(-816_036, 669_629);
    Assert.True(IslandNavigationGeometry.Distance(midnight, cox)
                < IslandNavigationGeometry.Distance(midnight, rickun));
}

[Fact]
public void ProjectToInset_preserves_one_scale_and_direction() {
    var points = new Dictionary<string, NavigationPoint> {
        ["northWest"] = new(0, 100),
        ["southEast"] = new(50, 0),
    };
    var projected = IslandNavigationGeometry.ProjectToInset(
        points, new NormalizedBounds(0, 0, 0.4, 0.3), 0.02);
    Assert.True(projected["northWest"].X < projected["southEast"].X);
    Assert.True(projected["northWest"].Y < projected["southEast"].Y);
    var sx = (projected["southEast"].X - projected["northWest"].X) / 50;
    var sy = (projected["southEast"].Y - projected["northWest"].Y) / 100;
    Assert.Equal(sx, sy, 10);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj --no-restore`

Expected: compilation fails because `NavigationPoint` and `IslandNavigationGeometry` do not exist.

- [ ] **Step 4: Implement minimal pure geometry**

Implement records plus:

```csharp
public static double Distance(NavigationPoint a, NavigationPoint b) {
    double dx = a.X - b.X;
    double dy = a.Y - b.Y;
    return Math.Sqrt(dx * dx + dy * dy);
}

public static IReadOnlyDictionary<string, NormalizedPoint> ProjectToInset(
    IReadOnlyDictionary<string, NavigationPoint> points,
    NormalizedBounds bounds,
    double padding) {
    if (points.Count == 0) return new Dictionary<string, NormalizedPoint>();
    double minX = points.Values.Min(p => p.X);
    double maxX = points.Values.Max(p => p.X);
    double minY = points.Values.Min(p => p.Y);
    double maxY = points.Values.Max(p => p.Y);
    double usableWidth = bounds.Right - bounds.Left - 2 * padding;
    double usableHeight = bounds.Bottom - bounds.Top - 2 * padding;
    double spanX = Math.Max(maxX - minX, 1);
    double spanY = Math.Max(maxY - minY, 1);
    double scale = Math.Min(usableWidth / spanX, usableHeight / spanY);
    double usedWidth = spanX * scale;
    double usedHeight = spanY * scale;
    double originX = bounds.Left + (bounds.Right - bounds.Left - usedWidth) / 2;
    double originY = bounds.Top + (bounds.Bottom - bounds.Top - usedHeight) / 2;
    return points.ToDictionary(
        pair => pair.Key,
        pair => new NormalizedPoint(
            originX + (pair.Value.X - minX) * scale,
            originY + (maxY - pair.Value.Y) * scale));
}
```

- [ ] **Step 5: Run tests and commit**

Run: `dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj`

Expected: all geometry tests pass.

Commit: `test: add island navigation geometry`

---

### Task 2: Island Model and CSV Navigation Data

**Files:**
- Modify: `Model/Islands.cs`
- Modify: `CFunctions.cs:763-790`
- Modify: `Model/Barter.cs:555-561`
- Modify: `Resources/Islands.csv`
- Create: `Tools/IslandNavigationTests/IslandCatalogTests.cs`

**Interfaces:**
- Produces: `Islands.NavigationX`, `Islands.NavigationY`, `Islands.NavigationSource`, and `Islands.HasNavigationCoordinates`.
- Consumes: `NavigationPoint` from Task 1.

- [ ] **Step 1: Write failing catalog-schema tests**

Parse `Resources/Islands.csv` from the repository root and assert every nonblank row has exactly nine columns, finite columns 7 and 8, and a nonblank source column 9. Assert known calibrated values within a small tolerance:

```csharp
AssertCoordinate(rows, "Midnight", -321_664, -598_912, 1);
AssertCoordinate(rows, "Rickun", -816_036, 669_629, 2);
AssertCoordinate(rows, "Cox_Pirate", -747_393, 504_292, 2);
AssertCoordinate(rows, "Halmad", 558_999, 333_684, 1);
AssertCoordinate(rows, "Hakoven", 1_252_450, 547_567, 1);
```

- [ ] **Step 2: Run catalog tests to verify they fail**

Run: `dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj --filter IslandCatalogTests`

Expected: failure because current rows have six columns.

- [ ] **Step 3: Extend the model and loader**

Add nullable backing fields and a guarded setter:

```csharp
public double? NavigationX { get; set; }
public double? NavigationY { get; set; }
public string NavigationSource { get; set; } = string.Empty;
public bool HasNavigationCoordinates =>
    NavigationX.HasValue && NavigationY.HasValue &&
    double.IsFinite(NavigationX.Value) && double.IsFinite(NavigationY.Value);
public NavigationPoint NavigationPoint => HasNavigationCoordinates
    ? new NavigationPoint(NavigationX!.Value, NavigationY!.Value)
    : throw new InvalidOperationException($"Missing navigation coordinates for {IslandsName}");
```

Update `LoadIslandsCSV()` to require columns 6 and 7 as invariant-culture doubles and column 8 as source text. Log and skip malformed rows rather than silently accepting a half-coordinate. Copy all three fields in `Barter.CreateIslandFromCatalog()`.

- [ ] **Step 4: Populate all CSV navigation fields**

Append `NavigationX,NavigationY,NavigationSource` to every row. Use direct Something Lovely node coordinates when present, calibrated BDO Codex coordinates for the twelve Margoria NPCs and new-region destinations, and a single documented main-map affine conversion for remaining ordinary islands. Do not use display normalization or per-island penalties.

Required Margoria values are generated from `worldX = 25*codexX-1714970`, `worldY = -25*codexY+1805480`; required direct-node values include Midnight, Grándiha, Halmad, Kashuma, Derko, Hakoven, and Arehaza.

- [ ] **Step 5: Run catalog tests and the application build**

Run:

```powershell
dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj
dotnet build iBarter.csproj --no-restore
```

Expected: catalog tests pass and the WPF project builds with zero errors.

- [ ] **Step 6: Commit**

Commit: `feat: load calibrated island navigation coordinates`

---

### Task 3: Explicit Special Groups and Inset Projection

**Files:**
- Modify: `Navigation/IslandNavigationGeometry.cs`
- Modify: `Tools/IslandNavigationTests/IslandNavigationGeometryTests.cs`
- Modify: `Tools/IslandNavigationTests/IslandCatalogTests.cs`

**Interfaces:**
- Produces: `SpecialDisplayGroup`, `GetDisplayGroup(string islandName)`, `LeftInsetNames`, `RightInsetNames`, and `BottomEdgeNames`.
- Consumes: navigation points loaded in Task 2.

- [ ] **Step 1: Write failing group tests**

Assert exact membership and counts:

```csharp
Assert.Equal(14, IslandNavigationGeometry.LeftInsetNames.Count);
Assert.Equal(5, IslandNavigationGeometry.RightInsetNames.Count);
Assert.Equal(2, IslandNavigationGeometry.BottomEdgeNames.Count);
Assert.Equal(21, IslandNavigationGeometry.LeftInsetNames
    .Concat(IslandNavigationGeometry.RightInsetNames)
    .Concat(IslandNavigationGeometry.BottomEdgeNames).Distinct().Count());
```

Also assert the precise names listed in the approved specification and that ordinary `Iliya` returns `SpecialDisplayGroup.MainMap`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj --filter "Group|Membership"`

Expected: compilation fails because group APIs do not exist.

- [ ] **Step 3: Add explicit immutable group definitions**

Define `FrozenSet<string>` values for the approved 14/5/2 lists and return a group enum. Throw during static initialization if sets overlap or counts differ, making accidental catalog drift immediately visible.

- [ ] **Step 4: Add real-catalog projection tests**

Load the navigation coordinates and project the left and right sets into:

```csharp
new NormalizedBounds(0.0, 0.0, 0.3775, 0.3267)
new NormalizedBounds(0.8175, 0.0, 1.0, 0.3267)
```

Assert all points remain inside a `0.0125` padding, Rickun renders north of Cox, and the right group preserves its calibrated north/south ordering.

- [ ] **Step 5: Run tests and commit**

Run: `dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj`

Expected: all tests pass.

Commit: `feat: define barter map inset projections`

---

### Task 4: Replace Synthetic Solver Distance

**Files:**
- Modify: `ViewModel/ShipCargoViewModel.cs:684-778`
- Modify: `Tools/IslandNavigationTests/IslandNavigationGeometryTests.cs`

**Interfaces:**
- Consumes: `Islands.NavigationPoint` and `IslandNavigationGeometry.Distance`.
- Produces: unchanged `SolveOptimalRoute()` public behavior with corrected costs.

- [ ] **Step 1: Add the six-stop distance regression test**

At minimum, assert from the calibrated coordinate table that after Midnight, Cox is nearer than Rickun and that the tail total `Midnight→Cox→Rickun` is shorter than `Midnight→Rickun→Cox`. Keep chain-order integration assertions in Task 6 where the WPF view model is available.

- [ ] **Step 2: Run the regression and confirm the current synthetic implementation is not covered by navigation geometry**

Run: `dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj --filter DistanceRegression`

Expected: pass only for calibrated world data; this establishes the replacement metric before deleting old code.

- [ ] **Step 3: Replace `DistanceBetween()` and delete push code**

Use:

```csharp
private static double DistanceBetween(Islands a, Islands b) {
    if (ReferenceEquals(a, b) || a.Island == b.Island) return 0;
    return IslandNavigationGeometry.Distance(a.NavigationPoint, b.NavigationPoint);
}
```

Delete `PLACEHOLDER_PUSH`, `DISTANCE_PUSH_Y`, and `CentroidForDistance()`. Update comments and logs to say `world units`.

- [ ] **Step 4: Build and search for stale synthetic-distance references**

Run:

```powershell
dotnet build iBarter.csproj --no-restore
rg -n "PLACEHOLDER_PUSH|DISTANCE_PUSH_Y|CentroidForDistance|pushed further" ViewModel Navigation
```

Expected: build succeeds; `rg` returns no matches.

- [ ] **Step 5: Commit**

Commit: `fix: route with calibrated world distances`

---

### Task 5: Render Corrected Inset Positions

**Files:**
- Modify: `View/MapControl.xaml.cs:457-465, 823-1016`
- Modify: `Tools/IslandNavigationTests/IslandNavigationGeometryTests.cs`

**Interfaces:**
- Consumes: explicit special groups, inset bounds, and projected normalized points.
- Produces: `GetDisplayCenterNormalized(Islands)` used by both marker placement and route overlay.

- [ ] **Step 1: Add resize-independent projection tests**

Assert normalized projection results are unchanged when converted to both `800×450` and `1600×900` pixel sizes, apart from the exact factor of two. Assert navigation points remain unchanged.

- [ ] **Step 2: Run tests to verify the display-center API is missing**

Run: `dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj --filter Projection`

Expected: existing pure projection tests pass; application integration still lacks a common display-center API.

- [ ] **Step 3: Implement one display-center path**

Cache left/right normalized projections from current catalog navigation points. `GetDisplayCenterNormalized()` returns:

- left/right projected points for inset members;
- existing thickness centroid for main-map and bottom-edge members.

Use that method in both `GetIslandCenter()` and `ButtonInitialisation()` marker margins. Construct margins around the point without changing label offsets:

```csharp
var p = GetDisplayCenterNormalized(_barter.IsLand);
myGrid_Image.Margin = new Thickness(
    p.X * Grid_MapMain.ActualWidth,
    p.Y * Grid_MapMain.ActualHeight,
    (1 - p.X) * Grid_MapMain.ActualWidth - 10,
    (1 - p.Y) * Grid_MapMain.ActualHeight - 10);
```

- [ ] **Step 4: Build and manually inspect the map**

Run: `dotnet build iBarter.csproj --no-restore`

Launch the application, open the map at its default size, then resize the dock. Verify all 14/5 inset markers stay within their gray boxes, Rickun is north of Cox, Arehaza is south of the remote Valencia chain, and route arrows terminate on the moved markers.

- [ ] **Step 5: Commit**

Commit: `fix: project remote barter markers into map insets`

---

### Task 6: Route Integration Regression and Final Validation

**Files:**
- Create: `Tools/IslandNavigationTests/RouteFixtureTests.cs`
- Modify: `Navigation/IslandNavigationGeometry.cs`: add a pure precedence-aware route helper so the algorithm is testable without WPF.
- Modify: `ViewModel/ShipCargoViewModel.cs`: delegate greedy ordering to the behavior-preserving helper while retaining logging and collection mutation.

**Interfaces:**
- Consumes: calibrated coordinates and prerequisite masks.
- Produces: a deterministic ordered index list for the existing greedy route.

- [ ] **Step 1: Extract the greedy selection loop into a pure helper**

The helper signature must be:

```csharp
public static IReadOnlyList<int> GreedyPrecedenceRoute(
    NavigationPoint start,
    IReadOnlyList<NavigationPoint> stops,
    IReadOnlyList<IReadOnlySet<int>> prerequisites)
```

Preserve the existing stable tie behavior by scanning indices in input order and replacing the best candidate only on strictly smaller distance.

- [ ] **Step 2: Write the complete six-barter regression**

Use indices Balvege `0`, Narvo `1`, Grandiha `2`, Midnight `3`, Rickun `4`, Cox `5`, with prerequisite sets `{}`, `{}`, `{1}`, `{0}`, `{}`, `{}` and Iliya as start. Assert:

```csharp
Assert.Equal(new[] { 0, 1, 2, 3, 5, 4 }, order);
```

Add a second start-point case proving there is no hard-coded south-first or Cox-first rule.

- [ ] **Step 3: Run the focused route tests**

Run: `dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj --filter RouteFixtureTests`

Expected: all route fixtures pass.

- [ ] **Step 4: Run complete verification**

Run:

```powershell
dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj
dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj
dotnet build iBarter.csproj --no-restore
git diff --check
```

Expected: both test projects pass, WPF build succeeds, and `git diff --check` reports no whitespace errors.

- [ ] **Step 5: Review scope and commit**

Inspect `git diff --name-only` and confirm only the files enumerated in this plan plus generated project metadata changed. Do not stage unrelated pre-existing modifications.

Commit: `test: cover calibrated barter route regression`

---

## Final Acceptance Checklist

- [ ] All routeable CSV rows contain finite world navigation coordinates and sources.
- [ ] The solver has no synthetic coordinate pushes.
- [ ] The special groups are exactly 14/5/2 and non-overlapping.
- [ ] Inset projection preserves a uniform scale and direction.
- [ ] The six-barter route ends `Midnight → Cox_Pirate → Rickun`.
- [ ] Existing planner tests and the WPF build remain green.
- [ ] The final diff contains no unrelated user work.
