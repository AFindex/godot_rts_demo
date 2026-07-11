# Godot RTS 移动系统：进度回顾与剩余 TODO

更新日期：2026-07-11

这份文档是当前唯一的实施状态入口。它对照最初的 S0～S10 技术路线，区分已经形成可运行闭环的部分、只有原型的部分和尚未开始的部分。

## 1. 当前结论

项目已经具备一个可运行、可测试、可录像、可测性能的纯 C# RTS 移动内核：

- Godot 4.7 .NET 负责输入、绘制、NavMesh 查询和调试表现。
- 固定 Tick 模拟、单位数据、群组目标、Steering、碰撞、动态建筑、Portal 和狭口交通位于纯 C# 层。
- 59 个黑盒业务场景通过稳定测试接口驱动，不直接读取路径点、Steering、UnitStore、CombatStore 或队列内部数组。
- 测试可以自动录制 AVI，并通过 Git LFS 保存在仓库中。
- 独立纯 C# Release 基准覆盖 256、512、1000 单位移动，以及 128/256 总单位持续 AttackMove。

当前规模：

- 61 个 C# 源文件。
- 约 18,177 行 C#（按仓库源码统计）。
- 59 个黑盒场景。
- 覆盖 59 个逻辑场景的规范测试录像。
- Release 1000 单位移动 P95：约 10.62ms。
- Release 1000 单位当前线程分配：约 461B/Tick。

这已经是“可继续构建 RTS 游戏的移动内核原型”，还不是完整的《星际争霸 2》级移动、战斗和操作系统。

## 2. S0～S10 总览

| 阶段 | 状态 | 已完成 | 仍缺少 |
|---|---|---|---|
| S0 工程骨架 | 原型完成 | Godot 工程、纯 C# 模拟、固定 Tick、Godot 桥 | 独立 Runtime/Core/Editor 程序集边界 |
| S1 单位移动 | 完成 | Move、Stop、Hold、加速度、速度积分、到达、UnitMovementProfile Resource | 转向模板、Ground/Hover/Air 移动层 |
| S2 静态导航 | 原型完成 | Godot NavMesh、路径预算、命令版本隔离、Grid fallback、静态 Clearance Bake | 后台查询、NavMesh chunk、动态增量 Connectivity |
| S3 群体抵达 | Demo 完成 | 唯一槽位、Hungarian 分配、跨命令预留、两单位换槽、局部多单位重匹配、进入方向秩序、主动 Yielding、唯一 Overflow | 生产级 SlotDepth 场、完整碰撞优先级、编队形状保持 |
| S4 局部群体运动 | 原型完成 | SpatialHash、TTC、候选速度、避让侧记忆 | 更低成本的候选评估、复杂优先级、移动类型交互 |
| S5 碰撞与约束 | 运行时闭环完成 | 圆碰撞、动态占用、三档净空、四档建筑、Profile Resource、局部净空与全局 Connectivity Guard | 具名关键锚点、多移动层、非矩形 footprint |
| S6 高层路线与动态地图 | 大部分完成 | Portal A*、群组路线、动态 revision、局部失效、同命令批量共享改道、建筑移除恢复 | Sector、共享 corridor、Portal 自动生成、chunk 局部更新 |
| S7 狭口与卡死 | 大部分完成 | 车道、双向 admission、容量、排空、公平性、Hold 堵口、恢复阶梯 | 多连续狭口、复杂死锁、终点拥堵专用恢复 |
| S8 战斗移动 | Demo 闭环完成 | AttackMove、近战接触槽、远程攻击环、外圈 staging、Stop/Hold 索敌、多人重选敌、死亡/leash/路线恢复 | 后置的弹道、移动射击、动画事件、复杂目标权重和推挤优先级 |
| 操作层 | 第一层完成 | 每单位 Shift 队列、Control Group、SmartCommand、锁定攻击、命令反馈 | 命令日志/回放后再做双击选择、镜头定位、相机和 Minimap |
| S9 编辑器与数据烘焙 | Bake 闭环完成 | Navigation/Gameplay/Bake Resource、稳定哈希、CLI Generator/Validator、三档分量与 chunk `[Tool]` Preview | 增量 Baker、几何拖拽、热重载和放置差异面板 |
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
- 每次 Move 分配稳定 MovementGroupId 和组规模。
- 动态路径失效后先按 MovementGroupId 合并，再为同组受影响单位计算一次共享路线。
- 纯 C# Grid A* 作为动态占用 fallback。
- 目标槽位唯一分配。
- 不同命令波次之间共享槽位预留。
- 32 人以上编队在目标 140px 范围内周期性寻找严格降低总移动成本的两单位槽位交换。
- 换槽前验证双方直线路径和个体最大退化，并通过路径预算错峰生效。
- 48 人以上且仍有足够移动单位的编队，可低频选取最多 24 个局部单位做一次原子 Hungarian 重匹配，支持三单位以上循环换槽。
- 局部重匹配至少改变 3 个单位、必须降低总距离、限制个体退化，并只允许最多 4 个已就位阻挡者参与。
- 重匹配成本包含单位进入顺序与槽位深度的轻量秩匹配，不维护持续更新的全局 SlotDepth 场。
- 独立记录短期停滞与目标区累计驻留时间，不被局部换槽或通用重寻路错误清空。
- 外圈封闭或目标区驻留超过硬上限时，按同目标全部预留人口生成唯一 Overflow 槽位。
- Overflow 候选同时检查世界通行、所有槽位预留和所有单位当前占用。
- 内部单位受阻时，可把已就位阻挡者临时切换为 `MovingAside → Waiting → Returning`，让出通道后返回原槽位。
- 原槽位、临时让路点和返回目标同时参与预留冲突检测；让路有并发、频率、超时和冷却预算。
- 返回失败时把已验证的让路点提升为永久 Overflow，保证让路状态机有界结束。

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

