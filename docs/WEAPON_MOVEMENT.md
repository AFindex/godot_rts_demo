# 武器移动约束与移动射击

S8-E4a 将“攻击时能不能移动”建模为武器 Profile，而不是写死在单位状态机或 Godot 动画中。

## 数据合同

`CombatProfileSnapshot` 和 Production Catalog v5 新增两个独立布尔字段：

- `CanMoveDuringWindup`：AttackMove 已进入有效攻击位后，攻击前摇期间是否恢复原 AttackMove 路线。
- `CanMoveDuringCooldown`：完成攻击后，只要目标仍处于有效攻击距离，冷却期间是否继续原 AttackMove 路线。

默认值均为 `false`，因此 Marine、Marauder、SCV 等现有目录单位保持“停下前摇、停下冷却”的原语义。移动射击只应给明确需要的单位，不会全局改变地面部队手感。

两个字段分开保存，允许后续配置“移动中完成前摇但命中后停下”或“必须停下开火、冷却时可移动”等不同武器。Stop/Hold 永远不自动恢复路线；直接 AttackTarget 没有并行移动目标，也不会凭空产生移动。

## 命令与攻击周期

- 普通 Move/新 AttackMove/Stop/Hold 仍通过 `CombatStore.SetCommand` 清除尚未完成的 Windup，因此移动命令可以可靠取消攻击前摇。
- 已经开火后，Move 不清除 `CooldownRemaining`。玩家不能通过 Move/Attack 快速切换重置射速。
- 已经发射的确定性投射物不依赖攻击者继续保持攻击命令；移动或死亡都不取消飞行体。
- 移动射击恢复路径时会复用现有 MoveGoal；如果单位已经在同一路线上移动，不重复递增命令版本或每 Tick 申请新路径。
- 移动武器只有先通过正常追击/攻击槽验证才开始攻击周期。移动能力不会绕过射程、目标合法性、导航或占位规则。

## 持久化

两个字段属于权威未来行为，进入：

- Production Catalog v5；
- Production Command Log v7；
- Replay Package v16；
- Hot Snapshot v16；
- State Hash v17。

目录中的默认单位保持两个字段为 false，测试用移动武器通过稳定业务 Profile 构造。`combat-mobile-fire` 的 Package 和飞行中 Hot Snapshot 最终 Hash 完全一致，证明 true 值也走正式持久化路径。

## 测试指标

`combat-mobile-fire` 使用四条互不干扰的车道：

- 固定武器前摇位移 0.0px，移动武器同一前摇位移 59.8px；
- 开火后一秒固定武器位移 0.0px，移动武器位移 110.3px；
- Move 将未完成 Windup 从 0.60 秒清到 0，且没有产生 ProjectileLaunched；
- 已开火单位接受 Move 后 Cooldown 保持 `20.00 → 20.00`；
- 四个目标最终生命为 190/190/200/190；
- Package v16 / Hot v16 恢复最终状态完全一致。

下一段若继续战斗，应优先做有限、可解释的目标评分与重新选敌稳定性；转向动画、炮塔朝向和更复杂的攻击后摇等到单位表现确实需要时再进入。
