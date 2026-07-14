# 自动路线正确性、地图交互与界面焕新 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复大任务量路线排序、补充货源模式与自动恢复，统一简体中文名称，并完成船舱—地图联动及 Fluent 图标工具栏。

**Architecture:** `RouteStateTransition` 继续作为库存、依赖和载重的唯一真相；大任务使用有界 beam search 获取可行解，单路线动作排序器负责缩短已分组路线。Planner 倍率与验证后的船舱计划以候选事务发布。协调器输出纯 UI 状态，地图据此生成节点、粗体集合和可聚焦线段。Fluent 图标以项目内 WPF Geometry 和动态主题画刷提供。

**Tech Stack:** C# / .NET 10、WPF、xUnit v3 MTP、System.Text.Json、Syncfusion WPF、Microsoft Fluent UI System Icons SVG（MIT）。

## Global Constraints

- 不创建 worktree；继续在 `codex/automatic-multi-route-planning` 分支工作。
- 保留用户对 `Resources/Islands.zh-TW.csv` 和 `View/MapControl.xaml` 的未提交修改；需要使用“贝尔利亚村庄”时读取当前工作区内容，不覆盖用户修改。
- 每个行为先写失败测试并确认 RED，再写最小实现至 GREEN。
- `RoutePlanVerifier` 是所有候选路线发布和持久化前的最终门禁。
- `ALL` 仅用于地图总览；船舱卡片仍显示最后选择的具体路线。
- 中文使用台服游戏术语和简体字形；用户可见文本不得回退到内部岛屿 ID 或物品 ID。
- 不引入第三方 Fluent WPF 包；只导入所需 Microsoft SVG 路径并保留 MIT 来源说明。

---

### Task 1：依赖感知的单路线排序

**Files:**

- Create: `Routing/IntraRouteOrderOptimizer.cs`
- Modify: `Routing/AutomaticRouteHeuristic.cs`
- Modify: `Routing/RoutePairRebuilder.cs`
- Test: `Tools/AutomaticRoutePlanningTests/IntraRouteOrderOptimizerTests.cs`

**Interfaces:**

- Consumes: `RouteStateTransition.TryPickup/TryBarter`、`WarehouseUnloadPlanner.TryCompleteRoute`、`RoutePlanVerifier.Verify`。
- Produces: `IntraRouteOrderOptimizer.Improve(AutomaticRoutePlanningRequest, RouteSimulationState, CancellationToken)`。

- [ ] **Step 1: 写四岛失败测试**

构造 `Iliya` 仓库同时装入 A/B；`Hakoven: B→C`、`Lema: C→D`、`Arehaza: A→E`、`Iliya: E→F`。先回放错误顺序，再断言优化结果为 `Hakoven, Arehaza, Lema, Iliya`，且总距离下降、峰值不超 `TotalLT`。

- [ ] **Step 2: 运行 RED**

Run: `dotnet run --project Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --no-restore`

Expected: 新测试因 `IntraRouteOrderOptimizer` 不存在而失败。

- [ ] **Step 3: 实现动作子集搜索**

建立 `RouteAction`（原步骤索引、`WarehousePickupStep` 或 `BarterStep`）和搜索节点（完成 mask、回放 state、稳定键）。通过真实状态转换扩展动作；动作数不超过 14 时保留每个 `(mask,currentIsland)` 的最小距离状态，超过时每层保留 256 个候选。全部动作完成后重新规划卸货。

- [ ] **Step 4: 公平优化每条路线**

从整份计划初始状态依次重建每条路线；每条路线使用独立上限 `max(64, MaxLocalMoves / routeCount)`。候选只有在整份计划验证成功且目标严格改善时替换。移除会移动装货步骤、且共享一个全局尝试计数的旧 relocate/swap/2-opt 循环。

- [ ] **Step 5: 运行 GREEN**

Run: `dotnet run --project Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --no-restore`

Expected: 四岛顺序测试和现有 65 个测试全部通过。

---

### Task 2：大任务量可行解 beam search

**Files:**

- Create: `Routing/AutomaticRouteBeamSearch.cs`
- Modify: `Routing/AutomaticRoutePlanner.cs`
- Modify: `Routing/DemandBundleGenerator.cs`
- Test: `Tools/AutomaticRoutePlanningTests/AutomaticRouteBeamSearchTests.cs`

