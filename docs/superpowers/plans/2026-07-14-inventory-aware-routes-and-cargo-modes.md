# 库存感知航线与船舱模式统一实施计划

> 本计划在当前分支直接执行。每项先补失败测试，再写最小实现，最后运行相关回归。

**目标：** 让自动规划主动利用仓库库存，修正长路线绕行，并统一自动恢复、手动/自动切换、船舱卡片和地图高亮行为。

**总体结构：** 保留现有规划模型与校验器。大型问题同时比较贪心基线与有界库存感知搜索；路线内部增加经过完整状态重放的迁移优化。界面侧由 Coordinator 保存有效自动计划，Planner 负责恢复，ShipCargoViewModel 提供统一步骤卡片，RouteRenderSnapshot 提供地图高亮集合。

---

## 任务 1：用库存感知搜索改进大型路线

**文件：**

- 修改：`Routing/AutomaticRoutePlanner.cs`
- 修改：`Routing/AutomaticRouteBeamSearch.cs`
- 测试：`Tools/AutomaticRoutePlanningTests/AutomaticRouteHeuristicTests.cs`

**步骤：**

1. 增加一个超过 12 个交换任务的场景：第二仓库已有消费者需要的中间物品，生产者位于更晚的路径上。
2. 先确认现有规划没有在生产前使用该库存，测试失败。
3. 让大型规划在保留贪心解作为基线的同时运行有界 Beam Search。
4. 修改搜索退出行为：达到扩展上限时返回搜索期间找到的最佳、已验证完整解，而不是直接丢弃。
5. 使用既有目标函数比较候选与基线，只有更优且验证通过的计划才能替换。
6. 运行：`dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --filter FullyQualifiedName~Inventory`。

## 任务 2：为长路线增加可行性保护的迁移优化

**文件：**

- 修改：`Routing/IntraRouteOrderOptimizer.cs`
- 测试：`Tools/AutomaticRoutePlanningTests/IntraRouteOrderOptimizerTests.cs`

**步骤：**

1. 建立一个步骤数超过旧搜索预算有效深度、且某个途中交换被明显放到末尾的路线。
2. 断言优化后距离严格缩短，并保持每一步库存与负重合法；确认测试先失败。
3. 枚举“移除一个动作并插入另一位置”的确定性候选。
4. 对每个候选调用既有路线状态转换完整重放；无效候选立即丢弃。
5. 按现有目标函数接受严格改进，重复到没有改进或达到预算。
6. 运行对应优化器测试和全部 `AutomaticRoutePlanningTests`。

## 任务 3：Planner 加载后恢复保存路线

**文件：**

- 修改：`View/PlannerControl.xaml.cs`
- 测试：`Tools/CaptureRectGuardTest/Program.cs`

**步骤：**

1. 增加守卫测试，要求普通 Planner 加载完成后调用统一的自动路线恢复方法。
2. 提取 `TryRestoreAutomaticRouteAfterLoad`，在加载、刷新船舱之后构造当前请求并调用 Coordinator 恢复。
3. 启动加载复用相同入口，删除重复恢复逻辑。
4. 运行 `CaptureRectGuardTest`，并确认指纹不一致时不会恢复过期路线。

## 任务 4：保留自动路线并允许从手动模式切回

**文件：**

- 修改：`ViewModel/AutomaticRouteCoordinator.cs`
- 修改：`View/ShipCargoControl.xaml.cs`
- 测试：`Tools/CaptureRectGuardTest/Program.cs`

**步骤：**

1. 增加守卫测试：只要 Coordinator 仍有路线选项，下拉框就必须可用。
2. 修改 `SelectRoute` 和 `SelectAll`，使其显式切回自动模式、更新步骤并保存选择。
3. 手动模式将下拉选择显示为空，但不清除选项，保证重新选择同一条路线也能触发事件。
4. 保留输入变化触发的 `Invalidate` 逻辑，防止过期路线重新启用。

## 任务 5：实现手动顺序的逐步负重投影

**文件：**

- 新增：`Routing/ManualCargoProjector.cs`
- 测试：`Tools/AutomaticRoutePlanningTests/ManualCargoProjectorTests.cs`

**步骤：**

1. 写三组失败测试：依赖顺序正常、消费者早于生产者、ExtraLT 参与峰值。
2. 第一次模拟累计输入缺口，得到该手动顺序所需的最小初始货物。
3. 第二次从初始货物重放，记录每步完成后的 LT、总 LT 与峰值。
4. 保证数量不能变负，并对重复物品和零数量做确定性处理。
5. 运行手动投影测试。

## 任务 6：统一手动与自动船舱卡片

**文件：**

- 修改：`ViewModel/AutomaticRouteStepViewModels.cs`
- 修改：`ViewModel/ShipCargoViewModel.cs`
- 修改：`View/ShipCargoControl.xaml`
- 修改：`View/ShipCargoControl.xaml.cs`
- 测试：`Tools/CaptureRectGuardTest/Program.cs`

**步骤：**

1. 让手动步骤 ViewModel 保留 `SourceBarter`，同时复用自动交换步骤的图标、名称和负重字段。
2. `ShipCargoViewModel` 根据 `CargoDetails` 顺序建立 `ManualSteps`，并通过 `ManualCargoProjector` 更新 InitialLT、CurrentLT、PeakLT。
3. 手动模式的 ListBox 改绑 `ManualSteps`，复用自动卡片模板。
4. 拖放、点击和双击先从卡片取回 `SourceBarter`，保持原有排序与复制功能。
5. 增加模板与绑定守卫测试，运行船舱相关回归。

## 任务 7：加强当前路线地图高亮

**文件：**

- 修改：`ViewModel/RouteRenderSnapshot.cs`
- 修改：`View/MapControl.xaml.cs`
- 测试：`Tools/AutomaticRoutePlanningTests/RouteRenderSnapshotTests.cs`
- 测试：`Tools/CaptureRectGuardTest/Program.cs`

**步骤：**

1. 增加失败测试，要求快照的高亮岛集合同时包含交换岛和仓库岛。
2. 在手动和自动快照中生成统一 `HighlightedIslandIds`。
3. 地图使用该集合应用更醒目的字号、字重、边框和背景；取消选中后恢复基础样式。
4. 保留原有物品等级文字颜色和路线虚线绘制。
5. 运行地图快照与源代码守卫测试。

## 任务 8：全量验证、自审、提交并推送

**步骤：**

1. 运行：`dotnet test Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj`。
2. 运行：`dotnet test Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj`。
3. 运行：`dotnet test Tools/IslandNavigationTests/IslandNavigationTests.csproj`。
4. 运行：`dotnet run --project Tools/CaptureRectGuardTest/CaptureRectGuardTest.csproj`。
5. 运行主项目对应架构的 `dotnet build`。
6. 检查 `git diff --check`、变更范围和未跟踪文件，确保不包含用户已有修改。
7. 逐项对照设计约束自审，修复发现的问题并重复相关测试。
8. 只暂存本次文件，创建提交并推送 `codex/automatic-multi-route-planning`。
