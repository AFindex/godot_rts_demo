# War3 遭遇战全图导航审计

- 地图：`lordaeron_crossroads` / `FCB4A48BBCC8DE9A`
- 农民：`u0`，基地起点 `1385,1875`
- 净空：`Small` / `6`
- 网格：`267×160`，cell `24`，stride `32`
- 运行时每点最多 `2` 个完整模拟 Tick
- 采样：`45`，关键失败：`36`
- 耗时：底层 `9.564 ms`，运行时 `535.631 ms`

## 汇总

- 底层网格连通可达：44
- 运行时获得路径：8
- 底层返回不安全路径：0
- 底层可达但运行时失败：36

### 底层结果

| 状态 | 数量 |
|---|---:|
| Reachable | 44 |
| StaticObstacleBlocked | 1 |

### 运行时结果

| 状态 | 数量 |
|---|---:|
| PathReady | 8 |
| Unreachable | 37 |

### 高层拓扑结果

| 状态 | 数量 |
|---|---:|
| Unavailable | 45 |

### 对照结果

| 状态 | 数量 |
|---|---:|
| AgreeReachable | 8 |
| AgreeUnreachable | 1 |
| RuntimeFailureDespiteSimplePath | 36 |

### 分布式深探针阻断原因

| 状态 | 数量 |
|---|---:|
| terrain_transition | 36 |

## 自动原因判断

- 有 36 个点底层网格连通，但完整运行时仍失败。优先检查对应行的高层 route、slot 偏移和 RuntimeDetail。
- 分布式深探针中有 36 个失败路径包含 `terrain_transition`：网格只验证节点中心可站立，却没有验证相邻节点之间的扫掠圆可穿越，A* 会选中跨悬崖的伪边，最后被运行时路径合约拒绝。

## 关键失败样本（最多 80 个）

| 点 | 底层 | 运行时 | 对照 | 说明 |
|---|---|---|---|---|
| 12,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 3->4 is blocked: 780,1332->756,1332; cause=terrain_transition |
| 780,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 3->4 is blocked: 780,1332->756,1332; cause=terrain_transition |
| 1548,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1812,1356->1812,1332; cause=terrain_transition |
| 2316,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1812,1356->1812,1332; cause=terrain_transition |
| 3084,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1812,1356->1836,1332; cause=terrain_transition |
| 3852,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1524->1932,1524; cause=terrain_transition |
| 4620,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1524->1932,1524; cause=terrain_transition |
| 5388,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1524->1932,1524; cause=terrain_transition |
| 6156,12 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1524->1932,1524; cause=terrain_transition |
| 12,780 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 3->4 is blocked: 780,1356->756,1332; cause=terrain_transition |
| 780,780 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 3->4 is blocked: 780,1332->756,1332; cause=terrain_transition |
| 1548,780 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1812,1356->1812,1332; cause=terrain_transition |
| 2316,780 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1812,1356->1812,1332; cause=terrain_transition |
| 3084,780 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1548->1932,1524; cause=terrain_transition |
| 3852,780 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1524->1932,1524; cause=terrain_transition |
| 4620,780 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1524->1932,1524; cause=terrain_transition |
| 5388,780 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1524->1932,1524; cause=terrain_transition |
| 6156,780 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1524->1932,1524; cause=terrain_transition |
| 12,1548 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 3->4 is blocked: 684,1476->660,1476; cause=terrain_transition |
| 2316,1548 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1572->1932,1548; cause=terrain_transition |
| 4620,1548 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1908,1620->1932,1620; cause=terrain_transition |
| 6156,1548 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 4->5 is blocked: 5748,1620->5772,1596; cause=terrain_transition |
| 12,2316 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 3->4 is blocked: 684,2364->660,2364; cause=terrain_transition |
| 2316,2316 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 2->3 is blocked: 1908,2172->1932,2196; cause=terrain_transition |
| 3084,2316 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 2->3 is blocked: 1908,2172->1932,2196; cause=terrain_transition |
| 3852,2316 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 2->3 is blocked: 1908,2172->1932,2196; cause=terrain_transition |
| 4620,2316 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 2->3 is blocked: 1908,2100->1932,2124; cause=terrain_transition |
| 12,3084 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 4->5 is blocked: 780,2484->756,2508; cause=terrain_transition |
| 780,3084 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 4->5 is blocked: 780,2508->756,2508; cause=terrain_transition |
| 1548,3084 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1812,2484->1812,2508; cause=terrain_transition |
| 2316,3084 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 1->2 is blocked: 1812,2484->1812,2508; cause=terrain_transition |
| 3084,3084 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 2->3 is blocked: 1908,2244->1932,2268; cause=terrain_transition |
| 3852,3084 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 2->3 is blocked: 1908,2196->1932,2196; cause=terrain_transition |
| 4620,3084 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 2->3 is blocked: 1908,2220->1932,2220; cause=terrain_transition |
| 5388,3084 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 2->3 is blocked: 1908,2220->1932,2220; cause=terrain_transition |
| 6156,3084 | Reachable | Unreachable | RuntimeFailureDespiteSimplePath | Exact cell is reachable in the low-level clearance grid. Runtime rejected a target reachable in the low-level flood-fill; no high-level waypoint was assigned (topology=Unavailable, regions=-2->-2). Deep probe: final assigned-target segment failed: Segment 2->3 is blocked: 1908,2220->1932,2220; cause=terrain_transition |

## 俯视图颜色

左半图为底层网格连通性：绿色可达，蓝色地形阻挡，棕色静态阻挡，橙色动态建筑，紫色断开的连通分区，洋红色为 provider 返回了不安全路径。右半图为完整运行时：绿色获得路径，红色不可达，黄色超时，洋红色路径合约错误，蓝色命令被拒绝。白色十字为基地起点。

## 文件

- JSON：`D:\Godot\projs\godot_rts_demo\benchmark_results\war3_navigation_audit_tiny\war3_navigation_audit.json`
- CSV：`D:\Godot\projs\godot_rts_demo\benchmark_results\war3_navigation_audit_tiny\war3_navigation_audit.csv`
- 俯视图：`D:\Godot\projs\godot_rts_demo\benchmark_results\war3_navigation_audit_tiny\war3_navigation_audit.png`
