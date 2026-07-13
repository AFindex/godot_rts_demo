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

当前攻击支持瞬时命中与确定性跟踪投射物，但尚未建模转身时间；护甲、属性加成、多段攻击、最低伤害与武器科技等级已经进入正式结算，攻击起手、发射、命中与摧毁发布为独立确定性事件。

## 模块边界

- `CombatStore`：独立 SoA 数据，保存阵营、生命、攻击参数、命令意图、目标、原 AttackMove 终点和计时器。
- `CombatSystem`：纯 C# 固定 Tick 状态机，只负责确定性选敌、追击/攻击决策、伤害和死亡事件。
- `CombatEngagementSlotAllocator`：保存目标相对角度/半径；分配近战接触槽和远程攻击环，处理外圈 staging 与严格降误差换槽。
- `ICombatMovementDriver`：战斗层到导航层的窄接口；战斗系统不知道路径对象、Portal、Grid 或 Godot。
- `RtsSimulation`：把追击和恢复意图转换为现有路径请求，并负责死亡单位退出移动/碰撞状态。
- `MovementTestRig`：业务级测试接口，只暴露 `SpawnCombat`、`AttackMove` 和 `ObserveCombat`。
- `CombatEventStream`：固定容量派生事件流，按 Tick/Sequence 发布 `AttackStarted`、`Impact`、`TargetDestroyed`；表现层不读取状态机内部数组。
- `CombatDamageResolver`：纯 C# 命中公式，输入武器、目标防御和可用生命，输出每段/总伤害、实际段数、加成命中和击杀结果。
- `CombatContactPolicy`：从命令、战斗阶段和武器移动能力派生接触角色；碰撞器只消费逆移动性，不读取战斗状态机。

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
| Move | 移动途中不主动索敌；到点后转为空闲警戒 |
| AttackMove | 沿路线索敌；追击；死亡或脱离 leash 后恢复路线 |
| Idle/Stop | 空闲单位或 Stop 单位在 acquisition 范围内自动索敌并进行有 leash 的局部追击 |
| Hold | 取消原命令与恢复路线；只攻击当前射程内目标，不追击、不离开 Hold 位置 |

四种命令共享攻击结算，但移动和恢复语义互不污染。

活动采集者和施工 Builder 不执行空闲警戒，避免自动战斗打断采集、返货和施工。AttackMove 的路线恢复目标按单位保存为编队槽位；接敌期间保留移动组身份，脱战后不会把整组选中单位重新塞回一个公共点。终点编队完成一次性收口后仍保留 AttackMove 警戒语义，但不会为终态槽位继续抖动。

## 黑盒验收

- `attack-move-engage-resume`：偏离路线接敌、击杀、回到原路线。
- `combat-idle-auto-acquire`：6 个无 AttackMove/显式目标命令的空闲士兵自动消灭 3 个敌人，显式攻击命令为 0。
- `attack-move-squad-slot-resume`：16 人编队接敌后回到 16 个唯一槽位，全部停稳且长期漂移为 0。
- `attack-move-leash-resume`：耐久目标逃离 leash，攻击者放弃追击并继续前往原终点。
- `attack-move-command-isolation`：同图 Move 单位忽略敌人，AttackMove 单位接敌并恢复。
- `attack-move-cancel`：Stop/Hold 取消原 AttackMove 路线；Stop 可继续局部接敌，Hold 不恢复路线。
- `combat-melee-slots`：同侧 8 个近战单位经外圈 staging 全部进入 8 个唯一接触槽。
- `combat-ranged-ring`：10 个远程单位全部进入唯一攻击环。
- `combat-stop-hold-acquire`：Stop 局部追击；Hold 原地攻击近目标并忽略射程外目标。
- `combat-multi-retarget`：8 个攻击者连续消灭 4 个目标，随后 8/8 恢复原路线。
- `combat-event-stream`：25 HP 目标受到 12、12、1 三次实际伤害；严格产生 3 个起手、3 个命中和 1 个摧毁事件，序号连续且没有丢失。
- `combat-damage-matrix`：Resource Marauder 对 2 护甲 Light 造成 20、对 Armored 造成 30；`8+4 vs Armored` 的双段武器对 3 护甲目标造成 `9×2=18`。
- `combat-projectile-flight`：从稳定业务接口观察飞行中投射物；目标移动后仍跟踪，同一 ProjectileId 从发射贯穿到命中，飞行中 Hot Snapshot 恢复与连续运行最终 Hash 一致。
- `combat-projectile-presentation`：通过表现 Frame 验证 Bolt/Orb/Volley、10 点有界拖尾、3 个稳定命中提示、零事件丢失和 90/85/84 三组最终生命。
- `combat-mobile-fire`：固定/移动武器前摇位移 0.0/59.8px，开火后一秒位移 0.0/110.3px；Move 取消 0.60 秒前摇但保持 20 秒冷却，并验证 Package/Hot 恢复一致。
- `combat-target-selection`：Tick 30 保持初始目标、Tick 60 切换强语义优势目标、拒绝 771 分轻微优势，并验证玩家锁定目标不被 Priority 10 候选覆盖。
- `combat-contact-priority`：验证固定前摇、近战接触、固定冷却、移动射击和普通五类角色，以及相同穿透压力下 0.32/0.52/0.93px 的有序位移。
- `technology-research-upgrades`：Infantry Weapons 完成两级后，`10 + 2×2` 武器对 1 护甲目标预览并正式共享 13 伤害公式。