### 3.7 导航数据资产

- `RtsNavigationMapResource` 表达世界边界、静态障碍、Portal、Edge 和 Choke。
- `Main.tscn` 通过 Inspector 导出属性绑定导航资产。
- 启动时转换为不依赖 Godot 的 `NavigationMapSnapshot`，模拟层不持有 Resource。
- 格式版本当前为 1；不支持的版本明确拒绝。
- Portal、Edge、Choke 和障碍使用固定数值错误码验证。
- 相同输入生成 byte-identical 的 388 字节规范数据。
- 当前 Demo 稳定哈希为 `B8441F9F1544B950`。
- 提供命令行 Validator 和 Demo Resource Generator。
- 主 Demo 不再从 `DemoMapDefinition` 创建运行时地图；该类只保留为纯 C# 测试与示例资源重建夹具。

### 3.8 AttackMove 战斗移动

- `CombatStore` 与移动 SoA 分离，追击路径不会覆盖原 AttackMove 终点。
- 6 Tick 错峰最近目标扫描，相同距离按稳定 unit ID 决定。
- 目标移动时有预算地更新追击路径；静止目标不重复寻路。
- 进入射程后停止、执行攻击前摇、命中、冷却与生命扣减。
- 目标死亡或越过 leash 后清理交战状态并恢复原路线。
- 死亡单位保持稳定 ID，但退出 SpatialHash、Steering、积分、碰撞、选择和建筑占用。
- Move 不索敌；Stop/Hold 明确取消 AttackMove 目标和恢复意图。
- 近战使用目标相对唯一接触槽；被目标中心阻挡时按“近侧外圈 → 分段绕行 → 最终槽位”就位。
- 远程使用射程内唯一攻击环；单位交叉后只接受严格降低两单位总误差的换槽。
- Stop 会局部索敌并追击；Hold 只攻击射程内目标且不移动。
- 多人目标死亡后稳定重选敌，清空敌人后恢复各自原 AttackMove 路线。
- 详细边界见 `docs/ATTACK_MOVE.md`。

