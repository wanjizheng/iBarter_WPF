# EU TAGGED 双角色换货：机制调查与实施建议

调查日期：2026-09-13。对象：一艘船、已 TAG 的主号和小号、各一头小象。本文是研究和设计提案，尚未修改计算逻辑。

## 结论

值得加入。实际要计算的是“出发分货、逐岛交换、指定码头装卸、必要时回仓整理”的连续过程。人物和小象的作用不能折算为一个增加后的船舱 LT 数字：货在哪个背包、何时能转移、转移先后顺序，同样决定路线能否完成。

推荐以可用码头划分航段，联合搜索出发装载和途中转运；每条候选路线都必须逐步模拟验证，再比较预计总耗时。主号、小号、小象都可以参与，但小象必须在实际可接触的位置，不能假设 TAG 会让两头小象在任意港口随时出现。

## 查到的机制及证据边界

| 事项 | 研究结论 | 对实现的要求 |
|---|---|---|
| 船超重 | 用户已确认：能暂时超重，航速下降且不能冲刺 | 区分正常航行阈值和各操作的装载限制 |
| 码头装卸 | 官方指南有 Load Cargo，涉及人物背包和城镇仓库；具体码头不一定具备全部来源 | 按 NPC 的实际功能记录，不能仅用“有码头”布尔值 |
| TAG 人物携货 | 玩家有在出发时携带额外材料、途中码头补船和卸产物的完整实践 | 独立维护两个人物库存、当前位置和当前活动角色 |
| 人物超重叠货 | 玩家讨论强调转入前的负重状态，以及一次转一叠与不可叠物品逐件转移的差别 | 不能用“人物负重乘某倍数”作为恒定仓容 |
| 双小象 | 用户确认利用仓库往返叠货；本次没找到足以证明 EU 当前所有转移边界的完整公开说明 | 支持准备和整理流程；每头象单独记录位置、货物、格子和转移条件 |
| 库存周转 | 船和人物都满时可能无法执行下一次装卸，即使总货物理论上放得下 | 验证每一步，不允许原子式交换两个背包 |

