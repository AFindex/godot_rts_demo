# Godot RTS 移动系统：进度回顾与剩余 TODO

更新日期：2026-07-13

这份文档是当前唯一的实施状态入口。它对照最初的 S0～S10 技术路线，区分已经形成可运行闭环的部分、只有原型的部分和尚未开始的部分。

## 1. 当前结论

项目已经具备一个可运行、可测试、可录像、可测性能的纯 C# RTS 移动内核：

- Godot 4.7 .NET 负责输入、绘制、NavMesh 查询和调试表现。
- 固定 Tick 模拟、单位数据、群组目标、Steering、碰撞、动态建筑、Portal 和狭口交通位于纯 C# 层。
- 118 个黑盒业务场景通过稳定测试接口驱动，不直接读取路径点、Steering、UnitStore、CombatStore、EconomySystem、ConstructionSystem、ProductionSystem、TechnologySystem 或队列内部数组。
- 测试自动录制后转为经过逐帧验证的 AV1/WebM，并通过 Git LFS 保存在仓库中。
- 独立纯 C# Release 基准覆盖 256、512、1000 单位移动，以及 128/256 总单位持续 AttackMove 与高密度飞行投射物。

当前规模：

- 152 个 C# 源文件。
- 约 49,466 行 C#（按当前 `src/**/*.cs` 统计）。
- 118 个黑盒场景。
- AV1/WebM 采用固定 AV1/WebM 规范保存；当前里程碑新增场景有独立录像。
- Release 1000 单位移动 P95：约 10.07ms。
- Release 1000 单位当前线程分配：约 685B/Tick。

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
| S8 战斗移动 | E5a 接触优先级完成 | AttackMove、占位、统一伤害、确定性投射物/表现、移动射击、稳定选敌、五类派生接触角色 | 仅在宏观战线失败后开启 E5b |
| 操作层 | J2b2b 完成 | Shift 跨域任务、混合选择/编组、快照命令卡、Move/AttackMove/Rally/Build 目标模式、同类型多建筑生产/取消/队列聚合、SmartCommand、相机、Minimap | 图标、tooltip、热键重映射和最终皮肤 |
| S9 编辑器与数据烘焙 | 数据工作流闭环完成 | dirty chunks、Fresh Load、原子差异、文件监听/去抖/有限重试、Bake-only 自动提交、三档放置差异面板 | 按需的几何 Authoring Tool、边界 component graph |
| S10 性能与诊断 | 基础完成 | Phase timing、GC、黑盒测试、录像、Release benchmark、门槛 | 更全面场景、结构化 capture、热点优化、CI 门禁 |
| S11 实际 RTS 玩法 | H3/J2b2b + S8-E5a + SC2-D2a 工程闭环完成 | 双资源、建筑/生产/科技、扩张、视野/胜负、主动潜地、双 AI 持续遭遇战、完整操作与战斗、Package v29/Hot v28/Hash v29 | 后续能力由遭遇战关卡暴露的缺口驱动 |

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

当前 78 个场景覆盖：

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
- 单次动态 revision 的 dirty-chunk 重采样、GridPathProvider 运行时快路径、建筑加入/移除与全量拓扑一致性。
- 生成式 Navigation/Profile/Bake 变体、Godot Fresh Load、原子资源集拒绝、逐类型差异和重建影响等级。
- Bake-only 两阶段提交、Grid/放置守卫同时换代、活动编队重规划、录制期/错误哈希/不支持 Provider 拒绝。
- Resource 文件事件合批、半写文件有限重试、完整资源集 Fresh Load、Bake-only 自动提交，以及 Navigation/Profile 的 `RebuildSimulation` 边界。
- Minerals/Vespene/Supply 原子交易、失败不部分扣款、退款、矿脉互斥、Refinery 三工人采集、携带返还、节点枯竭转场和命令取消工作。
- 候选 footprint 对 Small/Medium/Large 的放置前后 Connectivity 差异、三档安全/断路判定、dirty chunks 和独立诊断面板。
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
- 同类型双击选择、友军 SelectionFilter、边缘/键盘平移、光标锚定缩放和编组双击定位。
- Minimap 世界/面板坐标往返、视口框、镜头定位、SmartCommand 意图和边界外拒绝。

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
- Godot AVI 只作为临时采集文件；成功后由固定版本 FFmpeg 编码为 AV1/WebM。
- 每段编码后校验 AV1 codec、分辨率和逐帧数量，再原子替换并删除临时 AVI。
- 每段录像保存 WebM、Godot 日志和包含 codec/CRF/preset 的 manifest。
- 单项失败不会中止其他录像。
- 当前仓库包含覆盖 78 个逻辑场景的规范录像。
- WebM 使用 Git LFS；FFmpeg 下载到忽略的 `tools/.cache/`，不提交第三方二进制。
- 85 段历史 AVI 已从 3,309,160,498 字节降到 228,515,601 字节，保留 6.91%。

注意：当前规范录像来自多个功能里程碑批次，并非全部在同一个 commit 上重新录制。发布正式版本前应执行一次全量重新录制，生成单一时间戳目录。

### 4.3 Release 基准

独立 `net9.0` Release 基准直接编译 Simulation 源文件：

| 单位数 | 平均 Tick | P95 | Hash 平均 | 当前门槛 | 分配/Tick |
|---:|---:|---:|---:|---:|---:|
| 256 | 0.89ms | 1.12ms | 0.860ms | 4ms | 27B |
| 512 | 3.60ms | 4.49ms | 1.205ms | 12.5ms | 182B |
| 1000 | 6.98ms | 8.50ms | 1.321ms | 16.67ms | 461B |

双方持续 AttackMove 的活跃战斗门槛：

| 总单位数 | 平均 Tick | P95 | Hash 平均 | 战斗阶段平均 | 当前门槛 | 分配/Tick |
|---:|---:|---:|---:|---:|---:|---:|
| 128 | 1.42ms | 2.00ms | 0.152ms | 0.53ms | 4ms | 2.3KB |
| 256 | 4.21ms | 7.13ms | 1.581ms | 2.06ms | 8ms | 4.0KB |

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

主 Demo 已从 `data/demo_navigation_map.tres` 加载地图，并要求加载源哈希匹配的 `data/demo_clearance_bake.tres`。Bake 保存三档 walkable 位图、component ID、16px cell 和 16×16-cell chunk；静态 Grid、放置基线和编辑器预览直接复用。相邻单次动态 revision 只重采样 dirty chunks，多次累计变更安全回退 Analyzer。`DemoMapDefinition` 只作为纯 C# 测试夹具和示例资产重建源。

TODO：

- 从 NavMesh/场景几何自动提取 Sector 和 Portal。
- 跨 chunk component 边界图，避免局部重采样后仍全图标号。
- EditorPlugin 中的 Portal/Choke 拖拽与连线。
- 可交互孤岛列表；放置前后 connectivity 差异面板已经完成。
- 真正出现格式 v2 后的显式 v1→v2 迁移器；当前未知版本稳定拒绝并由 Generator 规范重建。
- Resource 文件监听、Bake-only 自动提交、去抖和有限重试已经完成。

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

- Gameplay Profile 变化后的显式模拟重建和允许状态迁移清单。
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
- 稳定 unit ID 死亡清理、九个业务黑盒场景/录像和 128/256 活跃战斗基准。
- 固定容量 `CombatEventStream` 按 Tick/Sequence 发布单位与建筑共用的 AttackStarted、Impact、TargetDestroyed；溢出显式报告，热恢复清空派生事件。

单位与建筑真实伤害、护甲、属性加成、多段攻击以及攻防科技已在 S8-E2a/E2b 完成。后续进入确定性弹道；移动射击、复杂仇恨权重和友军/敌军推挤优先级继续由实际玩法决定。

### 6.2 操作层

已完成：

- 每单位 Shift 命令队列、16 条硬上限和 overflow 诊断。
- Control Group 支持单位/建筑混编、覆盖、添加、Alt/Alt+Shift 抢组、召回及死亡/销毁过滤。
- SmartCommandResolver 的地面、友军、敌军与 A 修饰语义。
- AttackTarget 与现有追击/占位/攻击系统接通。
- 单圈/双圈命令反馈和 HUD 队列计数。
- Worker/CombatUnit/Building 混合选择子组、Tab 切换和快照命令卡。
- Stop/Hold、Train/Research、取消生产/研究/施工的正式命令卡绑定。
- 十二个操作层业务黑盒场景、录像和全量回归。

后续 TODO：

- tooltip、图标、可重映射热键与最终 UI 皮肤；均属于表现增强，不阻塞玩法闭环。

### 6.3 S9 编辑器和数据管线

已完成：

- Navigation 与 Gameplay Profile Resource 转纯 C# 快照。
- 稳定格式验证、规范字节、哈希、Validator 和 Generator。
- Clearance Bake 格式 1、38,215 字节规范载荷、源导航哈希和独立 Generator/Validator。
- 三档静态 topology 的 Bake 复用、动态 revision 回退和 5×3 chunk 描述。
- footprint 按导航半径膨胀后的 dirty-chunk 规划与局部 walkability 重采样。
- `GridPathProvider` 相邻单 revision 增量快路径；多 revision、恢复和缺失差异时安全全量回退。
- 局部重采样后全图确定性 component 重标号，加入/移除均与全量分析严格一致。
- `[Tool]` 预览橙色高亮受影响 chunks，表现层只消费纯 C# 快照。
- Godot `CacheMode.Replace` Fresh Load 和 Navigation/Profile/Bake 原子资源集校验。
- 障碍、Portal/Edge/Choke、单位/建筑 Profile 与 Bake 的稳定差异报告。
- `None / RefreshPathingCaches / RebuildSimulation` 影响等级；不允许逐资源半更新。
- 生成的独立变体 `.tres`、解耦诊断 Control、黑盒测试和 AV1/WebM 录像。
- Bake-only 两阶段 Validate/Commit，同时替换 Grid Snapshot 与放置 Connectivity 基线。
- 活动单位按 MovementGroup 批量重规划；错误哈希、布局、录制中和不支持 Provider 均稳定拒绝。
- Replay Package 新录制必须绑定当前活动 Bake Hash。
- Small/Medium/Large 障碍净空轮廓、Portal 资格和四档建筑 footprint 的场景内 `[Tool]` 预览。
- 与 GridPathProvider 共用的三档全局 Connectivity Snapshot 和分量着色。
- 建筑放置前后分量比较与稳定 `DisconnectsNavigation` 业务结果。
- 纯 C# `BuildingConnectivityDiffSnapshot` 同时比较三档 before/after 拓扑，输出 blocked/split/disconnected 与 dirty chunks。
- 独立 Godot `RtsBuildingConnectivityDiffControl` 只消费差异快照；候选 footprint 与面板表现不进入业务判定。
- 与预览实现解耦的纯 C# 自测、Godot 黑盒用例和自动录像。
- `FileSystemWatcher` 线程只入队；Godot 主线程执行完整 Fresh Load、原子差异和提交。
- 纯 C# 250ms 去抖/重试状态机：合并 Changed/Created/Renamed 写入风暴，半写文件最多重试 5 次，新事件开启新 generation。
- Bake-only 候选自动两阶段提交；Navigation/Profile 候选只发布 `RebuildRequired`，不修改运行中 SoA。
- 独立工作流状态快照和 Godot Control；2 次 Bake 通知只提交 1 次，8 个单位全部重规划并到达。

TODO：

- 跨 chunk component 边界图和批量 revision 变更区域合并。
- Sector/Portal Authoring Tool。
- 具名关键锚点策略和可交互孤岛列表。

格式策略：当前三类资源均为格式 1；未知版本稳定拒绝，Generator 是规范重建入口。只有真实格式 2 规范出现后才增加显式迁移器，不维护猜测式兼容代码。

### 6.4 S11 实际 RTS 玩法

已完成第一层：

- `PlayerEconomyStore` 保存 Minerals、Vespene、Supply Used/Capacity。
- 双资源和人口一次性校验、原子扣费、失败不变、退款和人口释放。
- 资源节点保存类型、位置、余量、携带批量、采集时间和并发容量。
- 矿脉单工人采集；Vespene 需要 Refinery，启用后容量为 3。
- 工人执行前往、等待、采集、携带、返还和自动再次采集。
- 节点枯竭后自动选择最近的同类节点；错误所有权和缺少 DropOff 稳定拒绝。
- Move/Attack/Stop/Hold 等普通命令取消工作，避免后台采集抢回单位控制权。
- 状态 Hash v3 覆盖账本、节点、DropOff、工人阶段、目标、携带量和计时。
- 无经济工人的旧场景走快速路径；解耦 Economy Overview/Control 提供录像表现。

S11-B 已完成：

- 独立经济命令日志记录 Gather 与 Refinery 外部意图，不记录派生返还/移动。
- Replay Package v2 保存初始玩家、节点、DropOff 和工人清单。
- 热快照 v2 保存活跃采集状态，Checkpoint 可精确重演经济命令。
- 900 Tick 连续运行、Tick 300 checkpoint 与 Tick 240 直接热恢复最终状态一致。

S11-C～H 和 I1 均已完成；详细逐段状态见本文第 7 节。下一项只接受明确玩法缺口，不再沿旧列表重复实现。

详细路线见 `docs/ECONOMY_AND_PRODUCTION.md`。

### 6.5 确定性、回放和联机

已完成：

- 格式版本 1 的规范小端序命令日志、稳定 Hash 和严格错误拒绝。
- 在对应 Tick 的 `Step` 前注入外部解析命令；派生寻路、追击与队列出队由模拟自然重演。
- 覆盖未来模拟状态的精确 Hash、周期采样和首次分歧 Tick。
- 相同初始状态与 7 条命令重复执行至 840 Tick，最终状态 Hash 完全一致。
- 修改一条目标命令后在 Tick 360 定位首次采样分歧。
- 版本化 Replay Package、资源身份、初始单位/建筑清单和初始状态 Hash。
- 动态建筑放置/移除世界命令；同 Tick 固定执行世界、经济、单位命令。
- 版本化 checkpoint、Package/状态 Hash 绑定和中间 Tick 确定性 seek。
- 状态 Hash v3 在 v2 动态建筑/狭口未来态基础上加入经济状态。
- 进程内热快照直接恢复，无需从 Tick 0 重演。
- 版本化规范热快照载荷，可由上层保存到磁盘并跨进程重新加载。
- 68/68 黑盒回归、经济确定性专用录像和独立 Hash 性能门槛。

TODO：

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

S9 数据工作流已经闭环。编辑器几何工具与跨 chunk component graph 改为由实际地图生产和性能数据驱动，不再作为当前阶段尾项。

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
- 1000 单位 P95 继续低于 16.67ms，分配低于 1KB/Tick。（8.50ms / 461B）

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
- 静态 Grid/放置/Preview 复用 Bake；单次动态 revision 增量更新，多次累计变更自动回退 Analyzer。
- 16×16-cell chunk API、区域影响查询、5×3 编辑器 chunk 预览和规范录像。

已完成 dirty-chunk walkability、Resource Fresh Load/原子差异、文件监听/去抖/有限重试和 Bake-only 自动安全提交。边界 component graph 与 Portal/Sector 交互编辑改为按需项。

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

### F：操作表现第二阶段（已完成）

- `SelectionFilter` 稳定点选/框选，过滤死亡和敌方单位。
- 双击选择当前视野内相同 TypeId 友军，类型由上层 Profile/实体数据提供。
- CameraController 支持边缘滚动、方向键、世界边界和光标锚定缩放。
- 编组 0.35 秒双击召回时定位存活成员中心。
- Godot HUD 移至 CanvasLayer，选择框和命令坐标全部完成屏幕/世界转换。
- 60/60 当时的黑盒场景通过，规范录像已保存。