### 3.9 操作层第一阶段

- 每单位固定 16 条待执行命令，不使用组级共享队列。
- 非 Shift 命令立即替换并清空队列；Stop/Hold 当前是终止命令。
- 同序列且同 Tick 到期的单位重新批量发令，继续复用群组槽位和共享路线。
- 队列满时拒绝新增并记录 overflow，不覆盖旧命令。
- Ctrl+数字覆盖 Control Group，Shift+数字添加，数字召回并过滤死亡单位。
- SmartCommand：地面/友军位置为 Move，敌军为 AttackTarget，A 修饰为 AttackMove。
- Godot 中 Shift 队列使用双圈反馈；普通命令使用单圈反馈。
- 详细边界见 `docs/OPERATION_LAYER.md`。

## 4. 测试、录像和性能

### 4.1 已有黑盒场景

当前 59 个场景覆盖：

- 单单位移动。
- 开放场和密集编队。
- 对向流与垂直交叉流。
- 命令替换和快速连续改令。
- 编队部分冻结、释放后的终点重新收敛。
- 48 个外圈槽位先就位后释放 32 个内部预留单位。
- 不同速度导致后排超车后的局部多单位重新匹配。
- 80 个混合半径单位在世界角落的终点收敛。
- Small 使用窄 Portal、Large 选择宽 Portal。
- 动态建筑 24px 缝隙只允许 Small 通过，Large 有界不可达。
- 32×32、64×48、112×80、160×120 四档建筑 footprint。
- 建筑放置边界、静态/动态重叠、单位占用、假窄缝和贴墙封闭规则。
- 建筑封闭唯一全局通道时返回 `DisconnectsNavigation`，安全放置后 8/8 单位继续通过。
- 24 单位绕行四种尺寸建筑组成的错位障碍场。
- Godot Gameplay Profile Resource 转纯 C# 快照并驱动 3 种单位和 4 种建筑。
- Godot Clearance Bake Resource 解析版本化三层拓扑，核对源哈希并驱动静态 Connectivity 与 chunk 预览。
- Godot 编辑器净空预览同时输出 3 档尺寸、5 条 Portal 和 4 档建筑，并由纯 C# 快照驱动。
- 跨命令共享目标槽位。
- Stop 与 Hold。
- 混合单位半径和越界目标。
- 动态建筑局部失效、绕行、移除恢复和 Portal 改道。
- 48 单位活动 Portal 完整失效后的群组共享改道。
- Godot Resource 转纯 C# 快照后驱动 Portal 和 Choke 运行时。
- 正向和反向单向狭口。
- 平衡双向流、非对称双向流和错峰波次。
- Hold 堵口与释放。
- 临时包围恢复。
- 永久不可达重试上限。
- 192 单位压力场景。
- AttackMove 接敌击杀后恢复原路线、leash 脱战恢复、Move/AttackMove 隔离和 Stop/Hold 取消。
- 8/8 近战唯一接触槽、10/10 远程攻击环、Stop/Hold 自动索敌语义，以及 8 对 4 连续重选敌。
- 三段 Shift 路点、即时替换、16 条队列上限、Control Group 覆盖/添加/召回和 SmartCommand 连续语义。
- 版本化命令日志规范 round-trip、固定 Tick 精确回放、非法版本/截断拒绝和首次状态分歧定位。
- Replay Package 资源身份、初始单位/建筑重建、动态建筑世界命令和初始/最终状态校验。
- 版本化 checkpoint、Package 绑定、中间 Tick 确定性 seek、后半段状态采样一致性和篡改拒绝。
- 状态 Hash v2 的动态占用 next ID、私有狭口租约状态，以及活跃双向交通 checkpoint。
- Tick 240 直接热快照，覆盖战斗、Shift 队列、路径游标、动态建筑、狭口和待处理请求，恢复不重演早期 Tick。
- 3,646 字节持久化热快照规范编码、byte-identical round-trip 和损坏载荷拒绝。