这些场景不读取路径点、状态数组或选敌实现；底层替换后测试接口和业务预期可以保持不变。

## 收口与下一阶段

当前 Demo 的战斗移动阶段到此收口。下一阶段转向 Shift 命令队列、Control Group、SmartCommand、命令日志和确定性回放。

S8-E1 已重新开启战斗后续并完成事件边界。事件属于可重建输出，不进入权威状态；热恢复时清空，后续固定 Tick 会重新生成。固定容量溢出通过 `LostEvents` 明确报告，不会静默伪装成完整事件流。

S8-E2a 已完成单位伤害、Structure 属性加成、Production Catalog v3 和 Infantry Weapons 实时修正。命中事件额外携带 `DamagePerAttack/AttacksApplied/BonusApplied`，可以正确表现 `x2` 和属性克制。

S8-E2b 已完成 Building Type Catalog v2、建筑 Structure/Mechanical 属性、基础护甲、每级防御增量和 Fortification Doctrine。`PreviewCombatDamage(unit, building)` 与正式 AttackBuilding 走同一 Defense/Health 合同。

S8-E3a 已完成固定容量确定性投射物。武器载荷在发射时冻结，目标防御在命中时读取；目标失效会过期而不重定向。活跃投射物进入状态 Hash 与热恢复，Godot 只通过公开快照绘制，不读取投射物池内部结构。

S8-E3b 已完成可插拔弹道表现合同。纯 C# Composer 生成短期表现 Frame，Godot Layer 与主题 Resource 独立绘制；事件世界位置、拖尾和命中提示都不进入权威回放状态。详见 [战斗弹道表现边界](COMBAT_PRESENTATION.md)。

S8-E4a 已完成移动射击与武器移动约束。普通武器保持停下前摇/冷却；移动武器可分别配置前摇与冷却移动。Move 取消未完成前摇但不重置冷却，详见 [武器移动约束](WEAPON_MOVEMENT.md)。

S8-E4b 已完成有限目标评分与重新选敌稳定性。玩家显式目标最高优先；自动索敌使用固定四项评分、0.75 秒锁定、语义升级和 2,500 分门槛，详见 [自动目标评分](TARGET_SELECTION.md)。

S8-E5a 已完成战斗接触优先级。固定前摇与近战接触保持稳定但仍可被缓慢挤开；移动射击更接近普通穿行单位。角色完全由当前权威状态派生，不新增存档字段，详见 [战斗接触优先级](COMBAT_CONTACT.md)。

E5a 后不继续盲目调质量参数。只有宏观战线业务场景暴露可复现失败时才开启 E5b；动画、音效和特效继续直接消费当前事件/快照合同，不反向耦合战斗状态机。
