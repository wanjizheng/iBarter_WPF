# 自动路线精修 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 让自动路线具备多仓库分段装卸、可恢复计划、真实海峡折线距离、跨路线重建优化，并在船舱中用中文名称与物品图标完整展示。

**Architecture:** 路线状态机是唯一的库存/载重真相；航行走廊服务只负责距离成本，地图显示路径独立地连接相邻停靠岛；协调器负责验证后的计划持久化及 UI 选择状态。优化器只生成候选，最终必须由 `RoutePlanVerifier` 全量回放确认。

**Tech Stack:** C# / .NET 10 pure test tools、WPF、Newtonsoft.Json、xUnit v3。

## Global Constraints

- 不创建 worktree；在当前 `codex/automatic-multi-route-planning` 分支工作。
- 保留 `View/MapControl.xaml` 中用户未提交的坐标注释及其他无关未跟踪文件。
- 每个行为先写失败测试并确认 RED，再写最小实现至 GREEN。
- 所有新 UI 文案进入语言资源；不得以 ItemID 作为用户可见文字。
- 每个候选计划都必须满足库存非负、交换依赖、`ExtraLT + CargoLT <= TotalLT`。

---

## Task 1：分离航行走廊距离与地图显示路径

**Files:**

- Modify: `Navigation/IslandNavigationGeometry.cs`
- Create: `Navigation/ShippingCorridorGraph.cs`
- Modify: `Routing/RouteStateTransition.cs`
- Modify: `Routing/RouteRenderSnapshot.cs`
- Modify: `View/MapControl.xaml.cs`
- Test: `Tools/IslandNavigationTests/ShippingCorridorGraphTests.cs`
- Test: `Tools/AutomaticRoutePlanningTests/RouteRenderSnapshotTests.cs`

**Steps:**

1. 写测试证明普通海域仍为直线、外部与右上区域双向均经过 Halmad、右上内部按确认走廊连接、南部走 Grandiha–Midnight 折线。
2. 写测试证明显示路径始终只包含相邻停靠岛的两个端点，不暴露走廊中间导航点。
3. 实现不可变小型走廊图及折线长度；将状态转换中的距离改为该服务。
4. 地图对相邻停靠岛只绘制一条直线虚线；走廊中间点仅参与距离计算。
5. 修正 Velia 航行坐标与已确认的显示点，不改其他岛屿人工显示布局。

## Task 2：多仓库分段装卸状态机

**Files:**

- Modify: `Routing/AutomaticRouteModels.cs`
- Modify: `Routing/AutomaticRoutePlanningAdapter.cs`
- Modify: `Routing/RouteStateTransition.cs`
- Modify: `Routing/AutomaticRouteHeuristic.cs`
- Modify: `Routing/AutomaticRoutePlanner.cs`
- Modify: `Routing/RoutePlanVerifier.cs`
- Test: `Tools/AutomaticRoutePlanningTests/RouteStateTransitionTests.cs`
- Test: `Tools/AutomaticRoutePlanningTests/AutomaticRouteHeuristicTests.cs`
- Test: `Tools/AutomaticRoutePlanningTests/RoutePlanVerifierTests.cs`

**Steps:**

1. 写测试覆盖部分卸货后船上余货、第二仓库继续卸货、Crow Coin 无卸货、零初始库存的加权输出就近兜底。
2. 在请求中保留规划开始时的仓库物品归属；让卸货转换接收明确物品子集和是否结束路线。
3. 用确定性目的仓库分组器生成卸货停靠顺序；每次只卸属于当前仓库的物品。
4. 更新精确搜索、启发式和验证器，使多次卸货与下一条路线起点一致。
5. 完整回放所有旧测试并修复兼容性。

## Task 3：装卸步骤中文图标化

**Files:**

- Modify: `ViewModel/AutomaticRouteStepViewModels.cs`
- Modify: `ViewModel/AutomaticRouteCoordinator.cs`
- Modify: `View/ShipCargoControl.xaml`
- Modify: `Resources/Languages.xaml`
- Modify: `Resources/Languages.zh-CN.xaml`
- Modify: `Resources/Languages.zh-TW.xaml`

**Steps:**

1. 为仓库步骤建立物品行 VM：图标、显示名、数量。
2. 协调器从全局物品目录提供本地化显示名；仓库 ID 映射到语言资源。
3. XAML 用纵向 ItemsControl 展示图标与名称数量，每个装卸物品独占一行；删除原始 ID 拼接详情。
4. 切换语言时重建步骤 VM 并刷新。

## Task 4：船舶属性启动加载与路线持久化

**Files:**

- Create: `Routing/RoutePlanPersistence.cs`
- Modify: `App.xaml.cs`
- Modify: `View/ShipCargoControl.xaml.cs`
- Modify: `View/PlannerControl.xaml.cs`
- Modify: `ViewModel/AutomaticRouteCoordinator.cs`
- Test: `Tools/AutomaticRoutePlanningTests/RoutePlanPersistenceTests.cs`

**Steps:**

1. 写纯测试覆盖计划/选择状态 round-trip、schema 不匹配、指纹不匹配和损坏 JSON。
2. 把船舶属性读取提取成启动期安全方法，在协调器创建前完成。
3. 生成成功后原子保存计划；选择路线或 ALL 时更新选择状态。
4. Planner、Storage 和 Cargo 都加载后构建当前请求；指纹匹配并验证通过才恢复。
5. 旧/坏文件只记录诊断，不清空用户 Planner 或仓库数据。

## Task 5：跨路线成对破坏—重建

**Files:**

- Create: `Routing/RoutePairRebuilder.cs`
- Modify: `Routing/AutomaticRouteHeuristic.cs`
- Test: `Tools/AutomaticRoutePlanningTests/RoutePairRebuilderTests.cs`
- Test: `Tools/AutomaticRoutePlanningTests/AutomaticRouteHeuristicTests.cs`

**Steps:**

1. 构造右上五岛容量紧张夹具，先证明现有启发式被拆成多条路线。
2. 实现有界 beam/rebuild：合并两条路线任务，从真实前置库存状态重建一或两条路线。
3. 按路线数、走廊总距离、装货停靠数、峰值 LT、稳定 tie-break 比较候选。
4. 回放后续路线并由 verifier 验证整份计划；只提交严格更优候选。
5. 对所有路线对迭代至无改进或达到 `MaxLocalMoves`。

## Task 6：回归、自审、提交和推送

**Files:** all touched files only.

**Steps:**

1. 运行三个纯测试工具和主 WPF build。
2. 检查 `git diff --check`、用户未提交文件、可见 ItemID、重复直线距离实现、路线恢复失败路径。
3. 用实际 Resources 数据跑 28 路线规划夹具，核对右上区域合并、载重和多仓库步骤。
4. 自审严重度 P0–P2 问题并修复后重跑验证。
5. 只暂存本功能文件，提交并推送当前分支。