黑盒场景只使用以下稳定业务动作：

```text
Spawn
SpawnCombat
Move / AttackMove / AttackTarget / SmartCommand / Stop / Hold
Assign / Add / Recall ControlGroup
PlaceBuilding / RemoveBuilding
Step
Observe unit / combat / traffic / recovery / performance
```

底层实现更换时，优先只修改 MovementTestRig 适配器。

### 4.2 录像

- `tools/record_tests.ps1` 自动向运行时查询场景目录。
- 每个场景独立启动 Godot Movie Maker。
- 每段录像保存 AVI、Godot 日志和 manifest。
- 单项失败不会中止其他录像。
- 当前仓库包含覆盖 59 个逻辑场景的规范录像。
- AVI 使用 Git LFS。

注意：当前规范录像来自多个功能里程碑批次，并非全部在同一个 commit 上重新录制。发布正式版本前应执行一次全量重新录制，生成单一时间戳目录。

### 4.3 Release 基准

独立 `net9.0` Release 基准直接编译 Simulation 源文件：

| 单位数 | 平均 Tick | P95 | Hash 平均 | 当前门槛 | 分配/Tick |
|---:|---:|---:|---:|---:|---:|
| 256 | 1.28ms | 1.75ms | 0.888ms | 4ms | 27B |
| 512 | 3.68ms | 4.65ms | 1.469ms | 12.5ms | 182B |
| 1000 | 8.02ms | 10.62ms | 1.663ms | 16.67ms | 461B |

双方持续 AttackMove 的活跃战斗门槛：

| 总单位数 | 平均 Tick | P95 | Hash 平均 | 战斗阶段平均 | 当前门槛 | 分配/Tick |
|---:|---:|---:|---:|---:|---:|---:|
| 128 | 1.44ms | 1.86ms | 0.151ms | 0.51ms | 4ms | 2.3KB |
| 256 | 4.10ms | 5.49ms | 0.859ms | 1.97ms | 8ms | 4.0KB |

活跃追击会持续生成短路径，因此单独使用 8KB/Tick 分配门槛；非战斗移动仍保持 1KB/Tick 门槛。

当前热点排序：

1. Steering。
2. 动态圆碰撞。
3. 其他阶段明显较低。

这些数字是当前机器的本地基线，不代表所有硬件上的绝对保证。

## 5. 已知限制和技术债

### 5.1 终点收敛

32 人以上编队会交换局部错位槽位；内部单位被已就位单位挡住时，阻挡者可临时让路并返回；所有有限恢复仍失败后才分配唯一 Overflow。固定场景中，80 单位密集编队已达到 80/80；192 单位达到 192/192、最大误差 3.7px。

“外围先就位、内部槽位后到”的永久卡死已解决：专用场景外圈 48/48、内部 32/32，触发 8 次主动让路，最终 0 Moving、0 重叠、最大误差 7.2px。不同速度超车场景触发 1 次局部多单位重匹配、原子重排 10 人，最终 80/80；角落混合半径场景同样达到 80/80。

当前 Demo 的 A2 在这里收口。以下内容不再阻塞下一阶段，只作为未来手感打磨项：

- 完整的连续 SlotDepth / clearance 场，而不是当前局部进入秩序。
- 单位碰撞挤压业务优先级和战斗状态优先级。
- 进一步减少 Overflow、强化编队形状和终点专用 Steering。

### 5.2 群组路线共享不足

首次命令与动态路径失效都已支持群组路线共享。48 单位活动 Portal 关闭场景会一次失效全部 48 条路径，并把共享路线赋值从 48 次累计到 96 次；高层规划总数为 8，其中其余请求来自后续个体恢复，而不是失效瞬间的 48 次重复计算。

TODO：