### F2：解耦 Minimap（已完成）

- 纯 C# `MinimapSnapshot/Transform/InteractionResolver` 不依赖 Godot 和模拟内部结构。
- 独立 `RtsMinimapControl` 只绘制快照、发出世界坐标意图，不读取模拟、不发业务命令。
- `RtsDemo` 薄绑定复用现有相机控制器、选中集与 SmartCommand，不建立第二套语义。
- 显示障碍、单位、选择状态、不同 footprint 建筑和视口框；支持左键/拖动定位及右键命令。
- 61/61 黑盒场景通过，专用真实 UI 录像已保存。

### G：S9 dirty-chunk 增量 Connectivity（第一层完成）

- 单次建筑放置/移除只重采样导航半径影响到的 bake chunks。
- 示例 dirty chunks 为 `1,6`，重采样 512/3,080 cells（16.6%）。
- 局部 walkability 更新后全图稳定重标号，加入与移除结果逐 cell、逐 component ID 等于全量分析。
- `GridPathProvider` 已实际接入；连续多 revision 或快照恢复时明确回退全量分析。
- 编辑器预览以橙色覆盖相同 dirty chunks，不读取更新器内部结构。
- 62/62 黑盒场景、AV1/WebM 录像和全仓录像门禁通过。

### H：S9 Resource Fresh Load 与原子差异（第一层完成）

- `RuntimeResourceSetSnapshot` 要求候选 Bake 源哈希匹配候选 Navigation，全集通过才可接纳。
- `RuntimeResourceReloadPlan` 输出逐资源哈希、元素变更数量和应用影响等级。
- Navigation/Profile 改变为 `RebuildSimulation`；同源 Bake-only 改变为 `RefreshPathingCaches`。
- Godot Loader 使用 `CacheMode.Replace`，但不持有或部分修改模拟。
- 三个变体 `.tres` 由正式 Converter/ResourceSaver 生成，和正式 `data/` 隔离。
- 独立诊断 Control 只消费 Reload Plan；63/63 黑盒场景和 AV1/WebM 录像门禁通过。

### H2：Bake-only 两阶段安全提交（已完成）

- Grid/Fallback 与 BuildingConnectivityGuard 在任何写入前分别验证候选 Bake。
- 验证通过后同步换代并清空三档缓存；Commit 阶段不再执行可能失败的工作。
- 8 个移动单位按原编组全部重规划并 8/8 到达，reload 计数为 1。
- 错误 Navigation Hash 候选被拒绝，旧活动 Bake 和 reload 计数保持不变。
- 命令/Replay 录制期间与不支持 reload 的 Provider 明确拒绝。
- 64/64 当时的黑盒场景、Release 门槛和 AV1/WebM 全仓门禁通过。

### I：S9 放置前后 Connectivity 差异面板（已完成）

- 纯 C# 快照对同一候选 footprint 一次输出 Small/Medium/Large 三档 before/after 组件数、blocked cells、split components、disconnected cells 与受影响 chunks。
- 匹配静态 Bake 时复用其基线；候选态使用正式 `NavigationConnectivityAnalyzer`，不复制编辑器专用拓扑规则。
- 专用走廊夹具中阻断候选三档全部拒绝，安全候选三档全部保持连通；总计报告 1,902 个 disconnected cells，dirty chunks 为 2/1。
- Godot Control 只消费不可变快照，UI 可独立换皮或重排，不依赖模拟、GridPathProvider 或 Guard 私有状态。
- `building-connectivity-diff-preview` 黑盒场景与 AV1/WebM 录像已保存；当时 65/65 全量回归和 Release 性能门禁通过。

### J：S9 文件监听与自动安全提交（已完成）

- 文件系统线程只向并发队列写入资源类型；Godot API、Fresh Load、Converter 和模拟提交全部在主线程执行。
- 250ms 安静窗口合并一次编辑器保存产生的多事件；半写或暂时不匹配的资源集按 250ms 最多重试 5 次，不会无限循环。
- 候选始终按 Navigation/Profile/Bake 完整资源集加载和校验；不会逐文件修改活动运行态。
- `None` 更新监听基线，Bake-only 自动执行现有两阶段提交，Navigation/Profile 变化停在 `RebuildRequired`。
- 专用生成资产保持 Navigation/Profile 不变，只改变 Bake chunk 布局；2 次文件通知只产生 1 次提交，8 个活动单位全部重规划并到达。
- `resource-file-watch-workflow` AV1/WebM 录像已保存；66/66 全量回归、Release 性能门槛和 91 段全仓 AV1 门禁通过。

### K：S11-A 双资源经济与工人循环（已完成）

- Minerals/Vespene/Supply 使用同一原子账本；资源不足和人口阻塞不产生部分扣费。
- 三个矿脉与一个 Refinery 驱动 8 个实际移动工人；矿脉容量 1，气矿容量 3。
- 第一矿脉耗尽后等待工人自动转场；20 秒获得 105 Minerals 和 48 Vespene。
- 未建 Refinery、错误玩家、缺少 DropOff、枯竭节点和普通命令取消均有稳定语义。
- 状态 Hash 升级为 v3；经济 UI 和世界节点表现只消费不可变快照。
- `economy-dual-resource` AV1/WebM 录像已保存；67/67 全量回归、Release 性能和 92 段全仓 AV1 门禁通过。

### L：S11-B 经济回放与持久化（已完成）

- 新增独立 `EconomyCommandLogSnapshot` 格式 1；成功 Gather 与 Refinery operational 变化按 Tick 记录，载荷有稳定 Hash 和严格验证。
- 采集派生的 Move/Stop、Return Cargo、自动再采集不进入玩家命令日志；`economy-replay-persistence` 明确验证 7 条经济意图对应 0 条重复单位移动。
- Replay Package 升级为格式 2，保存初始玩家账本、资源节点、DropOff、工人注册关系和初始工作状态。
- 回放执行顺序固定为世界命令 → 经济命令 → 单位命令 → Tick，避免同 Tick 跨域顺序漂移。
- 持久化热快照升级为格式 2，完整保存节点占用和每个工人的阶段、目标、货物与采集计时；采集计时完成后归零，维持可序列化状态不变量。
- Tick 0 完整回放、Tick 300 checkpoint 续跑、Tick 240 活跃采集直接恢复到 Tick 900 的状态 Hash 完全一致。
- 未知版本和截断的经济日志、Replay Package 与热快照均被拒绝；68/68 全量黑盒回归通过。
- 专用 AV1/WebM 录像位于 `test_videos/20260711_163809/`；索引现有 92 段录像、覆盖 68 个逻辑场景。

### M：S11-C1 正式玩法建筑与施工生命周期（已完成）

- 新增独立 `ConstructionSystem`；正式建筑实体持有稳定 ID、玩家、类型、Bounds、导航 Footprint、施工者、阶段、进度、生命和可选 Refinery 节点。
- Building Type Profile 声明双资源成本、建造时间、最大生命、人口、退款率、施工策略与功能，不把种族差异写死在系统中。
- Supply Depot、Barracks、Command Center、Refinery 分别使用 48×48、112×80、160×120、72×72，覆盖实际业务中的多尺寸建筑。
- 工人需要抵达外缘施工位；Continuous Worker 被 Move/Gather 打断后进入 WaitingForBuilder，可由合法工人续建。StartAndRelease 策略已有独立语义。
- 建造事务先完成所有权、资源和放置预检，提交后原子扣费；取消返还 75%，建筑受伤可摧毁，人口效果和 Refinery operational 状态随生命周期正确加入/撤销。
- 同一工人不能并行施工；错误所有权、资源不足、缺失/重复气矿绑定和放置失败返回稳定结果。
- 正式建筑拥有不可变观察快照；Godot 绘制类型配色、名称与进度条，资源点和导航 Footprint 不再充当建筑表现。
- 直接动态 Footprint 删除不能绕过正式建筑生命周期，避免“占用消失但实体继续施工”。
- 状态 Hash 升级为 v4，包含建筑身份、类型、所有权、阶段、进度、生命与 Refinery 绑定。
- `construction-gameplay-buildings` 960 Tick 验收四建筑完工、取消退款、暂停续建、生命伤害、人口 6/38、资源 250/300 和 Refinery 启用；69/69 全量回归通过。
- 专用 AV1/WebM 录像位于 `test_videos/20260711_170403/`；索引现有 93 段录像、覆盖 69 个逻辑场景。

### N：S11-C2 建造回放与活跃施工持久化（已完成）

- 新增独立 Construction Command Log 格式 1，记录成功的 Build/Cancel/Resume；未知版本、截断、非法 Profile、非法 Tick 顺序稳定拒绝。
- Build 记录解析后的完整 Building Type Profile，历史录像不依赖回放时的当前资产参数。
- Replay Package 升级为格式 3，初始清单包含正式建筑状态，命令区增加 Construction Log。
- 同 Tick 固定顺序为 World → Construction → Economy → Unit → Tick。
- 施工派生的 Place/Remove Footprint 与 Move/Stop 不进入 World/Unit Log；验收明确为 7 条建造命令、0 条派生 World Command、1 条真实玩家 Move。
- 热快照升级为格式 3，保存施工接近点、施工者、状态、进度、生命、完整类型和 Refinery 绑定，并验证实体与 Footprint、资源节点的一致性。
- Tick 0 全量回放、Tick 360 checkpoint、Tick 300 活跃施工直接恢复到 Tick 900 的周期采样和最终 Hash 完全一致。
- `construction-replay-persistence` 最终资源为 750/300、人口上限 38，热快照 2,751 字节；70/70 全量回归通过。
- 专用 AV1/WebM 录像位于 `test_videos/20260711_173933/`；索引现有 94 段录像、覆盖 70 个逻辑场景。

### O：S11-C3 建筑战斗目标与选择观察（战斗层已完成）

- CombatStore 新增显式 `CombatTargetKind`、TargetUnits 和 TargetBuildings；没有使用负数 ID 或复用 Unit ID 空间。
- UnitOrder 新增 AttackBuilding，活动/待执行队列均保存 TargetBuilding；Unit Command Log 升级为格式 2。
- SmartCommand Resolver 支持 EnemyBuilding；Godot 右键敌方建筑下达攻击，点击己方建筑显示选择框、HP 和生命周期。
- 建筑追击使用 Footprint 外缘合法点，攻击距离使用单位到矩形最近点，不会把不可达中心当目标。
- 建筑攻击复用单位前摇、冷却和伤害 Profile；摧毁走 Construction 生命周期，移除导航占用、Supply 与 Refinery 效果。
- 当前 AttackMove 仍只自动索敌单位；建筑自动索敌等待可见性和目标优先级规则，避免无条件吸向附近建筑。
- Replay Package 升级为 v4、热快照为 v4、状态 Hash 为 v5，完整保存 Unit/Building 目标类型和命令队列。
- `combat-attack-building` 中 8 个单位通过 SmartCommand 摧毁 2,000 HP 建筑，Footprint 归零、Supply 回退，并验证战斗中热恢复与完整回放一致。
- 71/71 全量回归通过；专用 AV1/WebM 录像位于 `test_videos/20260711_182334/`，索引现有 95 段录像、覆盖 71 个场景。

### P：S11-C4 Building Type 数据工作流（已完成）

- 新增版本化 `BuildingTypeCatalogSnapshot v1`，规范字节和稳定 Hash 不依赖 Godot；E2a 加入 Academy 后当前 Hash 为 `57DB7C4B43C00E8E`。
- 严格校验未知版本、空目录、非连续 ID、重复名称、数值/枚举和 Supply/Production/TownHall/Refinery 功能契约。
- `RtsBuildingTypeCatalogResource` 与逐项子 Resource 可在 Inspector 编辑造价、人口、工期、生命、尺寸、施工策略和节点约束。
- 主场景显式绑定 `data/demo_building_types.tres`；转换器支持 `CacheMode.Replace` Fresh Load，并只向模拟层交付不可变快照。
- `DemoBuildingTypes`、Resource、黑盒业务场景使用同一目录契约；避免代码常量和资产各自漂移。
- `BuildingTypeCatalogSelfTest` 验证规范 round-trip、资产一致性和非法 Refinery 契约拒绝。
- `building-type-resource-runtime` 不接触转换器或施工内部结构，直接用加载快照完成四种尺寸建筑的扣费、施工、人口和气矿启用。
- 历史 Build 命令继续保存已解析完整 Profile，因此平衡资产更新不会篡改旧 Replay Package。
- 72/72 全量回归、Release 性能门槛和专用 AV1/WebM 录像通过；录像位于 `test_videos/20260711_184359/`，索引现有 96 段录像、覆盖 72 个场景。

### Q：S11-D1 生产队列与出生运行时（已完成）

- 新增版本化纯 C# `ProductionCatalogSnapshot v1`，包含 3 个 Unit Type 和 3 个 Production Recipe，稳定 Hash 为 `88CB72E34880A0B7`。
- Unit Type 完整声明移动、战斗与 Worker 语义；Recipe 声明生产建筑、双资源/人口成本、工期和退款率。
- 每个生产建筑拥有独立五格队列；入队时原子扣除资源并预留人口，失败不产生部分修改，取消完整释放人口。
- 所有权、建筑完工状态、Producer Type、队列上限、资源和人口均有稳定拒绝结果。
- 十二个确定性出口候选同时检查世界几何、动态建筑和单位重叠；全部封死后完成订单停在 `WaitingForExit`，出口释放后再安全生成。
- Rally 地面命令作为派生系统 Move，不进入玩家 Unit Command Log；生产 Worker 时同步注册经济身份。
- 已出生单位的玩家/人口账本属于生产未来态；单位死亡在下一玩法 Tick 释放人口，但不返还已完成生产的资源。
- 生产建筑摧毁会全额退款并清理所有未完成订单、预留人口和 Rally 状态。
- Godot 世界建筑绘制只读取不可变生产队列快照，显示单位、进度、等待状态和队列数量。
- 状态 Hash 升级为 v6，覆盖生产者、订单顺序、进度、状态与 Rally 未来态。
- `production-queue-exit-rally` 覆盖队列满、错误生产建筑、人口阻塞、取消、十二出口全封、延迟出生、Rally、单位死亡释放人口和建筑摧毁清理；73/73 全量回归与 Release 门槛通过。
- 专用 22 秒 AV1/WebM 录像位于 `test_videos/20260711_190740/`；索引现有 97 段录像、覆盖 73 个场景。

### R：S11-D2 生产回放与热状态持久化（已完成）

- 新增 `ProductionCommandLogSnapshot v1`，成功的 Train/Cancel/Rally 使用独立命令语义；Train 保存解析后的完整 Recipe 和 Unit Type。
- Replay Package 升级为 v5，初始生产状态包含 next order ID、逐建筑队列/Rally 和已出生单位人口账本。
- 固定重放顺序升级为 World → Construction → Production → Economy → Unit → Tick；出生 Rally Move 仍是派生系统命令，不进入 Unit Log。
- Hot Snapshot 升级为 v5，规范正文增加 Production Runtime Snapshot，并对 Producer、所有权、Recipe、队列顺序、进度、等待状态、订单 ID 和人口账本做严格交叉验证。
- 进程内 Capture/Restore 与持久化编解码共用同一纯 C# Production Snapshot，不维护两套状态结构。
- `production-replay-persistence` 使用 4 条生产命令、8 条动态出口命令和 0 条派生 Unit 命令，验证生产中、WaitingForExit 和出生后人口账本三种热状态。
- Tick 0 完整回放、Tick 300 checkpoint、Tick 270/330/420 三份热恢复到 Tick 540 的周期采样与最终 Hash 全部一致。
- Production Log、Package v5 和 Hot Snapshot v5 的 round-trip、未知版本、截断载荷与 Package 绑定拒绝均进入黑盒门禁。
- 74/74 全量回归与 Release 性能门槛通过；9 秒 AV1/WebM 录像位于 `test_videos/20260711_194814/`，索引现有 98 段录像、覆盖 74 个场景。

