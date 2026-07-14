# Clearance Bake 数据格式

更新日期：2026-07-11

## 目标

把只依赖静态导航几何的 Small、Medium、Large 可走位图和连通分量提前烘焙，避免主场景启动、编辑器预览和首次静态 Grid 查询各自重复全图 flood fill。Bake 是导航 Resource 的派生资产，不是第二份可编辑地图真值。

## 数据流

```text
RtsNavigationMapResource
  → NavigationMapSnapshot（稳定哈希）
  → ClearanceBakeSnapshot.Build
  → 规范二进制 + FNV-1a 稳定哈希
  → RtsClearanceBakeResource（Base64 载荷）
  → GridPathProvider / BuildingConnectivityGuard / ClearancePreviewSnapshot
```

导航几何发生变化后必须重新生成 Bake。加载时会比较 `SourceNavigationHash`；过期资产返回 `SourceNavigationMismatch`，主 Demo 不会静默使用错误数据。

## 格式 1

规范载荷按小端序写入：

```text
magic
format version
source navigation hash
world bounds
cell size
columns / rows
chunk size in cells
layer count
for Small, Medium, Large:
  movement class
  navigation radius
  packed walkable bits
  dense component ID per cell
```

当前 Demo：

- Cell：16px。
- Grid：77×40，共 3,080 cells。
- Chunk：16×16 cells。
- Chunk 网格：5×3，共 15 chunks。
- 层：Small 6px、Medium 8px、Large 12px。
- 规范载荷：38,215 字节。
- 源导航哈希：`B8441F9F1544B950`。
- Bake 哈希：`C14A74B57CF07284`。

Resource 同时保存格式、源哈希、Bake 哈希、Cell/Chunk 尺寸、行列数和载荷字节数。Converter 会逐项与载荷核对，避免 Inspector 元数据和实际数据漂移。

## Chunk API

`ClearanceBakeSnapshot.Chunk(id)` 返回稳定 chunk ID、cell 范围和世界包围盒。`FindIntersectingChunks(area)` 把动态 footprint 或局部编辑区域映射为受影响 chunk ID 列表。

当前 chunk 信息同时用于编辑器显示和运行时增量更新。动态占用 revision 大于 0 时：

- 静态 Bake 不再作为最终拓扑。
- 当缓存拓扑与当前世界只相差一次建筑放置或移除时，`GridPathProvider` 使用 `IncrementalNavigationConnectivityUpdater`，仅重采样 footprint 按导航半径膨胀后覆盖的 chunks。
- Walkability 局部更新后仍对全图执行确定性 component 标号，因此结果与 `NavigationConnectivityAnalyzer` 全量分析逐 cell、逐 component ID 一致。
- 如果一次查询前累计了多次 revision、恢复了快照或缺少精确变更区域，则安全回退全量分析。
- `BuildingConnectivityGuard` 的放置前候选分析仍使用全量拓扑；它需要比较尚未写入世界的假设 footprint，不复用运行时 revision 快路径。

当前示例修改只重采样 2/15 chunks、512/3,080 cells（16.6%），建筑加入和移除都与全量结果严格一致。下一层才是边界 component graph；它可以进一步避免全图 component 重标号，但不再阻塞现有增量 walkability 收益。

同源 Bake 更新可以通过 `RtsSimulation.TryCommitClearanceBake` 两阶段提交。Grid Provider 与 Building Connectivity Guard 会先独立验证源哈希、世界/Cell 布局和 Movement Class 半径；全部通过后同步替换缓存，并为活动编队重新发起路径请求。录制期间、错误源哈希和不支持 reload 的 Provider 不会修改现有缓存。

## 稳定错误码

- `3001`：不支持的格式版本。
- `3002`：Magic Header 错误。
- `3003`：源导航哈希非法。
- `3101`：Grid/Chunk 布局非法。
- `3201`～`3205`：层数量、等级、半径、载荷或 component ID 非法。
- `3301`：存在尾随载荷。
- `3401`～`3404`：Resource 缺失、Base64/元数据非法、源导航不匹配或声明哈希不匹配。

截断载荷会稳定返回 `InvalidLayerPayload`。相同导航输入重复生成 byte-identical 载荷与相同 Bake 哈希。

## 命令

```powershell
.\tools\generate_demo_clearance_bake.ps1
.\tools\validate_clearance_bake.ps1
```

Generator 覆盖 `data/demo_clearance_bake.tres`。Validator 独立加载 Navigation 与 Bake Resource，完成二进制解析、元数据、源哈希和 Bake 哈希检查。

## 验收

- 纯 C# 重复烘焙和序列化往返结果一致。
- 三层 node/component 数据完整。
- 截断载荷被拒绝。
- 静态 Bake 路径与动态 revision 回退路径均能查询成功。
- 局部区域稳定映射到合法 chunk ID。
- 单次动态 revision 实际接入 `GridPathProvider` 增量快路径；加入/移除结果均与全量分析完全一致。
- 示例只重采样 512/3,080 cells，并明确报告 dirty chunk `1,6`。
- Bake-only 候选在 8 单位移动途中完成原子提交和 8/8 重规划；错误 Navigation Hash 候选保持旧 Bake。
- Godot 黑盒场景确认正式 Resource 使用 `StaticBake`，三层分量一致，并在录像显示 15 个 chunk。