- 共享 corridor 和分段缓存。
- 同组不可达结果共享。
- 跨相邻 Tick 的重复恢复请求合并。
- 显式 GroupRoute revision 与已通过 waypoint 游标。

### 5.3 地图数据管线仍缺自动烘焙和交互编辑

主 Demo 已从 `data/demo_navigation_map.tres` 加载地图，并要求加载源哈希匹配的 `data/demo_clearance_bake.tres`。Bake 保存三档 walkable 位图、component ID、16px cell 和 16×16-cell chunk；静态 Grid、放置基线和编辑器预览直接复用，动态 revision 安全回退 Analyzer。`DemoMapDefinition` 只作为纯 C# 测试夹具和示例资产重建源。

TODO：

- 从 NavMesh/场景几何自动提取 Sector 和 Portal。
- 受影响 chunk 增量重采样和跨 chunk component 边界图。
- EditorPlugin 中的 Portal/Choke 拖拽与连线。
- 孤岛列表和放置前后 connectivity 差异面板。
- 格式版本迁移器。
- Resource 热重载和差异诊断。

### 5.4 动态导航仍是混合方案

Godot NavMesh 处理静态地图；动态建筑通过占用栅格验证和按 Movement Class 缓存的 Grid A* fallback 处理。放置后的路径判定已经按单位导航半径工作。

TODO：

- NavMesh chunk 局部重烘焙。
- 非矩形和旋转 footprint。
- 建筑压住单位时的安全迁移。
- Portal/Sector 局部连通性更新。

### 5.5 单位尺寸与地形类型

当前已形成 Small/Medium/Large 三档运行时尺寸语义。Grid、Portal Edge、Godot 路径复验和动态占用统一使用向上取整的导航半径；物理碰撞和槽位仍使用真实半径。详见 `CLEARANCE_AND_MOVEMENT_CLASS.md`。

TODO：

- Gameplay Profile 热重载与差异诊断。
- 地面、悬浮、空中等移动层。
- 地形软代价和不可通行标签。

### 5.6 数据结构和算法热点

- SpatialHash 仍使用 Dictionary + List，不是 dense bucket/prefix sum。
- Steering 每个拥堵单位最多评估 12 个方向和 48 个邻居。
- Candidate 旋转仍会重复计算三角函数。
- DestinationSlotAllocator 使用 Hungarian，超大单次命令为 O(n³)。
- 1000 单位基准通过，但 Steering 仍占主要时间。

## 6. 未完成的大模块

### 6.1 S8 战斗移动

已完成：

- AttackMove 命令与路线恢复。
- 最近敌人错峰索敌、追击、leash 和脱战。
- 攻击前摇、冷却、即时伤害、目标死亡和移动约束。
- Move/AttackMove 隔离，Stop/Hold 显式取消交战。
- 近战唯一接触槽、外圈 staging 与局部严格降误差换槽。
- 远程射程内攻击环。
- Stop 局部索敌追击、Hold 射程内攻击但不追击。
- 目标死亡后的多人稳定重新选敌。
- 稳定 unit ID 死亡清理、八个业务黑盒场景/录像和 128/256 活跃战斗基准。

后置，不阻塞下一层：弹道、移动射击、复杂仇恨权重、友军/敌军推挤优先级和动画事件。

### 6.2 操作层

已完成：

- 每单位 Shift 命令队列、16 条硬上限和 overflow 诊断。
- Control Group 覆盖、添加、召回和死亡过滤。
- SmartCommandResolver 的地面、友军、敌军与 A 修饰语义。
- AttackTarget 与现有追击/占位/攻击系统接通。
- 单圈/双圈命令反馈和 HUD 队列计数。
- 五个业务黑盒场景、录像和全量回归。

后续 TODO：

- 双击选择同类单位。
- 选择优先级和 SelectionFilter。
- 资源和建筑右键语义。
- 相机边缘滚动、缩放和快速定位。
- Minimap 命令。
- 编组双击镜头定位、Alt 移出/窃取编组。