### S：S11-D3a Production Catalog 数据工作流（已完成）

- 新增 `RtsProductionCatalogResource`、Unit Type 子资源和 Recipe 子资源；Inspector 可编辑移动、战斗、Worker、Producer、双资源/人口、工期和退款率。
- Recipe 使用稳定 Unit Type ID 引用，不在每条 Recipe 内复制一份可漂移的 Unit Profile。
- 转换器重新推导 Movement Class/Navigation Radius，并只向模拟层交付不可变 `ProductionCatalogSnapshot v1`。
- 主场景绑定 `data/demo_production_catalog.tres`；独立生成/验证脚本覆盖引擎保存和 `CacheMode.Replace` Fresh Load。
- 严格拒绝空目录、未知版本、null 子资源、非连续 ID、重复名称、越界 Unit Type 引用和不一致移动/战斗/配方约束。
- `ProductionCatalogDiff` 分开报告 Unit Type 与 Recipe 变化；自测验证单项工期变化为 `0/1`。
- `production-catalog-resource-runtime` 使用加载资产从 Barracks/Command Center 生产 Marine、Marauder、SCV，并验证战斗生命、Worker 注册、资源 1250/475 和人口 4/35。
- Train 日志仍保存解析后的完整 Recipe/Unit Type，Resource 调参不改变历史 Package v5。
- 75/75 全量回归与 Release 性能门槛通过；22 秒 AV1/WebM 录像位于 `test_videos/20260711_200053/`，索引现有 99 段录像、覆盖 75 个场景。

### T：S11-D3b Rally SmartCommand 目标协议（已完成）

- 新增纯 C# `RallyTarget`：None/Ground/ResourceNode/FriendlyUnit，实体目标保存稳定 ID 和回退坐标。
- Godot 选中生产建筑后右键可命中资源点、友军单位或地面；Snapshot 驱动 Rally 连线，不向模拟层传 Node。
- Worker 对资源 Rally 自动进入 Gather；普通单位对资源点 Move；友军目标在出生时按 ID 解析，失效时确定性降级到记录坐标。
- Production Log v2、Replay Package v6、Hot Snapshot v6、State Hash v7 同步升级并严格验证类型、有限坐标与引用范围。
- `production-rally-smart-targets` 覆盖三类目标、生产中热恢复、完整回放、规范 round-trip、未知版本和截断拒绝。
- 76/76 全量黑盒回归通过。
- Release 256/512/1000 单位移动 P95 为 1.77/5.76/9.00ms，128/256 单位持续战斗 P95 为 2.55/3.86ms；Hash v7 在 1000 单位下平均 1.32ms。
- 22 秒 AV1/WebM 录像位于 `test_videos/20260711_203001/`；全库校验通过 101 个视频、49 个 manifest、100 个场景引用，编码均为 AV1。

### 下一阶段边界

S11-D 已整体收口。

### U：S11-E1 建筑前置与生产可用性（已完成）

- Production Catalog 升级为 v2；Recipe 支持最多 32 个唯一 `CompletedBuilding(TypeId, Count)` 条件，并深拷贝进入不可变运行时快照。
- Resource 新增条件子资源；加载时同时验证空项、重复类型、非法数量以及跨 Building Type Catalog 越界引用。Demo Hash 更新为 `DE89CDDC5527EF18`。
- Production 返回 `MissingPrerequisite` 和逐条件 `CurrentCount/RequiredCount` Availability Snapshot；Godot 只读该快照显示配方可用性。
- 新订单实时检查已完成且存活的己方建筑数量；已入队订单不因后续目录调参被篡改。
- Production Log v3、Replay Package/Hot Snapshot v7、State Hash v8 完整编码活动订单的前置条件。
- `production-building-prerequisites` 覆盖 2 Barracks + 1 Command Center 的锁定、补齐解锁、生产、完整回放、生产中热恢复和协议拒绝。
- 77/77 全量黑盒回归通过；Release 256/512/1000 移动 P95 为 1.64/4.71/11.86ms，128/256 战斗 P95 为 1.80/4.57ms。
- 22 秒 AV1/WebM 位于 `test_videos/20260711_205020/`；全库验证通过 102 个视频、50 个 manifest、101 个场景引用。

### V：S11-E2a 正式研究与升级运行时（已完成）

- 建筑目录加入第五类 96×72 Academy/Research，Hash 更新为 `57DB7C4B43C00E8E`；研究不与 Barracks 训练队列错误并行。
- 纯 C# Technology Catalog v1 提供 Infantry Weapons 多等级，以及互斥的 Assault/Fortification Doctrine。
- 正式 Research/CancelResearch 订单执行资源扣除、75% 取消退款、建筑/科技前置、AlreadyQueued、MaximumLevel 和 MutuallyExclusive 校验。
- 玩家完成等级保存当时完整不可变科技 Profile；恢复时严格拒绝等级越界、重复 ID、互斥冲突、研究者错误和队列冲突。
- 设施日志 v4、Replay Package/Hot Snapshot v8、State Hash v9 保存等级、队列、进度、前置和互斥未来态。
- Godot 建筑表现读取 Research Snapshot 绘制紫色进度与目标等级，不读取系统内部集合。
- `technology-research-upgrades` 覆盖取消重排、等级 1→2、前置、排队/完成互斥、最大等级、研究中热恢复和完整回放。
- 78/78 全量回归通过；Release 256/512/1000 移动 P95 为 1.60/7.17/8.26ms，128/256 战斗 P95 为 2.75/3.83ms。
- 22 秒 AV1/WebM 位于 `test_videos/20260711_211332/`；全库验证通过 103 个视频、51 个 manifest、102 个场景引用。

### 下一阶段边界

### W：S11-E2b Technology Resource 数据工作流（已完成）

- 新增 Technology/Requirement/RtsTechnologyCatalog 三层 Godot Resource 与纯 C# 转换器。
- 主场景绑定 `data/demo_technology_catalog.tres`；格式 v1、稳定 Hash `8F9990031AA55B5E`、3 项科技。
- 转换后条件使用 `ImmutableArray`；严格拒绝 null 子资源、非法字段、重复条件和目录结构错误。
- 与 Building Catalog 交叉校验 Researcher 必须为 Research 建筑、Building 前置不越界；科技前置只能指向更小 ID，静态保证无环。
- `--generate-demo-technology-catalog` / `--validate-technology-catalog` 及 PowerShell 包装脚本覆盖引擎保存和 `CacheMode.Replace` Fresh Load。
- Simulation SelfTest 与 `technology-research-upgrades` 均注入加载后的科技快照，并校验与规范 Hash 一致。
- 78/78 全量回归通过；更新后的 22 秒 AV1/WebM 位于 `test_videos/20260711_213803/`。
- 全库录像验证通过 104 个视频、52 个 manifest、103 个场景引用，编码均为 AV1。

### 下一阶段边界

S11-E 已收口。

### X：S11-F1 扩张、基地经济与工人转场（已完成）

- Town Hall 只在正式完工事件注册基地；每座基地拥有独立 DropOff。Town Hall 被摧毁时两者同步停用，不留下可继续交资源的幽灵节点。
- 资源点在 360 世界单位经济半径内按最近有效基地确定归属，距离相同时以 Base ID 稳定决胜；资源归属和饱和度均为确定性派生数据，不复制成第二份可变账本。
- `EconomyBaseSnapshot` 只暴露业务只读字段：Town Hall/DropOff 身份、位置、是否有效、矿气节点数、当前/理想工人数和饱和度。
- 工人转场按“距目标基地距离、Unit ID”稳定选人，再按目标采集点 `assigned/capacity` 与 Node ID 稳定分配；非法玩家、基地、数量、同基地和无目标资源都有固定结果码。
- Economy Command Log 升级 v2 并记录成功的 TransferWorkers；Replay Package/Hot Snapshot 升级 v9，State Hash 升级 v10，均保存并验证 Base/DropOff 生命周期未来态。
- `economy-expansion-saturation` 仅通过测试业务 Facade 验证 `8/4 + 0/4 -> 4/4 + 4/4`、同基地拒绝、转场日志规范往返，以及扩张摧毁后的停用。
- 79/79 全量黑盒回归通过。Release 性能门禁：256/512/1000 移动 P95 为 1.72/4.94/9.49ms，分配为 27/182/461B/Tick；128/256 战斗 P95 为 2.05/6.05ms。
- 15 秒 AV1/WebM 位于 `test_videos/20260711_221503/`；全库录像验证通过 105 个视频、53 个 manifest、104 个场景引用，编码均为 AV1。

### Y：S11-F2 玩家视野、所有权与命令权限（已完成）

- 新增纯 C# `PlayerVisibilitySystem`：32 单位网格、单位 224/建筑 256/Town Hall 320 视野半径；当前 Visible 每 Tick 确定性重建，Explored 单调持久。
- `PlayerViewSnapshot` 深拷贝己方和当前可见动态实体；敌人离开视野后不再泄露实时位置、生命、施工、生产或研究状态。
- 资源点未探索时隐藏；已探索但当前不可见时只保留 ID/种类/位置，实时 Remaining 以 `-1` 表示未知。
- 正式 `IssuePlayer*` 入口统一验证玩家注册、非空选择、单位存活/所有权、敌我关系和目标当前可见；失败不部分执行也不进入命令日志。
- Godot 世界绘制、SmartCommand 命中和 Minimap 标记消费 PlayerView；Debug DynamicFootprint 不再绕过雾层泄露隐藏建筑。
- Hot Snapshot v10 保存并严格验证有序玩家探索 bitset；Replay Package v10 从未探索 Tick 0 确定性重建；State Hash v11 覆盖探索未来态。
- `player-visibility-authority` 覆盖隐藏目标拒绝、错误所有者拒绝、侦察显形、离开视野再次隐藏、资源探索记忆、完整回放和探索中热恢复。
- 80/80 全量黑盒回归通过。Release 性能：256/512/1000 移动 P95 为 1.97/4.56/9.25ms，分配 27/182/461B/Tick；128/256 战斗 P95 为 2.00/4.77ms。
- 15 秒 AV1/WebM 位于 `test_videos/20260711_224643/`；全库验证通过 106 个视频、54 个 manifest、105 个场景引用，编码均为 AV1。

### Z：S11-G 玩家能力、失败与比赛终局（已完成）

- 新增纯 C# `MatchSystem`；比赛只能在 Tick 0 以至少两名已注册唯一玩家启动，并持有 Setup/Running/Completed 与 Active/Defeated/Victorious 状态。
- `EstablishedPresence` 避免初始化阶段误判；建立过正式建筑后，活跃建筑归零才失败。失去 Town Hall 但仍有生产建筑不会出局。
- `PlayerCapabilitySnapshot` 为 UI/AI 提供活跃/完工建筑、Town Hall、生产/研究设施、工人和作战单位计数，不暴露 Construction/UnitStore 内部集合。
- 采集、转场、建造、生产、研究、Move、AttackMove、Stop、Hold 和 SmartCommand 统一获得非参赛者、已失败与终局门禁。
- Replay Package/Hot Snapshot 升级 v11，State Hash 升级 v12；严格持久化参赛者、建立存在、失败 Tick、完成 Tick 和胜者。
- `match-capability-elimination` 三方场景覆盖 Town Hall 丢失但继续存活、最后建筑出局、唯一胜者、终局命令拒绝、规范载荷、前段完整回放与终局热恢复。
- Godot HUD 只消费 `MatchSnapshot` 展示能力和胜者。详细设计见 `docs/MATCH_LIFECYCLE.md`。
- 81/81 全量黑盒回归通过。Release 性能：256/512/1000 移动 P95 为 1.35/6.24/9.32ms，分配 27/182/461B/Tick；128/256 战斗 P95 为 2.69/4.59ms。
- 10 秒 AV1/WebM 位于 `test_videos/20260711_234241/`；全库验证通过 107 个视频、55 个 manifest、106 个场景引用，编码均为 AV1。

### AA：可扩展 AI 宿主架构（基础已完成，策略待实现）

- AI 独立于 `RtsSimulation.Tick` 和 Godot；只读 PlayerView、经济、基地、设施队列与 Match 观察快照，不接触寻路、Steering 或权威内部集合。
- `IRtsAiPolicy` 只输出 Gather/Transfer/Build/Train/Research/Move/AttackMove 高层意图；Adapter 通过正式玩家命令执行，因此权限、业务拒绝和命令日志不复制。
- `RtsAiDirector` 提供 Player ID 稳定顺序、周期/offset 错峰、有界意图数，以及 Policy ID/格式版本/内部状态的严格捕获恢复。
- 回放只重放已经执行的游戏命令，不重新运行 AI；实时续跑将 Simulation Hot Snapshot 与 Director Snapshot 配对保存。
- `AiArchitectureSelfTest` 已验证 Tick 调度、容量裁剪、策略状态恢复和继续执行，且 fake policy 完全不依赖具体模拟实现。
- 详细架构和下一层模块边界见 `docs/AI_ARCHITECTURE.md`。

### AB：S11-H1 模块化完整对局 AI（已完成）

- `ModularSkirmishAiPolicy` 拆为 Economy/Build/Production/Technology/Scouting/Defense/Combat 七类无状态 Planner；策略只负责知识更新、阶段和统一调度。
- `StrategicBlackboard` 保存阶段、侦察/进攻/扩张冷却、最后敌情、放置重试和逐建筑退避；格式 v1 为 102 字节，支持严格恢复、截断拒绝和恢复后下一决策一致。
- `AiIntentArbiter` 按 EmergencySupply/Defense/Combat/Expansion/Infrastructure/Production/Technology/Economy/Scouting 稳定优先级裁剪，并预留单 Tick 矿、气、人口、单位和设施占用。
- 观察合同新增工人目标、设施 Builder、科技等级/排队和执行反馈；Planner 不读取 UnitStore、路径、Steering 或 Godot Node。
- 真实闭环发现施工工人会被经济 Planner 重新派走；现已通过 Builder 占用和正式 `ResumeBuild` 意图解决，不依赖场景特判。
- Combat 在未知区域使用 AttackMove；敌方单位/建筑可见后使用正式 AttackUnit/AttackBuilding，确保能摧毁建筑结束比赛。
- `ai-modular-skirmish` 最终达到 10 工人/10 作战单位、S1/B1/R1/A1/CC2、Infantry Weapons 和 Tick 1,893 胜利；命令覆盖 e14/b7/p18/u8。
- 82/82 全量黑盒回归通过。Release 性能：256/512/1000 移动 P95 为 1.53/4.51/10.83ms，分配 27/182/461B/Tick；128/256 战斗 P95 为 2.23/4.71ms。
- 60 秒 AV1/WebM 位于 `test_videos/20260712_005031/`；全库验证通过 108 个视频、56 个 manifest、107 个场景引用，编码均为 AV1。

H1 当时限定的下一段为 S11-H2：AI 配置 Resource、双 AI 错峰自对战、Simulation+Director 成对热恢复和 AI 命令 Package 全程回放；完成情况如下。

