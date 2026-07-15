# 地形数据编辑与导出工作流

这套工具用于编辑“玩法地形”，不是用贴图反推寻路。编辑数据、结构检查、运行时快照和 3D 表现彼此分开：编辑器可以高频迭代，而模拟始终只读取已经验证过的不可变 `TerrainMapSnapshot`。

## 打开与编辑

1. 在 Godot 中打开 `demo/terrain/TerrainAuthoringWorkspace.tscn`。
2. 在 2D 视图选中 `Authoring` 节点。右侧会出现 `RTS Terrain` 面板，Inspector 中显示笔刷和覆盖层参数。
3. 修改地图列数、行数或格子大小后，点击 `Initialize / Resize Cell Arrays`。扩大或缩小地图会保留新旧范围的重叠格子；不会把一维数组错误地按新宽度重新解释。
4. 选择笔刷类型、半径和值，在 2D 视图按住左键拖动。一次拖动只形成一个 Godot 撤销记录，`Ctrl+Z` 会精确恢复整次笔画。
5. 点击 `Validate Terrain` 查看结构错误；点击 `Validate + Export Runtime .tres` 只在检查通过时导出。

当前编辑资产为 `data/demo_terrain_authoring.tres`，示例运行时输出为 `data/demo_authored_terrain.tres`。

## 笔刷之间互不代替

- Height：只改悬崖层。
- Surface：只改表面材质编号。
- Pathing：只改 Ground / Shallow Water / Deep Water / Air Blocked 通行位。
- Buildable：只改可建造标记。
- Vision：只改视野遮挡。
- Creep：只改菌毯限制。
- Ramp：写入低层高度和坡道方向，并清除该格可建造标记。
- Clear Ramp：只删除坡道方向，不偷偷改回高度、表面或通行。

圆形笔刷按格子中心计算影响范围。拖动过程中只更新一份内存编辑文档，结束笔画时才一次性写回 Godot Resource，避免每经过一个格子都重建全部数组。

## 覆盖层

`Overlays` 可以独立组合 Surface、Height、Ground Pathing、Water Pathing、Air Blocking、Buildable、Vision、Creep、Ramp 和 Validation。覆盖层只负责显示；切换颜色或关闭显示不会改变任何玩法字段和运行时哈希。

## 导出前必须通过的检查

- 坡道不能出界，且两端必须连接相邻高度层。
- 可通行地面不能形成没有入口的孤立区域。
- 出生点、资源点、目标点必须落在合法地面，并能与出生区域连通。
- 仅按格子中心看似连通、但按配置单位半径没有足够净空的通道会明确报为过窄。
- 数组长度、材质编号、锚点编号和运行时基础字段必须合法。

错误包含稳定错误码和 `[column,row]` 坐标。检查失败时不会生成新的运行时资产，也不会把不完整编辑数据交给模拟层。

## 数据边界

- `RtsTerrainAuthoringResource` 保存可编辑字段、材质描述、检查参数和出生/资源/目标锚点。
- `TerrainAuthoringDocument` 负责纯 C# 笔刷与结构检查，不依赖 Godot 节点。
- `TerrainAuthoringResourceConverter` 是唯一的编辑资产转换边界。
- `RtsTerrainMapResource` 保存验证后的规范 payload、版本和哈希。
- `TerrainMapSnapshot` 是运行时权威数据；寻路、建造、视野和表现只读它，不读取编辑器数组。

因此，后续更换编辑面板、笔刷交互或美术预览不会要求修改模拟；运行时格式发生升级时，也只需要维护转换和版本校验，不让编辑器对象进入回放或热快照。

## 自动检查和录像

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --terrain-authoring-self-test

F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --validate-demo-terrain-authoring

.\tools\record_demo.ps1 -Demo terrain-authoring
```

自动演示会依次验证合法地图、正交笔刷、错误坡道的拒绝与精确坐标、修复后的规则笔刷，以及导出后重新加载的哈希一致性。录像只有在 `RTS_TERRAIN_AUTHORING_DEMO_PASS` 出现后才会编码为 AV1/WebM。
