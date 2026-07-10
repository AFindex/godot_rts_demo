# AttackMove 最小闭环

更新日期：2026-07-11

## 当前边界

这一阶段只完成可复用的战斗移动骨架，不继续扩张为完整战斗系统。当前闭环为：

```text
AttackMove 路线
→ 错峰扫描最近敌人
→ 保存接敌位置和原 AttackMove 终点
→ 临时追击路径
→ 进入攻击范围后停止移动
→ 前摇 / 命中 / 冷却
→ 目标死亡或越过 leash
→ 恢复原 AttackMove 终点
```

当前攻击是即时命中抽象，没有弹道、转身、动画事件和护甲结算。

## 模块边界

- `CombatStore`：独立 SoA 数据，保存阵营、生命、攻击参数、命令意图、目标、原 AttackMove 终点和计时器。
- `CombatSystem`：纯 C# 固定 Tick 状态机，只负责确定性选敌、追击/攻击决策、伤害和死亡事件。
- `ICombatMovementDriver`：战斗层到导航层的窄接口；战斗系统不知道路径对象、Portal、Grid 或 Godot。
- `RtsSimulation`：把追击和恢复意图转换为现有路径请求，并负责死亡单位退出移动/碰撞状态。
- `MovementTestRig`：业务级测试接口，只暴露 `SpawnCombat`、`AttackMove` 和 `ObserveCombat`。

移动路径与战斗意图不能共用同一个“最终目标”。追击时 `SlotTarget` 可以反复改变，但 `CombatStore.AttackMoveGoals` 保留原始终点；脱战恢复不依赖当前追击路径。

## 确定性和性能约束

- 索敌每 6 Tick 错峰执行，不让所有 AttackMove 单位在同一 Tick 扫描。
- 当前候选搜索为稳定 unit ID 顺序的线性扫描；距离相同使用更小 unit ID。
- 追击目标在局部未移动时不重复寻路；明显移动时最多约每 0.2 秒更新追击路径。
- 攻击、伤害和死亡都发生在 60Hz 固定 Tick。
- 死亡不压缩数组、不改变 unit ID；`Alive=false` 的单位从 SpatialHash、Steering、积分、碰撞、选择和建筑占用检查中排除。
- 无 AttackMove 单位的 1000 单位基准仍为 9.24ms P95、约 461B/Tick。

## 当前命令语义

| 命令 | 当前语义 |
|---|---|
| Move | 只移动，不主动索敌 |
| AttackMove | 沿路线索敌；追击；死亡或脱离 leash 后恢复路线 |
| Stop | 取消路径、目标和 AttackMove 恢复意图，当前不会原地自动攻击 |
| Hold | 取消路径、目标和 AttackMove 恢复意图并固定位置，当前不会自动攻击 |

Stop/Hold 的自动攻击语义故意留给下一层；当前先保证四种命令不会互相污染。

## 黑盒验收

- `attack-move-engage-resume`：偏离路线接敌、击杀、回到原路线。
- `attack-move-leash-resume`：耐久目标逃离 leash，攻击者放弃追击并继续前往原终点。
- `attack-move-command-isolation`：同图 Move 单位忽略敌人，AttackMove 单位接敌并恢复。
- `attack-move-cancel`：Stop/Hold 取消当前目标和恢复意图，之后保持对应移动状态。

这些场景不读取路径点、状态数组或选敌实现；底层替换后测试接口和业务预期可以保持不变。

## 下一层：有限范围

下一轮只做“战斗占位与索敌语义”，验收后立即收口：

1. 近战目标周围的唯一攻击槽，避免所有近战单位追向同一中心点。
2. 远程攻击环与射程保持，目标移动时有预算地换槽/重追。
3. Stop 在原地索敌、Hold 只攻击射程内目标且不追击。
4. 目标死亡后的稳定重新选敌和多人同目标测试。

弹道、移动射击、仇恨权重、单位碰撞业务优先级、动画同步和真实伤害系统不在这一轮内。
