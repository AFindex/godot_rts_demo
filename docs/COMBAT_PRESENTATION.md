# 战斗弹道表现边界

S8-E3b 把弹道表现从 `RtsDemo` 的世界绘制函数中拆成三层，方便轨迹、颜色、材质和特效高频迭代，而不修改权威战斗逻辑。

## 分层

1. `CombatProjectileSystem` 只保存确定性飞行状态和冻结武器载荷，进入 State Hash 与 Hot Snapshot。
2. `CombatPresentationComposer` 消费公开 `CombatProjectileSnapshot` 和派生 `CombatEventBatch`，生成不可变 `CombatPresentationFrame`。它只维护最多 10 个拖尾点和 0.4 秒命中/过期提示，不属于回放权威状态。
3. `RtsCombatProjectileLayer` 只绘制 Frame。Godot 颜色、半径、拖尾宽度和命中半径来自 `data/demo_combat_presentation_theme.tres`，可以换皮而不改 Composer 或模拟层。

`RtsDemo` 是薄绑定：每个物理帧读取上次 Event Sequence 之后的事件，将 Frame 传给独立 Layer。Layer 不持有 `RtsSimulation`、`CombatStore` 或投射物池引用。

## 表现合同

`CombatProjectilePresentationSnapshot` 提供稳定 ProjectileId、位置、朝向、短拖尾和视觉类别：

- `Bolt`：默认单段、无属性 Bonus 武器。
- `Orb`：带属性 Bonus 的单段武器。
- `Volley`：多段武器。

默认分类器只是 Demo 策略；`CombatPresentationComposer` 构造函数接受可替换 resolver，实际游戏可按单位/武器表现 ID 映射，而不用改 Composer。

`CombatPresentationCueSnapshot` 提供 Impact/Expired、世界位置、归一化年龄、伤害、Bonus 和 ProjectileId。`CombatEvent` 因此增加 `WorldPosition`；事件仍是可重建输出，不进入 Package、Hot Snapshot 或 State Hash。

事件流溢出通过 Frame 的 `LostEvents` 原样暴露。热恢复导致 Event Sequence 重置时 Composer 会清空自身短期历史，避免把恢复前拖尾接到恢复后的新事件上。

## 测试

- `CombatPresentationSelfTest`：验证 Bolt/Orb/Volley 分类、朝向、两点拖尾、Impact/Expired 提示和 0.4 秒有界回收。
- `combat-projectile-presentation`：只通过 `MovementTestRig.ObserveCombatPresentation` 读取业务快照，验证 3 类样式、10 点拖尾、3 个稳定命中 ID、90/85/84 生命结果和零事件丢失。
- AV1/WebM：`test_videos/20260712_155038/` 同时包含更新后的跟踪弹道和三样式表现用例。

当前不实现粒子系统、贴图动画、音频或对象池。这些属于 Godot Layer 的后续表现实现，不需要改变当前帧合同；只有实际单位设计需要新的业务字段时才扩展权威数据。
