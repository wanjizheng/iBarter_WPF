This is a .Net application inspired by the Barter Template developed by Crippling Depression. It was the best bartering tool in the Black Desert Online. 

This is still in beta, so please help me test the application and provide feedback.

This tool uses image recognition to identify barter items in the game; then you can plan your route. To use this application, you first need to start the iBarter.exe file with admin permission, then the main interface shown below:

![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/538b032b-dbcc-42ec-b69d-29396a7d603f)

Click the "Windows" menu and then select the "BarterScanner", it will then open a new window shown below:

![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/2d0f6260-0756-42ad-ac42-71d965f785aa)

Assuming you have the barter window opened in the game, click the first button in the BaarterScanner window to start scanning. Do not move the barter window in the game during the scan process, and you will get a message (in the Log window) once the scan has been done.

![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/a4d72086-2c56-4cc3-997e-8b0d14443081)

Once you finish the scan, the identified information will show in the BarterScanner window. It is not 100% accurate at the moment, so you can edit the information if necessary.

![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/fd566e04-1e43-4aef-b2eb-dd4b3ca4d3d9)

Once you are happy with the result, click the second button in the BarterScanner window to add it to your barter plan (you can access the plan from the main window).

![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/4adfe7d8-9750-45d7-ac4e-34e59bca089d)

There are seven buttons on the Planner window, from the left to the right, they are:
1. Create a new plan.
2. Save the current plan.
3. Open an existing plan.
4. Grouping.
5. Clean the current plan.
6. Finish the current plan and add the results to the storage record (Windows=>Storage Manager)
7. Auto Plan: fill every unfinished Eq. value from a zero baseline, preserving CK rows and respecting the 1,000,000 parley budget. Pick a strategy from the dropdown next to it before clicking.

### Auto Plan strategies

The Auto Plan button sits next to a strategy dropdown. All three strategies share these contracts:

- Only unfinished (`CK` unchecked) rows are touched — CK rows keep their current `Eq.` value byte-for-byte.
- The total parley shown in the toolbar after the click equals `sum(effective parley × multiplier)` and never exceeds 1,000,000.
- If the planner cannot satisfy a route (cycle, missing inventory, or budget exhausted), the row's `Eq.` stays at zero and a localized message is written to the log.
- Results are deterministic: identical inputs always produce identical multipliers.

Strategies:

- **Crow Coin First** — fill Crow-Coin-producing routes first by descending coin output, then by output-per-parley efficiency, then by row id. Any remaining budget is spent on non-crow routes in input-level order (LV4 → LV5 → LV6 → LV7).
- **Profit First** — Crow-Coin routes are excluded. The planner only considers "leaf" routes (whose output is not consumed by another route in the plan), ranked by descending target tier (LV7 → LV6 → LV5 → LV4), then by output, then by efficiency, then by row id.
- **Restock First** — two-phase. Phase 1 fills LV5 and LV6 outputs toward the `Max LV5` and `Max LV6` combo box targets by descending deficit ratio; overshoot is allowed only when one indivisible exchange crosses the target. Phase 2 spends any remaining parley on the lowest-projected LV1–LV4 inventory item; LV1–LV4 have no implicit cap and LV7 outputs are excluded.

Columns in the table are:
1. Group: Group number.
2. LV: Barter item level.
3. CK: Tick this after you finish it; this will remove the item from the Map.
4. Eq.: Exchange quantity. The number of times you can barter this item.
5. No.: Required item number.
6. Location: The Islands name.
7. I1 No: Required item number.
8. I1: Required item icon.
9. Item: Required item name.
10. I2: Exchange item icon.
11. Exchange: Exchange item name.
12. I2 No.: Exchange item number.
13. Inv: How many items you currently have in your storage (will need to configure the Storage Manager to make this number right).
14. InvChange: The number of items you will get after you finish this barter.

Use the planner to plan your barter accordingly, and you will be able to see these items in the Map view which helps you decide which one do you want to barter first.
![image](https://github.com/wanjizheng/iBarter_WPF/assets/15932911/d9d26329-3c19-41b2-bb77-68b825173d09)

In the map view, double-click an item will make it "CK" (remove it from the map and tick the CK box in the planner). Use the mid-button on your mouse to add an item to your Ship Cargo, which will help you work out the LT limit. Click the mid-button again will remove it from the cargo view. Right click will take you to the item record in your Planner. 

In the Ship Cargo view, click the left mouse button twice will copy the first item's name to your clickboard. Double click the right button will copy the second item's name.

## 自动多路线规划

点击 Planner 的 **Auto Plan** 后，程序会先保留原有逻辑计算所有未完成列的 `Eq.`，再根据四个仓库的独立库存、岛屿位置、交换前后重量与 `TotalLT` 自动生成航线。路线可能包含仓库装货、岛屿交换和终点卸货步骤；同一条路线可以先去伊利亚装 LV1–LV5，再在顺路时去贝利亚装 LV6。

- 选择 **Route N**：船舱显示该路线的完整步骤，地图只绘制该路线；`InitialLT`、`CurrentLT` 和 `PeakLT` 使用求解器逐步骤验证后的数值。
- 选择 **ALL**：地图同时用不同颜色显示全部路线；船舱和 LT 保持最近选择的具体路线不变。
- 地图中键选岛、船舱拖动、Clean 和 Optimal Route 仍属于手动模式。切回手动模式后，原有船舱内容和顺序保持不变。
- `Optimal` 表示搜索空间已完整检查；“最佳已知”表示已经找到并验证了可行方案，但在搜索限制内尚未证明它是全局最优。
- 修改 Planner 的 Eq/CK、仓库数量、`ExtraLT` 或 `TotalLT` 会立即清除过期的自动路线，避免继续显示基于旧数据的结果。

自动路线只存在于当前运行状态，不会覆盖或保存手动船舱 JSON。

