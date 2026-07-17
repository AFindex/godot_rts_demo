# War3 遭遇战全图导航审计

- 地图：`lordaeron_crossroads` / `FCB4A48BBCC8DE9A`
- 农民：`u0`，基地起点 `1385,1875`
- 净空：`Small` / `6`
- 网格：`267×160`，cell `24`，stride `32`
- 运行时每点最多 `2` 个完整模拟 Tick
- 采样：`45`，关键失败：`0`
- 耗时：底层 `384.821 ms`，运行时 `602.696 ms`

## 汇总

- 底层网格连通可达：44
- 运行时获得路径：45
- 底层返回不安全路径：0
- 底层可达但运行时失败：0

### 底层结果

| 状态 | 数量 |
|---|---:|
| Reachable | 44 |
| StaticObstacleBlocked | 1 |

### 运行时结果

| 状态 | 数量 |
|---|---:|
| PathReady | 45 |

### 高层拓扑结果

| 状态 | 数量 |
|---|---:|
| DifferentRegionsNoRoute | 1 |
| DifferentRegionsWithRoute | 40 |
| SameRegion | 4 |

### 对照结果

| 状态 | 数量 |
|---|---:|
| AgreeReachable | 44 |
| RuntimeAdjustedUnreachableRequest | 1 |

## 自动原因判断

- 有 1 个目标与基地属于不同地形 region，但高层拓扑没有给出任何坡道 waypoint；低层 A* 因而会直接尝试跨越悬崖边界。这是高层 route 图未连通/坡道连接缺失的直接证据。
- 有 1 个原请求点本身不可站立，但运行时槽位分配器把农民改派到附近可走点并成功；这类不是连通性成功，而是目标修正。
- 未发现底层/运行时不一致或路径合约错误。

## 关键失败样本（最多 80 个）

| 点 | 底层 | 运行时 | 对照 | 说明 |
|---|---|---|---|---|

## 俯视图颜色

左半图为底层网格连通性：绿色可达，蓝色地形阻挡，棕色静态阻挡，橙色动态建筑，紫色断开的连通分区，洋红色为 provider 返回了不安全路径。右半图为完整运行时：绿色获得路径，红色不可达，黄色超时，洋红色路径合约错误，蓝色命令被拒绝。白色十字为基地起点。

## 文件

- JSON：`D:\Godot\projs\godot_rts_demo\benchmark_results\war3_navigation_audit_fixed_tiny_spatial_20260717\war3_navigation_audit.json`
- CSV：`D:\Godot\projs\godot_rts_demo\benchmark_results\war3_navigation_audit_fixed_tiny_spatial_20260717\war3_navigation_audit.csv`
- 俯视图：`D:\Godot\projs\godot_rts_demo\benchmark_results\war3_navigation_audit_fixed_tiny_spatial_20260717\war3_navigation_audit.png`