官方通用指南同时写了装船不能超过最大负重；玩家描述则包含交换后超重等流程。因此，“码头主动装船”“交换产生货物”“卸到人物”“超重航行”必须分别建规则。公开资料尚不足以证明 EU 当前每一种操作都统一适用 170%；不应直接把现有上限全部改成 170%。参见[官方 Stable & Wharf](https://blackdesert.pearlabyss.com/TR/en-us/Game/Wiki?_masterWikiNo=35)、[人物携货讨论](https://www.reddit.com/r/blackdesertonline/comments/1vhqmyu/daily_questions_and_answers_post_faq_newreturning/)、[新换货讨论](https://www.reddit.com/r/blackdesertonline/comments/1snwsur/hows_bartering_now/)。官方链接为其他区域的通用指南，不是 EU 逐港实测。

例如人物可以接收一整叠后超重，不代表仍可以接收第二种物品；把不可叠的多件商品一次选中，也不能自动视为同一种堆叠操作。容量模型需要物品 ID、可叠性、数量、格子及操作前后状态。[玩家对人物负重和多叠货物的讨论](https://www.reddit.com/r/blackdesertonline/comments/kypdnx/)

## 码头资料清单

以下是与海上换货相关的候选清单，不是已逐一实测的 EU 全功能白名单。“NPC 存在”与“当前能为个人船装卸指定货物”分开记录；旧玩家案例只证明该流程曾被使用。

| 地点 | 普通码头 NPC / 证据 | 规划用途与限制 |
|---|---|---|
| Velia | Croix | 仓库出发与回仓整理核心候选 |
| Iliya | Dario；当地仓库 Baori | 岛链中部仓库核心候选 |
| Epheria | Bartholomeo | 西部仓库出发与回仓整理核心候选 |
| Ancado Inner Harbor | Samia | 东部仓库核心候选；不能用 Sausan 坐标或 Shakatu 库存替代 |
| Kuit | Gafur；有玩家中途卸货、补材料案例 | 优先纳入西部人物中转；不凭有码头假设有直接仓库 |
| Lema | Bolhi；官方历史更新加入 Wharf 菜单 | 优先纳入中部人物中转；住宅仓库箱路径与码头直取仓库是两种动作 |
| Outpost Supply Port | Flanche；玩家中转案例 | 西部沿岸人物中转候选 |
| Abandoned Pier，Shakatu 附近 | Purio；玩家明确遇到只能显示人物库存 | 人物中转候选，不能当作 Shakatu 仓库直连 |
| Abandoned Pier，Riyed / Runn 一带 | Torio | 另一个同名码头；必须用 NPC 和坐标区分，功能待核实 |
| Arehaza | Luicy | 有普通码头 NPC 资料；人物装卸及仓库功能分别核实 |
| Grandiha | Derensha | 南部候选；不能误用公会管理员 Dumba |
| Starry Midnight Port | Neltia | 南部候选；不能误用公会管理员 Chikuro |
| Oquilla’s Eye | Ravikel | 普通码头候选；不能误用公会管理员 Elro |
| Crow’s Nest | Anax | 攻略记载有码头；不因此视为有仓库 |
| Dallae Pier | Sungoo | 晨曦候选；不能误用公会管理员 Jaeho |
| Haemo Island | Gurong | 晨曦候选；需核实个人船装卸菜单 |
| Cheongsa Island | Gangman | 晨曦候选；需核实个人船装卸菜单 |
| Byukgye Island | Darirong | 晨曦候选；需核实个人船装卸菜单 |
| Moodle / Nampo | Yooan | 晨曦候选；仓库连接需单独记录 |
| Sausan / Edania 相关交易点 | 有近期玩家报告这条线缺少人物装卸功能 | 保留明确例外，未核清具体端点前不启用通用人物中转 |
| Hakoven 及普通无人码头小岛 | 本次没有取得足以支持人物装卸的证据 | 不允许路线在此凭空取出主号、小号的备用材料 |

码头姓名和地点参考：[Wharf Managers 名录](https://orbit-games.com/en/black-desert-en/bdo-knowledge/characters-bdo/other-people-en/wharf-managers/)、[岛屿居民](https://orbit-games.com/en/black-desert-en/bdo-knowledge/characters-bdo/other-people-en/residents-of-the-isles/)、[Stars End 居民](https://orbit-games.com/en/black-desert-en/bdo-knowledge/characters-bdo/calpheon-characters-bdo/residents-of-stars-end/)、[Grandiha 居民](https://orbit-games.com/en/black-desert-en/bdo-knowledge/characters-bdo/kamasylvia-characters-bdo/people-of-grandiha/)、[Neltia](https://blackdesertonline.fandom.com/wiki/Neltia)、[Ravikel](https://bdolytics.com/en/db/knowledge/1455)、[Luicy](https://bdocodex.com/us/theme/1049/)、[Crow’s Nest 攻略](https://www.blackdesertfoundry.com/bartering-guide/)。名录混有公会管理员，不能整表导入为个人装卸点。

具体操作证据：[Kuit、Outpost、Abandoned Pier 中转案例](https://www.reddit.com/r/blackdesertonline/comments/kypdnx/)、[Shakatu 附近码头无仓库选项的案例](https://www.reddit.com/r/blackdesertonline/comments/fhez1y/)、[Lema 官方历史更新](https://blackdesert.pearlabyss.com/ASIA/en-US/News/Notice/Detail?_boardNo=458)。

特别值得保留的是近期玩家的 Sausan—Edania 案例：在目的地预放驴，把人物上的 T6 暂放驴，处理 T7 并卖出，再取回 T6。它说明“在当地预置坐骑”可以改变可执行动作，但这条评论没有逐个列清两端 NPC 菜单，因此不能据此断言该地区所有 NPC 都无装卸功能。[玩家原帖](https://www.reddit.com/r/blackdesertonline/comments/1vfdt2v/which_carrack_to_go_for_as_a_first_one/)

## 玩家高效做法如何转为软件策略

1. **出发前分配下一段和后续段材料。** 船先装第一段要用的货，人物携带未来补货；不是等船超重以后才开始利用人物背包。
2. **让卸货点靠近增重交换。** 产物导致超重的交易尽量安排在可用码头附近，卸到人物后恢复正常航行。必须比较这段慢航与绕路回仓哪一个更快。
3. **用已存货物缩短链条。** 有库存时不必每趟从 T1 一直做到底；可以先完成高阶交换，再补库存。实际配方与刷新内容用当次数据，不能照搬旧攻略收益和比例。
4. **把回仓当成必要时的主动动作。** 回仓可能释放两个角色的接收资格并重组货物；它应进入候选路线，而不是计算失败后的人工补救。

公开玩家案例中，Epheria 出发携带额外材料，经过西部岛群后在 Kuit 补船、处理产物，再继续下一批岛，正是这种“分段供货”的方式。这里采用流程思路，不采用旧文中的利润和商品出售规则。[区域路线实例](https://www.reddit.com/r/blackdesertonline/comments/kypdnx/)、[库存与路线讨论](https://www.reddit.com/r/blackdesertonline/comments/1hxz4hy/)

## 最关键：预先检查装卸是否会卡住

建议对每次到港运行一次小规模的转移顺序搜索。例如同时考虑“先把小号材料装船”“先把船上产物移到主号”“先出售被明确指定出售的成品”“回仓存货”，而不是直接把两个背包的最终内容互换。

如果小号仍背着后半程材料、船已无合法接收空间、主号也无法再接收、当前码头又没有可用仓库或可接触坐骑，那么计划必须在到达这个状态前修正：调整出发分货、提前卸货、改变交易顺序、减少本段批次，或者插入仓库停靠。

“船和小号满”不是必然死锁：主号可能仍能接货，某次合法交易可能减重，或当地存在可用周转动作。软件必须找出具体可执行顺序；若找不到，就不能宣称路线可行。不能预设码头一定允许临时超重装船来解除冲突。

预留余量也不能固定为 2,000 LT 或某个百分比。需要保留的是下一步所需的接收条件，包括转入前人物负重、空格、物品是否同叠，以及船下一次合法装载量。出发准备本身也应输出和验证顺序，避免一开始就生成现实中装不出来的库存分配。

## 与当前 iBarter 计算的差异

本次直接检查了当前 F: 工作区源码。

| 当前行为 | 相关代码 | 所需变化 |
|---|---|---|
| 只有 TotalLT / ExtraLT 船容量 | Routing/AutomaticRoutePlanningAdapter.cs | 增加角色、小象和操作规则配置 |
| 取货和交换后大于正常船容量即失败 | Routing/RouteStateTransition.cs | 区分每种操作条件及航行状态 |
| 按正常船容量提前拆交换批次 | Routing/AutomaticRoutePlanningAdapter.cs | 批次拆分与装载、中转联合决定，避免提前排除可行方案 |
| 状态只有船和仓库库存 | Routing/RouteSimulationState.cs | 加入两个人物、两头象及位置；允许必要的再次回仓 |
| 按总航程优先排序 | Routing/AutomaticRouteModels.cs | 新模式可按预计总时间排序，包括慢航和装卸时间 |
| 地图有岛和换货 NPC | Resources/Islands.csv、IslandBarterLocations.csv | 单独建立普通码头坐标与能力，不能用换货 NPC 坐标冒充 |
| 外部极限求解协议只有单船运输结构 | Routing/ExtremeRouteSolverProtocol.cs | 扩展协议和求解器；尚不支持时明确提示，不静默套旧模型 |
| 已有连续回放、计划保存 | Routing/RouteReplay.cs、RoutePlanPersistence.cs | 把所有转移动作纳入回放、存档、恢复与重新规划 |

另有现存映射 `("Ancado", "Sausan")`。需要查清历史用途并将“逻辑库存”“真实码头位置”分离，不能让新功能据此认为 Sausan 能直接访问 Ancado 仓库。

Model/CargoProperty.cs 已有 170% 相关颜色显示，但显示警告不等于自动规划支持该机制。只改颜色、加总 LT 或放宽一处上限都不足以完成这项功能。

## 推荐落地顺序

**先做共享模拟器和设置，再做搜索优化。** 保留现有任务选择、议价力预算和航道距离能力，在运输阶段接入新模型。

第一步，建立码头能力目录和独立库存。设置提供主号/小号最大 LT、日常占用 LT、可用格数、初始货物；两头小象分别提供所在地和库存。码头记录个人船装卸、直连仓库 ID、住宅箱路径、可用售货 NPC、证据日期和启用状态。人物规则与船规则版本化，不把未知边界伪装成精确结论。

第二步，实现明确动作：准备装载、船与当前人物转移、切换 TAG 角色、人物与当地小象转移、仓库存取、交换、航行、指定出售。每一步检查前置条件并只改变对应库存；出售必须由用户交易目标允许，不能为了腾空间擅自卖掉保留材料。

第三步，围绕可用码头搜索航段及货物分配。外层选择下一批交易和下一中转点，内层生成能执行的装卸顺序，遇到冲突回溯出发分货或插入仓库。优先使用有限候选和确定性搜索控制计算规模；发布前以共享模拟器完整回放。

第四步，增加“TAG 辅助”模式，显示出发三份装货清单、各码头按顺序的操作、每段船载重、需要保留的接收条件以及回仓原因。提供偏好“预计最快 / 少操作”；时间未知时显示估算和假设，不宣称精确最优。正常航速、超重航速、靠港和切角耗时可以配置。

第五步，把新设置、码头能力版本、初始库存写入计划指纹与存档，完成一步后更新对应容器，重新规划继承实际携货。新模式关闭时保持原有行为。

最低验证场景：初始船装不下但分货后可行；有码头无仓库；无码头岛不能补货；人物第二叠接收失败；两角色无接收能力导致必须回仓；同一状态通过不同顺序成功周转；不可叠 T6/T7 逐件处理；小象在别处不能使用；短暂超重后卸货恢复航速；计划中断恢复无库存重复。比较新旧方案时必须完成同一组交换，分别展示时间、距离、慢航段和操作次数。

## 尚不能声称已核实的内容

没有登录 EU 客户端逐港检查菜单，也没有取得所有超重边界的当前官方说明。下一阶段需要针对规则边界做小范围游戏验证，尤其是船主动装载与交换产物的上限、人物临界负重的严格比较符号、不同物品的单叠转入、两头小象的实际部署方式，以及 Sausan / Edania 的具体 NPC 能力。上述未知应隔离在数据与规则层，不能阻止先实现已明确的状态模型，也不能被默认当作无限容量。

旧攻略仍有过时商品重量与收益；当前项目已包含 T2=400、T6/T7=2000 的重量表。本次建议不回退这些数据，也不沿用旧攻略的仓库远程运输捷径。[2026 年官方换货更新，Asia 区域资料](https://blackdesert.pearlabyss.com/Asia/en-US/News/Notice/Detail?_boardNo=13108)

## 保存点

原有修改已提交并推送到 origin/development：b3fce4911fcbe12a6921592b1544becd36d143dd（Syncfusion 34.2.7）。本文为随后新增的研究文档，未提交；本次没有修改或声称验证新算法。
