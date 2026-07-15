# 测试地形预制库

这套资产用于寻路、建造、视野、动态阻挡和性能测试。它们不是同一张地图换颜色：每个稳定 ID 都对应不同的高度、水域、坡道或障碍结构，并声明起点、终点、建筑探针和可选动态阻挡区。

## 预制目录

| ID | 结构 | 主要用途 |
|---|---|---|
| `open-field` | 平地、泥地、路线外浅水池 | 无障碍基线、直线移动、合法建筑区 |
| `single-wide-ramp` | 单个 128px 正向坡道 | 标准跨层移动、坡道编队 |
| `parallel-ramp-bypass` | 近窄坡与远宽坡 | 建筑封近坡、改道、拆除恢复 |
| `narrow-wide-choice` | 20px 与 120px 两种坡道 | Small/Medium/Large 尺寸门禁 |
| `three-level-switchback` | 三层、两段分离爬升 | 多坡道序列、后续 Choke 接管 |
| `ring-plateau` | 中央高地、四面入口 | 四方向坡道、平行路线选择 |
| `sunken-basin` | 中央低地、四面出口 | 反向坡道、下坡和离开盆地 |
| `shallow-water-fords` | 深水带、两处浅水浅滩 | Ground 过浅水、深水阻断、浅水禁造 |
| `island-causeway` | 双岛、两格宽陆桥 | 单通路断连保护、动态封桥与恢复 |
| `vision-ridge` | 贯穿地图的高地视野墙 | 连续上/下坡、视野阻挡、反向坡道 |
| `alternating-gates` | 三道错位墙和交替开口 | 长折线路径、拐角净空、局部寻路压力 |
| `large-four-routes` | 64×40、四条跨层路线 | 大图拓扑、平行路线和性能统计 |

四个坡道方向都有实际覆盖：`PositiveX`、`NegativeX`、`PositiveY`、`NegativeY`。预制首次接入时发现负方向坡道的局部拓扑搜索只取到了坡道格和一侧邻格；现在搜索矩形会同时包含低端与高端，环形高地、下沉盆地和视野山脊的 10 条负/正方向连接均通过。

## 代码与资产边界

- `TerrainTestPresetCatalog` 是稳定元数据与几何来源，纯 C#，不依赖 Godot Node。
- `test_resources/terrain_presets/*.tres` 是从不可变 `TerrainMapSnapshot` 生成的运行时资产，可直接被其他测试加载。
- 每个条目保存稳定 ID、用途、Tags、Start、Goal、连通预期、坡道数量、Medium 高层路线长度、建筑探针和可选动态阻挡区。
- `.tres` 的哈希必须与目录定义一致。图集发现资产缺失、损坏或过期会拒绝启动，不会悄悄回退到代码地图。

重新生成资产：

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . res://demo/3d/TerrainPresetGallery3D.tscn `
  -- --generate-terrain-preset-assets
```

## 验证

专项自测对 12 张地图逐一检查：

- ID 合法且唯一，运行时哈希 12/12 唯一。
- 规范字节反序列化后哈希不变。
- 起点和终点对 8px 半径单位均为合法站位。
- 实际连通状态与声明一致。
- 建筑探针的可建造结果与声明一致。
- 自动拓扑坡道数与声明一致，Medium 高层路线经过的 Choke 数与声明一致。
- `narrow-wide-choice` 用 4px Bake 锁定 20px 坡道允许 Medium、拒绝 Large；Large 路线必须选择 120px 坡道。
- `parallel-ramp-bypass` 封近坡后仍连通；`island-causeway` 封住唯一陆桥后必须断连。

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --terrain-presets-self-test

.\tools\record_demo.ps1 -Demo terrain-presets
```

`TerrainPresetGallery3D.tscn` 只负责显示已保存资产。左右方向键或 Space 浏览；自动录像依次展示全部 12 张地图、起点、终点、建筑探针和动态阻挡区。

当前录像位于 `test_videos/20260715_204017/terrain-presets.webm`：721 帧、24.03 秒、AV1/WebM、CRF 32、preset 8；Manifest 记录 `presets=12, ramps=21, assets=12, hashes=12`。

加入预制与负方向坡道修复后，原有 128/128 黑盒业务回归继续全部通过；没有修改既有狭口、移动或建造阈值。
