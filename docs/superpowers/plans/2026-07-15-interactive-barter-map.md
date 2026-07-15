# 可缩放航线地图 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为现有 WPF 航线地图加入统一的缩放/平移视口、受限的标签字号和更清晰的路线步骤视觉。

**Architecture:** `MapViewportState` 维护缩放与平移，`MapControl` 将状态映射到 `Grid_MapMain` 的 RenderTransform。海图、岛屿、路线和箭头仍在同一场景内；标签在缩放事件时重算本地字号，使最终屏幕字号处于 11–16 DIP。

**Tech Stack:** C# 13、.NET 10、WPF、现有 `CaptureRectGuardTest` 守卫项目。

## Global Constraints

- 保留 `iBarterMap.png`、现有归一化坐标、航线计算与储存数据。
- 缩放范围固定为 0.65–2.50；复位为 1.00 和平移 `(0,0)`。
- 中键岛屿交换和右键打开 Planner 行为不变；平移使用空白海域右键拖拽。
- 标签屏幕字号保持在 11–16 DIP；未选路线可读但降低对比度。
- 不引入 Web 地图、SVG 运行时依赖或 GIS 数据。
- 不暂存现有无关用户改动或 `.claude` 嵌套工作区。

---

## File Structure

- Create `View/MapViewportState.cs`: 无 WPF 依赖的缩放锚点和平移状态。
- Modify `View/MapControl.xaml`: 用裁剪视口包装现有地图场景，加入右上角缩放工具栏。
- Modify `View/MapControl.xaml.cs`: 鼠标交互、Transform 应用、标签缩放、路线步骤标记。
- Modify `Tools/CaptureRectGuardTest/Program.cs`: 回归守卫。

### Task 1: 纯缩放状态与回归测试

**Files:**

- Create: `View/MapViewportState.cs`
- Modify: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**

- Produces: `MapViewportState.ZoomAt(Point anchor, double multiplier)`, `PanBy(Vector delta)`, `Reset()`，以及 `Scale/OffsetX/OffsetY`。

- [ ] **Step 1: 写出失败守卫**

在 `CaptureRectGuardTest` 中要求 `MapViewportState` 存在、最小/最大缩放常量为 `0.65/2.50`，并要求 `MapControl` 使用 `ZoomAt` 而非按 Margin 改写缩放。

- [ ] **Step 2: 运行守卫并确认失败**

Run: `dotnet run --project Tools/CaptureRectGuardTest/CaptureRectGuardTest.csproj --no-restore`

Expected: 以缺少 `MapViewportState` 相关文本失败。

- [ ] **Step 3: 实现最小状态对象**

```csharp
public sealed class MapViewportState {
    public const double MinScale = 0.65;
    public const double MaxScale = 2.50;
    public double Scale { get; private set; } = 1;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }
    public void ZoomAt(Point anchor, double multiplier) {
        double next = Math.Clamp(Scale * multiplier, MinScale, MaxScale);
        double ratio = next / Scale;
        OffsetX = anchor.X - (anchor.X - OffsetX) * ratio;
        OffsetY = anchor.Y - (anchor.Y - OffsetY) * ratio;
        Scale = next;
    }
    public void PanBy(Vector delta) { OffsetX += delta.X; OffsetY += delta.Y; }
    public void Reset() { Scale = 1; OffsetX = 0; OffsetY = 0; }
}
```

- [ ] **Step 4: 运行守卫并提交**

Run: `dotnet run --project Tools/CaptureRectGuardTest/CaptureRectGuardTest.csproj --no-restore`

Commit: `feat(map): add viewport state`

### Task 2: WPF 视口、缩放与平移

**Files:**

- Modify: `View/MapControl.xaml`
- Modify: `View/MapControl.xaml.cs`

**Interfaces:**

- Consumes: `MapViewportState`。
- Produces: 鼠标滚轮缩放、空白海域右键平移、缩放工具栏和复位。

- [ ] **Step 1: 用失败守卫声明视口结构**

守卫要求 XAML 含 `MapViewport`、`ScaleTransform x:Name="MapScaleTransform"`、`TranslateTransform x:Name="MapTranslateTransform"`，并要求代码含 `MapViewport_MouseWheel`、`MapViewport_MouseRightButtonDown`、`ResetMapViewport`。

