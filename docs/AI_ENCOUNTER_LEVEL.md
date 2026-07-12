# 双 AI 遭遇战测试关卡

`ai-continuous-encounter` 是一个面向观看和业务验收的 60 秒双 AI 关卡。它与 `ai-dual-runtime-replay` 的职责不同：后者验证 AI 热恢复和命令回放协议，本关卡验证玩家实际能看到的开局、发展、科技、扩张、持续进攻、战损和补兵过程。

## 解耦结构

关卡位于测试编排层，不进入 `RtsSimulation.Tick`，也不为 AI 增加关卡特判：

```text
AiEncounterLevelDefinition
  地图边界 / 静态障碍 / 资源簇 / 双方出生配置 / AI Profile
          ↓
AiEncounterLevelOrchestrator
  Prepare → Start recording → Begin → StartEconomy → AttachAi
          ↓
IAiEncounterLevelRuntime
  只允许注册玩家、生成工人/资源、正式建造/采集、挂接 AI 和读取快照
          ↓
MovementTestRigAiEncounterRuntime
  把关卡合同适配到稳定业务测试门面
          ↓
正式 Economy / Construction / Production / Technology / Unit 命令
```

`AiEncounterLevelDefinition` 是纯数据：双方使用同一地图和镜像自然矿，但可以选择不同 `AiDifficultyProfile`、决策周期和 offset。关卡没有直接增加资源、生成军队、强制研究或替 AI 发攻击命令；初始战略储备用于保证科技与扩张都能进入 60 秒观察窗口，后续经济仍由真实工人采集。

`IAiEncounterLevelRuntime` 不暴露 `UnitStore`、`CombatStore`、路径、Steering、生产队列内部数组或 Godot Node。以后底层模拟、寻路或表现替换时，关卡定义和业务期望不需要跟着修改；只有适配器负责转换稳定 ID 和命令结果。

## 编排生命周期

1. `Prepare` 创建资源、注册玩家和初始工人，并建立比赛参与者。
2. Replay Package 在所有初始权威状态完成后开始录制。
3. `Begin` 通过正式施工命令创建高生命起始基地；基地不是直接塞进建筑 Store。
4. Tick 60 基地完成后，`StartEconomy` 下达初始采集，避免未完成基地没有 DropOff 的非法命令。
5. Standard/Aggressive AI 以 0/5 Tick offset 挂接，之后关卡不再干预双方决策。

地图为 1500×850 的镜像战场。中线上下两块障碍形成中央遭遇通道；双方主矿、气矿和自然矿独立，远端工人提供自然矿初始视野。起始基地拥有 12,000 HP，确保观察窗口用于持续交战而不是过早结束比赛。

## 独立遥测

`AiEncounterTelemetry` 每 30 Tick 读取一次不可变宏观业务快照；工人循环则每 Tick 读取稳定的 `AiEncounterWorkerSignal`，避免漏掉同一秒内发生的入账与再次出发。它记录：

- 建立基地、完整基础设施、首次科技、扩张和首次攻击 Tick；
- AI 正式成功执行攻击意图后公开的 `LastAttackTick`；
- 双方攻击指令次数；
- `CombatEventStream` 中按攻击者玩家归属统计的实际 Impact；
- 最大军队、首次攻击后的最小军队，用于证明真实战损；
- 三项正式科技的最高累计等级；
- 最终经济、建筑、AI 阶段和比赛状态。
- 每个工人是否实际经历 `GoingToResource → Gathering → ReturningCargo`；只有 `ReturningCargo(有货) → GoingToResource(货物清零)` 才计为一轮完整采集。

遥测不决定 AI 行为，也不把测试结果写回模拟。`ModularSkirmishAiPolicy.LastAttackTick` 是只读诊断值，来源仍是已持久化的 `StrategicBlackboard`，不会产生第二份策略状态。

## 采集循环与 Mineral Walk

此前 Town Hall 把 DropOff 注册在建筑中心，而 160×120 的实体碰撞会让工人无法进入固定的 52px 入账圈。现在 DropOff 合同持久化 `ArrivalRadius`：普通投递点保持默认半径，Town Hall 按实际建筑长边半径加交互余量注册。农民无需进入建筑碰撞体即可完成入账，且不同尺寸基地不再依赖魔法常量。

`WorkerCollisionPolicy` 只在 `GoingToResource` 和 `ReturningCargo` 两段关闭单位—单位碰撞，Steering 与最终圆碰撞解算消费同一个瞬时掩码；`WaitingForResource`、`Gathering`、Idle、施工、普通移动和战斗不享受穿行。静态障碍与建筑仍由 `StaticWorld.ConstrainDisc` 约束，因此采矿工人可以穿过单位，但不能穿过建筑。

表现层把完整循环直接画出来：SCV 使用菱形轮廓，显示 `> RES / MINING / < M+6 / < G+5 / IDLE`，去程与返程分别连向资源点和投递点，携货时显示资源色标记。Marine 使用轻型圆形缺口，Marauder 使用重型双环；五类建筑使用独立几何符号和玩家色外框。

## 自动测试镜头

`VisualTestSession` 提供纯测试表现合同 `CameraKeyframe(tick, center, zoom)`。镜头轨道使用平滑插值，只改变 Godot 绘制变换，不读取或写入模拟、AI、寻路和录像状态。本关卡先保持双方总览，再依次巡视左侧采集、右侧采集、中央交战、双方自然扩张，最后回到总览；录像不再把右侧基地留在 1280×720 画布外。

## 当前验收结果

- 左/右完整基础设施：Tick 570 / 630；
- 首次科技：Tick 660 / 870；
- 自然扩张：Tick 1140 / 1230；
- 首次攻击：Tick 690 / 750；
- 攻击指令：51 / 83；
- 实际命中：84 / 30；
- 最大军队到战损低点：13→2 / 5→1；
- 累计科技等级：4 / 4；
- 最终基地：2 / 2，比赛仍在进行；
- 完整采集循环：114 / 96，双方均覆盖去程、采集、携货返程和再次出发；
- 正式命令：经济 32、建造 12、生产/研究 54、单位 166；
- Replay Package v18 / Hot Snapshot v18 可规范往返，State Hash v19。

录像位于 `test_videos/20260712_231126/ai-continuous-encounter.webm`，AV1/WebM、1802 帧、32,617,953 字节；包含总览和自动平滑镜头巡视。

这个关卡已经形成后续复杂玩法的集成入口。新增单位、技能、科技或 AI Planner 时，应扩展数据定义和公开遥测，而不是在关卡里直接调用底层系统或为某个固定 Tick 写作弊脚本。
