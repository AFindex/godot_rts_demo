# 命令队列、Control Group 与 SmartCommand

更新日期：2026-07-12

## 语义来源与当前边界

这一层参考 Blizzard 的 [Simplified Controls](https://news.blizzard.com/en-us/article/6640645/game-guide-simplified-controls)、[Special Control](https://news.blizzard.com/en-us/article/4552955/game-guide-special-control) 与 [Legacy of the Void Beta 2.5.3](https://news.blizzard.com/en-gb/starcraft2/19821986/legacy-of-the-void-beta-patch-2-5-3)：Shift 追加顺序命令，Ctrl+数字覆盖编组，Shift+数字添加，Alt 组合执行抢组，数字键召回。

Control Group 只保存选择集合，不持有共享命令。命令始终进入每个单位自己的队列。

## 每单位命令队列

- `UnitCommandQueueStore` 使用固定 SoA 环形队列，每单位最多 16 条待执行命令。
- 支持 Move、AttackMove、AttackTarget、AttackBuilding、Stop、Hold、GatherResource 和 ResumeConstruction 的活动命令记录。
- Shift+SmartCommand 追加；不带 Shift 的命令清空全部待执行命令并立即替换。
- Stop/Hold 当前作为终止命令：立即清空队列，不参与 Shift 追加。
- 队列满时拒绝新命令并累加 overflow，不分配新容器、不覆盖旧命令。
- 同一次选择发出的命令带相同 SequenceId；单位按个体进度完成前序命令。
- 同 SequenceId 且恰好在同 Tick 到期的单位会重新批量发令，继续复用群组槽位、共享 Portal 路线和路径预算。
- 死亡会清空该单位队列；Control Group 召回时过滤死亡单位。

## Control Group

- Ctrl+0～9：覆盖对应组。
- Shift+0～9：把当前选择添加到对应组，不改变当前选择。
- Alt+0～9：覆盖对应组，同时把当前选择从所有其他组移出。
- Alt+Shift+0～9：添加到对应组，同时把当前选择从所有其他组移出。
- 0～9：召回仍存活/未销毁且仍归玩家控制的单位和建筑，按 Kind/Entity ID 稳定排序。
- 0～9 在 0.35 秒内再次召回同一组：把镜头定位到当前存活成员的中心。
- 编组只保存纯 C# `ControlGroupEntity(Kind, EntityId)`，不持有 Node、Godot 对象或业务系统引用；实体生命期和所有权由召回者提供。
- 编组列表在写入时去重并保持 Unit→Building、Entity ID 升序；建筑 ID 不依赖预设容量。

单位/工人/建筑混合选择子组和 Tab 切换在 S11-J1 完成；建筑混合编组和 Alt 抢组在 S11-J2a 完成。

## 选择过滤与双击

- `SelectionFilter` 只接收稳定候选快照，不读取 Godot Node、UnitStore 或选择框状态。
- 点选按命中距离选择友军；同距离使用稳定 Unit ID。
- 框选过滤死亡单位和非玩家阵营，结果按 Unit ID 排序。
- 双击单位会选择当前相机可见区域内相同 `TypeId` 的存活友军。
- `TypeId` 由上层实体/Gameplay Profile 提供；选择系统不使用半径或速度猜测单位类型。

## 相机

- 屏幕四边 18px 边缘滚动；方向键提供等价键盘移动。
- 滚轮缩放范围 1.0～2.4，缩放前后的光标世界坐标保持不变。
- 平移速度按缩放反比调整，并在世界边界内夹紧。
- 编组双击以存活成员平均位置定位，不改变当前缩放。
- `OperationCameraController` 使用纯 `System.Numerics`，Godot `Camera2D` 只做显示适配。

## SmartCommand

```text
地面             → Move
友军单位位置     → Move
敌军单位         → AttackTarget
资源点           → 工人 Gather；非工人 Move
等待施工者的己方建筑 → 最低 ID 合格工人 Resume；其余单位 Move
A 修饰的任意目标 → AttackMove
```

AttackTarget 使用现有战斗槽、追击与攻击结算，但锁定目标死亡后停止，不像 AttackMove 那样自动重选敌或恢复路线。

资源与友方建筑上下文由纯 C# 模拟层拆分混合选择，不由 Godot UI 猜测单位能力。拆分前先验证全部工人采集条件，避免一组选中单位只执行一半；续建按稳定 Unit ID 选择唯一施工者。Gather/Resume 会清空对应工人的旧单位队列和旧经济工作，连续运行与 Replay Package 重放走同一正式入口。

Shift+资源和 Shift+待续建建筑会生成正式 `GatherResource` / `ResumeConstruction` UnitOrder。目标资源 ID 使用独立字段，不复用 Building ID；任务在未来 Tick 出队时重新检查目标、所有权、余量、DropOff、建筑阶段和工人资格。失效任务会在一个 Tick 内完成并继续下一条队列命令，不会卡住整条工作流。

即时 Gather/Resume 也登记为活动任务，因此后续 Shift 命令会等待资源工作结束或施工完成。经济/施工系统产生的内部 Move/Stop 只改变移动状态，不覆盖玩家活动任务类型。

Godot Demo 中普通命令显示单圈反馈，Shift 队列显示双圈反馈；敌军锁定攻击和 AttackMove 使用红色反馈。

## 命令卡目标模式

- `TargetCommandRequest` 冻结命令类型与当前活动子组的稳定 Unit/Building ID；不会持有选择控件、Node 或模拟引用。
- `TargetCommandResolver` 只解析 Primary/Secondary、世界坐标和 Shift：左键确认，右键取消；Move/AttackMove/Build 的 Shift 确认进入队列并保持目标模式，后续以右键或 Escape 退出。
- Move、Attack Move 分别进入正式 `IssuePlayerMove` / `IssuePlayerAttackMove` 玩家门禁；Rally 对活动同类型生产建筑子组执行正式 `SetProductionRallyTarget`，继续支持 Ground/Resource/FriendlyUnit。友军目标在新单位出生后形成系统派生 Follow，目标死亡后继续到最后位置；目标若在出生前已死亡则不执行 Rally。该派生状态进入热快照与 Hash，但不伪装成玩家 Unit Command。
- `RtsTargetCommandOverlay` 只消费请求与光标世界坐标，独立绘制颜色、准星和提示；换样式、文案和动画不修改输入解析或业务命令。
- 编组召回、Space 全选会取消未完成目标模式；请求中的实体在确认时仍由正式业务 API 再验证生命期、所有权和比赛状态。

### Build 放置

- Worker 活动子组按 `BuildingTypeCatalogSnapshot` 生成 Supply Depot、Barracks、Command Center、Refinery、Academy 动作；尺寸、费用、功能和最小通行等级都来自 Catalog。
- `RtsSimulation.PreviewConstruction` 是无副作用正式入口，与 `IssueConstruction` 共用比赛、工人、资源、Refinery、footprint、单位重叠、净空和 Connectivity Guard 验证；预览不会创建动态占用、扣资源或写命令日志。
- 光标中心按 8px 网格吸附。Refinery 在 Vespene 节点 32px 内吸附到节点中心；否则明确显示 `RefineryNodeRequired`。
- 多选 Worker 按目标距离、Unit ID 稳定排序，逐个调用正式预览并选第一个合格工人；因此忙碌或失效工人不会阻塞仍可用的近邻工人。
- 预览按吸附位置缓存，并至多每 6 Tick 刷新相同位置的资源/生命期状态，避免每渲染帧重复执行 Connectivity Guard。
- `BuildTargetPreviewSnapshot` 只向 Overlay 提供 bounds、builder、resource、CanPlace 和稳定状态；Overlay 绿色显示 Success，红色显示具体 Construction/Placement Code。
- 左键只在最新正式预览通过时调用 `IssueConstruction`；失败保留目标模式以便改位置。普通成功退出；Shift 成功创建软 Reservation 并保持目标模式，可继续摆放下一座。右键/Escape 始终无副作用取消。
- Shift Build 接受时预扣资源并保存完整解析后 Profile，但未轮到的 Reservation 不创建 Hard Footprint。执行时重新验证静态放置；失效项全退并继续，动态友军仍走既有确定性撤离；玩家主动取消保持 75% 退款。
- 施工多单位让位使用独立系统临时覆盖层：Idle/Stop/Move 友军可被分配到唯一外沿槽，但其活动 Move 和 Shift 队列不被改写；Hard Commit 后继续原订单。Hold、采集、其他施工者/订单和敌军在 E0 证据冻结前保持等待。
- 施工和经济系统派生的 Stop 不会清空玩家 Shift 队列。Builder 死亡时，已经轮到的可续建 Reservation 保持 `WaitingForBuilder`，其未开始未来项全退并取消。

### 同类型多建筑生产

- 语义参考 [Blizzard Buildings Guide](https://news.blizzard.com/en-us/article/4488317/game-guide-buildings)：同类型生产建筑组成活动子组后，一次 Train 意图会扇出到所有当前可用生产者。
- `ProductionGroupSnapshot` 是纯 C# 聚合合同，只保存稳定 Building/Order ID、Recipe ID 和进度；UI 不读取 `ProductionSystem` 内部队列。
- `ProductionBatchPlanner` 按 Building ID 生成确定性计划并汇总 `ready X/N · queued Q`。执行阶段逐个调用正式 `IssueProduction`，共享资源、人口、前置和队列容量仍由业务层重新验证。
- 一次批量取消对每座生产建筑只撤销最新一条匹配配方订单；不同配方不会被误删，也不依赖命令卡当前布局或按钮索引。
- Rally 继续作用于活动同类型建筑子组；未完成建筑则聚合为 Cancel Construction，不参与 Train。

## Minimap 与 UI 解耦

Minimap 按三层组合，表现层可以高频换皮、改布局或加入动效，而不修改模拟和命令语义：

- `MinimapSnapshot`、`MinimapTransform`、`MinimapInteractionResolver` 是纯 C# 数据与规则，只表达世界边界、可见区域、障碍、标记和世界坐标意图。
- `RtsMinimapControl` 是纯表现 Control，只消费快照并发出 `FocusRequested` / `SmartCommandRequested`；它不持有 `RtsSimulation`，也不直接发命令。
- `RtsDemo` 是薄组合层，负责从现有世界生成快照，并把世界坐标意图接到 `OperationCameraController` 和既有 SmartCommand 解析。

当前支持静态障碍、友军/敌军、选择高亮、不同 footprint 建筑和视口框；左键或左键拖动定位镜头，右键发出 SmartCommand，A/Shift 修饰语义与主视图一致。

## 黑盒验收

- `queued-waypoints`：依次完成三段 Move，两个 Shift 命令均被记录为完成。
- `queued-command-replace`：即时命令清空两条待执行命令并到达新目标。
- `queued-capacity-limit`：16/16 待执行，额外两条得到 2 次显式 overflow。
- `control-group-recall`：Ctrl 覆盖 4 人、Shift 添加 2 人、召回 6 人并全部到达。
- `control-group-mixed-steal`：混合保存 2 Worker、Marine 与 Barracks；Alt 覆盖抢组和 Alt+Shift 添加抢组后，原组只剩 Marine，新组得到 2 Worker + Barracks，三个可移动单位全部按召回结果到达。
- `smart-command-sequence`：友军位置 Move → 敌军 AttackTarget → 地面 Move，目标死亡且两个 Shift 命令完成。
- `smart-command-gameplay-context`：乱序混合选择右键矿点后两名工人采集、Marine 移动；右键待续建建筑稳定选择最低 ID 工人，Package 重放后 Hash 一致。
- `smart-command-shift-worker-tasks`：同时验证 Move→Gather、Move→Resume 和 Move→已枯竭资源→Move；Tick 60 的三条待执行跨域任务可由 Hot Snapshot v12 精确恢复，失效任务有界跳过，Package v12 最终 Hash 一致。
- `operation-selection-camera`：稳定点选、可见同类型双击、友军框选、光标锚定缩放、边缘滚动和编组双击定位全部通过。
- `minimap-interaction`：世界/面板坐标往返、视口框、定位意图、SmartCommand 意图和边界外拒绝全部通过，并录制真实 Minimap Control。
- `operation-mixed-command-card`：2 Worker、1 Combat Unit、1 Barracks 形成 3 个子组；快照命令卡完成生产、取消和重新生产，最终出生单位。
- `operation-target-command-mode`：两段 Shift Move、右键取消、Ground Rally、Attack Move 全部通过稳定测试 Facade，目标模式保持/退出状态正确且 3/3 单位到达。
- `operation-build-placement-mode`：静态障碍返回 `StaticObstacleOverlap`、不修改世界并保持目标模式；合法预览选择最近 worker 1，提交后 Supply Depot 完成。
- `construction-queued-builds`：八次 Shift Build 覆盖三种尺寸、预扣、软 Reservation、晚期静态失效、动态撤离、75% 取消、Builder 死亡、继续回矿、Replay 与热恢复。
- `construction-multi-unit-eviction`：四档建筑覆盖 1/8/8/32 友军唯一分槽、Hold 保守等待、Move/Shift 队列保留，以及活动撤离期间的 Replay/Hot 精确恢复。
- `operation-production-group-batch`：3 座 Barracks 两次批量 Train 得到 6 个订单；每建筑取消最新订单后剩 3 个，最终出生 3 个 Marine。

## 当前收口

操作表现已覆盖选择、混合子组、快照命令卡、Move/AttackMove/Rally/Build 目标模式、同类型多建筑批量生产/取消/队列聚合、相机、混合编组/Alt 抢组、编组定位和 Minimap。图标、tooltip、可重映射热键、皮肤与动画属于后续表现增强，不阻塞玩法闭环。