### AC：S11-H2 AI 数据、自对战与确定性恢复（已完成）

- 新增纯 C# `AiConfigurationCatalogSnapshot v1`，Standard/Aggressive 两档配置 Hash 为 `509CED7A999A2BD0`；连续 ID、唯一名称、所有阈值和有限半径均严格验证。
- 新增 Inspector 可编辑 `RtsAiConfigurationCatalogResource`/Profile 子资源、Fresh Load Converter 和 `data/demo_ai_configurations.tres`；主场景显式绑定资源。
- 多 AI 共用一个 Director/Adapter，按 Player ID 排序；Standard 12 Tick offset 0，Aggressive 10 Tick offset 5，调度错峰。
- `RtsAiRuntimeState` 同 Tick 捕获 Simulation Hot State 与全部 Director/Policy future。Tick 1,200 恢复后重新运行两个 AI 到 4,200，最终 Hash 与连续局一致。
- Director 将已仲裁意图按 Construction→Production/Research→Economy→Unit 稳定域顺序执行，修复策略优先级顺序与 Replay Runner 不同导致的 Hash 分歧。
- Replay Package v11 从 Tick 0 只重放 31 条经济、10 条建造、51 条生产/研究和 130 条单位命令，不创建 AI，最终 Hash 与连续局一致。
- `ai-dual-runtime-replay` 4,200 Tick 黑盒自对战通过；70 秒 AV1/WebM 位于 `test_videos/20260712_011731/`。
- 83/83 全量黑盒回归通过。Release 性能：256/512/1000 移动 P95 为 2.69/4.55/8.87ms，分配 27/182/461B/Tick；128/256 战斗 P95 为 1.95/6.80ms。
- 全库录像验证通过 109 个视频、57 个 manifest、108 个场景引用，编码均为 AV1。

S11-H 已收口。后续不继续无证据优化 AI 微操；转入实际玩法闭环剩余的明确功能或由新失败场景驱动改进。

### AD：S11-I1 资源与施工 SmartCommand（已完成）

- `SmartCommandTargetKind` 增加 ResourceNode 与 FriendlyBuilding；Godot 只负责从当前 PlayerView/可见建筑解析目标，能力拆分位于纯 C# 模拟层。
- 右键资源时先原子验证全部选中工人，再分别发正式 Gather；非工人批量 Move。右键 `WaitingForBuilder` 己方建筑时，按最低 Unit ID 选唯一工人 Resume，其余单位 Move。
- A 修饰统一覆盖为 AttackMove。没有合格工人或建筑无需续建时保持普通 Move 语义。
- Gather/Resume 正式入口会清理工人的旧单位队列；Resume 同时取消旧采集工作，修复经济/施工两个系统争用同一工人的问题。
- `smart-command-gameplay-context` 验证乱序混合选择、两工人采集、Marine 移动、最低 ID 续建和 Package 重放 Hash 一致；I2 更新录像位于 `test_videos/20260712_021335/`。

### AE：S11-I2 跨域 Shift 工人任务（已完成）

- `UnitOrderKind` 正式增加 `GatherResource` 与 `ResumeConstruction`；资源目标使用独立 `TargetResourceNode`，活动/待执行 SoA、逻辑队列、状态 Hash 和持久化编码均不借用建筑字段。
- 即时 Gather/Resume 登记为活动任务；后续 Shift 命令等待采集结束或施工完成。经济/施工派生 Move/Stop 不再覆盖玩家任务类型。
- 延迟任务出队时重新验证目标、玩家、工人、资源余量/Refinery/DropOff 和建筑阶段；失效任务下一 Tick 完成并继续后续队列。
- Command Log 升级 v3，Package/Hot Snapshot 升级 v12，State Hash 升级 v13；未知旧版本继续明确拒绝。
- `smart-command-shift-worker-tasks` 同时验证 Move→Gather、Move→Resume、Move→枯竭资源→Move，以及 Tick 60 三条待执行任务的 Hot 恢复和 Tick 0 Package 重放一致性。
- 85/85 全量黑盒回归通过；I1/I2 两段更新录像位于 `test_videos/20260712_021335/`。
- Release 性能：256/512/1000 移动 P95 为 1.28/4.45/9.03ms，1000 单位分配 461B/Tick；128/256 战斗 P95 为 2.00/5.05ms。录像门禁通过 112 个视频、59 个 manifest、111 个场景引用，编码均为 AV1。

### AF：S11-J1 混合选择与解耦命令卡（已完成）

- 新增纯 C# `GameplaySelectionSnapshot`：Worker、CombatUnit、Building 按 Kind/Type/Entity ID 稳定分组，重复输入去重，活动子组在快照重建后保持，Tab/Shift+Tab 双向循环。
- `CommandCardComposer` 只组合活动子组与业务候选；不可变 Action Snapshot 保存 Enabled/Status，不读取模拟或 Godot。
- 独立 `RtsCommandCardControl` 只绘制快照并发出动作意图；换布局、图标、动画和皮肤不修改业务规则。
- Godot 薄绑定支持 Stop/Hold、Train、Research、取消生产/研究/施工；生产/科技 Enabled 与原因来自正式 Availability。
- 点选和 Shift 点选允许单位与多个己方建筑共同存在；框选仍只加入单位。命令只作用于当前活动子组。
- `OperationPresentationSelfTest` 验证 4 实体/3 子组、去重、稳定排序、正反循环、候选过滤和禁用原因。
- `operation-mixed-command-card` 验证 2 Worker + 1 Combat Unit + 1 Barracks、3 个子组，以及 Train→Cancel→Train 后出生单位；真实 UI AV1 位于 `test_videos/20260712_024209/`。
- 86/86 全量黑盒回归通过。Release 256/512/1000 移动 P95 为 1.49/4.48/10.21ms，1000 单位分配 461B/Tick；128/256 战斗 P95 为 1.39/4.31ms。录像门禁通过 113 个视频、60 个 manifest、112 个场景引用，编码均为 AV1。

### AG：S11-J2a 混合 Control Group 与 Alt 抢组（已完成）

- `ControlGroupManager` 改为保存纯 C# `ControlGroupEntity(Kind, EntityId)`；单位与建筑共享同一组，写入去重且按 Kind/Entity ID 稳定排序，不依赖建筑容量。
- Ctrl/Shift 保持覆盖/添加；Alt+数字覆盖目标组并从其他组移出所选实体，Alt+Shift+数字添加到目标组并从其他组移出。
- 召回时由 Godot 薄组合层过滤死亡单位、销毁建筑和非玩家实体；编组索引本身不访问模拟、Node 或 UI。双击召回的镜头中心同时纳入单位与建筑。
- `ControlGroupSelfTest` 验证乱序/重复混合实体、稳定排序、Alt 覆盖抢组、Alt+Shift 添加抢组和召回过滤。
- `control-group-mixed-steal` 通过稳定测试 Facade 构造 2 Worker + Marine + Barracks；最终 group 1 为 `1u/0b`、group 2 为 `2u/1b`，三个移动实体全部抵达。
- 专用 12 秒 AV1/WebM 位于 `test_videos/20260712_030457/`；编码为 AV1、CRF 32、preset 8，约 552KB。
- 87/87 全量黑盒回归通过。Release 256/512/1000 移动 P95 为 1.52/4.87/10.08ms，1000 单位分配 461B/Tick；128/256 战斗 P95 为 2.27/4.72ms。
- 全仓录像门禁通过 114 个视频、61 个 manifest、113 个场景引用，编码均为 AV1。

J2b 的明确边界：目标选择模式（Move/AttackMove/Build/Rally）、同类型多建筑批量生产/队列聚合，以及可重映射热键、tooltip、图标与最终表现。它们不再反向扩张 J2a 的编组数据结构。

### AH：S11-J2b1 命令卡目标模式（已完成）

- 新增纯 C# `TargetCommandRequest/Resolver/Resolution`；请求冻结活动子组稳定 ID，解析器只处理左右键、世界坐标和 Shift，不访问模拟或 Godot。
- 命令卡 Worker/Combat 子组提供 Move、Attack Move；生产建筑子组提供 Rally。确认后分别进入正式玩家移动、玩家攻击移动和生产 Rally API，不复制权限、队列或目标规则。
- Shift+Move/AttackMove 追加命令并保持目标模式；右键或 Escape 退出且不发命令。编组召回和 Space 全选也会取消旧目标请求。
- 活动同类型生产建筑子组可同时设置 Rally；确认前统一验证建筑仍存活且属于玩家，避免已失效选择造成部分写入。
- 独立 `RtsTargetCommandOverlay` 只消费请求与光标世界坐标，负责准星、颜色、提示面板；目标表现可独立换皮和动画。
- `OperationPresentationSelfTest` 增加请求规范化、Shift 保持、右键取消和 Rally 建筑排序；`operation-target-command-mode` 通过稳定 Facade 验证两段 Shift Move、取消、Rally、Attack Move 和 3/3 到达。
- 18 秒 AV1/WebM 位于 `test_videos/20260712_034401/`，约 743KB；抽帧确认命令卡、目标准星和独立提示面板无裁切。
- 88/88 全量黑盒回归通过。Release 256/512/1000 移动 P95 为 1.73/4.71/9.25ms，1000 单位分配 461B/Tick；128/256 战斗 P95 为 1.94/4.98ms。
- 全仓录像门禁通过 115 个视频、62 个 manifest、114 个场景引用，编码均为 AV1。

J2b2 的边界收缩为：Build 建筑类型选择/放置预览/合法性反馈，以及同类型多建筑批量生产和队列聚合。图标、tooltip、快捷键重映射与最终皮肤继续后置，不阻塞玩法闭环。

### AI：S11-J2b2a Build 放置模式（已完成）

- Worker 命令卡直接从 Building Type Catalog 生成 5 种建筑动作，覆盖 48×48、72×72、96×72、112×80、160×120 实际尺寸及 Supply/Production/TownHall/Refinery/Research 功能。
- `TargetCommandRequest` 增加 Build 和稳定 Building Type ID；`BuildTargetSnapper` 以纯 C# 执行 8px 位置吸附，Build 不伪装成可排队命令。
- `RtsSimulation.PreviewConstruction` 与正式提交共用 `ValidateConstructionRequest`，完整覆盖比赛门禁、工人资格/忙碌、资源、Refinery、重叠、净空和 Connectivity Guard，同时不修改世界、不扣费、不写日志。
- 多 Worker 按目标距离和 Unit ID 稳定排序，选择第一个正式预览通过者；Refinery 在 32px 内吸附 Vespene 节点，否则显示明确失败原因。
- 相同吸附位置的预览按请求/位置缓存，并每 6 Tick 刷新动态业务状态；Overlay 只消费 `BuildTargetPreviewSnapshot`，红绿 footprint 与文字状态不进入组合或模拟层。
- 非法点击保留 Build 目标模式，合法点击进入正式 `IssueConstruction` 并退出；右键/Escape 无副作用取消。
- `operation-build-placement-mode` 先在静态障碍得到红色 `StaticObstacleOverlap` 且建筑数不变，再移动到绿色合法位置，稳定选择最近 worker 1 并完成 Supply Depot。
- 8 秒 AV1/WebM 位于 `test_videos/20260712_040340/`，约 242KB；抽帧确认红/绿 footprint、5 建筑命令卡和状态面板均清晰。
- 89/89 全量回归通过。Release 256/512/1000 移动 P95 为 1.99/5.17/8.17ms，1000 单位分配 461B/Tick；128/256 战斗 P95 为 1.56/4.99ms。
- 全仓录像门禁通过 116 个视频、63 个 manifest、115 个场景引用，编码均为 AV1。

Build 不再继续扩张，除非实际玩法暴露明确问题。J2b2b 完成情况如下。

### AJ：S11-J2b2b 同类型多建筑生产与队列聚合（已完成）

- 新增纯 C# `ProductionGroupSnapshot` 和 `ProductionBatchPlanner`；只消费稳定 Building/Order ID、Recipe ID、进度与正式 Availability，不读取 Godot Control 或生产队列内部容器。
- 活动同类型生产建筑子组的一次 Train 会按 Building ID 稳定扇出到所有当前可用生产者。计划生成后仍逐建筑进入正式 `IssueProduction`，因此共享资源、人口、前置和队列容量会按同一顺序重新验证，部分成功具有确定性。
- 命令卡聚合显示生产建筑数、可发令数、总排队数和最常见阻塞原因；Rally 同样覆盖整个同类型子组。
- 批量取消采用“每座建筑最新一条匹配配方订单”的明确策略，不误删其他单位配方，也不依赖 UI 队列索引。未完成的同类建筑聚合执行 Cancel Construction，不参与生产。
- `MovementTestRig` 只公开 TrainBatch、CancelNewestProductionBatch 和 ObserveProductionGroup 业务 Facade；测试不读取 `ProductionSystem` 内部实现。
- `ProductionGroupPresentationSelfTest` 验证乱序/重复输入规范化、可用生产者计划、队列聚合与取消选择；`operation-production-group-batch` 验证 3 Barracks 两次批量生产形成 `[2,2,2]`，取消后 `[1,1,1]`，最终出生 3 个 Marine。
- 10 秒 AV1/WebM 位于 `test_videos/20260712_114535/`，289,259 字节；抽帧确认 `Barracks ×3`、`ready 3/3 · queued 6` 和 `Cancel Marine ×3` 清晰可见。
- 90/90 全量黑盒回归通过。Release 256/512/1000 移动 P95 为 3.76/5.95/8.98ms，1000 单位分配 461B/Tick；128/256 战斗 P95 为 1.49/4.98ms。
- 全仓录像门禁通过 117 个视频、64 个 manifest、116 个场景引用，编码均为 AV1。

J2 玩法操作闭环至此收口。下一步优先选择能暴露新业务问题的明确玩法功能；图标、tooltip、快捷键重映射和皮肤单独作为表现层任务，不反向修改生产语义。

### AK：S8-E1 确定性战斗事件流（已完成）

- `CombatEventStream` 使用固定 4,096 容量环形缓冲，不在攻击热路径增长容器；每条事件具有固定 Tick 和单调 Sequence。
- 攻击起手、实际命中和单位/建筑摧毁统一发布 `AttackStarted`、`Impact`、`TargetDestroyed`。命中携带实际扣除伤害和剩余生命，不要求表现层反查 `CombatStore`。
- 事件是可重建派生输出，不进入 State Hash、Replay Package 或 Hot Snapshot；热恢复时明确 Reset，避免把恢复前表现事件误当作恢复后事件。
- 消费者按 Sequence 增量读取；读取落后超过容量时返回 `LostEvents`，不会静默生成不完整动画或诊断。
- `MovementTestRig.ObserveCombatEvents` 是稳定业务 Facade；`combat-event-stream` 以 25 HP 目标验证三次伤害 12/12/1、三次起手、三次命中、一次摧毁、连续序号和零丢失。
- 纯 C# 自测额外使用容量 3 的缓冲验证覆盖、丢失计数、尾部读取和 Reset。
- 5 秒 AV1/WebM 位于 `test_videos/20260712_125206/`，103,453 字节。
- 91/91 全量黑盒回归通过；Release 256/512/1000 移动 P95 为 1.06/3.85/7.53ms，128/256 活跃战斗 P95 为 1.92/4.82ms，战斗分配约 2.3/4.0KB/Tick；全仓录像门禁通过 118 个视频、65 个 manifest、117 个场景引用，编码均为 AV1。

S8-E2a 完成情况如下。

### AL：S8-E2a 资源驱动单位伤害与武器科技（已完成）

