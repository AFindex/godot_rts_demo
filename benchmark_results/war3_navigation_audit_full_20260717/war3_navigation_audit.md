# War3 遭遇战全图导航审计

- 地图：`lordaeron_crossroads` / `FCB4A48BBCC8DE9A`
- 农民：`u0`，基地起点 `1385,1875`
- 净空：`Small` / `6`
- 网格：`267×160`，cell `24`，stride `1`
- 运行时每点最多 `2` 个完整模拟 Tick
- 采样：`42720`，关键失败：`34096`
- 耗时：底层 `288.236 ms`，运行时 `455704.588 ms`

## 汇总

- 底层网格连通可达：41131
- 运行时获得路径：7549
- 底层返回不安全路径：0
- 底层可达但运行时失败：34096

### 底层结果

| 状态 | 数量 |
|---|---:|
| DynamicObstacleBlocked | 358 |
| Reachable | 41131 |
| StaticObstacleBlocked | 769 |
| TerrainBlocked | 302 |
| WorldBoundaryBlocked | 160 |

### 运行时结果

| 状态 | 数量 |
|---|---:|
| PathReady | 7549 |
| Unreachable | 35171 |

### 高层拓扑结果

| 状态 | 数量 |
|---|---:|
| EmptyPortalGraph | 42720 |

### 对照结果

| 状态 | 数量 |
|---|---:|
| AgreeReachable | 7035 |
| AgreeUnreachable | 1075 |
| RuntimeAdjustedUnreachableRequest | 514 |
| RuntimeFailureDespiteSimplePath | 34096 |

### 分布式深探针阻断原因

| 状态 | 数量 |
|---|---:|
| terrain_transition | 101 |

## 自动原因判断

- 有 34096 个点底层网格连通，但完整运行时仍失败。优先检查对应行的高层 route、slot 偏移和 RuntimeDetail。
- 本次 42720 个运行时请求全部接入了空的 Portal 图；当前 War3MapRuntime.CreateNavigation() 没有生成任何 portal node、edge 或 choke。因此只要直线被地形挡住，高层规划器就不可能给出绕坡路线。
- 分布式深探针中有 101 个失败路径包含 `terrain_transition`：网格只验证节点中心可站立，却没有验证相邻节点之间的扫掠圆可穿越，A* 会选中跨悬崖的伪边，最后被运行时路径合约拒绝。
- 有 514 个原请求点本身不可站立，但运行时槽位分配器把农民改派到附近可走点并成功；这类不是连通性成功，而是目标修正。

## 关键失败样本（最多 80 个）

| 点 | 底层 | 运行时 | 对照 | 说明 |
|---|---|---|---|---|
| 12,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 3->4 is blocked: 780,1332->756,1332; cause=terrain_transition |
| 36,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 60,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 84,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 108,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 132,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 156,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 180,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 204,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 228,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 252,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 276,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 300,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 324,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 348,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 372,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 396,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 420,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 444,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 468,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 492,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 516,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 540,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 564,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 588,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 612,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 636,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 660,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 684,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 708,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 732,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 756,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 780,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 804,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 828,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 852,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 876,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 900,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 924,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 948,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 972,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 996,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1020,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1044,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1068,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1092,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1116,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1140,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1164,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1188,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1212,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1236,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1260,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1284,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1308,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1332,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1356,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1380,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1404,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1428,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1452,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1476,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1500,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1524,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1548,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1572,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1596,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1620,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1644,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1668,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1692,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1716,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1740,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1764,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1788,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1812,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1836,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1860,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1884,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |
| 1908,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=EmptyPortalGraph, regions=-2->-2). |

## 俯视图颜色

左半图为底层网格连通性：绿色可达，蓝色地形阻挡，棕色静态阻挡，橙色动态建筑，紫色断开的连通分区，洋红色为 provider 返回了不安全路径。右半图为完整运行时：绿色获得路径，红色不可达，黄色超时，洋红色路径合约错误，蓝色命令被拒绝。白色十字为基地起点。

## 文件

- JSON：`D:\Godot\projs\godot_rts_demo\benchmark_results\war3_navigation_audit_full_20260717\war3_navigation_audit.json`
- CSV：`D:\Godot\projs\godot_rts_demo\benchmark_results\war3_navigation_audit_full_20260717\war3_navigation_audit.csv`
- 俯视图：`D:\Godot\projs\godot_rts_demo\benchmark_results\war3_navigation_audit_full_20260717\war3_navigation_audit.png`
