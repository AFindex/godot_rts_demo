# S11 实际 RTS 玩法：经济、建造与生产

更新日期：2026-07-11

## 目标

S11 不做只能演示一次的最小采矿脚本，而是建立能继续承载建造、生产、科技、扩张和胜负规则的玩法层。移动内核仍负责位置、路径和碰撞；玩法层持有玩家资源、工作任务和业务结果，Godot UI 只消费快照并发送意图。

参考 SC2 的通用经济语义：矿物是主要通用资源，Vespene 用于高阶内容；矿脉同一时刻只允许一个工人采集，气矿需要精炼设施且通常由三个工人饱和；工人持续执行前往、采集、携带、返还和再次出发，节点最终枯竭。种族差异不写死在通用层。

## S11-A：双资源经济底座（已完成）

### 玩家账本

`PlayerEconomyStore` 按稳定 PlayerId 保存：

- Minerals。
- Vespene Gas。
- Supply Used / Capacity。
- 注册玩家与稳定快照。
- Minerals、Vespene、Supply 的原子扣费。
- 失败不产生部分扣款。
- 取消退款和人口释放。
- 增加人口上限。

稳定结果包括非法玩家、非法成本、矿物不足、气体不足和人口阻塞。这套交易接口将由建造、生产、科技研究共同复用。

### 资源与工人状态机

资源节点声明类型、位置、剩余量、每次携带量、采集时间和并发容量。Vespene 节点可以要求 Refinery，在设施可用前返回 `RefineryRequired`。

工人状态为：

```text
Idle
  -> GoingToResource
  -> WaitingForResource
  -> Gathering
  -> ReturningCargo
  -> GoingToResource
```

当前规则：

- 矿脉容量配置为 1，额外工人等待而不是并行生成资源。
- Refinery 容量配置为 3。
- 采集完成时从节点原子扣除携带量。
- 返回玩家最近且接受对应资源的 DropOff 后才入账。
- 当前节点枯竭后自动寻找最近的同类可用节点。
- 工人死亡或玩家发出普通 Move/Attack/Stop/Hold 时释放采集占用并取消工作。
- 错误所有权、无 DropOff、未建 Refinery 和枯竭节点均返回稳定结果，不隐式执行部分命令。

### 确定性与表现边界

状态 Hash v4 已加入玩家账本、节点余量/占用、工人阶段，以及正式建筑的类型、所有权、施工进度、生命与 Refinery 绑定。无经济工人的旧场景仍走零成本快速路径。

`EconomyOverviewSnapshot` 是 UI 边界。`RtsEconomyControl` 只绘制资源、人口、工人阶段和节点汇总；Godot 世界表现也只读取节点快照，不访问经济内部数组。

经济与建造状态现已纳入 Replay Package v3 与持久化热快照 v3。Gather/Refinery 和 Build/Cancel/Resume 分属独立版本化日志；内部移动、停止、返还货物和 Footprint 变更保持派生，不重复记录。

## 黑盒验收

`economy-dual-resource` 使用 8 个实际采集工人、3 个矿脉和 1 个气矿：

- 先验证矿物/气体/人口扣费、失败不变和 75% 退款。
- 未建 Refinery 时采气被拒绝，启用后 3 个工人并行采集。
- 错误玩家不能控制其他玩家工人。
- 普通 Move 会取消采集工作。
- 第一矿脉枯竭，等待工人自动转移到后备矿。
- 20 秒内实际获得 105 Minerals 和 48 Vespene。
- 8/8 工作工人保持有效循环，节点占用不超过声明容量。

测试通过稳定 `MovementTestRig` 经济接口驱动，不读取 `EconomySystem` 内部集合。录像使用 AV1/WebM。

## 后续顺序

### S11-B：经济确定性格式（已完成）

- `EconomyCommandLogSnapshot` 以格式 1 记录成功的 Gather 和 Refinery operational 变化；未知版本、截断、非法命令和 Tick 逆序均拒绝。
- Return Cargo、自动转矿、内部 Stop/Move 属于确定性派生状态，不进入外部命令日志，避免回放时双重执行。
- Replay Package 升级为格式 2，初始清单保存玩家账本、资源节点、DropOff、工人注册关系和空闲工作态。
- 每 Tick 固定执行世界命令、经济命令、单位命令，再推进模拟。
- 持久化热快照升级为格式 2，保存进行中的工作阶段、目标节点、携带物、剩余工作时间和节点占用。
- Checkpoint 从 Tick 0 重演经济命令；热快照直接恢复活跃采集，不重演此前 Tick。
- `economy-replay-persistence` 在 900 Tick 验证 7 条经济命令、0 条重复派生移动、Tick 300 checkpoint 和 Tick 240 热恢复最终 Hash 精确一致。
- 专用 AV1/WebM 录像位于 `test_videos/20260711_163809/`。