### 6.3 S9 编辑器和数据管线

已完成：

- Navigation 与 Gameplay Profile Resource 转纯 C# 快照。
- 稳定格式验证、规范字节、哈希、Validator 和 Generator。
- Clearance Bake 格式 1、38,215 字节规范载荷、源导航哈希和独立 Generator/Validator。
- 三档静态 topology 的 Bake 复用、动态 revision 回退和 5×3 chunk 描述。
- Small/Medium/Large 障碍净空轮廓、Portal 资格和四档建筑 footprint 的场景内 `[Tool]` 预览。
- 与 GridPathProvider 共用的三档全局 Connectivity Snapshot 和分量着色。
- 建筑放置前后分量比较与稳定 `DisconnectsNavigation` 业务结果。
- 与预览实现解耦的纯 C# 自测、Godot 黑盒用例和自动录像。

TODO：

- Gameplay Profile/Bake Resource 热重载与格式迁移。
- Chunk 增量 Occupancy/Clearance 更新。
- Sector/Portal Authoring Tool。
- 具名关键锚点策略、孤岛列表和放置前后差异面板。

### 6.4 确定性、回放和联机

已完成：

- 格式版本 1 的规范小端序命令日志、稳定 Hash 和严格错误拒绝。
- 在对应 Tick 的 `Step` 前注入外部解析命令；派生寻路、追击与队列出队由模拟自然重演。
- 覆盖未来模拟状态的精确 Hash、周期采样和首次分歧 Tick。
- 相同初始状态与 7 条命令重复执行至 840 Tick，最终状态 Hash 完全一致。
- 修改一条目标命令后在 Tick 360 定位首次采样分歧。
- 版本化 Replay Package、资源身份、初始单位/建筑清单和初始状态 Hash。
- 动态建筑放置/移除世界命令；同 Tick 固定先世界、后单位命令。
- 版本化 checkpoint、Package/状态 Hash 绑定和中间 Tick 确定性 seek。
- 状态 Hash v2 补齐动态建筑 next ID 和狭口私有未来态。
- 进程内热快照直接恢复，无需从 Tick 0 重演。
- 版本化规范热快照载荷，可由上层保存到磁盘并跨进程重新加载。
- 59/59 黑盒回归、六段确定性/回放录像和独立 Hash 性能门槛。

TODO：

- 当前 Demo 范围内无剩余确定性阻塞项；新玩法状态按需扩展格式。
- 服务端权威、Lockstep 或混合方案决策。
- 客户端表现插值与模拟状态分离。

## 7. 推荐后续顺序

### 下一步 A：终点局部重排和 GroupRoute 共享（核心完成）

已完成：

- 大编队目标附近的安全两单位换槽。
- MovementGroupId、组规模和动态失效批处理。
- 同组受影响单位共享一次替代高层路线。
- 两条独立黑盒场景、录像和 HUD 诊断计数。

尚未完全关闭：

- 复杂干扰后的尾部 5%～10% 单位仍可能形成物理局部平衡。
- 动态绕障后的终点专用恢复还未独立于通用卡死恢复。
- 共享的是 waypoint 数组，尚未形成带游标和 revision 的完整 corridor 对象。

### 下一步 B：运行时导航数据格式和 Godot Resource（核心完成）

已完成：

- Resource 可以表达当前 Demo 地图全部数据。
- `Main.tscn` 可以在 Inspector 中替换地图 Resource。
- 启动时转换为不依赖 Godot 的不可变快照。
- 错误 Portal、Edge、Choke 和 obstacle 得到固定数值错误码。
- 同一输入生成 byte-identical 规范数据和稳定哈希。
- 有独立验证脚本、生成脚本、黑盒场景和录像。

后续 S9 继续做增量 Baker、编辑器几何工具和格式迁移；静态 Bake/Preview 闭环已经完成。

