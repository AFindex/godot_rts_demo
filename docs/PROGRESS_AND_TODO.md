# Godot RTS 移动系统：进度回顾与剩余 TODO

更新日期：2026-07-10

这份文档是当前唯一的实施状态入口。它对照最初的 S0～S10 技术路线，区分已经形成可运行闭环的部分、只有原型的部分和尚未开始的部分。

## 1. 当前结论

项目已经具备一个可运行、可测试、可录像、可测性能的纯 C# RTS 移动内核：

- Godot 4.7 .NET 负责输入、绘制、NavMesh 查询和调试表现。
- 固定 Tick 模拟、单位数据、群组目标、Steering、碰撞、动态建筑、Portal 和狭口交通位于纯 C# 层。
- 25 个黑盒业务场景通过稳定测试接口驱动，不直接读取路径点、Steering 或 UnitStore 内部状态。
- 测试可以自动录制 AVI，并通过 Git LFS 保存在仓库中。
- 独立纯 C# Release 基准覆盖 256、512 和 1000 单位。

当前规模：

- 20 个 C# 源文件。
- 约 5,634 行 C#。
- 25 个黑盒场景。
- 25 段规范测试录像。
- Release 1000 单位 P95：约 11.14ms。
- Release 1000 单位当前线程分配：约 461B/Tick。

这已经是“可继续构建 RTS 游戏的移动内核原型”，还不是完整的《星际争霸 2》级移动、战斗和操作系统。

## 2. S0～S10 总览

| 阶段 | 状态 | 已完成 | 仍缺少 |
|---|---|---|---|
| S0 工程骨架 | 原型完成 | Godot 工程、纯 C# 模拟、固定 Tick、Godot 桥 | 独立 Runtime/Core/Editor 程序集边界 |
| S1 单位移动 | 完成 | Move、Stop、Hold、加速度、速度积分、到达 | 转向模板、不同运动类型配置资源 |
| S2 静态导航 | 原型完成 | Godot NavMesh、路径预算、命令版本隔离、Grid fallback | 路径缓存、后台查询、NavMesh chunk |
| S3 群体抵达 | 原型完成 | 唯一槽位、Hungarian 分配、跨命令槽位预留 | 终点局部重排、槽位交换、编队形状保持 |
| S4 局部群体运动 | 原型完成 | SpatialHash、TTC、候选速度、避让侧记忆 | 更低成本的候选评估、复杂优先级、移动类型交互 |
| S5 碰撞与约束 | 原型完成 | 圆碰撞、静态墙约束、动态建筑占用栅格 | Clearance Field、不同尺寸等级、非矩形 footprint |
| S6 高层路线与动态地图 | 大部分完成 | Portal A*、群组路线、动态 revision、局部失效、建筑移除恢复 | Sector、共享 corridor、Portal 自动生成、chunk 局部更新 |
| S7 狭口与卡死 | 大部分完成 | 车道、双向 admission、容量、排空、公平性、Hold 堵口、恢复阶梯 | 多连续狭口、复杂死锁、终点拥堵专用恢复 |
| S8 战斗移动 | 未开始 | 无 | AttackMove、攻击槽位、追击、leash、恢复原路线 |
| S9 编辑器与数据烘焙 | 未开始 | 当前运行时数据结构可作为输入 | Resource、Baker、Validator、Preview、格式版本 |
| S10 性能与诊断 | 基础完成 | Phase timing、GC、黑盒测试、录像、Release benchmark、门槛 | 更全面场景、结构化 capture、热点优化、CI 门禁 |

## 3. 已完成的运行时闭环

### 3.1 命令与单位状态

- 60Hz 固定模拟 Tick。
- Move、Stop、Hold。
- 命令版本隔离，旧路径结果不能覆盖新命令。
- 移动、等待路径、到达、Hold、不可达状态。
- 有限卡死恢复，不会无限重寻路。

### 3.2 路径与群组目标

- 运行时 Godot NavigationServer2D NavMesh 烘焙和查询。
- 每 Tick 路径查询预算。
- Portal 高层 A*。
- 群组共享高层 waypoint，每单位生成分段路径。
- 纯 C# Grid A* 作为动态占用 fallback。
- 目标槽位唯一分配。
- 不同命令波次之间共享槽位预留。

### 3.3 群体移动与碰撞

- SpatialHash 邻居查询。
- TTC 风险评估。
- 多角度 Candidate Steering。
- 避让侧记忆，减少左右反复横跳。
- 三轮圆碰撞位置修正。
- Hold、Arrived、Idle 和 Moving 使用不同推挤质量。
- 动态修正后重新执行墙体与狭口入口约束。

### 3.4 动态建筑

- 稳定 DynamicFootprintId。
- 16px 动态占用栅格。
- Navigation revision。
- 只失效剩余路径与建筑相交的单位。
- 完全封路后明确不可达。
- 建筑移除后恢复之前被阻塞的命令。
- 动态关闭 Portal 路线后选择替代路线。
- Godot 中可按 B 放置建筑、按 X 移除最近建筑。