- `CombatDamageResolver` 以不可变 Weapon/Defense/Health 输入结算：基础伤害、Light/Armored/Biological/Mechanical/Structure/Massive 属性位集、BonusVs、1～32 段攻击、护甲逐段扣减、0.5 最低单段伤害和生命上限截断。
- 每级武器升级可分别增加基础伤害与属性加成；`CombatSystem` 从正式 `TechnologySystem` 读取 Infantry Weapons 等级，`PreviewCombatDamage` 与正式命中共享同一公式。
- `CombatStore` SoA 保存 Armor、Attributes、AttacksPerVolley、Bonus 与升级量；State Hash v14、Replay Package/Hot Snapshot v13、Production Command Log v5 全部编码并验证新字段。
- Production Catalog v3 和 `UnitTypeProfileResource` 暴露全部战斗字段。生成的 `data/demo_production_catalog.tres` Hash 为 `BC15AFA55BA3455E`；Marine/SCV 声明 Light，Marauder 声明 Armored、1 护甲和 `+10 vs Armored`。
- Impact 事件携带总伤害、每段伤害、实际段数和 BonusApplied，后续伤害数字/弹道不需要反推 `x2` 或属性克制。
- `CombatDamageSelfTest` 验证升级、属性命中、多段、护甲和最低伤害；`combat-damage-matrix` 通过加载目录验证 20/30/18 三组伤害；科技场景验证 Infantry Weapons 2 级把 `10` 基础伤害提升为对 1 护甲目标的 13。
- AV1/WebM 位于 `test_videos/20260712_143026/`：伤害矩阵 159,587 字节，科技重录 474,837 字节。
- 92/92 全量黑盒回归通过；Release 256/512/1000 移动 P95 为 2.38/4.60/8.53ms，128/256 活跃战斗 P95 为 1.36/5.10ms，战斗分配约 2.3/4.0KB/Tick。录像门禁通过 120 个视频、66 个 manifest、119 个场景引用，编码均为 AV1。

S8-E2b 完成情况如下。

### AM：S8-E2b 建筑防御与统一伤害合同（已完成）

- Building Type Profile/Catalog v2 增加 Armor、Attributes、ArmorUpgradePerLevel；所有建筑必须包含 Structure，Demo 五类建筑使用 Structure|Mechanical。
- Supply Depot/Barracks/Command Center 提供 0/1/2 护甲测试梯度，Refinery/Academy 为 1；全部每级 Fortification +1。生成资产 Hash 为 `8706BAAF85DDD1B7`。
- `BuildingDefense` 从正式 TechnologySystem 读取 Fortification Doctrine；`PreviewCombatDamage(unit, building)` 与正式 AttackBuilding 共用 Weapon/Defense/Health 和 `CombatDamageResolver`。
- Construction Command Log 升级 v2；Replay Package/Hot Snapshot 升级 v14，State Hash 升级 v15，完整保存建筑 Armor/Attributes/升级量。
- `combat-building-defense` 用 48×48、112×80、160×120 三种尺寸验证 30/29/28 基础伤害、Fortification 后 29/28/27，并用正式攻击把 Supply Depot 400 HP 降到 371。
- 6 秒 AV1/WebM 位于 `test_videos/20260712_145433/`，226,005 字节。
- 93/93 全量黑盒回归通过；Release 256/512/1000 移动 P95 为 1.44/4.50/8.21ms，128/256 活跃战斗 P95 为 1.45/5.89ms，战斗分配保持约 2.3/4.0KB/Tick。录像门禁通过 121 个视频、67 个 manifest、120 个场景引用，编码均为 AV1。

单位/建筑统一伤害阶段至此收口。下一段 S8-E3 为确定性弹道。

### AN：S8-E3a 确定性投射物运行时（已完成）

- `CombatProjectileSystem` 使用固定 4,096 容量和稳定单调 ID；池满时明确发布过期事件，不会把远程武器静默降级为瞬时命中。
- 发射时冻结武器伤害、段数、属性 Bonus 与武器科技等级；飞行中按固定 Tick 跟踪单位或建筑当前位置，命中时读取目标当前护甲、属性和生命。
- 目标在命中前死亡或失效时投射物明确过期且不重定向；攻击者死亡不取消已经发射的权威投射物。
- `ProjectileLaunched/ProjectileExpired/Impact` 共用稳定 ProjectileId；`ObserveCombatProjectiles` 只暴露上层表现所需的位置、速度与目标身份。
- 活跃投射物进入 Hot Snapshot v15、Replay Package v15 和 State Hash v16。Production Catalog v4 / Production Command Log v6 增加 ProjectileSpeed；Demo 目录 Hash 为 `F259DFB22A646242`。
- `CombatProjectileSelfTest` 覆盖稳定 ID、跟踪转向、快照恢复和失效过期；`combat-projectile-flight` 在 Tick 180 捕获飞行中投射物，目标随后移动，最终命中 ID 一致且热恢复最终 Hash 完全一致。
- 94/94 全量黑盒回归通过。Release 256/512/1000 移动 P95 为 1.82/6.11/9.20ms；128/256 投射物战斗 P95 为 3.06/3.29ms，测量末帧仍有 417/163 个活跃投射物，分配约 1.7/5.2KB/Tick。
- 8 秒 AV1/WebM 位于 `test_videos/20260712_152027/`，180,258 字节。全仓录像门禁通过 122 个视频、68 个 manifest、121 个场景引用，编码均为 AV1。

S8-E3a 至此收口。下一段为 E3b 弹道表现合同。

### AO：S8-E3b 解耦弹道表现合同（已完成）

- `CombatEvent` 增加派生 `WorldPosition`；AttackStarted、Launch、Expire、Impact 和 Destroy 都能直接驱动表现，不需要反查 CombatStore 或已经销毁的建筑。
- `CombatPresentationComposer` 只消费公开投射物快照与增量事件，输出不可变 Frame；每个飞行体最多 10 个拖尾点，Impact/Expired 提示最多保留 0.4 秒，Event Sequence 重置时自动清空短期历史。
- 默认可替换 resolver 将单段普通武器映射为 Bolt、属性 Bonus 映射为 Orb、多段攻击映射为 Volley。实际项目可替换映射，不修改模拟层。
- `RtsCombatProjectileLayer` 是独立 Node2D，只消费 Frame；`data/demo_combat_presentation_theme.tres` 独立配置六类颜色、三类半径、拖尾宽度和命中半径。`RtsDemo` 仅保留薄绑定。
- `CombatPresentationSelfTest` 覆盖样式、朝向、拖尾、Impact/Expired 和有界回收；`combat-projectile-presentation` 通过稳定 Facade 验证三样式、10 点拖尾、命中 ID 1/2/3、90/85/84 生命与零事件丢失。
- 95/95 全量黑盒回归通过。Release 256/512/1000 移动 P95 为 3.24/5.26/8.46ms；128/256 投射物战斗 P95 为 2.16/3.04ms，末帧 417/163 个活跃投射物。
- 两段 AV1/WebM 位于 `test_videos/20260712_155038/`，分别为 178,595 和 218,406 字节。全仓录像门禁通过 124 个视频、69 个 manifest、123 个场景引用，编码均为 AV1。

E3b 至此收口。下一段为 E4a 移动射击与武器移动约束。

### AP：S8-E4a 移动射击与攻击周期命令语义（已完成）

- Combat Profile 新增 `CanMoveDuringWindup` 和 `CanMoveDuringCooldown`，默认 false；现有 Marine/Marauder/SCV 保持固定武器语义，移动射击是按武器显式开启的能力。
- 移动武器先正常追击并通过攻击槽/射程验证，再在 AttackMove 的前摇或冷却阶段恢复原路线；Stop/Hold 和没有并行路线的 AttackTarget 不会漂移。
- Move、新 AttackMove、Stop、Hold 会清除未完成 Windup，因此可可靠取消攻击；已经开火后的 Cooldown 不被命令重置，无法通过操作刷射速。
- 恢复相同 AttackMove Goal 时检测现有 Moving/WaitingForPath 状态，不会每 Tick 增加命令版本或重复申请路径。
- Production Catalog v5、Production Command Log v7、Package/Hot v16 和 State Hash v17 保存两个行为字段；目录 Hash 为 `722A502E8B6F3F33`。
- `combat-mobile-fire` 四车道对照得到前摇位移 0.0/59.8px、开火后一秒位移 0.0/110.3px、Windup `0.60→0` 且无发射、Cooldown `20.00→20.00`；最终生命 190/190/200/190，热恢复最终 Hash 完全一致。
- 96/96 全量黑盒回归通过。Release 256/512/1000 移动 P95 为 1.81/6.74/9.45ms；128/256 投射物战斗 P95 为 2.06/3.14ms，末帧 417/163 个活跃投射物。
- 12 秒 AV1/WebM 位于 `test_videos/20260712_165436/`，600,447 字节。全仓录像门禁通过 125 个视频、70 个 manifest、124 个场景引用，编码均为 AV1。

E4a 至此收口。下一段为 E4b 有限目标评分与重新选敌稳定性。

### AQ：S8-E4b 有限目标评分与稳定重选（已完成）

- 自动候选先通过敌对、存活和射程，再按 `distance² - priority×12000 - bonusMatch×6000 - armedThreat×3000` 选择最低分；完全同分按稳定 Unit ID。
- 目标 Profile 新增 0～10 `AutoTargetPriority`，默认 0；`PreviewAutoTargetScore` 公开距离、优先级、Bonus、Threat 与总分，调试层不复制公式。
- 初次锁定至少 0.75 秒，每 12 Tick 最多重查一次；新目标必须改善 2,500 分，并在 Priority、Bonus Match 或 Armed Threat 至少一项语义升级。相同语义目标不因纯距离变化抖动。
- 玩家 AttackTarget 完全跳过自动重选；建筑仍走显式 AttackBuilding，不混入未经验证的单位权重。
- Production Catalog v6、Production Command Log v8、Package/Hot v17 和 State Hash v18 保存 AutoTargetPriority 与 TargetLockRemaining；目录 Hash 为 `6A33CA0648280068`。
- `combat-target-selection` 验证 Tick 30 保持初始目标、Tick 60 切换强目标、拒绝仅改善 771 分的同类目标、玩家显式锁定不被 Priority 10 覆盖；评分 10000/-4100/-4871，热恢复最终 Hash 一致。
- 高密度基准曾暴露纯距离重选导致攻击波次被反复取消，最终规则明确要求语义升级。修正后 128/256 单位末帧恢复 417/163 个投射物，P95 为 2.01/3.00ms。
- 97/97 全量黑盒回归通过。Release 256/512/1000 移动 P95 为 1.37/4.29/11.50ms；状态 Hash v18 在 1000 单位下平均 1.76ms。
- 5 秒 AV1/WebM 位于 `test_videos/20260712_171341/`，126,349 字节。全仓录像门禁通过 126 个视频、71 个 manifest、125 个场景引用，编码均为 AV1。

E4b 至此收口。下一段 E5a 只处理战斗推挤与接触优先级：近战接触者、正在前摇的固定武器、移动射击单位和普通穿行单位之间的质量规则；不重写现有碰撞解算器，也不引入通用物理标签系统。

### AR：S8-E5a 战斗接触与推挤优先级（已完成）

- 新增无状态 `CombatContactPolicy`，从移动模式、战斗阶段、命令意图、定位类型与当前武器移动能力派生接触角色；不增加 Profile、Replay、Hot Snapshot 或 Hash 字段。
- 固定前摇、近战接触、固定冷却、移动射击、普通单位的逆移动性为 0.08/0.14/0.30/0.72/原移动模式，阻力等级为 4/3/2/1/0。所有战斗角色都保留有限让位能力，不制造永久战斗墙。
- MobileWeapon 必须同时处于 AttackMove 且当前攻击阶段允许移动；Hold/AttackTarget 即使武器支持移动，也不会绕过命令语义。
- 碰撞阶段每单位只派生一次快照，原圆碰撞、SpatialHash 和位置约束仅消费逆移动性；`PreviewCombatContact` 通过稳定门面公开角色与成对修正占比。
- `combat-contact-priority` 以四条隔离车道覆盖五种角色；面对相同 Idle 接触，修正占比为 0.110/0.177/0.316/0.526，相同一 Tick 压力下固定前摇/近战/固定冷却位移为 0.32/0.52/0.93px。
- Package v17 / Hot v17 恢复最终 Hash 完全一致，证明角色可从权威状态派生，无需持久化瞬时碰撞标签。
- 98/98 全量黑盒回归通过。Release 256/512/1000 单位移动 P95 为 1.34/4.77/8.43ms；128/256 战斗 P95 为 1.20/3.21ms，末帧 403/174 个活跃投射物。
- 3 秒 AV1/WebM 位于 `test_videos/20260712_174811/`，138,587 字节。全仓录像门禁通过 127 个视频、72 个 manifest、126 个场景引用，编码均为 AV1。

E5a 至此收口。为避免无穷调参，暂不自动开启 E5b：只有新的宏观战线黑盒场景暴露可复现的疏通、接战保持或永久堵塞失败，才针对该失败增加有限规则；没有失败证据时转向计划中的其他玩法能力。

### AS：S11-H3 解耦双 AI 持续遭遇战关卡（已完成）

- 新增纯数据 `AiEncounterLevelDefinition`：独立声明 1500×850 镜像战场、中央遭遇通道、主矿/气矿/自然矿、初始玩家状态、起始基地和 Standard/Aggressive Profile。
- `AiEncounterLevelOrchestrator` 通过 `IAiEncounterLevelRuntime` 执行 Prepare、Begin、StartEconomy 与 AttachAi；关卡不读取 Store、不直接生成军队、不强制研究、不补运行时资源，也不替 AI 发攻击命令。
- `MovementTestRigAiEncounterRuntime` 是唯一适配层，只调用稳定玩家、建造、采集、AI 和观察接口。Replay Package 在初始状态完成后、正式施工命令前建立录制边界。
- `AiEncounterTelemetry` 每 30 Tick 消费宏观快照，并每 Tick 通过稳定工人信号跟踪采集状态迁移；只有携货返程完成入账并再次出发才计为完整循环，不把诊断写回策略或模拟。
- 修复 160×120 Town Hall 中心投递绕行：DropOff 持久化建筑矩形半尺寸，返矿按农民当前位置选择扩张矩形的最近边缘点；双方完成采集循环 77/79 次，边缘采样 82:0/86:0。采矿去程/返程通过独立策略关闭单位碰撞，但仍受建筑和静态世界约束。
- 双方完整基础设施 Tick 为 570/630，首次科技 690/870，自然扩张 1140/1230，首次攻击 690/750；最终累计科技 4/4、基地 2/2、攻击指令 47/83、实际命中 72/40、军队峰值到低点 11→2/6→1，比赛仍为 Running。
- 正式命令覆盖经济 30、建造 12、生产/研究 52、单位 155；Replay Package/Hot Snapshot v19、State Hash v20 规范往返成功。
- 101/101 全量黑盒回归通过。Release 256/512/1000 移动 P95 为 1.54/5.21/10.86ms；128/256 战斗 P95 为 2.83/3.43ms，全部通过预算。
- 单位/建筑获得稳定视觉身份，SCV 显示采集状态、货物和往返连线；专用平滑镜头轨道先总览再巡视双方经济、中央交战与扩张，且只作用于测试表现。
- 世界主体、弹道/拖尾、命中圈/伤害数字、目标预览和 Clearance 预览统一消费同一世界画布变换，修复自动镜头下独立效果层位置偏移。
- 新增独立 `economy-mass-mining` 压力关卡：4 基地、32 片矿、96 农民，600 Tick 完成 172 次循环并采集 4,032 Minerals；456 次返矿边缘采样全部合法且无不可达单位。测试只使用稳定业务门面，底层寻路或经济实现可替换。
- 60 秒 AI 与 10 秒大规模采矿 AV1/WebM 位于 `test_videos/20260713_002852/`，分别为 30,265,782 / 6,187,939 字节，AV1/CRF32/preset8。

