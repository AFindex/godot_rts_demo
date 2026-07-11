# 全局 Navigation Connectivity Guard

更新日期：2026-07-11

## 解决的问题

局部净空检查只能发现“建筑之间留下了过窄缝隙”，不能识别一个合法贴墙建筑是否封死地图唯一通道。Connectivity Guard 在业务放置写入动态占用前，比较候选建筑加入前后的全局可走分量，防止玩家无意中把指定 Movement Class 的现有通路切断。

## 模块边界

```text
StaticWorld
  → NavigationConnectivityAnalyzer
  → NavigationConnectivitySnapshot
     ├─ GridPathProvider：起终点分量快速拒绝
     ├─ BuildingConnectivityGuard：候选放置前后比较
     ├─ ClearancePreviewSnapshot：编辑器分量着色
     └─ BuildingConnectivityDiffSnapshot：三档候选差异诊断
```

拓扑算法只有一份。`GridPathProvider` 不再维护私有的 walkable/component 烘焙代码；寻路、放置和编辑器使用相同的八邻域、禁止对角穿角、净空半径和动态 revision 语义。

## 数据结构

`NavigationConnectivitySnapshot` 是不可变约定的数据对象，包含：

- 世界边界、cell size、行列数和 Navigation Radius。
- 每个 cell 的 walkable 状态与 component ID。
- 每个分量的 ID、cell 数量和包围盒。
- 生成时的 `WorldRevision`。

默认 cell size 为 16px，与动态占用栅格一致。`GridPathProvider` 保持原来的可配置 cell size，并把相同值传给 Analyzer。

## 放置判定

`BuildingConnectivityGuard` 为 Small、Medium、Large 分别缓存当前 revision 的基线 Snapshot。检查候选建筑时：

1. 使用该建筑 `MinimumPassageClass` 的导航半径扩张候选 footprint。
2. 在不修改 `StaticWorld` 的情况下生成候选 Snapshot。
3. 对每个基线分量追踪候选中仍可走 cell 所属的分量。
4. 如果一个原分量的剩余 cell 分散到两个或更多候选分量，返回不保留连通性。
5. `BuildingPlacementValidator` 返回稳定结果 `DisconnectsNavigation`，不增加 revision，也不触发路径失效。

建筑完全覆盖少量区域但没有把剩余区域切开时不会误判为断路。已有地图本来就有多个孤岛时，只禁止进一步切开其中某个分量，不要求不同孤岛互相连通。

业务规则通过 `BuildingPlacementRules.PreserveConnectivity` 显式控制，默认开启。脚本化地图事件和压力测试仍可调用强制 `PlaceBuilding`，从而有意封路并测试动态改道/不可达行为。

## 缓存与性能

- 基线按 Movement Class 和 `WorldRevision` 缓存。
- 放置成功或移除建筑都会改变 revision，下一次检查自动重建对应基线。
- 候选分析只在低频建筑放置时运行，不进入 60Hz 单位模拟 Tick。
- 正常寻路继续复用按 revision 缓存的 Snapshot，没有新增每 Tick 分配。

静态 revision 的基线可以直接来自 Clearance Bake；动态 revision 则回退 Analyzer。当前 1000 单位基准为平均 7.99ms、P95 9.78ms、461B/Tick，仍低于 16.67ms 与 1KB/Tick 门槛。

## 验收与录像

- 纯 C# 自测：基线 1 个分量；封路候选变成 2 个分量并报告 split；安全候选保持连通。
- 业务黑盒：封闭唯一缺口返回 `DisconnectsNavigation`，安全建筑成功，8/8 单位仍穿过通道。
- VisualTestSession 提供通用诊断区域，录像中以红色标出被拒绝的 footprint；该显示不参与业务判定。
- 放置差异快照同时检查 Small/Medium/Large；阻断夹具为 3/3 拒绝，安全夹具为 3/3 保持连通，并由独立 Godot Control 展示 before/after 指标。

## 当前边界

- 这是全局采样 Grid 连通性，不是精确多边形布尔运算；正确性粒度由 cell size 决定。
- 当前保护的是“已有可走区域不被进一步切开”，还没有出生点、资源区或基地出口等具名关键锚点策略。
- Bake 已提供 16×16-cell chunk 描述和区域到 chunk 的映射；单 revision 已局部重采样 walkability，但 component 重标号仍是全图执行。
- 尚未把 Portal/Sector 图与 Grid 分量做双向一致性诊断。

若后续地图规模证明全图 component 重标号成为瓶颈，再维护跨 chunk 边界图；当前转入实际玩法，不再扩张诊断 UI。
