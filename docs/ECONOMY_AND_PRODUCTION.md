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

状态 Hash v3 已加入玩家账本、节点余量/占用、DropOff、工人阶段、目标、携带物和工作计时。无经济工人的场景走零成本快速路径，不在每 Tick 扫描所有单位。

`EconomyOverviewSnapshot` 是 UI 边界。`RtsEconomyControl` 只绘制资源、人口、工人阶段和节点汇总；Godot 世界表现也只读取节点快照，不访问经济内部数组。

经济状态现已纳入 Replay Package v2 与持久化热快照 v2。外部 Gather/Refinery 意图使用独立版本化日志；前往资源、停止采集、返还货物和再次出发仍由状态机派生，不重复记录为玩家移动命令。

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

### S11-C：建造系统

- 建筑 Type Profile：双资源成本、建造时间、生命、Footprint、Supply、前置科技。
- Placement 成功后原子扣费，创建 UnderConstruction 实体。
- 建造进度、受击、取消、退款、工人死亡和建筑完成。
- Refinery 必须绑定 Vespene 节点。
- 通用建造策略接口承载 SCV 持续施工、Probe 启动后离开、Drone 消耗自身等差异。

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
