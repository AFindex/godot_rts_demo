# Godot 编辑器多尺寸净空预览

更新日期：2026-07-11

## 目标

在不运行完整 RTS 模拟的情况下，从同一份 Navigation 与 Gameplay Profile Resource 直接检查 Small、Medium、Large 三档单位能否通过 Portal，以及不同尺寸建筑对可通行空间的实际要求。预览只消费正式数据，不维护第二套编辑器专用净空规则。

## 数据流

```text
RtsNavigationMapResource + RtsGameplayProfilesResource
  → 已有 Resource Converter
  → NavigationMapSnapshot + GameplayProfileCatalogSnapshot + ClearanceBakeSnapshot
  → ClearancePreviewSnapshot（纯 C#）
  → ClearancePreview2D（Godot [Tool] 绘制）
```

`ClearancePreviewSnapshot` 是渲染、纯 C# 自测和 Godot 黑盒录像之间的稳定边界。它输出世界边界、障碍、三档净空摘要、共享 Connectivity Snapshot、Portal 可通行矩阵和建筑尺寸，不暴露 A* 或运行时单位状态。

## 画面语义

- 绿色：Small，导航半径 6px，要求宽度 14px。
- 黄色：Medium，导航半径 8px，要求宽度 18px。
- 红色：Large，导航半径 12px，要求宽度 26px。
- 蓝色 Portal：Large 也能通过；否则按可通过的最高等级着色。
- Portal 标签由实际宽度和三位等级组成，例如 `22px SM-`、`96px SML`。
- 灰色实心矩形是静态障碍；外侧三圈分别是按三档导航半径膨胀后的禁入边界。
- 底部建筑面板同时显示 Pylon、Barracks、Factory、CommandCenter 的 footprint，以及配置要求宽度形成的外框。
- `ConnectivityClass` 选择当前着色等级；不同连通分量使用不同的低透明度底色，图例显示各等级的分量数量。
- 有匹配 Bake 时图例显示 `source=StaticBake`，并绘制稳定 chunk ID 和世界边界；Bake 过期时不会被采用。
- 传入局部变更区域时，按 Large 导航半径保守膨胀后高亮橙色 `DIRTY C#`；绘制节点只消费纯 C# 预览快照。

## Godot 接入

`Main.tscn` 挂载 `ClearancePreview2D`，Inspector 中引用：

- `NavigationMapAsset = data/demo_navigation_map.tres`
- `GameplayProfilesAsset = data/demo_gameplay_profiles.tres`
- `ClearanceBakeAsset = data/demo_clearance_bake.tres`
- `Enabled = true`

节点使用 `[Tool]`，在编辑器场景视图中每 0.5 秒刷新一次。普通游戏运行时不显示它；测试用例通过 `SetRuntimeSnapshots` 为 `clearance-editor-preview`、Bake 和增量 chunk 场景开启相同绘制路径，因此录像验证的不是另一份测试专用实现。

## 验收

纯 C# 自测验证：

- 输出正好 3 个移动等级和 4 个建筑等级。
- 三档都生成 Connectivity Snapshot，并报告有效分量数量。
- 22px Portal 的结果为 `SM-`。
- 96px Portal 的结果为 `SML`。

Godot 黑盒场景验证正式 Demo Resource 能生成 3 档、5 条 Portal 和 4 档建筑预览，并自动录制 1280×720、30 FPS、10 秒 AV1/WebM。场景通过 VisualTestCatalog 公开的稳定用例入口启动，不访问 `ClearancePreview2D` 的私有绘制细节。

`clearance-incremental-chunks` 额外验证 dirty chunks 为 `1,6`，仅重采样 512/3,080 cells；加入与移除后的拓扑都严格等于全量分析。录像以橙色覆盖层显示相同两个 chunks。

## 当前边界

- 这是场景内 `[Tool]` 预览基线，还没有独立 EditorPlugin Dock、点击选择或拖拽 Portal。
- 当前能显示全局连通分量，但没有点击分量、孤岛列表或放置前后差异面板。
- 已能显示受影响 chunks，并由运行时增量更新器消费同一 chunk 规划；尚没有 EditorPlugin 重烘焙按钮。
- 障碍与建筑均按轴对齐矩形显示；尚不支持旋转和非矩形 footprint。
- Resource 改动可定时刷新画面，但运行中的模拟尚未实现差异热重载。

下一步是增加孤岛/放置差异面板、Resource 热重载和边界 component graph。绘制节点继续只消费分析结果，不实现拓扑算法。
