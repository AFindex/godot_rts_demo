# Godot RTS 移动系统：进度回顾与剩余 TODO

更新日期：2026-07-10

这份文档是当前唯一的实施状态入口。它对照最初的 S0～S10 技术路线，区分已经形成可运行闭环的部分、只有原型的部分和尚未开始的部分。

## 1. 当前结论

项目已经具备一个可运行、可测试、可录像、可测性能的纯 C# RTS 移动内核：

- Godot 4.7 .NET 负责输入、绘制、NavMesh 查询和调试表现。
- 固定 Tick 模拟、单位数据、群组目标、Steering、碰撞、动态建筑、Portal 和狭口交通位于纯 C# 层。
- 33 个黑盒业务场景通过稳定测试接口驱动，不直接读取路径点、Steering 或 UnitStore 内部状态。
- 测试可以自动录制 AVI，并通过 Git LFS 保存在仓库中。
- 独立纯 C# Release 基准覆盖 256、512 和 1000 单位。

当前规模：

- 33 个 C# 源文件。
- 约 8,637 行 C#。
- 33 个黑盒场景。
- 覆盖 33 个逻辑场景的规范测试录像。
- Release 1000 单位 P95：约 10.07ms。
- Release 1000 单位当前线程分配：约 461B/Tick。

这已经是“可继续构建 RTS 游戏的移动内核原型”，还不是完整的《星际争霸 2》级移动、战斗和操作系统。

## 2. S0～S10 总览

| 阶段 | 状态 | 已完成 | 仍缺少 |
|---|---|---|---|
| S0 工程骨架 | 原型完成 | Godot 工程、纯 C# 模拟、固定 Tick、Godot 桥 | 独立 Runtime/Core/Editor 程序集边界 |
| S1 单位移动 | 完成 | Move、Stop、Hold、加速度、速度积分、到达 | 转向模板、不同运动类型配置资源 |
| S2 静态导航 | 原型完成 | Godot NavMesh、路径预算、命令版本隔离、Grid fallback | 路径缓存、后台查询、NavMesh chunk |
| S3 群体抵达 | Demo 完成 | 唯一槽位、Hungarian 分配、跨命令预留、两单位换槽、局部多单位重匹配、进入方向秩序、主动 Yielding、唯一 Overflow | 生产级 SlotDepth 场、完整碰撞优先级、编队形状保持 |
| S4 局部群体运动 | 原型完成 | SpatialHash、TTC、候选速度、避让侧记忆 | 更低成本的候选评估、复杂优先级、移动类型交互 |
| S5 碰撞与约束 | 第一层完成 | 圆碰撞、静态墙约束、动态建筑占用、Small/Medium/Large、按尺寸 Grid/Portal/动态净空 | 放置前连通性检查、多移动层、非矩形 footprint |
| S6 高层路线与动态地图 | 大部分完成 | Portal A*、群组路线、动态 revision、局部失效、同命令批量共享改道、建筑移除恢复 | Sector、共享 corridor、Portal 自动生成、chunk 局部更新 |
| S7 狭口与卡死 | 大部分完成 | 车道、双向 admission、容量、排空、公平性、Hold 堵口、恢复阶梯 | 多连续狭口、复杂死锁、终点拥堵专用恢复 |
| S8 战斗移动 | 未开始 | 无 | AttackMove、攻击槽位、追击、leash、恢复原路线 |
| S9 编辑器与数据烘焙 | 基础完成 | Godot Resource、纯 C# snapshot、格式版本、固定错误码、稳定哈希、CLI Validator/Generator | 自动 Baker、几何拖拽、连线和多尺寸 Preview |
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

## 4. 测试、录像和性能

### 4.1 已有黑盒场景

当前 33 个场景覆盖：

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
- 当前仓库包含覆盖 33 个逻辑场景的规范录像。
- AVI 使用 Git LFS。

注意：当前规范录像来自多个功能里程碑批次，并非全部在同一个 commit 上重新录制。发布正式版本前应执行一次全量重新录制，生成单一时间戳目录。

### 4.3 Release 基准

独立 `net9.0` Release 基准直接编译 Simulation 源文件：

| 单位数 | 平均 Tick | P95 | 当前门槛 | 分配/Tick |
|---:|---:|---:|---:|---:|
| 256 | 1.18ms | 1.69ms | 4ms | 27B |
| 512 | 4.02ms | 4.84ms | 12.5ms | 182B |
| 1000 | 8.12ms | 10.07ms | 16.67ms | 461B |

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

### 5.3 地图数据管线仍缺自动烘焙和预览

主 Demo 已从 `data/demo_navigation_map.tres` 加载地图，经过验证后转成纯 C# 快照。`DemoMapDefinition` 只作为不依赖 Godot 的测试夹具和示例资产重建源，不再参与主 Demo 启动。

TODO：

- 从 NavMesh/场景几何自动提取 Sector 和 Portal。
- EditorPlugin 中的 Portal/Choke 拖拽与连线。
- Small、Medium、Large 多尺寸连通性预览。
- 格式版本迁移器。
- Resource 热重载和差异诊断。

### 5.4 动态导航仍是混合方案

Godot NavMesh 处理静态地图；动态建筑通过占用栅格验证和按 Movement Class 缓存的 Grid A* fallback 处理。放置后的路径判定已经按单位导航半径工作。

TODO：

- NavMesh chunk 局部重烘焙。
- 非矩形和旋转 footprint。
- 建筑放置合法性与 clearance 检查。
- 建筑压住单位时的安全迁移。
- Portal/Sector 局部连通性更新。

### 5.5 单位尺寸与地形类型

当前已形成 Small/Medium/Large 三档运行时尺寸语义。Grid、Portal Edge、Godot 路径复验和动态占用统一使用向上取整的导航半径；物理碰撞和槽位仍使用真实半径。详见 `CLEARANCE_AND_MOVEMENT_CLASS.md`。

TODO：

- UnitMovementProfile Resource 和编辑器多尺寸连通性预览。
- 地面、悬浮、空中等移动层。
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

- UnitMovementProfile Resource。
- BuildingFootprint Resource。
- Occupancy/Clearance Baker。
- Sector/Portal Authoring Tool。
- Connectivity Validator。
- 多单位尺寸导航预览。
- 格式迁移器。

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

留在后续 S9：自动 Baker、编辑器几何工具、Preview 和格式迁移。

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
- 1000 单位 P95 继续低于 16.67ms，分配低于 1KB/Tick。（10.07ms / 461B）

明确收口边界：当前不会继续加入完整挤压优先级、全局 SlotDepth 场或为了减少少量 Overflow 而反复调参；这些必须由后续实际玩法需求重新驱动。

### 下一步 C：Clearance 与 Movement Class（第一层完成，继续推进）

已完成：

- Small、Medium、Large 运行时等级与导航半径。
- 大小单位按声明宽度选择不同 Portal。
- Grid 三档连通性缓存和动态 revision 失效。
- Godot NavMesh 候选路径按单位净空复验并回退对应 Grid。
- 动态建筑窄缝对 Small/Large 给出不同且有界的结果。

下一层：建筑放置前 connectivity/clearance 合法性检查、UnitMovementProfile Resource 和编辑器多尺寸 Preview。

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