### 3.5 狭口交通

- Approaching、Traversing、Exiting 状态。
- 稳定横向顺序和多车道偏移。
- 每个狭口独立的方向租约。
- Admission、入口容量和等待槽。
- 最大批次、最小批次、排空切换。
- 双向等待公平性和防饥饿。
- 碰撞推挤后的物理入口闸。
- Hold 单位位于通道内时关闭两端入口。
- Conflict tick、最大等待、队列深度和 blocker 诊断。

### 3.6 卡死恢复

恢复阶梯：

```text
切换避让侧
→ 局部重寻路
→ 重新计算 Portal 路线
→ 放弃高层路线，直接 Grid fallback
→ WaitingForClearance
→ 最多两次有限重试
→ Unreachable
```

- 使用“剩余路径长度是否下降”判断进度，不把绕圈和横向抖动当成进度。
- 狭口控制期间不进入通用恢复，避免破坏 admission。
- 距唯一目标槽位很近时由 Arrival 和碰撞收敛负责，不反复重寻路。
- 稳定移动 2.5 秒后恢复阶段归零。
- 路径查询明确失败时不会无限循环。

## 4. 测试、录像和性能

### 4.1 已有黑盒场景

当前 25 个场景覆盖：

- 单单位移动。
- 开放场和密集编队。
- 对向流与垂直交叉流。
- 命令替换和快速连续改令。
- 跨命令共享目标槽位。
- Stop 与 Hold。
- 混合单位半径和越界目标。
- 动态建筑局部失效、绕行、移除恢复和 Portal 改道。
- 正向和反向单向狭口。
- 平衡双向流、非对称双向流和错峰波次。
- Hold 堵口与释放。
- 临时包围恢复。
- 永久不可达重试上限。
- 192 单位压力场景。

黑盒场景只使用以下稳定业务动作：

```text
Spawn
Move / Stop / Hold
PlaceBuilding / RemoveBuilding
Step
Observe unit / traffic / recovery / performance
```

底层实现更换时，优先只修改 MovementTestRig 适配器。

### 4.2 录像

- `tools/record_tests.ps1` 自动向运行时查询场景目录。
- 每个场景独立启动 Godot Movie Maker。
- 每段录像保存 AVI、Godot 日志和 manifest。
- 单项失败不会中止其他录像。
- 当前仓库包含覆盖 25 个逻辑场景的规范录像。
- AVI 使用 Git LFS。

注意：当前规范录像来自多个功能里程碑批次，并非全部在同一个 commit 上重新录制。发布正式版本前应执行一次全量重新录制，生成单一时间戳目录。

### 4.3 Release 基准

独立 `net9.0` Release 基准直接编译 Simulation 源文件：

| 单位数 | 平均 Tick | P95 | 当前门槛 | 分配/Tick |
|---:|---:|---:|---:|---:|
| 256 | 1.35ms | 2.16ms | 4ms | 27B |
| 512 | 6.60ms | 11.01ms | 12.5ms | 182B |
| 1000 | 8.68ms | 11.14ms | 16.67ms | 461B |

当前热点排序：

1. Steering。
2. 动态圆碰撞。
3. 其他阶段明显较低。

这些数字是当前机器的本地基线，不代表所有硬件上的绝对保证。

## 5. 已知限制和技术债

### 5.1 终点收敛

少量单位可能在距离唯一槽位约 50～120px 处形成局部平衡，保持 Moving，但不进入严格到达范围。

TODO：

- 终点区域局部槽位重排。
- 两单位槽位交换。
- 对已到达单位降低软阻挡影响。
- 为无法靠近的单位选择备用槽位。
- 终点收敛专用 Steering 权重。

### 5.2 群组路线共享不足

首次命令共享高层路线，但动态失效后的受影响单位目前可能逐单位重算。

TODO：

- CommandGroupId。
- GroupRoute 生命周期和 revision。
- 同组受影响路径合并重算。
- 共享 corridor 和分段缓存。
- 同组不可达结果共享。

### 5.3 地图数据仍然手写

当前 DemoMapDefinition 手工声明障碍、Portal、Edge 和 Choke。

TODO：

- Runtime 数据格式与版本。
- Godot Resource 编辑入口。
- Resource 转不可变纯 C# snapshot。
- 确定性 Hash。
- 非法连接和宽度验证。
- 导航预览。

### 5.4 动态导航仍是混合方案

Godot NavMesh 处理静态地图；动态建筑通过占用栅格验证和 Grid A* fallback 处理。

TODO：

- NavMesh chunk 局部重烘焙。
- 非矩形和旋转 footprint。
- 建筑放置合法性与 clearance 检查。
- 建筑压住单位时的安全迁移。
- Portal/Sector 局部连通性更新。