- [ ] **Step 2: 运行守卫并确认失败**

Run: `dotnet run --project Tools/CaptureRectGuardTest/CaptureRectGuardTest.csproj --no-restore`

- [ ] **Step 3: 包装现有场景并绑定事件**

将现有 `Grid_MapMain` 放进 `MapViewport`，并在地图场景上配置：

```xml
<Grid.RenderTransform><TransformGroup>
  <ScaleTransform x:Name="MapScaleTransform" />
  <TranslateTransform x:Name="MapTranslateTransform" />
</TransformGroup></Grid.RenderTransform>
```

在事件中调用 `viewportState.ZoomAt(e.GetPosition(MapViewport), e.Delta > 0 ? 1.15 : 1 / 1.15)`，随后将 `ScaleX/ScaleY/X/Y` 更新到 Transform。右键只在空白海图或视口本身时开始平移，避免抢占岛屿右键导航。

- [ ] **Step 4: 运行守卫、构建并手动验证**

Run: `dotnet run --project Tools/CaptureRectGuardTest/CaptureRectGuardTest.csproj --no-restore`

Run: `dotnet build iBarter.csproj --no-restore -p:Platform=x86`

Manual: 在地图上滚轮缩放、右键拖动空白海面、点复位；中键岛屿仍能增删船舱项，右键岛屿仍定位 Planner。

- [ ] **Step 5: 提交**

Commit: `feat(map): add zoomable route viewport`

### Task 3: 受限标签字号与路线步骤视觉

**Files:**

- Modify: `View/MapControl.xaml.cs`
- Modify: `Tools/CaptureRectGuardTest/Program.cs`

**Interfaces:**

- Consumes: `MapViewportState.Scale`。
- Produces: `GetLabelScreenFontSize(bool highlighted)`、`ApplyViewportLabelScale()`、`DrawRouteStepMarker()`。

- [ ] **Step 1: 写出失败守卫**

要求标签字号计算显式使用 `Math.Clamp(..., 11, 16)`，路线绘制调用 `DrawRouteStepMarker`，未聚焦路线使用低于 1 的 `Opacity`，并保留 `IsFocusedSegment` 高亮路径。

- [ ] **Step 2: 运行守卫并确认失败**

Run: `dotnet run --project Tools/CaptureRectGuardTest/CaptureRectGuardTest.csproj --no-restore`

- [ ] **Step 3: 实现标签和路线层次**

```csharp
private double GetLabelScreenFontSize(bool highlighted) {
    double normal = 13 * Math.Sqrt(viewportState.Scale);
    return Math.Clamp(highlighted ? normal + 2 : normal, 11, 16);
}
```

在 `IslandsButtonRearrange()` 的尺寸缓存键中加入当前字号；只在字号变化时重新测量。`DrawRoutePath()` 为每个到达岛屿加一个不可点击圆形 `Ellipse` 与居中的 `TextBlock`，显示 `i + 1`。未聚焦线使用现有路线色、`Opacity = 0.55`、`StrokeThickness = 2`；聚焦线保留现有发光和更粗线宽。

- [ ] **Step 4: 完整验证**

Run: `dotnet run --project Tools/CaptureRectGuardTest/CaptureRectGuardTest.csproj --no-restore`

Run: `dotnet build iBarter.csproj --no-restore -p:Platform=x86`

Run: `git diff --check`

Manual: 以 65%、100%、250% 查看文字；确认文字可读、路线编号随岛屿移动、全部路线仍显示不同颜色、选中路线明显但其他路线仍可辨认。

- [ ] **Step 5: 提交**

Commit: `feat(map): improve route hierarchy and label scaling`

## Final Acceptance Checklist

- [ ] 滚轮以鼠标锚点缩放，范围为 65%–250%。
- [ ] 空白海域右键平移不破坏岛屿中键与右键行为。
- [ ] 岛屿、路线、箭头、仓库和步骤编号同步缩放。
- [ ] 标签屏幕字号在 11–16 DIP，默认仍显示物品细节。
- [ ] 选中路线突出，未选路线仍可读。
- [ ] 自动路线、手动路线、地图高亮和船舱联动仍可用。
