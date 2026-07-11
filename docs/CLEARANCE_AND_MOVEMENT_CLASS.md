# Clearance 与 Movement Class

更新日期：2026-07-11

## 1. 当前目标

这一阶段解决“单位物理半径不同，但路径层固定按 8px 查询”的不一致。当前实现把物理碰撞半径和导航净空半径分开：碰撞、槽位和最终位置约束继续使用真实半径；Grid、Portal 和路径验证使用向上取整后的导航等级。

## 2. 尺寸等级

| Movement Class | 物理半径范围 | 导航半径 | 最小声明宽度 |
|---|---:|---:|---:|
| Small | `0 < r <= 6` | 6px | 14px |
| Medium | `6 < r <= 8` | 8px | 18px |
| Large | `r > 8` | 至少 12px | 至少 26px |

最小声明宽度使用：

```text
required width = navigation radius * 2 + 2px safety margin
```

大于 12px 的物理单位不会被压回 12px，而是继续使用自身半径作为导航半径。非法的零、负数、NaN 或 Infinity 半径会在单位创建时直接拒绝。

## 3. 运行时数据流

```text
physical radius
  → MovementClearanceProfile
  → MovementClass + navigation radius
  ├─ Portal edge width filtering
  ├─ Grid connectivity cache
  ├─ Godot path post-validation
  ├─ dynamic occupancy segment/disc checks
  └─ group route uses maximum navigation radius
```

`UnitStore` 为每个单位保存 `MovementClasses` 和 `NavigationRadii`。同一 Move 中，高层共享 Portal 路线按组内最大导航半径规划，保证共享路线对组内所有单位安全；进入分段路径后，每个单位仍按自己的导航半径查询和验证。

## 4. Grid、Portal、Godot 与动态建筑

### Grid

`IPathProvider.FindPath` 现在每次请求显式携带导航半径。`GridPathProvider` 为 Small、Medium、Large 各维护一份按 `NavigationRevision` 失效的 walkable/component 缓存，避免混合半径请求反复重建全图。

静态 revision 0 优先从 `ClearanceBakeSnapshot` 加载三档拓扑；出现动态建筑后回退统一 Analyzer。Grid cell 已统一为 16px，与动态占用和 Bake 对齐。

### Portal

Portal Edge 继续保存实际可通行宽度。只有 `edge.Width >= navigationRadius * 2 + 2` 时该等级单位才能使用该边；窄边被过滤后，A* 可以选择更长但更宽的替代路线。

### Godot NavMesh

当前 Demo 仍只烘焙一张约 8px 的 Godot NavigationPolygon。它被视为候选路径源，而不是最终净空真值：

- 每条 Godot 路径都用请求单位的导航半径重新检查静态和动态障碍。
- Large 路径不满足净空时回退到 Large Grid。
- Small 在 8px NavMesh 没有路径但 6px 实际可通过时，也可回退到 Small Grid。

后续如需减少 fallback，可为三档烘焙独立 Godot Navigation Map；这不是当前正确性的前置条件。

### 动态建筑

动态占用查询已经按请求导航半径扩张 footprint。放置或移除建筑会增加 `NavigationRevision`，三档 Grid 缓存在下一次对应查询时独立重建。

业务放置使用 `TryPlaceBuilding`，并与地图脚本使用的强制 `PlaceBuilding` 分离。当前预设 footprint 为：

| 建筑等级 | 尺寸 |
|---|---:|
| Small | 32×32px |
| Medium | 64×48px |
| Large | 112×80px |
| Huge | 160×120px |

业务放置会在写入动态占用前检查：有限且为正的 footprint、世界边界、静态障碍重叠、已有动态建筑重叠、单位占用，以及与世界边界/静态障碍/动态建筑之间是否留下小于指定 Movement Class 的假通道。0 宽贴墙或贴建筑表示明确封闭，允许放置；只有 `0 < gap < required width` 才拒绝。

稳定结果码包括 `InvalidFootprint`、`OutsideWorld`、`StaticObstacleOverlap`、`DynamicFootprintOverlap`、`UnitOverlap`、`InsufficientClearance` 和 `DisconnectsNavigation`。

局部检查通过后，`BuildingConnectivityGuard` 会按建筑声明的 `MinimumPassageClass` 比较放置前后的采样连通分量。只要候选 footprint 把原本同一分量中的剩余可走区域切成两块或更多，就拒绝放置。地图脚本仍可使用强制 `PlaceBuilding`，业务放置也可通过 `PreserveConnectivity=false` 显式关闭该策略。