H3 提供后续复杂玩法的集成基线。新单位、技能、科技或 AI Planner 应优先接入该关卡的数据定义和公开遥测；不得为了关卡结果在底层系统加入固定 Tick 特判。

### AT：启动页与解耦测试中心（已完成）

- 正常启动增加中文入口页；默认 RTS 演示在后台完成导航同步后暂停，用户选择进入时原样继续。
- 测试中心展示全部 100 个 `VisualTestCatalog` 场景，支持分类、case id/中文说明搜索、详情查看和直接运行；运行中可手动返回，完成后自动带通过/失败摘要返回。
- 纯 C# `TestShowcaseCatalog` 只维护展示元数据，Godot `RtsLaunchScreen` 只绘制快照并发出 case id，不读取 `MovementTestRig`、Store 或测试场景内部状态。
- 默认演示运行时与测试运行时隔离保存；反复切换时销毁并重建 HUD，避免控件、事件和选择状态叠加。
- `TestShowcaseCatalogSelfTest` 强制每个可执行 case 都有唯一中文说明，验证分类、搜索和未知 case 拒绝；新增 `frontend-test-browser` 黑盒场景走同一全量回归与录像入口。
- 100/100 全量回归通过。专用 AV1/WebM 位于 `test_videos/20260712_203124/`；全仓录像门禁通过 129 个视频、74 个 manifest、128 个场景引用，编码均为 AV1。

### AU：跨电脑 C# 首次还原（已修复）

- 删除 `NuGet.Config` 中开发机 `F:\my_work\...\GodotSharp\Tools\nupkgs` 绝对路径，稳定版 `Godot.NET.Sdk/4.7.0` 只从公开 NuGet v3 源还原。
- 新增 `global.json`：最低选择 .NET SDK 9.0.100，并通过 `latestFeature` 接受机器上更新的稳定 .NET 9 feature band，不会意外滚动到 .NET 10。
- README 明确 Godot 4.7 .NET/Mono、x64 .NET 9 SDK、首次 `restore/build` 和失败缓存清理流程。
- 使用全新空 NuGet 包目录和 `--no-cache` 实测下载 `godot.sourcegenerators`、`godotsharp`、`godotsharpeditor` 后 Release 编译通过，0 错误/0 警告；恢复常规包目录后 Debug 编译和 100/100 Godot 全量回归通过。

### AV：默认大型 Player vs AI 可玩对局（已完成）

- 默认入口从 96/40 个预生成测试单位改为正式玩法对局；`PlayableSkirmishScenario` 是无 Godot 依赖的组合根，地图/资源/开局/AI 配置不进入模拟内核，也不复制玩法实现。
- 地图扩大为 3120×1720，中央上下双通道、四处外围扩张和两组中央富矿；共 78 个资源节点，矿物单点 6500～10000、气矿单点 6000。
- 双方各以 Command Center、12 SCV、1800 Minerals、600 Vespene 开局，初始 SCV 自动采主矿；没有预生成军队、科技或生产设施。
- P2 挂载正式模块化 AI，目标 24 农民、10 人军队出击；建设、采气、生产、科技、扩张、侦察、防守和进攻均通过正式意图执行器与玩家命令完成。
- 正常玩家视角启用迷雾，镜头从 P1 主基地开始；方向键/边缘滚屏、滚轮、小地图、选择、SmartCommand、编组、命令卡、Rally 与建筑/生产/科技流程全部沿用正式交互。
- 删除遗留 `B/X` 直接增删动态 Footprint 调试路径；`B` 现在使用选中农民进入正式 Supply Depot 放置，气矿放置也改为选择可见、未启用且要求 Refinery 的节点。
- `GodotPathProvider` 的同步探针不再固定使用世界中心，而是确定性搜索最近合法点，支持中心被障碍占据的地图；新图烘焙得到 18 个 Godot NavMesh 多边形并同步成功。
- 纯 C# 1800 Tick 验收通过：玩家矿量 1800→2166、敌方发展到 6 座设施、比赛 Running；101 项既有视觉黑盒与新增场景烟测全量通过。
- 30 秒玩家视角 AV1/WebM 位于 `test_videos/20260713_012005/`，1280×720、903 帧、CRF32/preset8。

详细配置见 [默认可玩对局](PLAYABLE_SKIRMISH_DEMO.md)。后续对该 Demo 的扩展应继续修改场景数据或正式系统能力，不在 `RtsDemo` 中增加固定 Tick 玩法脚本。

### AW：SC2 操作与玩法细节对齐研究（已完成研究基线）

- 新增 [StarCraft II 操作与玩法细节对齐研究](SC2_ALIGNMENT_RESEARCH.md)，按 Blizzard 官方、稳定社区资料和必须实机确认三档管理证据，不把论坛观察直接冻结成实现。
- 采矿部分明确 Harvest/Return Cargo 关闭单位—单位碰撞、建筑/地形仍阻挡的碰撞矩阵；核对当前 `WorkerCollisionPolicy`、Steering 与最终圆碰撞均消费同一双向抑制规则，主路径已对齐。
- 记录 SC2 常规/富矿每趟 5/7、矿点占用约 1.99 秒、离开停顿约 0.3571 秒、八片矿两工人长期饱和和三工人气矿等校准数据；这些属于内容参数，不写死到通用内核。
- 建造部分把 Placement Grid、Pathing Grid、Placement Footprint、Hard Pathing Footprint、资源排斥与动态单位占位拆开；当前 `UnitOverlap` 全拒绝和下单即硬占地被列为明确高优先级差距。
- 提出 Preview → Reservation/Ghost → Revalidate/Evict → Hard Footprint 的施工协议，并列出己方 Idle/Move/Hold/Harvest、盟友、可见敌人、隐藏/潜地敌人和晚到单位的 SC2 实机录像矩阵。
- 发现当前施工中建筑仍使用完整基础/升级护甲，而 Blizzard 明确施工结构没有伤害减免；列为独立 P0 黑盒修正项。
- 规划 P0 `economy-mineral-walk-collision-matrix`、`construction-soft-friendly-occupants`、`construction-hidden-enemy-blocker`、`construction-under-build-defense`，以及 P1 自动分矿、显式 Return Cargo、Late Blocker、Shift Build Queue 和生产出口测试。
- 为防止无限对齐，明确只强制控制正确性和高影响经济/战术结果；像素级加速度、完整三族例外和每建筑特殊出口只有出现真实失败证据时继续。

本段只建立研究与验收合同，没有修改运行时行为。下一步应先完成 SC2 当前客户端的友军预占位实机矩阵，再设计 Reservation 格式；在证据确认前不直接实现自动推开规则。

### AX：SC2 对齐实施计划（已完成规划）

- 新增 [StarCraft II 对齐实施计划](SC2_ALIGNMENT_PLAN.md)，把研究基线转换为不含负责人、排期和会议噪音的纯 AI 工作包。
- 固定五层放置边界：Terrain Buildability、Static Pathing、Structure Placement Footprint、Structure Pathing Footprint 与 Dynamic Occupants，禁止继续用一次 `UnitOverlap` 校验承担完整施工时序。
- 固定 Preview → Reservation/Ghost → Dynamic Revalidation/Eviction → Hard Commit 生命周期；接受命令时分配稳定 Building/Reservation ID，Hard Commit 前不分配硬 Footprint。
- 将 C0 静态/动态校验拆分和 C1 Reservation 设计为不依赖实机证据的先行工作；友军 Idle/Hold/Harvest 等精确让位策略仍由 SC2 实机 E0 矩阵冻结，缺证据时保持保守阻挡。
- P0 分为施工正确性与独立 Mineral Walk 碰撞矩阵；P1 固定显式 Return Cargo、批量自动分矿、Shift Build Queue 和生产出口/Rally 边界；P2 内容机制不阻塞核心收口。
- 每个工作包都限定允许修改范围、稳定合同、黑盒用例、Replay/Hot/Hash 要求、AV1 录像门禁和停止条件；每项机制最多一个隔离矩阵加一个复杂对局，防止进入启发式优化地狱。

下一项建议直接实施 `M0 economy-mineral-walk-collision-matrix`：它不依赖 SC2 实机未知项，能先锁住已经正确的采矿碰撞语义；随后实施 `C0` 放置校验分层和 `C1` Reservation/Ghost。若暂时无法取得 SC2 实机录像，可以继续修施工护甲、Return Cargo 与自动分矿，但不提前写死友军让位策略。

### AY：M0 Mineral Walk 碰撞矩阵（已完成）

- 新增 `economy-mineral-walk-collision-matrix` 独立黑盒场景：24 个正式 Worker 分四条矿路往返，前三条穿过友军 Hold、敌军小型单位和大型单位密墙，第四条必须绕过正式施工完成的 Supply Depot。
- 测试不读取 `_unitCollisionSuppressed`、Steering、UnitStore 或 NavMesh 私有数据，只通过正式 Gather/Stop/Hold/Attack/Build 门面与稳定 Worker、Order、Unit、Building、Recovery 快照验收业务结果。
- 去程 24/24、返程 24/24；三类单位车道均记录到真实圆重叠，证明不是从单位旁绕过；建筑穿透样本 0 且确认发生绕行；不可达 0。
- 三个重叠探针在 Mineral Walk 中分别切换 Stop、Hold、AttackTarget，经济状态恢复 Idle、订单正确，并在 2/4/2 Tick 完成正常碰撞分离，远低于 120 Tick 上限。
- 现有正式运行时通过矩阵，无需为测试调整 WorkerCollisionPolicy、Steering 或最终碰撞解算；首轮返程计数异常来自测试误加东侧 DropOff，按正式最近投递语义定位并删除污染夹具。
- Release/Debug 构建均为 0 错误、0 警告；102/102 全量 Godot 黑盒回归通过。
- 专项 AV1/WebM 位于 `test_videos/20260713_113009/`：1280×720、452 帧、CRF 32、preset 8；全仓录像校验通过 133 个视频、77 个 manifest、132 个场景引用，编码均为 AV1。

M0 已收口，不继续调整采矿 Steering。下一项按 [SC2 对齐实施计划](SC2_ALIGNMENT_PLAN.md) 进入 `C0`：把静态 Placement、动态开工占位校验与 Hard Footprint Commit 拆成独立合同，同时保持当前玩家可观察行为不变。

### AZ：C0 建筑放置校验分层（已完成）

- `BuildingPlacementValidator` 不再用单次 `Validate` 混合全部语义；新增 `StaticPlacementResult`、`DynamicStartValidationResult` 和 `BuildingPlacementAssessment`，静态层负责请求合法性、世界边界、静态障碍、既有建筑、clearance 与 Connectivity Guard，动态层只负责活单位占位。
- 新增 `HardFootprintCommitResult` 与 `RtsSimulation.TryCommitHardFootprint`：Hard Commit 在同一权威调用中重新评估分层结果，成功后才原子创建 Dynamic Footprint、触发导航失效和回放世界变更。
- 保留旧组合错误优先级：Invalid/Outside/StaticObstacle/DynamicFootprint 等早期静态失败先返回；随后是 UnitOverlap；最后才是 InsufficientClearance/DisconnectsNavigation。现有 Preview、TryPlaceBuilding、IssueConstruction 的对外结果码和时序不变。
- 稳定 `MovementTestRig` 新增分层评估与 Hard Commit 快照，不暴露 UnitStore、DynamicOccupancyGrid 或 Validator 内部；`building-placement-rules` 独立验证成功、非法尺寸、越界、静态障碍、既有结构、单位占位和窄缝，并保持最终仅两座合法建筑。
- 本包没有新增权威未来态，不升级 Replay Package、Runtime Hot Snapshot 或 State Hash；Reservation 生命周期留给 C1/C4。
- Release/Debug 构建 0 错误、0 警告；102/102 全量黑盒回归通过。Release 移动 256/512/1000 单位 P95 为 1.406/3.789/8.168ms，战斗 128/256 单位 P95 为 0.778/2.245ms，全部通过预算。
- 更新 AV1/WebM 位于 `test_videos/20260713_114013/`：1280×720、182 帧、CRF 32、preset 8；全仓录像校验通过 134 个视频、78 个 manifest、133 个场景引用，编码均为 AV1。

C0 已收口，下一项进入 `C1` Construction Reservation/Ghost：接受建造命令后保存施工意图和扣费，但 Builder 到达前不再创建硬 Pathing Footprint。C1 必须和 C4 的最小持久化骨架连续推进，不能把只存在内存中的 Reservation 合入主线。

### BA：C1 Reservation/Ghost + C4 最小持久化闭环（已完成）

- 新增独立 `ConstructionReservationStore` 与稳定 `ConstructionReservationId`。建造命令成功后立即扣费并创建 Reservation/Ghost；Builder 到达前 `FootprintId=0`，不进入 Dynamic Occupancy、不触发导航失效，普通单位可直接穿过。
- Reservation 参与正式建造命令和通用 Placement 冲突，同位置第二订单在扣费前返回 `DynamicFootprintOverlap`；取消只释放 Reservation 并按 Profile 返还 75%，Refinery 节点预订也随取消可靠释放。
- Builder 到达后重新执行分层 Placement，成功才原子创建唯一 Hard Footprint。接近点使用四边候选、直达优先和真实路径可达性；40px 施工起始容差仍由动态占位校验保证不会在 footprint 内隔空开工。
- 开工时若出现单个己方非施工 Builder 阻挡，`ConstructionStartEvacuation` 按最近合法边确定性撤离；施工 Builder 在 Reserved/Blocked 接近阶段临时忽略单位碰撞，但仍受建筑与地形约束。该策略解决复杂 AI 基地永久悬挂，但 Hold/Harvest 原订单恢复、盟友/敌人/隐藏目标和 1/8/32 多单位槽位仍列入 C2/C3。
- Godot 只从不可变 `GameplayBuildingSnapshot` 读取 Reservation/Footprint/State/Bounds，绘制半透明 `GHOST`；表现层没有读取 ReservationStore。友方建筑 SmartCommand 的非 Builder 单位改为移动到可达建筑外沿，不再把建筑中心当作不可达 Move Goal。
- Construction Command Log v3、Replay Package/Hot Snapshot v20、State Hash v21 保存 Reservation Next ID/条目、可空 Footprint、接受 Tick、接近点和生命周期。Tick 120 的 ReservedApproach 热快照为 3351B，恢复到 Tick 720 与原运行最终 Hash 一致。
- 新增 `construction-reservation-hard-commit` 黑盒录像：验证 Ghost、重复预订拒绝、三单位穿行、晚到友军撤离、取消退款、唯一 Hard Commit 和热恢复。Release/Debug 构建 0 错误/0 警告，103/103 全量回归通过。
- Release 基准通过：移动 256/512/1000 单位 P95 为 1.338/5.058/8.891ms，分配为 315/470/749B/Tick；战斗 128/256 单位 P95 为 1.174/2.686ms，全部在预算内。
- 专项 AV1/WebM 位于 `test_videos/20260713_123959/`，1280×720、362 帧、CRF 32、preset 8；全库录像校验通过 135 段视频、79 个 manifest、134 个场景引用，编码均为 AV1。

