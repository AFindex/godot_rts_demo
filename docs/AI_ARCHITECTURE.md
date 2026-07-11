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

## 4. 后续策略模块

下一阶段在上述合同之上实现一个完整而非最小的对局 AI。策略内部建议保持以下模块独立：

- `EconomyPlanner`：矿气目标比例、基地饱和、工人转场、扩张触发。
- `BuildPlanner`：供应预警、建筑前置、候选放置与失败退避。
- `ProductionPlanner`：工人不停产、兵种配比、队列预算和设施扩充。
- `TechnologyPlanner`：科技价值、互斥路线、资源保留。
- `ScoutingPlanner`：已探索区域、最后已知信息、侦察路线与超时。
- `CombatPlanner`：集结、兵力门槛、防守响应、AttackMove 目标。
- `StrategicBlackboard`：只保存策略记忆和已提交计划，不复制权威经济或单位状态。
- `IntentArbiter`：按供应安全、保命、经济、科技、进攻的稳定优先级裁剪单 Tick 意图。

模块不能直接调用模拟命令；它们提交候选计划，由策略统一仲裁后写入 `IAiIntentSink`。这保证未来增加难度层、不同种族策略或行为树时，不需要改模拟核心。

## 5. 已有验收

`AiArchitectureSelfTest` 使用完全独立的 fake observation/executor/policy 验证：周期 4、offset 1 的 Tick 调度；单意图容量裁剪；Tick 9 捕获后在新 Director 中从 Tick 13 精确继续；策略状态和执行顺序一致。测试不读取任何模拟内部集合。

当前只完成宿主架构，尚未把“会经营并打完一局”的具体策略标记为完成。