## 5. 黑盒验收

### `clearance-portal-choice`

同一静态障碍提供 22px 的短 Portal 和 96px 的长 Portal：

- Small 实际经过上方窄路线。
- Large 过滤窄边并经过下方宽路线。
- 两个单位最终均到达，0 重叠、0 不可达。

### `clearance-dynamic-gap`

两个动态建筑形成 24px 缝隙：

- Small 穿过并到达。
- Large 不尝试物理硬挤，保持在墙前并通过有限恢复进入 `Unreachable`。

场景只使用 Spawn、Move、PlaceBuilding、Step 和业务状态观察，不读取 Grid、Portal 或 UnitStore 内部实现。

### `building-footprint-sizes`

Small、Medium、Large、Huge 四种业务 footprint 同时合法放置，并验证实际尺寸和建筑计数。

### `building-placement-rules`

覆盖动态重叠、静态重叠、越界、压住单位、Large 净空不足，以及贴墙明确封闭。所有分支均验证稳定业务结果码。

### `building-size-navigation`

四种尺寸建筑形成错位障碍场，24 个单位全部绕行到达，0 重叠、0 不可达，动态 revision 为 4。

### `building-connectivity-guard`

两段静态墙只留下一个 112×80px 缺口。相同尺寸的候选建筑与上下墙贴合，局部规则认为 0 宽是明确封闭，但全局比较发现 Large 的单一分量会被切成两个，因此返回 `DisconnectsNavigation`。另一处安全建筑正常放置，8/8 单位继续穿过缺口。

## 6. 编辑器多尺寸预览

`Main.tscn` 中的 `ClearancePreview2D` 是一个 `[Tool]` 节点，直接读取 Navigation 与 Gameplay Profile Resource，并先转换成纯 C# `ClearancePreviewSnapshot`：

- Small/Medium/Large 分别以绿、黄、红显示静态障碍膨胀轮廓。
- 每条 Portal Edge 显示实际宽度和 `SML` 可通行标记，例如 `SM-` 表示 Large 被过滤。
- 图例显示三档导航半径、最小要求宽度、可通行边数和全局分量数。
- 当前选择的 Movement Class 使用低透明度分量着色；拓扑与 GridPathProvider 共用 `NavigationConnectivityAnalyzer`。
- 有有效 Bake 时显示 `source=StaticBake` 和 16×16-cell chunk 边界；没有或动态失效时明确使用运行时分析。
- 底部同时显示 32×32、64×48、112×80、160×120 四档建筑 footprint 和要求净空外框。
- 编辑器中每 0.5 秒重建预览，Resource 数值修改后不需要启动游戏即可检查。
- 普通运行时默认不绘制；只有 `clearance-editor-preview` 自动测试显式启用，避免污染正式表现。

纯预览快照不依赖 Godot Node，也被 `ClearancePreviewSelfTest` 复用。测试只断言输入资产对应的业务输出，不读取绘制节点内部状态：22px Edge 必须显示 `SM-`，96px Edge 必须显示 `SML`。详见 `CLEARANCE_EDITOR_PREVIEW.md`。

候选建筑另由纯 C# `BuildingConnectivityDiffSnapshot` 输出三档 before/after 差异；独立 Godot Control 只读取快照并显示组件数、blocked/split/disconnected 与 dirty chunks，不把放置判定或拓扑算法耦合进 UI。

## 7. 当前性能

| 单位数 | 平均 Tick | P95 | 分配/Tick |
|---:|---:|---:|---:|
| 256 | 1.20ms | 1.76ms | 27B |
| 512 | 3.96ms | 4.88ms | 182B |
| 1000 | 8.45ms | 11.75ms | 461B |

## 8. 后续层

当前完成的是运行时尺寸语义与路径一致性第一层，后续按顺序推进：

1. 在已有 dirty-chunk 重采样上增加边界 component graph。
2. 在已有 Bake-only 安全提交上增加文件监听、自动触发去抖和格式迁移。
3. Editor 中的 Portal/Choke 交互编辑、局部非法窄口定位和可交互孤岛列表。
4. Ground、Hover、Air 等移动层，以及地形软代价和标签。
5. 非矩形/旋转 footprint 与局部 NavMesh chunk 更新。