**Interfaces:**

- Consumes: 从 `AutomaticRoutePlanner` 提取为 internal 的 `Expand(request,state,fullMask)` 和 `SearchKey(state)`。
- Produces: `RouteIncumbent? AutomaticRouteBeamSearch.TryFindIncumbent(...)`。

- [ ] **Step 1: 写贪心死路回归**

建立 13 个以上任务：最近动作会留下无法装载的高重量输出，而先完成释放载重/解锁链的动作可以分两条路线完成。断言现有贪心为 null，beam search 返回可验证计划。

- [ ] **Step 2: 运行 RED**

Run: `dotnet run --project Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --no-restore`

Expected: 因 `AutomaticRouteBeamSearch` 不存在而失败。

- [ ] **Step 3: 实现有界搜索**

每轮从 beam 扩展所有合法后继，按 `(estimatedRouteCount, -completedCount, distance, pickups, peak, stableKey)` 排序；同一 `SearchKey` 只保留较低成本；beam 宽度 512，总扩展数不超过 `MaxExpandedStates`。遇到完成状态立即验证并作为 incumbent。

- [ ] **Step 4: 接入 Planner**

超过 `ExactTaskLimit` 时：先取贪心 incumbent；若无解则运行 beam search；有解后再运行单路线排序和 pair rebuild。仍返回 `BestKnownWithinLimit`，诊断包含 `exact-search-skipped`。

- [ ] **Step 5: 用当前保存数据做一次非提交诊断**

从运行目录的 Planner/Storage/ShipProperty JSON 构建请求，确认“补充货源优先”选出的 20 个任务不再返回 `NoFeasibleSolutionWithinLimit`。此诊断数据不加入 git。

- [ ] **Step 6: 运行 GREEN**

Run: `dotnet run --project Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --no-restore`

Expected: 所有路线测试通过，新计划完整回放且无超重。

---

### Task 3：Planner 与船舱路线原子发布

**Files:**

- Create: `Planning/AutomaticPlanningTransaction.cs`
- Modify: `View/PlannerControl.xaml.cs`
- Modify: `ViewModel/AutomaticRouteCoordinator.cs`
- Modify: `Routing/RoutePlanPersistence.cs`
- Modify: `Routing/RoutePlanFingerprint.cs`
- Test: `Tools/PlannerAutoPlannerTests/AutomaticPlanningTransactionTests.cs`
- Test: `Tools/AutomaticRoutePlanningTests/RoutePlanPersistenceTests.cs`

**Interfaces:**

- Produces: `AutomaticPlanningTransaction.Prepare(...)` 返回候选倍率和候选路线请求；`AutomaticRouteCoordinator.TryGenerateCandidateAsync(...)` 失败不改变当前计划；`CommitCandidate(...)` 只接受已验证计划。

- [ ] **Step 1: 写失败不变更测试**

设置已有有效 plan 和倍率，令候选路线返回无解；断言倍率、当前 plan、选择项和持久化文件字节均不变。

- [ ] **Step 2: 写成功一起更新测试**

候选可行时断言倍率先不变，调用 commit 后倍率、plan 和持久化快照同时变为新版本。

- [ ] **Step 3: 运行 RED**

Run: `dotnet run --project Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --no-restore`

- [ ] **Step 4: 实现候选事务**

把 `ButtonAdv_AutoPlan_Click` 中“写 liveRows、SaveData、GenerateAsync”的顺序改为：构造候选快照 → 求路线 → 成功后应用 liveRows → 更新库存/贡献度/地图 → 发布并保存。失败只记录日志。

- [ ] **Step 5: 迁移持久化目录和版本**

新路径为 `Environment.SpecialFolder.LocalApplicationData/iBarter/automatic-route-plan.json`。新路径不存在而旧路径存在时尝试读取旧文件；下一次成功保存写新路径。指纹加入常量 `RoutingAlgorithmVersion`。

- [ ] **Step 6: 增加恢复诊断**

`RoutePlanPersistence.TryLoad` 返回枚举原因 `Missing/FingerprintMismatch/InvalidJson/SchemaMismatch/Success`；启动日志使用本地化文字说明未恢复原因。

- [ ] **Step 7: 运行 GREEN**

Run: `dotnet run --project Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --no-restore`

Run: `dotnet run --project Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --no-restore`

---

