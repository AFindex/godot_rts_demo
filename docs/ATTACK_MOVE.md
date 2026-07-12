# AttackMove 与战斗占位闭环

更新日期：2026-07-11

## 当前边界

这一阶段完成可复用的战斗移动与占位骨架，不扩张为完整伤害/表现系统。当前闭环为：

```text
AttackMove 路线
→ 错峰扫描最近敌人
→ 保存接敌位置和原 AttackMove 终点
→ 分配近战接触槽或远程攻击环
→ 必要时经近侧接近点沿目标外圈分段就位
→ 进入攻击范围后停止移动
→ 前摇 / 命中 / 冷却
→ 目标死亡或越过 leash
→ 恢复原 AttackMove 终点
```

当前攻击仍是即时命中抽象，没有弹道和转身；护甲、属性加成、多段攻击、最低伤害与武器科技等级已经进入正式结算，攻击起手、命中与摧毁发布为独立确定性事件。

## 模块边界

- `CombatStore`：独立 SoA 数据，保存阵营、生命、攻击参数、命令意图、目标、原 AttackMove 终点和计时器。
- `CombatSystem`：纯 C# 固定 Tick 状态机，只负责确定性选敌、追击/攻击决策、伤害和死亡事件。
- `CombatEngagementSlotAllocator`：保存目标相对角度/半径；分配近战接触槽和远程攻击环，处理外圈 staging 与严格降误差换槽。
- `ICombatMovementDriver`：战斗层到导航层的窄接口；战斗系统不知道路径对象、Portal、Grid 或 Godot。
- `RtsSimulation`：把追击和恢复意图转换为现有路径请求，并负责死亡单位退出移动/碰撞状态。
- `MovementTestRig`：业务级测试接口，只暴露 `SpawnCombat`、`AttackMove` 和 `ObserveCombat`。
- `CombatEventStream`：固定容量派生事件流，按 Tick/Sequence 发布 `AttackStarted`、`Impact`、`TargetDestroyed`；表现层不读取状态机内部数组。
- `CombatDamageResolver`：纯 C# 命中公式，输入武器、目标防御和可用生命，输出每段/总伤害、实际段数、加成命中和击杀结果。

移动路径与战斗意图不能共用同一个“最终目标”。追击时 `SlotTarget` 可以反复改变，但 `CombatStore.AttackMoveGoals` 保留原始终点；脱战恢复不依赖当前追击路径。

## 确定性和性能约束

- 索敌每 6 Tick 错峰执行，不让所有 AttackMove 单位在同一 Tick 扫描。
- 当前候选搜索为稳定 unit ID 顺序的线性扫描；距离相同使用更小 unit ID。
- 追击目标在局部未移动时不重复寻路；明显移动时最多约每 0.2 秒更新追击路径。
- 槽位用目标相对角度/半径表达，目标移动时无需全局重分配。
- 同侧近战遇到目标中心阻挡时，使用“近侧外圈 → 分段绕行 → 最终槽位”；交叉后只接受严格降低两单位总槽位误差的交换。
- 攻击、伤害和死亡都发生在 60Hz 固定 Tick。
- 死亡不压缩数组、不改变 unit ID；`Alive=false` 的单位从 SpatialHash、Steering、积分、碰撞、选择和建筑占用检查中排除。
- 无 AttackMove 单位的 1000 单位基准为 9.17ms P95、约 461B/Tick。
- 双方持续 AttackMove 时，128/256 总单位为 3.42/4.70ms P95；战斗阶段平均 0.78/1.74ms，平均分配约 2.3/4.0KB/Tick，门槛为 8KB/Tick。

## 当前命令语义

| 命令 | 当前语义 |
|---|---|
| Move | 只移动，不主动索敌 |
| AttackMove | 沿路线索敌；追击；死亡或脱离 leash 后恢复路线 |
| Stop | 取消原命令与恢复路线；在 acquisition 范围内自动索敌并进行有 leash 的局部追击 |
| Hold | 取消原命令与恢复路线；只攻击当前射程内目标，不追击、不离开 Hold 位置 |

四种命令共享攻击结算，但移动和恢复语义互不污染。

## 黑盒验收

- `attack-move-engage-resume`：偏离路线接敌、击杀、回到原路线。
- `attack-move-leash-resume`：耐久目标逃离 leash，攻击者放弃追击并继续前往原终点。
- `attack-move-command-isolation`：同图 Move 单位忽略敌人，AttackMove 单位接敌并恢复。
- `attack-move-cancel`：Stop/Hold 取消原 AttackMove 路线；Stop 可继续局部接敌，Hold 不恢复路线。
- `combat-melee-slots`：同侧 8 个近战单位经外圈 staging 全部进入 8 个唯一接触槽。
- `combat-ranged-ring`：10 个远程单位全部进入唯一攻击环。
- `combat-stop-hold-acquire`：Stop 局部追击；Hold 原地攻击近目标并忽略射程外目标。
- `combat-multi-retarget`：8 个攻击者连续消灭 4 个目标，随后 8/8 恢复原路线。
- `combat-event-stream`：25 HP 目标受到 12、12、1 三次实际伤害；严格产生 3 个起手、3 个命中和 1 个摧毁事件，序号连续且没有丢失。
- `combat-damage-matrix`：Resource Marauder 对 2 护甲 Light 造成 20、对 Armored 造成 30；`8+4 vs Armored` 的双段武器对 3 护甲目标造成 `9×2=18`。
- `technology-research-upgrades`：Infantry Weapons 完成两级后，`10 + 2×2` 武器对 1 护甲目标预览并正式共享 13 伤害公式。

这些场景不读取路径点、状态数组或选敌实现；底层替换后测试接口和业务预期可以保持不变。

## 收口与下一阶段

当前 Demo 的战斗移动阶段到此收口。下一阶段转向 Shift 命令队列、Control Group、SmartCommand、命令日志和确定性回放。

S8-E1 已重新开启战斗后续并完成事件边界。事件属于可重建输出，不进入权威状态；热恢复时清空，后续固定 Tick 会重新生成。固定容量溢出通过 `LostEvents` 明确报告，不会静默伪装成完整事件流。

S8-E2a 已完成单位伤害、Structure 属性加成、Production Catalog v3 和 Infantry Weapons 实时修正。命中事件额外携带 `DamagePerAttack/AttacksApplied/BonusApplied`，可以正确表现 `x2` 和属性克制。

S8-E2b 已完成 Building Type Catalog v2、建筑 Structure/Mechanical 属性、基础护甲、每级防御增量和 Fortification Doctrine。`PreviewCombatDamage(unit, building)` 与正式 AttackBuilding 走同一 Defense/Health 合同。

下一段进入确定性弹道；之后再评估移动射击、仇恨权重和单位碰撞业务优先级。动画、音效和特效直接消费当前事件合同，不反向耦合战斗状态机。