Build/Cancel、Train/Rally 不做没有实体语义的占位命令；它们分别随 S11-C 建造和 S11-D 生产进入同一版本化边界。

### S11-C：建造系统（第一层已完成）

已完成：

- `BuildingTypeProfile` 声明类型、功能、尺寸、双资源成本、建造时间、最大生命、人口贡献、退款率、施工方法和 Refinery 约束。
- Supply Depot、Barracks、Command Center、Refinery 使用 48×48、112×80、160×120、72×72 四种实际尺寸。
- 事务顺序为命令/所有权/资源/放置预检 → 创建导航占用 → 原子扣费 → 创建正式建筑实体；异常提交会回滚 Footprint。
- 工人走到建筑外缘施工点后才开始进度；`ContinuousWorker` 必须持续在场，普通 Move/Gather 会暂停，可重新指派续建。
- `StartAndRelease` 策略边界已经存在，后续种族 Profile 可以选择，不把种族逻辑写进账本。
- 未完工取消移除占用并按 Profile 返还 75%；受击可摧毁，完工人口建筑增加上限，摧毁后安全扣回但不低于已用人口。
- Refinery 必须绑定有效且未占用的 Vespene 节点，完成后节点才 operational，取消/摧毁重新关闭。
- 旧的直接 Footprint 删除接口不能绕过正式建筑生命周期。
- Godot 根据不可变 `GameplayBuildingSnapshot` 绘制类型配色、名称和施工进度，不把资源点或导航矩形伪装成建筑。
- `construction-gameplay-buildings` 在 960 Tick 验证四建筑完工、第五建筑取消、资源精确为 250/300、人口 6/38、暂停续建、生命伤害与 Refinery 启用。
- AV1/WebM 录像位于 `test_videos/20260711_170403/`。

S11-C2 已完成：

- Build/Cancel/Resume 使用 `ConstructionCommandLogSnapshot` 格式 1，Build 保存解析后的完整 Type Profile，不受未来资产调参影响。
- Replay Package v3 保存初始 Construction 清单和建造命令日志；施工派生的 Place/Remove Footprint 不进入 World Command，避免双重执行。
- 每 Tick 顺序固定为世界变更 → 建造命令 → 经济命令 → 单位命令 → Tick。
- 热快照 v3 保存施工接近点、施工者、阶段、进度、生命、类型 Profile 和 Refinery 绑定。
- `construction-replay-persistence` 验证 7 条建造意图、0 条重复 World Command、Tick 360 checkpoint 与 Tick 300 活跃施工热恢复完全一致。
- 专用 AV1/WebM 录像位于 `test_videos/20260711_173933/`。

下一段：建筑成为战斗目标，以及 Godot Resource 化 Building Type Catalog；随后进入 S11-D 生产与人口队列。

### S11-D：生产与人口

- 单位 Type Profile：成本、人口、生产时间、移动/战斗 Profile。
- 每建筑生产队列、取消退款和队列上限。
- Rally Point 支持地面、资源节点和单位目标。
- 出生出口合法位置搜索、被堵等待、重新选点和生产建筑摧毁处理。
- 人口在入队时预留，取消/失败释放，完成时不重复扣除。

### S11-E：科技、扩张和胜负

- 前置建筑、科技等级、升级队列和互斥规则。
- 新基地 DropOff、资源饱和度和工人转场。
- 建筑/单位所有权、可见业务信息和命令权限。
- 玩家失败、关键建筑、生产能力和比赛结束状态。
- 最小脚本 AI 驱动采集、建造、生产和 AttackMove，形成可持续对局。

## 明确不提前耦合

- 不把 Terran/Protoss/Zerg 建造差异写进资源账本。
- 不为经济 UI 直接暴露 SoA 或可变集合。
- 不在建造系统出现前扩写没有消费者的科技字段。
- 不因矿线局部拥堵立刻重写 Steering；先用黑盒录像定位是否是玩法阻塞。
- 不在经济回放格式完成前假装旧热快照支持经济局。
