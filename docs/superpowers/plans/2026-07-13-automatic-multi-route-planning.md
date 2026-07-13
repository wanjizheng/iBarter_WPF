# 自动多路线规划实施计划

> **给代理开发者：** 必须使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，严格按任务顺序实施。每个步骤使用复选框（`- [ ]`）跟踪；每个任务完成后先运行该任务的验证，再提交，等待 Review 通过后才能进入下一任务。

**目标：** 把 Planner 自动规划选出的全部交换项目转换为满足多仓库库存、交换依赖和逐步骤 LT 限制的多路线方案，并联动船舱路线选择与地图分色虚线，同时完整保留现有手动船舱工作流。

**架构：** 新建不依赖 WPF 的 `Routing` 领域层，使用统一状态转换器、确定性的 Anytime Branch-and-Bound 求解器和结果重放验证器生成不可变 `RoutePlan`。WPF 层通过协调器原子发布路线、维护手动/自动模式和输入失效；船舱与地图只消费同一份已验证路线快照，不再分别推断自动路线。

**技术栈：** C# 13、.NET 10、WPF、Syncfusion WPF、xUnit 2.9.3；不新增 OR-Tools 或其他求解器依赖。

## 全局约束

- 设计依据：`docs/superpowers/specs/2026-07-13-automatic-multi-route-planning-design.md`。
- 不修改现有三种 Planner 自动规划策略及其 Eq. 计算结果。
- 自动路线只读取 `Eq. > 0 && !ExchangeDone` 的行。
- 一个 Planner 行在第一版中不可拆分到多条路线。
- 每次装货和交换后都必须满足 `ExtraLT + 船上货物重量 <= TotalLT`。
- 路线结束时全部船上物品落入终点仓库，下一条路线从同一仓库开始。
- 优化目标严格按“路线数、总距离、仓库装货次数、最大 PeakLT、稳定 ID”字典序比较。
- 只有穷举搜索完成才允许返回 `Optimal`；受限搜索必须返回 `BestKnownWithinLimit`。
- 自动路线不得写入、覆盖或保存现有手动 `CargoDetails`。
- `ALL` 只改变地图渲染，不改变最近选择的具体路线船舱内容和 LT。
- 现有地图中键选岛、手动 Optimal Route、手动船舱 JSON 和金色虚线必须保持兼容。
- 求解器不得读取 `App`、WPF 控件、`ObservableCollection` 或本地化显示名称。
- 所有物品匹配使用 ItemID；RowId 和 WarehouseId 必须稳定且与语言无关。
- 当前工作区已有大量用户改动；每次提交只能包含本任务列出的文件，不能格式化或覆盖无关文件。

---

## 文件结构和职责

### 新增领域文件

- `Routing/AutomaticRouteModels.cs`：请求、任务、仓库、路线、步骤、状态枚举和诊断等不可变公开模型。
- `Routing/CargoWeightTable.cs`：唯一的等级重量映射。
- `Routing/RouteSimulationState.cs`：求解期间使用的深拷贝状态。
- `Routing/RouteStateTransition.cs`：装货、交换、卸货的唯一状态转换实现。
- `Routing/AutomaticRoutePreflight.cs`：输入验证、ItemID 依赖和不可达输入诊断。
- `Routing/RoutePlanFingerprint.cs`：输入规范化和 SHA-256 指纹。
- `Routing/DemandBundleGenerator.cs`：生成精确且有限的需求装货组合。
- `Routing/AutomaticRouteHeuristic.cs`：快速生成第一份可行上界并进行确定性局部改进。
- `Routing/AutomaticRoutePlanner.cs`：Anytime Branch-and-Bound / Best-First Search。
- `Routing/RoutePlanVerifier.cs`：从原始请求重放并验证返回方案。
- `Routing/AutomaticRoutePlanningAdapter.cs`：把 WPF 层预先截取的纯 DTO 快照转换成纯请求。
- `Routing/RouteRenderSnapshot.cs`：供地图使用的纯路线节点快照。

### 新增 WPF/协调文件

- `ViewModel/AutomaticRouteCoordinator.cs`：取消、版本号、发布、模式、路线选择和失效。
- `ViewModel/AutomaticRouteStepViewModels.cs`：仓库步骤和交换步骤的显示适配。

### 新增测试项目

- `Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj`
- `Tools/AutomaticRoutePlanningTests/RouteTestData.cs`
- `Tools/AutomaticRoutePlanningTests/RouteStateTransitionTests.cs`
- `Tools/AutomaticRoutePlanningTests/AutomaticRoutePreflightTests.cs`
- `Tools/AutomaticRoutePlanningTests/DemandBundleGeneratorTests.cs`
- `Tools/AutomaticRoutePlanningTests/AutomaticRouteHeuristicTests.cs`
- `Tools/AutomaticRoutePlanningTests/AutomaticRoutePlannerTests.cs`
- `Tools/AutomaticRoutePlanningTests/RoutePlanVerifierTests.cs`
- `Tools/AutomaticRoutePlanningTests/RoutePlanFingerprintTests.cs`
- `Tools/AutomaticRoutePlanningTests/RouteRenderSnapshotTests.cs`

### 修改现有文件

- `iBarter.csproj`：排除新测试目录，避免测试源码被主项目默认编译。
- `App.xaml.cs`：创建全局 `AutomaticRouteCoordinator`，在初始化完成后连接数据源。
- `Model/CargoProperty.cs`：增加 `PeakLT`，保留现有手动字段语义。
- `ViewModel/ShipCargoViewModel.cs`：增加模式与自动步骤集合；保留 `CargoDetails` 和手动排序算法。
- `ViewModel/StorageViewModel.cs`：把集合变化和条目数量变化统一暴露成 `StorageChanged`。
- `View/PlannerControl.xaml.cs`：自动规划成功后异步启动路线求解。
- `View/ShipCargoControl.xaml`：路线选择器、仓库/交换步骤模板和 PeakLT 展示。
- `View/ShipCargoControl.xaml.cs`：按模式切换 ItemsSource 和命令行为。
- `View/MapControl.xaml.cs`：从路线渲染快照绘制一条或多条虚线。
- `Resources/i18n/Strings.en-US.xaml`：英文 UI、状态和诊断文案。
- `Resources/i18n/Strings.zh-TW.xaml`：繁体中文对应文案。
- `README.md`：说明自动路线与手动路线两种模式。

---

### Task 1：建立纯领域模型、重量表和测试骨架

**文件：**

- 新建：`Routing/AutomaticRouteModels.cs`
- 新建：`Routing/CargoWeightTable.cs`
- 新建：`Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj`
- 新建：`Tools/AutomaticRoutePlanningTests/RouteTestData.cs`
- 新建：`Tools/AutomaticRoutePlanningTests/AutomaticRouteModelsTests.cs`
- 修改：`iBarter.csproj:13-25`

**接口：**