C1 与 C4 至此收口。下一项继续 C2/C3 时，只补阻挡分类、多单位撤离和订单恢复，不再重写 Reservation 生命周期；若暂时没有 E0 实机证据，优先推进独立且官方规则明确的 C5“施工中 Armor=0”。

### BB：C5 施工护甲完成边界（已完成）

- 建筑类型数据中的 `Armor` 和 `ArmorUpgradePerLevel` 继续表示完整配置；新增只读 `GameplayBuildingSnapshot.EffectiveArmor` 表示当前真实有效护甲。施工系统的低层快照保证所有未完成生命周期为 0，`RtsSimulation` 门面在 `Completed` 后再合并正式防御科技等级。
- `PreviewCombatDamage`、正式建筑命中和 Godot/UI 观察统一走同一有效护甲规则；测试快照的 `Armor` 现在表示业务可观察的当前值，表现层不再自行判断施工状态或计算科技加成。
- 新增实现无关的 `construction-under-build-defense` 黑盒场景：通过正式 Build、Research、AttackBuilding、Stop、Replay 和 Hot Snapshot 门面，在进度 12.5%、52.5%、98.3%、100% 发起四次真实攻击。
- 实测 Armor 为 `0/0/0/6`，伤害预览与真实 Impact 均为 `20/20/20/14`；Replay、Hot Restore 和原运行最终 Hash 均为 `9A2DB845CEDC72A2`。有效护甲没有进入权威存储，现有 Construction Log v3、Replay Package v20、Hot Snapshot v20、State Hash v21 无需升级。
- Release/Debug 构建 0 错误、0 警告；104/104 全量 Godot 黑盒回归通过。Release 移动 256/512/1000 单位 P95 为 `1.094/4.914/9.200ms`，战斗 128/256 单位 P95 为 `2.265/2.312ms`，全部通过预算。
- 专项录像位于 `test_videos/20260713_131501/`：1280×720、347 帧、AV1/WebM、CRF 32、preset 8；全库录像验证通过 136 段视频、80 个 manifest、135 个场景引用，编码均为 AV1。

C5 已收口。下一步不继续微调建筑护甲；按对齐依赖图，应回到仍部分完成的 C2/C3，但其中 Idle/Hold/Harvest、盟友/可见敌军/隐藏敌军和多单位让位策略仍受 E0 实机证据约束。在没有证据时，可优先推进不依赖未知规则的 P1 `E1` 显式 Return Cargo。

### BC：E1 显式 Return Cargo（已完成）

- 新增正式 `ReturnCargo` 单位订单、结果码、玩家批量门面和命令卡动作；只有携货 Worker 可执行，无货返回 `NoCargo`，无投递点返回 `MissingDropOff`。
- 修复携货 Worker 放派资源会直接清空 Cargo 的旧行为。矿→气、气→矿现在都先投递原 Cargo，再前往保存的新节点；Stop/Hold/Attack/Move 保留货物但清除恢复采集意图，之后显式返还会投递并 Idle。
- DropOff 途中失效会重新选择最近有效建筑边缘并真正更新移动目标；全部失效进入 `WaitingForDropOff`，停止移动但不丢 Cargo，任何有效 DropOff 恢复后自动续投。
- 普通显式返还写入 Economy Command Log v3；Shift Return Cargo 进入 Unit Command Log v4。Replay Package/Hot Snapshot 升级 v21，State Hash 升级 v22，旧版本明确拒绝。
- `economy-explicit-return-cargo` 验证普通返还恢复、Stop 后返还并 Idle、矿↔气双向 Cargo 保留、Shift Return Cargo、经济/单位日志规范往返、Replay 和 Tick 300 热恢复；结果全部为 True，命令数为 `e8/u3`。
- `economy-return-cargo-dropoff-loss` 验证主 DropOff 失效后改道备用点、全部失效后携货等待、`MissingDropOff`、45 Tick 后恢复续投和不可达 0。
- Release/Debug 构建 0 错误、0 警告；106/106 全量 Godot 黑盒回归通过。受影响的经济回放、Shift Worker、96 Worker 采矿和扩张饱和度专项均通过。
- AV1/WebM 位于 `test_videos/20260713_142242/` 与 `test_videos/20260713_142815/`，1280×720、CRF 32、preset 8；在已通过的 138/81/137 全库门禁基础上，重录 Shift Return Cargo 后当前库存为 139 个视频、82 个 manifest、138 个场景引用，新增文件由录制脚本逐项验证为 AV1。

E1 至此收口，不继续调整返矿路径启发式。下一项进入 E2：先拆分普通矿片的实际采集槽、理想分配、等待者和 MULE 独立通道，再实现事件驱动的批量自动分矿。

### BD：E2a 普通 Worker 自动分矿（已完成）

- 资源节点不再用一个 `HarvesterCapacity` 同时表达并发吞吐与软饱和：正式状态拆为 `NormalActiveSlots`、`IdealNormalAssignments`、`ActiveNormal`、`AssignedNormal`、`WaitingNormal`，并预留独立 MULE 计数；普通矿采用 `1` 个实际槽和 `2` 个理想位。
- 新增纯函数 `ResourceAssignmentPolicy`，按 Assigned/Ideal 负载、玩家首选、Worker→矿→DropOff 路程和稳定 Node ID 决胜。玩家批量 SmartCommand、Shift Gather 出队、资源 Rally、基地转场、占用到达和枯竭重选共用同一策略，不逐 Tick 扫描并重排全部农民。
- 节点计数随 Worker 状态迁移增量维护；热恢复解码会从 Worker 状态重新汇总并交叉校验 Active/Assigned/Waiting，负数、槽位越界和不一致快照会明确拒绝。
- `EconomyResourceNodeSnapshot` 暴露普通/MULE 各自 Active/Assigned 和普通 Waiting/Slots/Ideal；`EconomyBaseSnapshot` 分开暴露矿物、瓦斯 Assigned/Ideal，兼容总量只作为派生读值。
- `economy-auto-patch-distribution` 只走公开测试业务门面：同点矿分四批加入 12/16/24/32 Worker，八矿 Assigned 差 `<=1`，最终 `4/4/4/4/4/4/4/4`，每片 `ActiveNormal<=1`，等待队列出现，32 条经济命令 Replay 和 Tick 300 Hot Restore 精确一致。
- 可玩关卡普通/富矿及 96 Worker 压力场景改用 `IdealNormalAssignments=2`。Debug/Release 构建 0 警告；107/107 Godot 全量黑盒回归通过。Replay Package/Hot Snapshot 为 v22，State Hash 为 v23。
- 10 秒专项 AV1/WebM 位于 `test_videos/20260713_145302/`，1280×720、30 FPS、CRF 32、preset 8；全库媒体校验通过 140 个视频、83 个 manifest、139 个场景引用，均为 AV1。

E2a 在普通 Worker 主链收口。下一项是 E2b：实现不计入普通饱和度的单 MULE 独立采集槽并补 `economy-mule-independent-mining`；随后只补 Rally 填空、枯竭收缩和近/远矿边际收入门禁，不继续无证据优化普通分矿权重。

### BE：E2b MULE 独立采矿能力（已完成）

- 新增 `GathererCapability.None/NormalWorker/Mule`。资源采集、返还、批量 SmartCommand 和采矿碰撞读取 Gatherer 能力；施工、基地 Assigned/Ideal、经济 Worker 总数和比赛 Worker/CombatUnit 统计仍只认普通 Worker。
- 普通与 MULE 共用采集状态机和命令协议，但节点计数完全分离：普通通道使用 `ActiveNormal/AssignedNormal/WaitingNormal`，MULE 使用 `ActiveMules/AssignedMules`，每矿最多一个 Active MULE。批量 MULE 用自身 `AssignedMules/1` 负载分散，不占普通软饱和位。
- MULE 只能采 Minerals；对 Vespene 的正式 Gather 返回新增稳定结果码 `CapabilityUnavailable`。当前不引入 Orbital、召唤、寿命、修理、供给或正式 MULE 携带量，避免内容机制反向耦合经济内核。
- Worker 运行时条目保存 Gatherer 能力；热快照解码从每个采集者状态分别重建普通/MULE Active/Assigned 并与节点计数交叉校验。Replay Package/Hot Snapshot 升级 v23，State Hash 升级 v24，旧版本明确拒绝。
- `economy-mule-independent-mining` 验证同矿普通/MULE 双 Active、第二 MULE Waiting、MULE 采气拒绝、八 MULE 自动分散、普通 `16/16` 不变、公开 Worker 数不含 MULE、28 条经济命令 Replay 和 Tick 360 Hot Restore。
- Debug/Release 构建 0 警告，108/108 Godot 黑盒回归通过。12 秒专项 AV1/WebM 位于 `test_videos/20260713_150930/`，1280×720、30 FPS、CRF 32、preset 8；全库媒体校验通过 141 个视频、84 个 manifest、140 个场景引用，均为 AV1。

E2b 至此收口。下一项 E2c 只补现有机制的业务门禁：Rally 新 Worker 填空、矿片枯竭后收缩、近/远矿收入，以及第二/第三/第四 Worker 边际收益；完成后 E2 整体收口。

### BF：E2c 分矿生命周期与收入曲线门禁（已完成）

- 新增 `economy-assignment-lifecycle`，只经公开业务门面下达批量资源 SmartCommand、设置资源 Rally、生产 Worker、推进模拟并读取不可变经济快照；不访问分配器、经济状态机或内部数组。
- 15 个 Worker 同点八矿后形成 `7×2 + 1×1`；Rally 出生的第 16 个 Worker 选择此前唯一欠载矿片，最终形成 `8×2`，证明生产出口没有绕开统一 `ResourceAssignmentPolicy`。
- 一片低储量矿枯竭后，其 `ActiveNormal/AssignedNormal/WaitingNormal` 全部归零；16 个 Worker 自动收缩到余下七矿，Assigned 总数保持 16、最大差为 1。Replay Package 与 Tick 300 Hot Restore 精确一致。
- 新增 `economy-mining-income-curve`：统一携带量 5、占矿 1.99 秒、30 秒窗口下，近矿 1/2/3/4 Worker 收入为 `30/55/65/65`，边际为 `+25/+10/+0`；双 Worker 远矿收入为 `25`。门禁验证单调、递减边际、第四人零增益和矿距惩罚，不把特定测试地图数值写进通用内核。
- 两个场景都拥有独立中文测试中心条目和自动镜头；专项 AV1/WebM、日志与 manifest 位于 `test_videos/20260713_152402/`。
- 当前 147 个 C# 文件、约 48,432 行（含空行）；Debug/Release 构建 0 警告，110/110 Godot 黑盒回归通过；全库媒体校验通过 143 个视频、85 个 manifest、142 个场景引用，均为 AV1。Replay Package/Hot Snapshot 保持 v23，State Hash 保持 v24，本段未修改运行时序列化合同。

E2 自动分矿至此整体收口。后续只允许通过内容 Profile 调整携带量、采集时间与地图矿距；除非真实玩法或新的 SC2 实机证据击穿现有门禁，不再继续优化分配权重。按 [SC2 对齐实施计划](SC2_ALIGNMENT_PLAN.md)，下一项选择已经具备 Reservation/持久化前置条件、且不依赖 E0 未决实机矩阵的 P1 `Q1 Shift Build Queue`；C2/C3 的多单位让位和隐藏敌军策略继续等待实机证据，不凭猜测冻结。

### BG：Q1 Shift 连续建造队列（已完成）

- Build 进入正式 Shift UnitOrder 链：每一项接受时保存完整 Building Profile、Bounds、Refinery 目标和独立 Reservation，资源原子预扣；未来项不创建 Hard Footprint，轮到时重新做静态放置校验，再复用既有动态友军撤离与 Hard Commit 主链。
- 新增只读 `QueuedConstructionPolicy`，明确当前未知项为 `ReserveOnIssue / RefundAndContinue / RefundPendingReservations`。晚期静态失效全额退款并继续；玩家主动取消仍退 75%；Builder 死亡保留当前可续建项，未开始的未来 Reservation 全额退款并取消。
- 修复系统派生 Stop 会误清玩家 Shift 队列的问题：施工/经济内部 Stop 现在只改变移动状态，不覆盖玩家待执行工作流。
- `construction-queued-builds` 只使用测试业务门面，覆盖 8 次 Shift Build、三种不同尺寸、下单扣费、未来硬占地为零、静态失败跳过/全退、动态单位进入/撤离、单项主动取消、Builder 死亡、继续建造、最终回矿、Replay 与 Tick 90 Hot Restore。
- 专项结果：8 次下单均成功，余额 `5000→3750`；两条有效队列完成 `Completed/Canceled/Completed`，死亡 Builder 队列为 `WaitingForBuilder/Canceled`；Replay 与热恢复最终 Hash 精确一致。专项 AV1/WebM、日志与 manifest 位于 `test_videos/20260713_161943/`。
- Construction Command Log 升级 v4，Replay Package 升级 v24；Hot Snapshot 保持 v23、State Hash 保持 v24。当前 147 个 C# 文件、约 48,984 行；Debug/Release 构建 0 警告，111/111 Godot 黑盒回归通过；全库媒体校验通过 144 个视频、86 个 manifest、143 个场景引用，均为 AV1。

Q1 至此收口。下一项按对齐计划进入 X1：生产出口与 Rally 边界，只补方向、软/硬封口、解除恢复及目标死亡时点，不借此重写生产系统或单位碰撞。

### BH：X1 生产出口与 Rally 边界（已完成）

- 新增纯 C# `ProductionExitResolver`：保留 12 个稳定出口候选；有 Rally 时按到目标的距离和候选序号决定优先级，无 Rally 时保持固定顺序。地形、建筑和敌军单位是硬阻挡，友军独占是软阻挡。
- 软阻挡只在生产订单完成并检查出口时触发，最多读取 12 个候选和 32 个稳定 Unit ID；复用既有 `ConstructionStartEvacuation` 寻找合法外侧点，不新增逐 Tick 全局扫描。硬阻挡保持 `WaitingForExit`，阻挡解除后同一订单恢复出生。
- 有效友方单位 Rally 会创建系统派生 `FollowFriendly` 活动订单；目标每移动至少 8 px 才更新路径。目标在出生前死亡时新单位不执行 Rally；出生后死亡时订单转为 Move 并继续到最后位置。派生 Follow 不进入玩家命令日志。
- `production-exit-rally-boundaries` 只通过正式 Build、Train、Rally、Move、Hold、AttackTarget 和稳定观察快照验证四方向、友军软封、敌军硬封、解除恢复与两个目标死亡时序。520 Tick 结果为 `directions=True/True`、`soft=True/True`、`hard=True/True`、`death-before=True/True`、`death-after=True/True`，Replay 与 Tick 300 Hot Restore 精确一致。
- Replay Package 升级 v25、Hot Snapshot 升级 v24、State Hash 升级 v25；Unit Command Log 保持 v4、Production Command Log 保持 v8。Debug/Release 构建均为 0 错误、0 警告，112/112 Godot 黑盒回归通过。
- 专项 1280×720、262 帧、30 FPS、CRF 32、preset 8 的 AV1/WebM、日志和 manifest 位于 `test_videos/20260713_173106/`；全库媒体校验通过 145 个视频、87 个 manifest、144 个场景引用，均为 AV1。
- 当前规模为 148 个 C# 文件、约 49,624 行。