### Task 4：统一简体中文实体名称与界面词条

**Files:**

- Modify: `Localization/LanguageService.cs`
- Modify: `Model/Islands.cs`
- Modify: `Model/Items.cs`
- Modify: `ViewModel/AutomaticRouteCoordinator.cs`
- Modify: `ViewModel/AutomaticRouteStepViewModels.cs`
- Modify: `Resources/i18n/Strings.zh-TW.xaml`
- Test: `Tools/AutomaticRoutePlanningTests/AutomaticRouteLocalizationTests.cs`
- Test: `Tools/PlannerAutoPlannerTests/LocalizationResourceTests.cs`

**Interfaces:**

- Produces: `AutomaticRouteCoordinator.ResolveIslandDisplayName(string)`；中文设置值仍为 `1`，用户显示名称改为“简体中文”。

- [ ] **Step 1: 写交换步骤岛名失败测试**

给 `Hakoven` 目录名“哈科班岛”，断言 `BarterRouteStepViewModel.Title` 为“在 哈科班岛 交换”，而不是包含 `Hakoven`。

- [ ] **Step 2: 写资源简体检查**

解析中文 XAML，断言不存在已知界面繁体词：`視窗、計畫、儲存、載入、圖示、烏鴉、優先、自動規劃、貢獻度`；断言 Velia 仓库名称为“贝尔利亚村庄”。

- [ ] **Step 3: 运行 RED**

Run: `dotnet run --project Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --no-restore`

- [ ] **Step 4: 实现统一解析**

协调器从 `App.listIslands` 解析岛名并传给仓库/交换 VM；物品始终从 `App.listItems` 解析。保留用户当前 `Resources/Islands.zh-TW.csv` 文件，不修改该文件。

- [ ] **Step 5: 转换 UI 文案**

逐项把中文资源改为简体中文措辞，修改语言菜单名称和代码注释语义；保留资源键，避免破坏 XAML 绑定。

- [ ] **Step 6: 运行 GREEN**

Run: `dotnet run --project Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --no-restore`

Run: `dotnet run --project Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --no-restore`

---

### Task 5：路线选择、步骤聚焦和仓库地图节点

**Files:**

- Create: `Routing/RouteMapPresentation.cs`
- Modify: `ViewModel/AutomaticRouteCoordinator.cs`
- Modify: `ViewModel/AutomaticRouteStepViewModels.cs`
- Modify: `View/ShipCargoControl.xaml`
- Modify: `View/ShipCargoControl.xaml.cs`
- Modify: `View/MapControl.xaml.cs`
- Test: `Tools/AutomaticRoutePlanningTests/RouteMapPresentationTests.cs`

**Interfaces:**

- Produces: `RouteMapPresentationSnapshot`（可见路线、粗体交换岛、仓库节点、聚焦 route/step/segment）；`AutomaticRouteCoordinator.FocusStep(int stepIndex)`。

- [ ] **Step 1: 写纯状态失败测试**

验证选中路线只加粗自身交换岛、ALL 加粗全部路线交换岛、仓库步骤合并为单个仓库节点、聚焦第二个交换步骤得到其入站 segment index。

- [ ] **Step 2: 运行 RED**

Run: `dotnet run --project Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --no-restore`

- [ ] **Step 3: 实现 presentation snapshot**

从 `RoutePlan`、选择状态和聚焦步骤生成不可变快照。仓库节点按 island ID 合并 pickup/unload 标志；交换粗体集合不包含纯仓库停靠。

- [ ] **Step 4: 接入船舱单击**

自动模式下 `ListBoxItem` 单击调用 `FocusStep`，不启动拖动；数据模板为选中卡片增加左侧青色强调条和键盘焦点。

- [ ] **Step 5: 接入地图线段高亮**

路线 `Line.Tag` 改为包含 route number 和 segment index 的元数据。匹配聚焦线段时使用 4.5px 高亮和 drop shadow；若 `SystemParameters.ClientAreaAnimation` 为 true，播放一次 600ms 脉冲。

- [ ] **Step 6: 重构地图节点**

让 `IslandVisual` 支持 `Temp/Barter/Warehouse`。每次重排先根据 presentation snapshot 确保仓库节点，再统一调用 `GetDisplayCenterNormalized` 和 `AdjustLabels`。Velia 使用 `Resources/Islands.csv` 的显示坐标。