- 产出：`AutomaticRoutePlanningRequest`、`RouteBarterTask`、`RouteWarehouse`、`RoutePlan`、`PlannedRoute`、三种 `RouteStep`、`RouteLoadSnapshot`、`RoutePlanStatus`、`RoutePlanObjective`、`RouteDiagnostic`。
- 产出：`CargoWeightTable.GetWeightForLevel(int level)`。
- 约束：所有集合在构造时深拷贝为只读结构；对外模型不保存 `Barter`、`Items` 或 `Islands` 引用。

- [ ] **Step 1：创建测试项目并让主项目排除测试源码**

`Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj` 使用：

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
    <Compile Include="..\..\Routing\*.cs" Link="Routing\%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

在 `iBarter.csproj` 的测试排除 ItemGroup 中增加：

```xml
<Compile Remove="Tools\AutomaticRoutePlanningTests\**\*.cs" />
<None Remove="Tools\AutomaticRoutePlanningTests\**\*" />
<Content Remove="Tools\AutomaticRoutePlanningTests\**\*" />
```

- [ ] **Step 2：先写模型和重量失败测试**

至少包含：

```csharp
[Theory]
[InlineData(1, 100)]
[InlineData(2, 400)]
[InlineData(3, 900)]
[InlineData(4, 1000)]
[InlineData(5, 1000)]
[InlineData(6, 2000)]
[InlineData(7, 2000)]
[InlineData(0, 0)]
public void Weight_table_matches_existing_ship_cargo_rules(int level, int expected) =>
    Assert.Equal(expected, CargoWeightTable.GetWeightForLevel(level));

[Fact]
public void Objective_is_strictly_lexicographic() {
    var fewerRoutes = new RoutePlanObjective(1, 999_999, 99, 24_000, "z");
    var shorter = new RoutePlanObjective(2, 1, 1, 1, "a");
    Assert.True(fewerRoutes.CompareTo(shorter) < 0);
}
```

- [ ] **Step 3：运行测试并确认失败**

运行：

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj -v minimal
```

预期：编译失败，提示 `CargoWeightTable`、`RoutePlanObjective` 等类型不存在。

- [ ] **Step 4：实现模型和重量表**

公开签名固定为：

```csharp
namespace iBarter.Routing;