### A2：终点协作收敛 V2（当前 Demo 完成）

目标问题：外围单位先占位后形成封闭圈，部分单位持有内部槽位但无法穿过已到达单位。

已完成：

- 独立短期停滞和目标区总驻留计时。
- 同组及跨命令已就位阻挡识别。
- 按同目标总人口生成不重叠的唯一 Overflow 槽位。
- 通用重寻路和局部换槽不会丢失 V2 历史。
- 已就位阻挡者可临时让路、等待受助单位通过，再返回原槽位。
- 原槽位、让路点和返回目标均参与预留；状态机有并发上限、超时、冷却和确定性选择。
- 返回不能在期限内完成时，把安全让路点提升为 Overflow，避免产生新的永久卡死。
- 最多 24 人的低频局部原子重匹配，支持三单位以上循环换槽。
- 局部重匹配包含进入方向/槽位深度秩序、总成本下降和个体退化边界。
- 已到达单位不会被大规模重新拉走；最多 4 个已就位局部阻挡者可参与。
- 外圈封闭、速度超车和角落混合半径黑盒场景与最终录像。

性能约束：

- 多单位重匹配只处理 48 人以上、目标附近、仍有足够移动单位且持续无进展的编队。
- 使用低频检查和每 Tick 操作预算，不做持续全局 Hungarian。
- Unit ID 作为所有相同成本的确定性 tie-break。

验收：

- 专用外圈封闭场景不存在永久卡在内部槽位外的单位。（已通过）
- Overflow 槽位不与任何已有预留或单位占用重叠。
- 普通密集编队结果不退化。（80/80）
- Yielding 状态必须全部有界结束，测试结束时 `activeYield=0`。（已通过）
- 速度超车和角落混合半径场景均达到 80/80。（已通过）
- 1000 单位 P95 继续低于 16.67ms，分配低于 1KB/Tick。（10.62ms / 461B）

明确收口边界：当前不会继续加入完整挤压优先级、全局 SlotDepth 场或为了减少少量 Overflow 而反复调参；这些必须由后续实际玩法需求重新驱动。

### 下一步 C：Clearance 与 Movement Class（运行时与放置闭环完成）

已完成：

- Small、Medium、Large 运行时等级与导航半径。
- 大小单位按声明宽度选择不同 Portal。
- Grid 三档连通性缓存和动态 revision 失效。
- Godot NavMesh 候选路径按单位净空复验并回退对应 Grid。
- 动态建筑窄缝对 Small/Large 给出不同且有界的结果。
- Small/Medium/Large/Huge 四档建筑 footprint。
- 业务放置前检查边界、重叠、单位占用和局部假窄缝，并返回稳定结果码。
- 多尺寸建筑群的实际绕行闭环。
- 编辑器中三档障碍净空轮廓、Portal 宽度/资格和四档建筑 footprint 预览。
- 纯 C# 预览快照、自测、Godot 黑盒验收和 10 秒规范录像。
- 统一 `NavigationConnectivityAnalyzer`，由 Grid 寻路、业务放置和编辑器预览共同复用。
- 放置封闭原有分量时返回 `DisconnectsNavigation`；安全建筑仍能放置。
- 全局连通保护黑盒场景、通用录像诊断区域和 20 秒规范录像。
- 版本化 Clearance Bake Resource、稳定源哈希/Bake 哈希和 byte-identical 序列化。
- 静态 Grid/放置/Preview 复用 Bake；动态 revision 自动回退 Analyzer。
- 16×16-cell chunk API、区域影响查询、5×3 编辑器 chunk 预览和规范录像。

下一层：chunk 增量 Connectivity 与边界 component graph、Resource 热重载，以及 Portal/Sector 交互编辑。

### 下一步 D：AttackMove 最小闭环（已完成）