### 5.5 单位尺寸与地形类型

当前 fallback 主要按约 8px 导航半径工作，尚未形成完整 movement class。

TODO：

- Small、Medium、Large 等 clearance 等级。
- 地面、悬浮、空中等移动层。
- 不同等级可用 Portal。
- 地形软代价和不可通行标签。

### 5.6 数据结构和算法热点

- SpatialHash 仍使用 Dictionary + List，不是 dense bucket/prefix sum。
- Steering 每个拥堵单位最多评估 12 个方向和 48 个邻居。
- Candidate 旋转仍会重复计算三角函数。
- DestinationSlotAllocator 使用 Hungarian，超大单次命令为 O(n³)。
- 1000 单位基准通过，但 Steering 仍占主要时间。

## 6. 尚未开始的大模块

### 6.1 S8 战斗移动

TODO：

- AttackMove 命令与路线恢复。
- 近战包围槽位。
- 远程攻击环。
- 追击、leash 和脱战。
- 目标死亡后的重新选敌。
- 攻击前摇期间的运动约束。
- 移动射击。
- 友军避让与敌军阻挡规则。

### 6.2 操作层

TODO：

- Shift 命令队列。
- Control Group。
- 双击选择同类单位。
- 选择优先级和 SelectionFilter。
- SmartCommandResolver。
- 地面、敌军、友军、资源和建筑右键语义。
- 相机边缘滚动、缩放和快速定位。
- Minimap 命令。
- 命令反馈、光标和落点动画。

### 6.3 S9 编辑器和数据管线

TODO：

- MapNavigationConfig Resource。
- UnitMovementProfile Resource。
- BuildingFootprint Resource。
- Occupancy/Clearance Baker。
- Sector/Portal Authoring Tool。
- Connectivity Validator。
- 多单位尺寸导航预览。
- Byte-identical 烘焙测试。
- 稳定错误码。

### 6.4 确定性、回放和联机

TODO：

- 输入命令日志。
- 固定 Tick 回放。
- 周期状态 Hash。
- 相同输入重复执行一致性测试。
- 快照保存和恢复。
- 命令序列化。
- 服务端权威、Lockstep 或混合方案决策。
- 客户端表现插值与模拟状态分离。

## 7. 推荐后续顺序

### 下一步 A：终点局部重排和 GroupRoute 共享

原因：这是当前最直接影响“星际 2 式顺滑感”的缺口。

验收：

- 开放场 80 单位在固定时间内至少 78 个进入严格到达范围。
- 动态绕障 20 单位不再有距离槽位 50px 以上的长期 Moving 单位。
- 多波次共享目标不重叠，且终点可自动换槽。
- 动态失效后同组只产生一次高层路线重算。

### 下一步 B：运行时导航数据格式和 Godot Resource

原因：继续增加地图与 Choke 前，需要停止在 DemoMapDefinition 中硬编码。

验收：

- Resource 可以表达当前 Demo 地图全部数据。
- 启动时转换为不依赖 Godot 的不可变 snapshot。
- 错误 Portal、Choke 和 footprint 得到稳定错误。
- 同一输入生成 byte-identical 数据。

### 下一步 C：Clearance 与 Movement Class

验收：

- 大小单位选择不同 Portal。
- 建筑不能产生比声明 clearance 更窄的非法通道。
- Grid、Portal 和 NavMesh 对单位尺寸给出一致结果。

### 下一步 D：AttackMove 最小闭环

验收：

- 移动、接敌、占据攻击槽、目标死亡、恢复原路线完整闭环。
- Stop、Hold、Move、AttackMove 语义不互相污染。
- 有对应黑盒测试和录像。

### 下一步 E：操作层和确定性

先完成 Shift 队列、Control Group 和 SmartCommand，再加入命令日志、回放和状态 Hash。联机方案应建立在可重复回放之后。

## 8. 可以并行但不能提前耦合的优化

- Steering 预计算候选方向。
- Dense SpatialHash。
- Group path cache。
- 数组池审计。
- 结构化 Debug Capture。
- 512/1000 单位复杂障碍基准。
- Godot 实际 NavMesh 与 Grid fallback 对比测试。

这些优化不得改变黑盒命令接口，也不得让测试场景依赖具体实现。

## 9. 当前验证命令

构建：

```powershell
dotnet build .\rts-demo-1.csproj -c Release --no-restore
```

全部黑盒测试：

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --self-test
```

Release 性能基准：

```powershell
.\tools\run_benchmarks.ps1
```

录制全部测试：

```powershell
.\tools\record_tests.ps1
```

## 10. 仓库状态

- GitHub：`https://github.com/AFindex/godot_rts_demo`
- 默认分支：`main`
- AVI：Git LFS。
- 当前基线报告：`benchmark_results/latest.json`。
- 录像索引：`test_videos/README.md`。