- [ ] **Step 7: 自动路线岛名加粗**

自动模式使用 snapshot 的 `BoldBarterIslandIds`；手动模式继续读取 `CargoDetails`。

- [ ] **Step 8: 运行 GREEN 与编译**

Run: `dotnet run --project Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --no-restore`

Run: `dotnet build iBarter.csproj --no-restore -v minimal`

---

### Task 6：Fluent 图标与工具栏视觉系统

**Files:**

- Create: `Resources/Styles/FluentIcons.xaml`
- Create: `Resources/Styles/FluentControls.xaml`
- Create: `Resources/FluentIcons-LICENSE.txt`
- Modify: `App.xaml`
- Modify: `View/PlannerControl.xaml`
- Modify: `View/BarterScanner.xaml`
- Modify: `View/ShipCargoControl.xaml`
- Modify: `View/ShipCargoControl.xaml.cs`
- Modify: `View/StorageManagement.xaml`
- Test: `Tools/PlannerAutoPlannerTests/FluentResourceTests.cs`

**Interfaces:**

- Produces: Geometry keys `FluentIcon.DocumentAdd/Save/FolderOpen/ArrowSync/Broom/CheckmarkCircle/Route/Scan/AddSquare/Box/Location`；样式 `FluentToolbarButtonStyle`。

- [ ] **Step 1: 写资源失败测试**

解析四个主要 XAML，断言旧 `/Images/*.png` 工具栏引用不存在、所需 Geometry key 全部存在、按钮使用 `FluentToolbarButtonStyle`。

- [ ] **Step 2: 运行 RED**

Run: `dotnet run --project Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --no-restore`

- [ ] **Step 3: 导入官方路径**

从 Microsoft 官方 regular SVG 复制所需 path data 到 Geometry；记录仓库 URL、图标名和 MIT 许可。每个 Geometry 使用 20/24px viewbox 的统一 Stretch。

- [ ] **Step 4: 建立主题资源和控件模板**

定义深/浅主题下的背景、前景、强调、悬停、按下、焦点和禁用画刷。按钮模板用 `Path Fill={TemplateBinding Foreground}`；悬停改变背景/前景，按下使用 0.96 缩放，焦点显示 2px 描边。

- [ ] **Step 5: 替换工具栏**

Planner、扫描器、船舱使用统一 WPF Button 模板和 8px 间距。移除船舱 `ButtonAdv_OptimalRoute` 及其点击处理器；Planner 自动规划按钮保留并使用 Route 图标。

- [ ] **Step 6: 精修卡片与空白**

船舱卡片背景和描边改用动态资源，仓库卡片使用琥珀强调，交换卡片使用海图青选中条；保持 300px 宽时文字换行和物品纵向列表。

- [ ] **Step 7: 运行 GREEN 与人工截图检查**

Run: `dotnet run --project Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --no-restore`

Run: `dotnet build iBarter.csproj --no-restore -v minimal`

在深色和浅色主题下分别检查默认、悬停、按下、禁用和键盘焦点；确认没有位图按钮残留。

---

### Task 7：完整回归、自审、提交和推送

**Files:** All files modified above.

- [ ] **Step 1: 运行全部相关测试**

Run: `dotnet run --project Tools/AutomaticRoutePlanningTests/AutomaticRoutePlanningTests.csproj --no-restore`

Run: `dotnet run --project Tools/IslandNavigationTests/IslandNavigationTests.csproj --no-restore`

Run: `dotnet run --project Tools/PlannerAutoPlannerTests/PlannerAutoPlannerTests.csproj --no-restore`

- [ ] **Step 2: 编译和差异检查**

Run: `dotnet build iBarter.csproj --no-restore -v minimal`

Run: `git diff --check -- <本轮文件列表>`

确认用户原有 `Resources/Islands.zh-TW.csv`、`View/MapControl.xaml` 和其他未跟踪文件没有进入暂存区。

- [ ] **Step 3: 自审需求映射**

逐条核对用户 1–11：中文岛名、路线顺序、线段聚焦、岛名加粗、仓库节点、正确游戏名、启动恢复、移除船舱按钮、Fluent UI、补充货源可行解、简体界面。

- [ ] **Step 4: 提交并推送**

Commit message: `feat: refine route planning and modernize UI`

Push: `git push origin codex/automatic-multi-route-planning`