X1 与本轮可执行的 P1 对齐项至此收口。后续 TODO 不再是继续微调出口启发式；施工多单位让位的工程闭环在下一 BI 工作包实现，最终 Hold/Harvest/盟友/敌军策略仍等待 E0，隐藏敌军矩阵等待 Visibility/Detection。P2 的 Power/Creep/Add-on/Lift、三族建造方法和完整 MULE 内容只在实际玩法需要时开启。

### BI：C2/C3 施工多单位让位工程闭环（已完成）

- 新增纯 C# `ConstructionBlockerPolicy`：MovableFriendly、Hold、EconomyTask、AssignedBuilder、OtherOrder、AuthorityEnemy 使用稳定枚举和集中策略表；当前只让 Idle/Stop/Move 友军撤离，其他对象在没有 E0 实机证据时一律 Wait。
- 新增无状态 `ConstructionEvictionPlanner`：按 Unit ID 处理最多 64 个阻挡者，在三圈建筑外沿候选中为不同半径单位贪心分配唯一目标；每个候选同时校验静态世界、非撤离单位和已分配槽位，不把所有人推向同一点。
- 单位撤离改为 `UnitCommandQueueStore` 的系统临时覆盖层，不调用玩家 Move、不清活动订单和 Shift 队列。建筑仍处于 Reservation/BlockedAtStart 时单位停在外沿；Hard Commit 或取消后恢复原 Move，原 Idle/Stop 留在合法外侧。玩家新的非 Shift 命令可以显式覆盖系统计划。
- 占位集合不变且已有活动覆盖时不会重复分配；只有新阻挡者进入才重新生成计划。Hold、采集、其他施工者、其他活动订单和敌军不会被后台启发式改写。
- 新增 `construction-multi-unit-eviction` 黑盒：通过正式 Build→Hold Builder→单位进入→Stop/Hold→ResumeConstruction→Move 门面，验证四档 `48×48 / 72×64 / 112×80 / 160×120` 建筑，实际进入/撤离数为 `1/8/8/32`；五座建筑最终全部 Completed。
- 同一场景验证 Hold 建筑保持 `BlockedAtStart` 直到玩家解除；一个活动 Move 和一个后续队列项在临时让位期间保持，Hard Commit 后继续并完成。Replay、活动 32 人覆盖层 Hot Restore 和逐 Tick Trace 全部一致。
- Replay Package 升级 v26、Hot Snapshot 升级 v25、State Hash 升级 v26；Unit Command Log 保持 v4。113/113 Godot 全量黑盒回归通过。专项 1280×720、382 帧、约 12.7 秒、AV1/WebM、CRF 32、preset 8 位于 `test_videos/20260713_185509/`；全库媒体校验通过 146 个视频、88 个 manifest、145 个场景引用，均为 AV1。
- 当前规模为 149 个 C# 文件、约 50,448 行。

C3 的多单位分槽和订单覆盖机制至此不再重写。普通战争迷雾下的 PlayerKnown 边界由下一 BJ 工作包补齐；最终 Hold/Harvest/盟友/敌军动作仍等待 E0，Cloak/Burrow 的侦测矩阵等待正式 Detection 能力。P2 内容仍按真实玩法需求触发。

### BJ：PlayerKnown 施工放置与 Authority 重验边界（已完成）

- `BuildingPlacementValidator` 支持按调用方知识过滤单位和动态 Footprint；Construction Reservation 查询同样支持局部过滤。几何相关性先于可见性/归属判断，鼠标预览只检查候选附近对象，不形成建筑总数 × 施工实体数的全扫描。
- `PreviewConstruction` 与 `IssueConstruction` 使用同一个 PlayerKnown 合同：己方动态单位不再硬拒绝，可先建立 Reservation；当前可见敌军返回 `UnitOverlap`；当前不可见的敌军单位、Gameplay Building 与 Reservation 不改变公开结果。
- Builder 到场后仍用完整 Authority 世界执行动态阻挡和 Hard Commit。全局 Connectivity Guard 从玩家预览移到 Authority 提交边界；动态阻挡尚未清空时先返回廉价占位结果，不再每 Tick 重跑全图连通性分析。
- `PlayerBuildingViewSnapshot` 新增派生 `PublicConstructionStatus`：只暴露 `None / ClearingFriendlyUnits / KnownOccupant / WaitingForClearance`，不携带 blocker ID、敌我关系或隐藏原因。Godot/UI 继续只消费不可变玩家视图。
- 新增 `construction-player-known-placement` 黑盒，只使用正式 Preview、Build、Hold、Move 和 PlayerView：友军内预放置成功、可见敌军拒绝、隐藏敌军在下单时不出现在视图且预览/命令成功、到场后 Authority 等待、解除后三座建筑全部 Completed；Package/Hot 规范往返通过。
- 该功能没有新增权威未来态，继续使用 Replay Package v26、Hot Snapshot v25、State Hash v26。Debug/Release 均为 0 错误、0 警告；114/114 Godot 全量黑盒回归通过。
- 专项录像位于 `test_videos/20260713_194851/`：1280×720、182 帧、约 6.1 秒、AV1/WebM、CRF 32、preset 8。全库媒体门禁通过 147 个视频、89 个 manifest、146 个场景引用，均为 AV1。
- 当前规模为 149 个 C# 文件、约 50,832 行。

普通战争迷雾下的施工信息边界至此完成。剩余不是继续增加普通隐藏单位特判，而是两个有明确外部依赖的矩阵：E0 实机证据冻结 Hold/Harvest/敌军动作；正式 Ally 与 Cloak/Burrow/Detection 系统出现后补盟友、未侦测和已侦测对象。两者都不应重写 PlayerKnown/Authority 入口。

### BK：Cloak/Burrow/Detection 感知与施工矩阵（已完成）

- 依据 Blizzard Cloaking Guide 与官方 SC2 API，把普通地图视野、单位隐蔽类型、侦测范围和玩家相对显示状态拆开；`UnitPerceptionProfileSnapshot` 独立声明 `None/Cloaked/Burrowed + DetectionRange`，没有把能力字段塞进施工规则。
- `PlayerVisibilitySystem` 在原 32 单位视野网格旁维护每 Tick 派生的 Detection grid。普通视野与侦测同时成立才发布敌方隐蔽单位；己方为 `ConcealedOwn`，已侦测敌方为 `ConcealedDetected`，未侦测敌方不产生 PlayerView 条目。
- PlayerKnown 施工过滤、公开 `PublicConstructionStatus`、玩家显式目标权限和隐蔽目标自动索敌统一消费感知合同。普通目标的既有自动索敌范围没有被 224 视野半径暗改，旧战斗平衡保持不变。
- Burrowed 与普通非 Burrowed 单位的成对接触被抑制；Burrowed/Burrowed 仍接触，采矿/施工现有全单位穿行优先级保持不变。地形和 Hard Footprint 继续阻挡。
- 新增 `concealment-detection-construction` 黑盒：可见地面上的未侦测 Burrowed 阻挡不泄漏且 Preview/Issue 成功，Builder 到场进入 `BlockedAtStart/WaitingForClearance`；Detector 入场后发布 `ConcealedDetected/KnownOccupant` 并开放攻击命令，敌方移走后建筑 Completed。另一路普通单位穿过 Burrowed 单位，原对象不被推移。
- 权威 Profile 进入 Replay Package v27、Hot Snapshot v26、State Hash v27；专项场景验证 Package 规范往返、阻挡中的 Hot Restore 和最终 Hash 精确一致。115/115 Godot 全量黑盒回归通过，Debug 构建 0 错误、0 警告。
- 专项 AV1/WebM 位于 `test_videos/20260713_204122/`：1280×720、212 帧、约 7.1 秒、1,023,759 字节、CRF 32、preset 8。全库当前包含 148 个 WebM、90 个 manifest、147 个场景引用。

D0 至此收口。下一项优先实现独立的 Ally/外交与共享视野合同，再把同一感知查询扩展到 Self/Ally/Enemy；不要在此阶段增加 Cloak 能量、扫描或每族特殊例外。Hold/Harvest/可见敌军施工动作继续保持 E0 Unknown，不因侦测能力完成而猜测。

### BL：D1 阵营、共享视野与联盟胜负（已完成）

- 新增纯 C# `PlayerDiplomacySystem`：正式关系为 Own/Ally/Neutral/Enemy；未配置玩家保持独立 FFA，显式 alliance 使用保留 ID 区间外的稳定编号。`ConfigureAlliance` 只允许在 Tick 0、比赛和录像开始前调用，避免把中途外交混入本轮。
- Shared Vision 是联盟级配置，单位/建筑视野与 Detector 侦测都从来源玩家传播给盟友。PlayerView 新增 Ally 关系和 `ConcealedAlly`，敌方 Cloak/Burrow 仍要求共享或自身普通视野与 Detection 同时成立。
- 战斗自动索敌、显式目标、SmartCommand 和建筑攻击统一查询外交关系；不同 PlayerId 的盟友不会互相攻击。Godot 仅消费关系并使用 Own 蓝、Ally 青、Enemy 红的表现映射。
- 施工新增 `AuthorityAlly` 阻挡类型。可见盟友占位在 PlayerKnown 预览返回 `UnitOverlap`；Authority 默认稳定 Wait，不擅自给盟友下 Move。是否自动让位仍保留 E0，不以工程猜测冒充 SC2 证据。
- `MatchSystem` 改为按存活 alliance 判定终局：同一联盟的多个 Active 玩家同时 Victorious，先出局队友保持 Defeated；多人联盟胜利以 `WinnerAllianceId` 表示，`WinnerPlayerId=-1`。
- 联盟配置和胜方未来态进入 Replay Package v28、Hot Snapshot v27、State Hash v28；载荷验证显式联盟成员数量、ID、排序、共享视野一致性和多胜者联盟一致性。
- 新增稳定门面黑盒 `alliance-shared-vision-team-victory`：2v2 共享侦测、Ally/Enemy 视图、友军攻击拒绝、盟友施工占位、双人联盟胜利、Tick 100 回放和 Tick 300 终局热恢复全部通过。Debug 构建 0 错误、0 警告，116/116 Godot 全量黑盒回归通过。
- 专项录像位于 `test_videos/20260713_211815/`：1280×720、212 帧、约 7.1 秒、1,185,295 字节、AV1/WebM、CRF 32、preset 8。全库媒体门禁通过 149 个视频、91 个 manifest、148 个场景引用，均为 AV1。

D1 至此收口。后续不继续扩展动态外交或共享资源；E0 当前 SC2 客户端实机矩阵仍等待外部客户端。主动 Burrow/Unburrow 后来已按真实内容需求进入 D2a；扫描、能量、持续 Cloak 和种族例外继续按依赖顺序开启。

### BM：E0 施工阻挡证据收敛与保守策略矩阵（工程门禁已完成，SC2 实机待执行）

- 新增 [SC2 施工占位实验结果](SC2_EXPERIMENT_RESULTS.md)，把在线 A/B/C 级来源、可冻结事实、`Unknown` 项和当前项目策略放在同一台账。当前机器没有安装 SC2，因此明确不声称完成“当前客户端实机结果”。
- 在线资料足以确认建筑最终不能和单位重叠、敌方工人可以阻止施工，以及 Hold 是独立的保持位置命令；不足以确认 Hold、Gather/Return Cargo 和 Ally 是否会被建造命令强制改派。这些动作继续保守 `Wait`。
- 新增 `construction-blocker-policy-matrix` 稳定门面黑盒。五条车道分别验证 Own Idle 自动撤离，Own Hold、Own Gathering、AuthorityAlly、AuthorityEnemy 等待且不产生施工撤离；采矿任务的状态和 Target Node 保持不变。
- 已知 Ally 与可见 Enemy 在 PlayerKnown Preview 阶段均拒绝；晚到 Ally/Enemy 在 Authority 阶段等待，只有各自 Owner 发 Move 后才解除。采矿车道取消施工后 Worker 继续 Gathering。
- 五座建筑终态为 `Completed/Completed/Canceled/Completed/Completed`。场景将 Hold、Gather、Build 与不同玩家 Move 分散到独立 Tick，避免跨命令域同 Tick 调用顺序成为回放隐式依赖；初始态、9 个检查点、最终 Replay Package v28 和 Hot Snapshot v27 全部 Hash 精确一致。
- Debug 构建 0 错误、0 警告，117/117 Godot 全量黑盒回归通过。专项录像位于 `test_videos/20260713_214756/`：1280×720、362 帧、2,431,925 字节、AV1/WebM、CRF 32、preset 8。
- 全库媒体门禁通过 150 个视频、92 个 manifest、149 个场景引用，全部为 AV1。

E0 当前只剩真实 SC2 客户端动作矩阵：Idle/Hold/Gathering/ReturningCargo、盟友 Idle/Hold、可见/晚到/未侦测敌军以及必要的 1/8/32 与 Shift 时序。取得原始录像前不再改策略表，也不继续增加施工启发式；现有保守行为已经可玩、确定、可回放且不越权。

### BN：D2a 主动 Burrow/Unburrow 生命周期（已完成）

- 新增独立 `UnitConcealmentController` 与 `UnitConcealmentCapabilitySnapshot`。能力数据保存隐蔽类型、激活/解除耗时、隐蔽视野和移动/攻击许可；运行时使用 `Visible / Activating / Concealed / Deactivating`，没有把种族单位名写入视野、战斗或施工系统。
- `ActivateConcealment / DeactivateConcealment` 成为正式可排队 Unit Order。开始切换会停止当前订单并只失效一次旧路径；StandardBurrow 在激活、潜地和解除期间禁止 Move、AttackMove、目标攻击与 SmartCommand，完成解除后权限恢复。
- 激活完成前仍普通可见；完成后才切换 Burrowed 接触层和 96px 视野。解除完成前仍保持隐蔽。普通地面单位与 Burrowed 异层穿行，Burrowed/Burrowed、建筑和地形的既有阻挡不变。
- PlayerView 为当前可见条目发布过渡 Phase/进度，但只有 Own 条目可切换。侦测、自动索敌、显式目标、PlayerKnown 施工和 Authority 重验继续消费 D0/D1 通用合同，没有主动潜地专用施工分支。
- 能力配置、Phase、过渡剩余时间和基础/当前视野进入 Unit Command Log v5、Replay Package v29、Hot Snapshot v28、State Hash v29；非法枚举、时间、视野和状态组合在解码时拒绝。
- `active-burrow-detection-lifecycle` 只使用稳定业务门面，覆盖激活仍可见、完成后隐藏、视野缩减、行动拒绝、异层穿行、Detector 入场、解除期间仍隐蔽、恢复移动、Shift 往返，以及 Tick 50 过渡态 Hot Restore。Replay/Hot 最终 Hash 与原运行精确一致。
- Debug/Release 构建 0 错误、0 警告，118/118 Godot 全量黑盒回归通过。专项录像位于 `test_videos/20260713_223815/`：1280×720、242 帧、约 8.1 秒、1,414,982 字节、AV1/WebM、CRF 32、preset 8。
- 全库媒体门禁通过 151 个视频、93 个 manifest、150 个场景引用，全部为 AV1。详细边界见 [主动隐蔽能力合同](ACTIVE_CONCEALMENT.md)。

D2a 至此收口。下一项按依赖顺序做 D2b：先建立通用 Caster Energy、地图目标能力与有期限区域效果，再把 Scanner Sweep 作为 Detection Source 接入。不能用玩家全局冷却或 Orbital 特判抢跑；持续 Cloak、研究门槛、Burrow 移动例外和显形特效继续后置。

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
- AV1/WebM：Git LFS；临时 AVI 不入库。
- 当前基线报告：`benchmark_results/latest.json`。
- 录像索引：`test_videos/README.md`。
