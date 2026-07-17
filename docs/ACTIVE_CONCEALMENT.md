# 主动隐蔽能力合同

本文记录 Cloak/Burrow 内容能力与既有视野、侦测、碰撞、战斗和施工系统之间的稳定边界。实现位于纯 C# 模拟层；Godot/UI 只读取玩家视图快照。

## 1. 数据与状态

`UnitConcealmentCapabilitySnapshot` 描述单位是否可以主动切换隐蔽，以及：

- 隐蔽类型：`Cloaked` 或 `Burrowed`。
- 激活和解除耗时。
- 隐蔽后的视野半径。
- 隐蔽时是否允许移动、攻击。

运行时状态使用 `Visible → Activating → Concealed → Deactivating → Visible`。能力配置和当前状态分离：永久隐蔽测试/特殊单位仍可只声明当前 `ConcealmentKind`，不会被错误套用普通潜地的移动限制。

过渡时序冻结为：

- `Activating` 完成前仍按普通单位被敌方看见和碰撞。
- 激活完成 Tick 才切换为 `Burrowed/Cloaked`，同时切换视野和接触层。
- `Deactivating` 全程仍按隐蔽单位处理；完成 Tick 才恢复普通可见、普通接触和基础视野。
- 普通潜地配置在激活、潜地和解除期间均禁止移动与攻击；可移动潜行单位通过能力数据开启，不在系统中写单位类型分支。

## 2. 命令合同

主动切换是正式 `ActivateConcealment / DeactivateConcealment` 单位订单，不是表现开关：

- 经过玩家所有权、比赛状态和单位能力校验。
- 支持 Shift 队列；例如 `Burrow → Unburrow → Move` 可复用通用单位队列。
- 非队列命令只接受与当前稳定状态相符的切换，过渡中重复点击返回稳定的 `ContextActionUnavailable`。
- 开始切换会停止当前移动/战斗并只失效一次旧路径请求；后续 Tick 不重复增加命令版本。
- 被能力禁止的 Move、AttackMove、目标攻击和 SmartCommand 不进入模拟，也不会留下半条订单。

## 3. 下游系统消费方式

- `PlayerVisibilitySystem` 使用每单位当前视野半径；潜地缩短视野不会修改全局常量。
- PlayerView 对当前可见条目发布 Phase/进度以驱动过渡表现，只有 Own 条目拥有切换权限；敌方在激活完成前仍可见，完成后必须同时满足普通视野与侦测。
- 战斗系统通过能力查询决定能否攻击；不复制潜地枚举判断。
- 单位碰撞继续只读取当前 `ConcealmentKind`：普通地面单位与 Burrowed 异层穿行，Burrowed/Burrowed 仍接触，建筑和地形始终阻挡。
- PlayerKnown 施工与 Authority 重验继续使用既有感知和占位合同；主动潜地没有专用施工分支。
- `AbilityEventStream` 发布内容中立的 `active-concealment` / `active-reveal` 生命周期：命令正式开始为 `Started`，过渡完成同 Tick 发布 `Impact` 与 `Ended(Completed)`，过渡中死亡发布 `Interrupted(CasterDied)`。音频、特效和 UI 只消费事件，不反查本控制器的内部计时。

## 4. 持久化与黑盒门禁

能力、Phase、剩余过渡时间、基础/当前视野进入：

- Unit Command Log v5。
- Replay Package v29。
- Hot Snapshot v28。
- State Hash v29。

`active-burrow-detection-lifecycle` 只通过稳定业务门面验证激活过程、隐藏、视野缩减、行动限制、无效队列拒绝、异层穿行、侦测授权、解除过程、Shift 切换、中途热恢复，以及完整技能生命周期和施法者死亡中断。专项录像位于 `test_videos/20260713_223815/`。

## 5. 明确后置内容

本阶段不伪造 Orbital 或全局玩家能量。Scanner Sweep 必须建立在通用施法者能量、地图目标能力、持续区域效果和研究/生产前置之上，再向现有 Detection grid 注入有期限的侦测源。Cloak 的持续能量消耗、研究门槛、Burrow 移动例外和显形表现同样作为数据化内容扩展，不修改本文的状态与下游合同。