- 接敌、追击、攻击、目标死亡、leash 与恢复原路线已经闭环。
- Stop、Hold、Move、AttackMove 当前语义互不污染。
- 四个业务黑盒测试和规范录像全部通过。
- 攻击槽当时拆到 D2 实现，最小闭环没有用临时代码假占位。

### 下一步 D2：战斗占位与索敌语义（已完成）

- 近战 8/8 进入唯一接触槽；远程 10/10 进入唯一攻击环。
- Stop 局部追击，Hold 原地只攻击射程内目标。
- 8 个攻击者连续消灭 4 个目标后 8/8 恢复原路线。
- 48/48 全量黑盒场景通过，八段当前战斗录像保存。
- 战斗移动到此收口，不继续扩张伤害与表现系统。

### 下一步 E：操作层第一阶段（已完成）

- Shift 队列、Control Group、SmartCommand 和 AttackTarget 已闭环。
- 53/53 当时的黑盒场景通过，五段操作层录像保存。

### 下一步 E2：确定性命令日志与回放（已完成）

- 格式版本 1、规范字节、稳定日志 Hash 和严格载荷验证已经完成。
- 固定 Tick 回放、周期状态 Hash、首次分歧 Tick 和重复执行一致性已经完成。
- 55/55 当时的黑盒场景通过，两段确定性录像保存。
- 详细格式、覆盖边界和指标见 `docs/COMMAND_REPLAY.md`。

### 下一步 E3：Replay Package 与动态世界命令（已完成）

- 包含地图/Profile/Bake 格式版本和稳定 Hash，资源不匹配时拒绝播放。
- 包含模拟容量、初始状态 Hash、完整单位配置和初始建筑清单。
- 建筑放置/移除作为版本化世界命令重放；同 Tick 的固定执行顺序已定义。
- 690 字节验收包独立重建到 Tick 720 后状态 Hash 精确一致。
- 56/56 黑盒场景通过，规范录像已保存。

### E4.1：版本化 Checkpoint 与中间 Tick 恢复（已完成）

- 36 字节 checkpoint 绑定格式版本、状态 Hash 版本、Tick、Package Hash 和状态 Hash。
- 从 Tick 0 seek 至 Tick 240 后校验状态，并继续消费同一个 Replay Package Runner。
- 恢复后的 17 个后半段周期采样与连续运行完全一致。
- 未知版本、截断载荷和被篡改状态均明确拒绝。
- 57/57 黑盒场景通过，规范录像已保存。

### E4.2：直接运行时热快照（已完成）

- 状态 Hash v2 已补齐动态建筑 next ID 和狭口私有未来态。
- 深拷贝 Unit/Combat SoA、路径/路线游标、命令队列、动态占用、狭口租约和 Rts 私有请求队列。
- 从新模拟实例直接写回 Tick 240 状态，并按 Tick 重建 Replay Package 游标；不执行前 240 Tick。
- 快照时包含 AttackMove 接敌、未消费 Shift 命令、活动路径和动态建筑。
- 恢复后的 17 个后半段采样与连续运行完全一致，错误 Package 绑定被拒绝。
- 59/59 黑盒场景通过，规范录像已保存。

### E4.3：持久化热快照格式（已完成）

- 3,646 字节规范小端序载荷，绑定 Package、Tick、状态 Hash 版本和模拟容量。
- 按逻辑顺序保存 Shift 队列，不绑定环形缓冲物理布局。
- 完整 Unit/Combat/World/Choke/Private 状态 round-trip 后直接恢复。
- 未知版本、截断、尾部数据、结构非法、Package 不匹配和正文篡改均拒绝。
- 模拟层提供规范字节，上层可以直接保存到磁盘，不在核心层绑定文件路径。

### 下一步 F：操作表现第二阶段

确定性基础设施到此收口。下一步实现双击选择同类单位、SelectionFilter、相机边缘滚动/缩放、编组双击镜头定位及对应稳定业务测试；Minimap 命令随后接入。不会继续扩张联机协议或快照格式细节。

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
