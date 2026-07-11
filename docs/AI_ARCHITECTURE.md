# RTS AI 架构

当前阶段先固定 AI 的长期边界，再实现具体策略。目标不是把一个脚本塞进 `RtsSimulation.Tick`，而是让经济、生产、侦察、作战策略可以持续替换，同时不破坏确定性模拟、回放和黑盒测试。

## 1. 数据流

```text
RtsSimulation
  -> RtsSimulationAiAdapter.Capture(player)
  -> AiObservationSnapshot
  -> IRtsAiPolicy.Decide(...)
  -> bounded AiIntentBuffer
  -> RtsSimulationAiAdapter.Execute(...)
  -> IssueGather / IssueConstruction / IssueProduction / IssueResearch /
     IssuePlayerMove / IssuePlayerAttackMove
  -> 正式命令日志与模拟
```

AI 只能读取 `AiObservationSnapshot`。快照包含该玩家的 `PlayerViewSnapshot`、资源与人口账本、基地饱和度、己方设施队列和比赛状态。它不暴露 `UnitStore`、路径请求、Steering、动态占用集合或 Godot Node，因此 AI 不会获得玩家本不该知道的敌方信息，也不会依赖底层寻路实现。

策略只产生高层 `AiIntent`：采集、工人转场、建造、生产、研究、移动和 AttackMove。执行器把意图转换为与人类玩家相同的正式命令入口；权限、资源、前置、放置、视野和胜负校验只保留一份。

## 2. 调度与确定性

`RtsAiDirector` 按 Player ID 稳定排序。每个 AI 独立声明：

- 决策周期和 Tick offset；不同 AI 可以错峰，避免同一帧突刺。
- 单次最大意图数，当前硬上限 256；缓冲区满后稳定拒绝，不允许策略无限发命令。
- `PolicyId` 与 `StateFormatVersion`；策略内部计时器、阶段和记忆必须通过 `CaptureState/RestoreState` 保存。

Director 只在约定 Tick 采样一次快照并调用策略。失败的业务意图由正式命令返回拒绝，不允许策略绕过系统直接修状态。

## 3. 回放与存档边界

录像回放保存并执行 AI 已经成功下达的正式游戏命令，**回放时不重新运行 AI**。这样更换策略版本、调参或随机源不会改变旧录像。

实时局的续跑需要成对保存：

- `RuntimeHotSnapshot`：权威模拟状态。
- `RtsAiDirectorSnapshot`：每个 AI 的调度位置和版本化策略状态。

两者恢复到相同 Tick 后继续。Director 恢复时严格校验 Player、Policy ID、格式版本、周期和 offset；不匹配直接拒绝，不能静默丢失策略未来态。AI 状态不会混入纯模拟 Hash，因为它是命令生产者而不是权威世界状态。

## 4. 已实现策略模块

`ModularSkirmishAiPolicy` 已在上述合同之上实现完整的首版对局策略，模块保持独立：

- `EconomyPlanner`：矿气目标比例、基地饱和、采集重分配和跨基地工人转场。
- `BuildPlanner`：供应预警、建筑前置、扩张、候选放置、失败退避和续建。
- `ProductionPlanner`：工人不停产、兵种配比、队列预算和设施扩充。
- `TechnologyPlanner`：科技价值、互斥路线、资源保留。
- `ScoutingPlanner`：已探索区域、最后已知信息、侦察路线与超时。
- `DefensePlanner`：基地半径威胁检测和高优先级显式目标响应。
- `CombatPlanner`：兵力门槛、进攻冷却、最后敌情与 AttackMove 搜索目标；敌方实体可见后改用正式 AttackUnit/AttackBuilding。
- `StrategicBlackboard`：只保存策略记忆和已提交计划，不复制权威经济或单位状态。
- `IntentArbiter`：按供应安全、保命、经济、科技、进攻的稳定优先级裁剪单 Tick 意图。

模块不能直接调用模拟命令；它们提交带优先级、资源成本和互斥键的候选计划，由 `AiIntentArbiter` 统一裁剪。仲裁器维护单次决策的虚拟矿/气/人口账本，并对单位 ID 和设施键做占用，避免同一工人同时采集和施工、同一设施同 Tick 接受两份订单。

观察快照额外明确携带己方工人的工作目标和设施 Builder。经济 Planner 不会重新派走正在施工的工人；如果外部命令确实打断施工，Build Planner 会产生 `ResumeBuild`，继续通过正式施工入口恢复。该规则来自真实闭环测试发现的 Barracks 永久停在 `WaitingForBuilder` 问题，不是测试特判。

## 5. 已有验收

`AiArchitectureSelfTest` 使用完全独立的 fake observation/executor/policy 验证：周期 4、offset 1 的 Tick 调度；单意图容量裁剪；Tick 9 捕获后在新 Director 中从 Tick 13 精确继续；策略状态和执行顺序一致。测试不读取任何模拟内部集合。

`ModularAiPolicySelfTest` 进一步验证开局供给、工人生产、采集、发展期生产/研究/防守仲裁、102 字节 Blackboard 规范状态、截断拒绝和恢复后下一决策完全一致。

`ai-modular-skirmish` 运行 3,600 Tick 的真实业务闭环。AI 最终拥有 10 工人、10 作战单位、Supply Depot/Barracks/Refinery/Academy、两个 Command Center 和 Infantry Weapons；正式日志包含 14 条经济、7 条建造、18 条生产/研究和 8 条单位命令，并在 Tick 1,893 摧毁敌方基地获胜。

## 6. S11-H2 配置、自对战与恢复

AI 配置已经进入版本化 `AiConfigurationCatalogSnapshot v1` 和 Godot Resource。Standard 使用 12 Tick 周期，Aggressive 使用 10 Tick 周期；双 AI 额外采用 0/5 offset，Director 仍按 Player ID 稳定执行。详细字段见 [AI Configuration Resource](AI_CONFIGURATION_RESOURCE.md)。

`RtsAiRuntimeState` 将 Simulation process-local hot state 与 `RtsAiDirectorSnapshot` 绑定在同一 Tick。`ai-dual-runtime-replay` 在 Tick 1,200 捕获两个 AI 的 Blackboard/调度 future，恢复后重新运行策略到 Tick 4,200，最终 Hash 与连续局一致。

同一场景还保存完整 Replay Package（当前格式 v12），并从 Tick 0 按 Construction→Production/Research→Economy→Unit 的协议顺序重放 31/10/51/130 条正式命令。重放过程不创建 Director 或 Policy，最终 Hash 同样一致。为此 Director 在执行边界按相同域顺序稳定排序已被仲裁选中的意图；战略优先级决定选什么，回放协议决定跨域执行次序。

H2 至此收口。撤退、目标价值和更细防守只在以后有明确失败用例时推进，不作为当前无限优化项。