public readonly record struct RoutePoint(double X, double Y) {
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public sealed record RouteItem(string ItemId, string DisplayName, int Level, int UnitWeight);
public sealed record RouteItemQuantity(string ItemId, int Quantity);
public sealed record RouteWarehouse(
    string WarehouseId,
    string IslandId,
    RoutePoint Point,
    IReadOnlyDictionary<string, int> Inventory);
public sealed record RouteBarterTask(
    string RowId,
    string IslandId,
    RoutePoint Point,
    string Item1Id,
    int InputQuantity,
    string Item2Id,
    int OutputQuantity);
public sealed record RouteSearchLimits(int MaxExpandedStates, int MaxLocalMoves);
public sealed record AutomaticRoutePlanningRequest(
    IReadOnlyList<RouteBarterTask> Tasks,
    IReadOnlyDictionary<string, RouteItem> Items,
    IReadOnlyList<RouteWarehouse> Warehouses,
    int ExtraLT,
    int TotalLT,
    RouteSearchLimits Limits,
    string ConfigurationVersion);

public enum RoutePlanStatus {
    Optimal,
    BestKnownWithinLimit,
    Infeasible,
    NoFeasibleSolutionWithinLimit,
    Cancelled,
    InvalidInput,
}

public sealed record RouteDiagnostic(string Code, string RowId = "", string ItemId = "", string Detail = "");
public sealed record RouteLoadSnapshot(int CargoLT, int TotalWithExtraLT, int PeakTotalLT);

public abstract record RouteStep(string IslandId, RouteLoadSnapshot Load);
public sealed record WarehousePickupStep(
    string WarehouseId,
    string IslandId,
    IReadOnlyList<RouteItemQuantity> Items,
    RouteLoadSnapshot Load) : RouteStep(IslandId, Load);
public sealed record BarterStep(
    string RowId,
    string IslandId,
    RouteItemQuantity Consumed,
    RouteItemQuantity Produced,
    RouteLoadSnapshot Load) : RouteStep(IslandId, Load);
public sealed record WarehouseUnloadStep(
    string WarehouseId,
    string IslandId,
    IReadOnlyList<RouteItemQuantity> Items,
    RouteLoadSnapshot Load) : RouteStep(IslandId, Load);

public sealed record PlannedRoute(
    int Number,
    string StartWarehouseId,
    string EndWarehouseId,
    IReadOnlyList<RouteStep> Steps,
    double Distance,
    int InitialLT,
    int CurrentLT,
    int PeakLT);

public readonly record struct RoutePlanObjective(
    int RouteCount,
    double TotalDistance,
    int PickupStopCount,
    int MaxPeakLT,
    string StableTieBreak) : IComparable<RoutePlanObjective>;

public sealed record RoutePlan(
    RoutePlanStatus Status,
    IReadOnlyList<PlannedRoute> Routes,
    RoutePlanObjective? Objective,
    IReadOnlyList<RouteDiagnostic> Diagnostics,
    string InputFingerprint);
```

`RoutePlanObjective.CompareTo` 依次比较五个字段；距离使用 `double.CompareTo`，不提前四舍五入。模型构造辅助函数复制全部字典和列表，禁止调用方随后修改内容。

`CargoWeightTable` 的映射必须与现有 `ShipCargoControl.GetWeight` 完全一致。

`RouteTestData` 提供后续测试共用的确定性 fixture，固定签名和行为为：

```csharp
internal static class RouteTestData {
    // 一个 W 仓库、一个 A 岛任务；仓库持有 inputStock 个 IN。
    public static AutomaticRoutePlanningRequest SingleTask(
        int inputStock = 10,
        int inputQuantity = 2,
        int outputQuantity = 3,
        int inputLevel = 1,
        int outputLevel = 2,
        int extraLT = 0,
        int totalLT = 10_000);

    // 两个物品和两个独立任务；reverseDictionaryOrder 只改变字典插入顺序。
    public static AutomaticRoutePlanningRequest TwoItemRequest(bool reverseDictionaryOrder);

    // 分别改变 quantity、warehouse、total-lt 或 coordinate 中唯一一个字段。
    public static AutomaticRoutePlanningRequest Mutate(
        AutomaticRoutePlanningRequest source,
        string mutation);
}
```

所有坐标使用固定整数 `(0,0)`、`(10,0)`、`(20,0)`，RowId 使用 `r1/r2`，WarehouseId 使用 `W`，ItemID 使用 `IN/OUT`，搜索限制固定为 `100_000/1_000`。`Mutate` 遇到四个允许值之外的字符串时抛出 `ArgumentOutOfRangeException`。

- [ ] **Step 5：运行测试并确认通过**

运行同一测试命令。预期：全部 PASS。

- [ ] **Step 6：提交 Task 1**

```powershell
git add Routing/AutomaticRouteModels.cs Routing/CargoWeightTable.cs Tools/AutomaticRoutePlanningTests iBarter.csproj
git commit -m "feat(routing): add automatic route domain models"
```

**Review 门禁：** 模型层不得出现 `System.Windows`、`App`、`Barter`、`Items`、`Islands`；重量映射不得在第二个位置重复实现。

---

### Task 2：实现统一状态转换器和逐步骤 LT 模拟

**文件：**

- 新建：`Routing/RouteSimulationState.cs`
- 新建：`Routing/RouteStateTransition.cs`
- 新建：`Tools/AutomaticRoutePlanningTests/RouteStateTransitionTests.cs`

**接口：**

- 消费：Task 1 的请求、仓库、交换、步骤和载重模型。
- 产出：`RouteSimulationState.CreateInitial`、`RouteStateTransition.TryPickup`、`TryBarter`、`TryUnload`。
- 产出：失败转换不修改传入状态；成功转换返回深拷贝的新状态和一个明确步骤。

- [ ] **Step 1：写转换失败测试**

测试必须覆盖：

```csharp
[Fact]
public void Intermediate_output_peak_is_rejected_even_when_initial_and_final_are_safe() {
    // Extra=100, Total=1000；先装 1 个 100LT 输入，再交换得到 1 个 1000LT 输出。
    // 交换后总重 1100，必须失败，原状态库存和船舱均不变化。
}

[Fact]
public void Unload_moves_every_onboard_item_to_destination_warehouse() {
    // 卸货后 OnBoard 为空，终点仓库对应数量增加，Load.TotalWithExtraLT == ExtraLT。
}

[Fact]
public void Pickup_cannot_exceed_warehouse_stock_or_total_lt() {
    var insufficient = RouteTestData.SingleTask(inputStock: 1, inputQuantity: 2);
    var s1 = RouteSimulationState.CreateInitial(insufficient);
    var stockFailure = RouteStateTransition.TryPickup(insufficient, s1, "W",
        [new RouteItemQuantity("IN", 2)]);
    Assert.False(stockFailure.Success);
    Assert.Equal("insufficient-stock", stockFailure.Diagnostic?.Code);
    Assert.Same(s1, stockFailure.State);

    var overweight = RouteTestData.SingleTask(
        inputStock: 2, inputQuantity: 2, inputLevel: 7, totalLT: 3_000);
    var s2 = RouteSimulationState.CreateInitial(overweight);
    var weightFailure = RouteStateTransition.TryPickup(overweight, s2, "W",
        [new RouteItemQuantity("IN", 2)]);
    Assert.False(weightFailure.Success);
    Assert.Equal("overweight", weightFailure.Diagnostic?.Code);
    Assert.Same(s2, weightFailure.State);
}

[Fact]
public void Barter_consumes_exact_input_and_adds_exact_output() {
    var request = RouteTestData.SingleTask(inputQuantity: 2, outputQuantity: 3);
    var initial = RouteSimulationState.CreateInitial(request);
    var pickup = RouteStateTransition.TryPickup(request, initial, "W",
        [new RouteItemQuantity("IN", 2)]);
    Assert.True(pickup.Success);

    var barter = RouteStateTransition.TryBarter(request, pickup.State, taskIndex: 0);
    Assert.True(barter.Success);
    Assert.False(barter.State.OnBoard.ContainsKey("IN"));
    Assert.Equal(3, barter.State.OnBoard["OUT"]);
}
```

- [ ] **Step 2：运行定向测试并确认失败**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --filter FullyQualifiedName~RouteStateTransitionTests -v minimal
```

预期：缺少转换类型导致 FAIL。

- [ ] **Step 3：实现不可变状态和转换**

固定签名：

```csharp
public sealed class RouteSimulationState {
    public string CurrentIslandId { get; }
    public int CurrentRouteNumber { get; }
    public ulong CompletedMask { get; }
    public IReadOnlySet<string> VisitedWarehouseIds { get; }
    public IReadOnlyDictionary<string, int> OnBoard { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> WarehouseInventory { get; }
    public int CargoLT { get; }
    public int CurrentRoutePeakLT { get; }
    public IReadOnlyList<RouteStep> CurrentRouteSteps { get; }
    public IReadOnlyList<PlannedRoute> FinishedRoutes { get; }
    public double TotalDistance { get; }
    public int PickupStopCount { get; }
}

public sealed record RouteTransitionResult(
    bool Success,
    RouteSimulationState State,
    RouteStep? Step,
    RouteDiagnostic? Diagnostic);

public static class RouteStateTransition {
    public static RouteTransitionResult TryPickup(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        string warehouseId,
        IReadOnlyList<RouteItemQuantity> items);

    public static RouteTransitionResult TryBarter(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        int taskIndex);

    public static RouteTransitionResult TryUnload(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        string warehouseId);
}
```

每个转换按以下顺序执行：验证参数 → 克隆必要字典 → 应用数量变化 → 删除数量为零的规范化条目 → 重新计算 `CargoLT` → 加 `ExtraLT` → 验证 `TotalLT` → 计算从当前位置到目标岛的欧氏距离 → 追加步骤。任何失败返回原 `state` 引用和诊断码：`unknown-warehouse`、`insufficient-stock`、`overweight`、`missing-input`、`already-completed`、`warehouse-revisited`、`empty-route`。

`TryUnload` 只允许在当前路线至少完成过一个 `BarterStep` 后执行，防止搜索生成无限空路线。它先保存卸货前的载重作为 `PlannedRoute.CurrentLT`，再把船上所有正数量物品加入终点仓库，创建卸货后总重仅为 `ExtraLT` 的 `WarehouseUnloadStep`，封装当前 `PlannedRoute`，清空船舱、重置本路线仓库访问集合，并将下一路线当前位置设为终点仓库岛。

- [ ] **Step 4：运行转换测试并确认通过**

运行 Step 2 命令。预期：全部 PASS。

- [ ] **Step 5：运行全部新测试并提交**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj -v minimal
git add Routing/RouteSimulationState.cs Routing/RouteStateTransition.cs Tools/AutomaticRoutePlanningTests/RouteStateTransitionTests.cs
git commit -m "feat(routing): add cargo transition simulator"
```

**Review 门禁：** 必须验证失败无副作用；任何重量计算都调用 `CargoWeightTable` 或请求中的 `UnitWeight`，不能复制 switch。

---

### Task 3：实现预检查和稳定输入指纹

**文件：**

- 新建：`Routing/AutomaticRoutePreflight.cs`
- 新建：`Routing/RoutePlanFingerprint.cs`
- 新建：`Tools/AutomaticRoutePlanningTests/AutomaticRoutePreflightTests.cs`
- 新建：`Tools/AutomaticRoutePlanningTests/RoutePlanFingerprintTests.cs`

**接口：**

- 产出：`AutomaticRoutePreflight.Validate(request)`。
- 产出：`RoutePlanFingerprint.Compute(request)`。
- 规则：改变集合插入顺序不改变指纹；改变任何影响路线的数据必须改变指纹。

- [ ] **Step 1：写预检查和指纹失败测试**

覆盖：任务数 65、重复 RowId、非有限坐标、缺失 ItemID、负库存、`TotalLT <= 0`、`ExtraLT > TotalLT`、单任务空船仍超重、输入既无库存也无生产者、表面循环被初始库存打破。

指纹测试：

```csharp
[Fact]
public void Fingerprint_is_order_independent_for_dictionary_entries() {
    var forward = RouteTestData.TwoItemRequest(reverseDictionaryOrder: false);
    var reverse = RouteTestData.TwoItemRequest(reverseDictionaryOrder: true);
    Assert.Equal(RoutePlanFingerprint.Compute(forward), RoutePlanFingerprint.Compute(reverse));
}

[Theory]
[InlineData("quantity")]
[InlineData("warehouse")]
[InlineData("total-lt")]
[InlineData("coordinate")]
public void Fingerprint_changes_when_route_relevant_input_changes(string mutation) {
    var original = RouteTestData.TwoItemRequest(reverseDictionaryOrder: false);
    var changed = RouteTestData.Mutate(original, mutation);
    Assert.NotEqual(RoutePlanFingerprint.Compute(original), RoutePlanFingerprint.Compute(changed));
}
```

- [ ] **Step 2：运行定向测试并确认失败**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --filter "FullyQualifiedName~Preflight|FullyQualifiedName~Fingerprint" -v minimal
```

- [ ] **Step 3：实现预检查**

固定结果：

```csharp
public sealed record RoutePreflightResult(
    bool IsValid,
    IReadOnlyList<RouteDiagnostic> Diagnostics,
    IReadOnlyList<ulong> ProducerMasks);

public static class AutomaticRoutePreflight {
    public static RoutePreflightResult Validate(AutomaticRoutePlanningRequest request);
}
```

不可达判断使用库存闭包：从所有仓库初始库存合计开始，反复加入“当前输入可满足”的任务输出，直到没有变化；闭包结束后输入仍不可满足的任务记录 `unreachable-input`。该闭包只用于发现必然无解，不能代替实际分仓库存和路线顺序检查。

- [ ] **Step 4：实现规范化 SHA-256 指纹**

规范串必须按以下顺序写入 UTF-8：配置版本 → Extra/Total → 按 RowId 排序的任务全部字段 → 按 WarehouseId 排序的仓库坐标和按 ItemID 排序的库存 → 按 ItemID 排序的物品等级与重量 → 搜索限制。数值使用 `InvariantCulture` 和 round-trip 格式 `"R"`。

- [ ] **Step 5：运行测试、检查确定性并提交**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj -v minimal
git add Routing/AutomaticRoutePreflight.cs Routing/RoutePlanFingerprint.cs Tools/AutomaticRoutePlanningTests
git commit -m "feat(routing): validate and fingerprint route requests"
```

**Review 门禁：** 不允许用 `GetHashCode()` 充当持久指纹；预检查不能把“可能无解”错误标成 `Infeasible`。

---

### Task 4：生成精确的需求装货组合和快速可行方案

**文件：**

- 新建：`Routing/DemandBundleGenerator.cs`
- 新建：`Routing/AutomaticRouteHeuristic.cs`
- 新建：`Tools/AutomaticRoutePlanningTests/DemandBundleGeneratorTests.cs`
- 新建：`Tools/AutomaticRoutePlanningTests/AutomaticRouteHeuristicTests.cs`

**接口：**

- 产出：针对当前状态和仓库的有限、去重、按稳定顺序排列的装货组合。
- 产出：`AutomaticRouteHeuristic.TryBuildIncumbent`，只能返回可由转换器完整重放的方案。

- [ ] **Step 1：写需求组合测试**

至少验证：

- 一个任务只生成它缺少的数量，不把仓库全部库存装船；
- A→B、B→C 链只需要从仓库装 A；
- 两个独立任务生成单任务组合和可承载的联合组合；
- 相同需求向量去重；
- 超过容量的组合被过滤；
- 生成顺序不受字典插入顺序影响。

- [ ] **Step 2：运行需求测试并确认失败**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --filter FullyQualifiedName~DemandBundleGeneratorTests -v minimal
```

- [ ] **Step 3：实现需求向量 DP**

固定签名：

```csharp
public sealed record DemandBundle(
    IReadOnlyList<RouteItemQuantity> Items,
    ulong SupportedTaskMask,
    int TotalCargoLT,
    string StableKey);

public static class DemandBundleGenerator {
    public static IReadOnlyList<DemandBundle> Generate(
        AutomaticRoutePlanningRequest request,
        RouteSimulationState state,
        string warehouseId,
        ulong remainingMask);
}
```

使用子集 DP 枚举从当前船上库存出发的可执行任务前缀：追加一个任务时，如果当前模拟库存不足，则把缺口加入 `RequiredInitial`；随后扣除输入、加入输出。对同一任务子集保留互不支配的 `RequiredInitial` 向量，最后减去当前船上已有数量，过滤仓库库存不足和 LT 超限的向量。按 `TotalCargoLT`、支持任务数降序、`StableKey` 排序。

- [ ] **Step 4：写快速方案失败测试**

场景必须包括：

```text
Iliya 有 LV1 输入 → A 岛交换 → Velia 有 LV6 输入 → B 岛交换 → Velia 卸货
```

断言为一条路线、包含两个 `WarehousePickupStep`，每一步 LT 合法。另加 A/B 近、C/D 近且容量只能装两个任务的测试，断言启发式至少返回完整可行方案，不要求此时证明最优。

- [ ] **Step 5：实现确定性启发式**

固定签名：

```csharp
public sealed record RouteIncumbent(RoutePlan Plan, RouteSimulationState FinalState);

public static class AutomaticRouteHeuristic {
    public static RouteIncumbent? TryBuildIncumbent(
        AutomaticRoutePlanningRequest request,
        RoutePreflightResult preflight,
        CancellationToken cancellationToken);
}
```

算法顺序固定为：枚举每个可提供未完成输入的起点仓库 → 从当前状态优先选择无需新装货且可执行的最近任务 → 否则选择距离最短且能提供最优需求组合的尚未访问仓库 → 当前路线无法继续时返回最近可卸货仓库结束路线 → 重复直到全部完成。平局依次使用新增路线数、距离、仓库 ID、RowId。

完成方案后，在 `MaxLocalMoves` 限制内依次尝试 precedence-safe relocate、swap、2-opt；Task 4 直接从初始状态使用转换器完整重放候选，重放成功后才能接受。Task 5 引入 `RoutePlanVerifier` 后，用统一验证器替换这段内部重放包装。

- [ ] **Step 6：运行测试并提交**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj -v minimal
git add Routing/DemandBundleGenerator.cs Routing/AutomaticRouteHeuristic.cs Tools/AutomaticRoutePlanningTests
git commit -m "feat(routing): build deterministic route incumbents"
```

**Review 门禁：** 启发式不得直接修改状态字典；任何“看起来可行”的候选都必须通过转换器。

---

### Task 5：实现 Anytime 精确搜索和结果重放验证

**文件：**

- 新建：`Routing/AutomaticRoutePlanner.cs`
- 新建：`Routing/RoutePlanVerifier.cs`
- 新建：`Tools/AutomaticRoutePlanningTests/AutomaticRoutePlannerTests.cs`
- 新建：`Tools/AutomaticRoutePlanningTests/RoutePlanVerifierTests.cs`

**接口：**

- 消费：预检查、需求组合、启发式上界和统一转换器。
- 产出：具有真实 `RoutePlanStatus` 的 `RoutePlan`。
- 产出：`RoutePlanVerifier.Verify`，发布前必须成功。

- [ ] **Step 1：写暴力对照和状态真实性测试**

为 2–6 个任务实现仅存在于测试项目的暴力枚举器，并覆盖：

- A+B、C+D 的两路线分组距离最短；
- 跨两个仓库的一条路线优于两条路线；
- 生产者先于消费者；
- 第一条路线落库的产出被第二条路线取用；
- 一个不可拆分任务必然超重时为 `Infeasible`；
- `MaxExpandedStates=1` 且已有启发式方案时为 `BestKnownWithinLimit`；
- 完整枚举结束时才为 `Optimal`；
- 同一请求连续运行 20 次产生相同步骤和目标。

- [ ] **Step 2：运行求解器测试并确认失败**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --filter "FullyQualifiedName~AutomaticRoutePlannerTests|FullyQualifiedName~RoutePlanVerifierTests" -v minimal
```

- [ ] **Step 3：实现搜索状态键、优先队列和支配缓存**

公开入口固定为：

```csharp
public sealed class AutomaticRoutePlanner {
    public RoutePlan Plan(
        AutomaticRoutePlanningRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record RouteVerificationResult(
    bool Success,
    RouteDiagnostic? Diagnostic,
    RoutePlan? VerifiedPlan);

public static class RoutePlanVerifier {
    public static RouteVerificationResult Verify(
        AutomaticRoutePlanningRequest request,
        RoutePlan plan);
}
```

内部 `SearchKey` 必须包含当前位置、完成位图、当前路线仓库访问位图、船上按 ItemID 排序的数量、相关仓库动态库存和路线边界状态。不能只用完成位图缓存，因为同样完成集合可能具有完全不同的可用库存。

优先队列比较顺序：乐观路线数 → 乐观距离 →装货次数 → PeakLT → 稳定序号。稳定序号单调递增，禁止依赖字典迭代顺序。

- [ ] **Step 4：实现下界和状态扩展**

每个状态依次生成：

1. 当前船上输入足够、依赖满足的 `TryBarter` 动作；
2. 每个当前路线未访问仓库的 `DemandBundleGenerator` 装货动作；
3. 当前路线已经完成至少一个交换时，每个仓库的 `TryUnload` 结束路线动作。

下界至少包含：剩余任务所需的最少路线数、当前位置到最近剩余节点、剩余节点最小生成树和最近可结束仓库距离。下界只能低估，无法证明安全的剪枝不加入第一版。

达到 `MaxExpandedStates` 时：有 incumbent 返回 `BestKnownWithinLimit`；无 incumbent 返回 `NoFeasibleSolutionWithinLimit`。队列自然耗尽：有方案返回 `Optimal`；无方案返回 `Infeasible`。取消时返回 `Cancelled`。

- [ ] **Step 5：实现结果重放验证**

验证器从 `RouteSimulationState.CreateInitial(request)` 开始，严格按每个步骤调用相应转换方法，并检查：步骤类型、仓库、RowId、数量、载重快照、路线首尾、全部任务完成、目标元组和输入指纹。任一不一致返回 `verification-mismatch`，不得修补结果。

- [ ] **Step 6：运行新测试和现有规划/导航测试**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj -v minimal
dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj -v minimal
dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj -v minimal
```

预期：全部 PASS。

- [ ] **Step 7：提交 Task 5**

```powershell
git add Routing/AutomaticRoutePlanner.cs Routing/RoutePlanVerifier.cs Tools/AutomaticRoutePlanningTests
git commit -m "feat(routing): solve and verify multi-route plans"
```

**Review 门禁：** Review 时使用测试暴力枚举器逐个比较小实例；任何把受限结果标为 `Optimal` 的实现直接拒绝。

---

### Task 6：实现实时数据适配、协调器、模式和失效机制

**文件：**

- 新建：`Routing/AutomaticRoutePlanningAdapter.cs`
- 新建：`Routing/RouteRenderSnapshot.cs`
- 新建：`ViewModel/AutomaticRouteCoordinator.cs`
- 新建：`ViewModel/AutomaticRouteStepViewModels.cs`
- 新建：`Tools/AutomaticRoutePlanningTests/RouteRenderSnapshotTests.cs`
- 修改：`App.xaml.cs:34-42, 89-91, 112-128`
- 修改：`ViewModel/ShipCargoViewModel.cs:10-27`
- 修改：`ViewModel/StorageViewModel.cs:51-60, 164-171`
- 修改：`Model/CargoProperty.cs:9-56`

**接口：**

- 产出：从 WPF 层构造的纯快照生成请求，纯求解器和纯适配器都不接触全局对象。
- 产出：`CargoMode.Manual/AutomaticRoute`，`CurrentPlan`、`SelectedRouteNumber`、`ShowAllRoutes`。
- 产出：输入变化自动取消和失效；手动 `CargoDetails` 永不被自动路线覆盖。

- [ ] **Step 1：为适配和渲染快照写失败测试**

覆盖仓库映射：Velia→Velia、Iliya→Iliya、Epheria→Epheria、Ancado→Sausan；ItemID 库存保持分仓，不能像现有 Planner 适配那样求总和。覆盖 `ALL` 返回所有路线、具体选择只返回一条，且相邻相同岛屿只在渲染快照中折叠。

- [ ] **Step 2：实现适配器和渲染快照**

固定入口：

```csharp
public static class AutomaticRoutePlanningAdapter {
    public static AutomaticRoutePlanningRequest BuildRequest(
        IReadOnlyList<PlannerRouteSnapshot> plannerRows,
        IReadOnlyList<StorageItemSnapshot> storageItems,
        IReadOnlyList<IslandRouteSnapshot> islands,
        CargoCapacitySnapshot cargo,
        RouteSearchLimits limits);
}

public sealed record PlannerRouteSnapshot(
    string RowId, bool ExchangeDone, int ExchangeQuantity,
    string IslandId,
    string Item1Id, string Item1DisplayName, int Item1Level, int Item1Number,
    string Item2Id, string Item2DisplayName, int Item2Level, int Item2Number);
public sealed record StorageItemSnapshot(
    string ItemId, int Level,
    int Velia, int Iliya, int Epheria, int Ancado);
public sealed record IslandRouteSnapshot(string IslandId, RoutePoint Point);
public sealed record CargoCapacitySnapshot(int ExtraLT, int TotalLT);

public sealed record RouteRenderPath(int RouteNumber, int ColorIndex, IReadOnlyList<string> IslandIds);
public sealed record RouteRenderSnapshot(bool IsManual, bool ShowAll, IReadOnlyList<RouteRenderPath> Paths);
```

WPF 层在 UI 线程把 `Barter`、`Items`、`Islands` 和 `CargoProperty` 投影为上述快照。快照构造代码放在协调器的 UI 适配方法中；`AutomaticRoutePlanningAdapter` 自身保持纯 C#，其输入输出都不能包含实时对象。

- [ ] **Step 3：实现协调器状态机**

固定公开面：

```csharp
public enum CargoMode { Manual, AutomaticRoute }

public sealed class AutomaticRouteCoordinator : NotificationObject, IDisposable {
    public CargoMode Mode { get; }
    public RoutePlan? CurrentPlan { get; }
    public int? SelectedRouteNumber { get; }
    public bool ShowAllRoutes { get; }
    public IReadOnlyList<AutomaticRouteStepViewModel> VisibleAutomaticSteps { get; }
    public event EventHandler? RouteDisplayChanged;

    public Task<RoutePlan> GenerateAsync(AutomaticRoutePlanningRequest request);
    public void SelectRoute(int routeNumber);
    public void SelectAll();
    public void ActivateManual();
    public void Invalidate(string reason);
    public RouteRenderSnapshot GetRenderSnapshot(IReadOnlyList<Barter> manualCargo);
}
```

自动步骤 ViewModel 固定提供以下绑定面，避免 XAML 读取领域对象内部字典：

```csharp
public abstract class AutomaticRouteStepViewModel {
    public string Title { get; }
    public string Detail { get; }
    public string LoadText { get; }
}

public sealed class WarehouseRouteStepViewModel : AutomaticRouteStepViewModel {
    public string WarehouseId { get; }
    public bool IsUnload { get; }
}

public sealed class BarterRouteStepViewModel : AutomaticRouteStepViewModel {
    public string RowId { get; }
    public string Item1Icon { get; }
    public string Item2Icon { get; }
}
```

`GenerateAsync` 每次增加 RequestId，取消旧 CTS，在 `Task.Run` 中求解和验证，只在 RequestId 仍为最新且指纹未失效时发布。有效方案默认选择 Route 1；失败方案清除自动显示但不清空 `CargoDetails`。

- [ ] **Step 4：连接库存和 LT 失效事件**

`StorageViewModel` 增加 `public event EventHandler? StorageChanged`。集合新增/删除时订阅或取消每个 `Items.PropertyChanged`；四个 `StorageVeliaQuantity_*` 属性变化时触发一次事件。批量加载期间抑制逐条事件，在完成后触发一次。

`CargoProperty` 增加 `PeakLT` 属性。协调器只监听 `ExtraLT` 和 `TotalLT` 失效，不能因为自动路线更新 `InitialLT`、`CurrentLT`、`PeakLT` 而递归清除自己。

- [ ] **Step 5：把协调器注册到 App 并扩展 ShipCargoViewModel**

`App` 增加：

```csharp
public static AutomaticRouteCoordinator myRouteCoordinator = null!;
```

在 PVM、StorageVM、CVM 初始化后创建并连接协调器。`ShipCargoViewModel.CargoDetails` 保持原样；只增加只读模式代理和自动步骤集合，不移动或删除现有 `SolveOptimalRoute`。

- [ ] **Step 6：运行测试和构建**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj -v minimal
dotnet build iBarter.csproj -p:Platform=x86 -v minimal
```

- [ ] **Step 7：提交 Task 6**

```powershell
git add Routing/AutomaticRoutePlanningAdapter.cs Routing/RouteRenderSnapshot.cs ViewModel/AutomaticRouteCoordinator.cs ViewModel/AutomaticRouteStepViewModels.cs App.xaml.cs ViewModel/ShipCargoViewModel.cs ViewModel/StorageViewModel.cs Model/CargoProperty.cs Tools/AutomaticRoutePlanningTests
git commit -m "feat(routing): coordinate automatic route state"
```

**Review 门禁：** 在自动模式生成和切换路线后，序列化前后的手动 `CargoDetails` 必须逐项相同。

---

### Task 7：把 Planner 自动规划接到后台路线求解

**文件：**

- 修改：`View/PlannerControl.xaml.cs:1071-1217`
- 修改：`View/PlannerControl.xaml.cs` 中 New、Load、Clean、Eq/CK 编辑处理位置

**接口：**

- 消费：Task 6 的适配器和协调器。
- 行为：Eq. 计算仍然先完成；然后异步生成路线；路线失败不回滚 Eq.。

- [ ] **Step 1：把点击处理器改为异步且防重入**

签名改为：

```csharp
private async void ButtonAdv_AutoPlan_Click(object sender, RoutedEventArgs e)
```

保留 1077–1216 的现有 Eq. 快照、计算、应用、保存和日志逻辑。Eq. 成功应用后才调用 `AutomaticRoutePlanningAdapter.BuildRequest` 和 `await App.myRouteCoordinator.GenerateAsync(request)`。执行期间禁用 `ButtonAdv_AutoPlan`，在 `finally` 中恢复；第二次触发通过协调器取消旧求解。

- [ ] **Step 2：按状态记录本地化日志**

处理必须穷尽所有 `RoutePlanStatus`：

```csharp
switch (routePlan.Status) {
    case RoutePlanStatus.Optimal:
        App.myCFun.Log(svc.Localize("str.Log.AutoRoute.Optimal",
            routePlan.Routes.Count, routePlan.Objective?.TotalDistance ?? 0), Brushes.DarkOliveGreen);
        break;
    case RoutePlanStatus.BestKnownWithinLimit:
        App.myCFun.Log(svc.Localize("str.Log.AutoRoute.BestKnown",
            routePlan.Routes.Count, routePlan.Objective?.TotalDistance ?? 0), Brushes.Orange);
        break;
    case RoutePlanStatus.Infeasible:
        App.myCFun.Log(svc.Localize("str.Log.AutoRoute.Infeasible",
            routePlan.Diagnostics.FirstOrDefault()?.Detail ?? ""), Brushes.Red);
        break;
    case RoutePlanStatus.NoFeasibleSolutionWithinLimit:
        App.myCFun.Log(svc.Localize("str.Log.AutoRoute.NoFeasibleWithinLimit"), Brushes.OrangeRed);
        break;
    case RoutePlanStatus.Cancelled:
        App.myCFun.Log(svc.Localize("str.Log.AutoRoute.Cancelled"), Brushes.Gray);
        break;
    case RoutePlanStatus.InvalidInput:
        App.myCFun.Log(svc.Localize("str.Log.AutoRoute.InvalidInput",
            routePlan.Diagnostics.FirstOrDefault()?.Detail ?? ""), Brushes.Red);
        break;
}
```

- [ ] **Step 3：连接 Planner 输入失效**

New、Load、Clean、删除行、Eq 编辑、CK 变化完成后调用 `Invalidate`。Auto Plan 批量写 Eq. 时先由协调器开始新请求，避免每一行变化产生用户可见日志；最后只发布新请求结果。

- [ ] **Step 4：构建并手工验证失败不破坏 Eq.**

```powershell
dotnet build iBarter.csproj -p:Platform=x86 -v minimal
dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj -v minimal
```

手工构造 `TotalLT` 小于单个交换需要重量的情况：Eq. 仍保留，自动路线显示明确无解，手动船舱不变。

- [ ] **Step 5：提交 Task 7**

```powershell
git add View/PlannerControl.xaml.cs
git commit -m "feat(planner): generate routes after auto planning"
```

**Review 门禁：** 不允许把现有 Eq. 求解放入后台线程后直接访问 WPF 集合；只有纯请求可以进入 `Task.Run`。

---

### Task 8：实现船舱路线选择和仓库/交换步骤显示

**文件：**

- 修改：`View/ShipCargoControl.xaml:1-101`
- 修改：`View/ShipCargoControl.xaml.cs:20-39, 44-65, 82-236, 301-352`
- 修改：`Model/CargoProperty.cs`

**接口：**

- 消费：协调器的路线选项和 `VisibleAutomaticSteps`。
- 行为：自动列表和手动 CargoDetails 使用不同 ItemsSource；自动模式不允许拖动或清空手动数据。

- [ ] **Step 1：增加路线选择器和两种自动步骤模板**

在根 `UserControl` 增加 `xmlns:vm="clr-namespace:iBarter.ViewModel"`。在工具栏增加 `ComboBoxAdv_RouteSelector`，ItemsSource 绑定协调器提供的 `ALL / Route N` 选项。增加：

```xml
<DataTemplate DataType="{x:Type vm:WarehouseRouteStepViewModel}">
  <Border Padding="5" Margin="2" Background="#223B5B" CornerRadius="3">
    <StackPanel>
      <TextBlock Text="{Binding Title}" FontWeight="Bold" Foreground="#FFD166" />
      <TextBlock Text="{Binding Detail}" TextWrapping="Wrap" Foreground="White" />
      <TextBlock Text="{Binding LoadText}" Foreground="#9ED8FF" />
    </StackPanel>
  </Border>
</DataTemplate>
<DataTemplate DataType="{x:Type vm:BarterRouteStepViewModel}">
  <Border Padding="5" Margin="2" Background="#182532" CornerRadius="3">
    <StackPanel>
      <TextBlock Text="{Binding Title}" FontWeight="Bold" Foreground="White" />
      <StackPanel Orientation="Horizontal">
        <Image Source="{Binding Item1Icon}" Width="24" Height="24" />
        <TextBlock Text="{Binding Detail}" Margin="4,0" VerticalAlignment="Center" Foreground="White" />
        <Image Source="{Binding Item2Icon}" Width="24" Height="24" />
      </StackPanel>
      <TextBlock Text="{Binding LoadText}" Foreground="#9ED8FF" />
    </StackPanel>
  </Border>
</DataTemplate>
```

不要创建“假 Barter”表示仓库。

- [ ] **Step 2：实现模式切换和 ALL 语义**

具体 Route N：`ListBox_ShipCargo.ItemsSource = VisibleAutomaticSteps`，PropertyGrid 显示该路线 Initial/Current/Peak。`ALL`：只调用协调器 `SelectAll()`，ItemsSource 和三个 LT 保持上次具体路线。手动模式：恢复 `CargoDetails` 和原有 `UpdateCurrentLV()`。

- [ ] **Step 3：保护手动命令**

拖动、Clean 和 Optimal Route 只在 `CargoMode.Manual` 生效。自动模式下仓库行不执行复制；交换行双击复制时通过其 RowId 解析实时 Barter。开始地图中键手动操作时调用 `ActivateManual()`。

- [ ] **Step 4：移除自动模式下的重复重量计算**

自动路线 LT 直接读取 `PlannedRoute.InitialLT/CurrentLT/PeakLT`。现有 `UpdateCurrentLV()` 只服务手动 `CargoDetails`；把其中等级重量调用改为 `CargoWeightTable.GetWeightForLevel`，删除 `ShipCargoControl.GetWeight` 私有 switch，确保重量表唯一。

- [ ] **Step 5：构建并手工检查两种模式**

```powershell
dotnet build iBarter.csproj -p:Platform=x86 -v minimal
```

检查：手动船舱加载、拖动、Optimal、Clean、复制；自动 Route 1 仓库行、交换行、不可拖动；ALL 不改变列表；切回手动后原列表顺序和内容完全恢复。

- [ ] **Step 6：提交 Task 8**

```powershell
git add View/ShipCargoControl.xaml View/ShipCargoControl.xaml.cs Model/CargoProperty.cs
git commit -m "feat(cargo): display automatic route steps"
```

**Review 门禁：** 自动模式的任何按钮操作都不得调用现有 `SaveData()` 写入自动步骤。

---

### Task 9：重构地图虚线为共享快照和多路线分色渲染

**文件：**

- 修改：`View/MapControl.xaml.cs:314-510, 1063-1100`
- 修改：`Tools/AutomaticRoutePlanningTests/RouteRenderSnapshotTests.cs`

**接口：**

- 消费：`AutomaticRouteCoordinator.GetRenderSnapshot`。
- 保留：手动模式仍从 `CargoDetails` 构造一条金色路线。
- 产出：自动具体路线单色、ALL 多色；仓库节点在正确顺序内。

- [ ] **Step 1：扩展纯快照测试**

断言：Route 1 和 Route 2 使用稳定不同 `ColorIndex`；ALL 同时包含两者；具体选择只包含目标路线；`Iliya → A → Velia → B → Velia` 顺序保持；连续相同岛屿折叠但非连续重复保留。

- [ ] **Step 2：拆分手动快照和通用绘制**

把现有 `ComputeRoute()` 改为只负责 `BuildManualRoutePath()`；`DrawRouteOverlay()` 获取协调器快照并调用：

```csharp
private void DrawRoutePath(RouteRenderPath path, Brush stroke, DoubleCollection dashArray)
```

现有 `ROUTE_LINE_TAG` 清理机制、箭头、`IsHitTestVisible=false` 和 resize 重建逻辑继续使用。

- [ ] **Step 3：加入确定性颜色和线型表**

至少提供八种在深色地图上高对比的颜色。Route Number 决定颜色索引；超过颜色数量后循环颜色，同时按轮次使用 `{4,2}`、`{8,3}`、`{2,2}` 等不同虚线。手动路线固定使用现有 Gold 和 `{4,2}`。

- [ ] **Step 4：仓库节点与错误防御**

快照中的岛屿无法解析时跳过该路径并记录一次诊断，不能让 100ms timer 持续刷日志。使用 `(fingerprint, islandId)` 去重诊断。零长度腿不绘制。

- [ ] **Step 5：运行测试、构建和手工地图检查**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj -v minimal
dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj -v minimal
dotnet build iBarter.csproj -p:Platform=x86 -v minimal
```

调整窗口尺寸，确认所有路线跟随岛屿移动；ALL 分色；选择具体路线只剩一条；切回手动恢复金色线。

- [ ] **Step 6：提交 Task 9**

```powershell
git add View/MapControl.xaml.cs Tools/AutomaticRoutePlanningTests/RouteRenderSnapshotTests.cs
git commit -m "feat(map): render selected and all automatic routes"
```

**Review 门禁：** 自动地图不能再读取 `CargoDetails`；手动地图不能依赖 `RoutePlan`。

---

### Task 10：补齐本地化、文档和端到端回归

**文件：**

- 修改：`Resources/i18n/Strings.en-US.xaml`
- 修改：`Resources/i18n/Strings.zh-TW.xaml`
- 修改：`README.md`
- 修改：前述文件中 Review 发现的仅限本功能缺陷

**接口：**

- 所有用户可见的新文本都有 en-US 和 zh-TW 同名 key。
- README 说明自动/手动模式、ALL 和结果状态含义。

- [ ] **Step 1：增加并核对本地化 key**

至少包含：路线选择器、ALL、Route 格式、装货、卸货、步骤后 LT、PeakLT、正在求解、最优、最佳已知、无解、搜索受限、已取消、输入非法、缺少库存、单任务超重、坐标缺失、路线失效。

使用一致 key 前缀：

```text
str.ShipCargo.AutoRoute.*
str.Log.AutoRoute.*
str.Msg.AutoRoute.*
```

运行以下命令确认两份字典 key 集合一致：

```powershell
$en = Select-String 'x:Key="([^"]+)"' Resources/i18n/Strings.en-US.xaml | ForEach-Object { $_.Matches.Groups[1].Value }
$zh = Select-String 'x:Key="([^"]+)"' Resources/i18n/Strings.zh-TW.xaml | ForEach-Object { $_.Matches.Groups[1].Value }
Compare-Object ($en | Sort-Object) ($zh | Sort-Object)
```

预期：无输出。

- [ ] **Step 2：更新 README**

新增章节说明：Auto Plan 完成后自动求路线；路线可能包含仓库装货行；Route N 更新船舱和单路线地图；ALL 只展示地图；手动模式仍通过中键和 Optimal Route 使用；`Optimal` 与“最佳已知”含义不同。

- [ ] **Step 3：运行完整自动测试**

```powershell
dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj -v minimal
dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj -v minimal
dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj -v minimal
dotnet test Tools/CaptureRectGuardTest/CaptureRectGuardTest.csproj -v minimal
dotnet build iBarter.csproj -p:Platform=x86 -v minimal
```

预期：所有测试 PASS，构建零错误。

- [ ] **Step 4：执行端到端手工验收矩阵**

逐项记录证据：

1. 19 个实际有效交换行能生成路线，UI 不冻结；
2. 伊利亚 LV1–LV5 与贝利亚 LV6 出现在同一条路线的不同仓库步骤；
3. 每个步骤显示的 LT 均不超过 TotalLT；
4. Route 1/2 切换会更新步骤和地图；
5. ALL 同时分色绘制且船舱不变；
6. 修改 Eq、CK、Storage、ExtraLT、TotalLT 分别使路线失效；
7. 路线失效后手动 CargoDetails、顺序和金色虚线仍存在；
8. 重复点击 Auto Plan 会取消旧任务，不发布旧结果；
9. 英文和繁体中文切换后所有新文字刷新；
10. 退出重开后手动船舱存档仍正常，自动路线不会错误持久化。

- [ ] **Step 5：提交 Task 10**

```powershell
git add Resources/i18n/Strings.en-US.xaml Resources/i18n/Strings.zh-TW.xaml README.md
git commit -m "docs: document automatic multi-route planning"
```

---

## 最终 Review 清单

我在 MiniMax 完成代码后按以下顺序 Review；任何高优先级项目失败都不接受合并：

### P0：正确性和数据安全

- 所有自动路线都通过 `RoutePlanVerifier` 从原始请求完整重放。
- 每次 pickup/barter 后检查 LT，不能只检查 Initial/Current。
- 分仓库存没有提前求总和；Velia 的物品不能从 Iliya 装出。
- 第一条路线卸货后，物品只增加到真实终点仓库。
- 依赖使用 ItemID，不使用英文或中文名称。
- 自动路线不修改、不排序、不保存 `CargoDetails`。
- 旧后台任务不能覆盖新请求。
- `Optimal` 只在搜索队列完整耗尽后返回。

### P1：算法质量和确定性

- 小型实例与测试暴力枚举的路线数和距离完全一致。
- 目标使用字典序，不使用魔法权重。
- 状态缓存包含船上库存和分仓动态库存。
- 支配剪枝是保守的；删除任一支配条件后只影响速度，不应改变正确结果。
- 相同输入和状态限制重复运行产生相同结果。
- 达到限制仍返回可重放 incumbent，并标记 `BestKnownWithinLimit`。

### P1：模式和 UI

- 自动和手动 ItemsSource 明确分离。
- ALL 只改变地图，保持最近具体路线的船舱和 LT。
- 仓库步骤不是伪造的 Barter，不能拖动或标记完成。
- 手动 Optimal Route、Clean、拖动、复制和 JSON 保存全部回归通过。
- 地图和船舱读取同一个 RoutePlan，不各自重新排序。

### P2：维护性

- 纯 Routing 层无 WPF 和全局 App 依赖。
- 重量表只有一个实现。
- 新文案在 en-US 和 zh-TW 中 key 完全一致。
- 诊断可定位 RowId、ItemID、仓库和数量。
- 新文件职责与本计划的文件结构一致，没有新增巨型 UI code-behind 算法。

### 最终证据

Review 结论必须附：

- 五个测试项目/命令的最新输出；
- x86 主项目构建输出；
- 小型暴力对照结果；
- 19 行实际计划运行截图或日志；
- Route/ALL/Manual 三种地图状态截图；
- `git diff --check` 输出；
- `git status --short`，确认没有混入无关用户文件。
