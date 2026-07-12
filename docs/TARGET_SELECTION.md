# 自动目标评分与稳定重选

S8-E4b 为 AttackMove、Stop 和 Hold 的自动索敌增加有限评分。它不是通用仇恨系统，也不会覆盖玩家显式 AttackTarget。

## 初始评分

候选必须先通过敌对关系、存活状态和 AcquisitionRange；Hold 仍只看实际攻击射程。合法单位按以下固定公式选择最低分：

```text
score = distanceSquared
      - AutoTargetPriority * 12000
      - weaponBonusMatch * 6000
      - armedThreat * 3000
```

- `AutoTargetPriority` 是目标 Profile 的 0～10 小整数，默认 0。
- `weaponBonusMatch` 表示攻击者 BonusVs 与目标 Attributes 相交。
- `armedThreat` 表示目标基础 AttackDamage 大于 0。
- 完全同分时选择稳定最低 Unit ID。

`PreviewAutoTargetScore(attacker, target)` 返回距离平方、优先级、Bonus 命中、武装威胁和总分，测试、调试 UI 或平衡工具不需要复制评分公式。

## 重选约束

初次锁定后至少保持 0.75 秒。之后每 12 Tick 最多检查一次，并同时要求：

1. 新目标总分至少改善 2,500；
2. 新目标在基础优先级、武器属性克制或武装威胁中至少一项相对当前目标升级。

因此，相同语义目标之间不会因为战线移动产生纯距离抖动；距离仍决定初次选择，但不能单独打断已经建立的交战。新目标通过后会释放旧攻击槽、清除未完成前摇并重新执行正式追击/占位。

玩家显式 AttackTarget 完全不执行自动重选。目标死亡或失效后仍按既有命令语义处理；显式目标不会被更高优先级敌人抢走。

当前自动评分只处理单位。建筑仍由玩家显式 AttackBuilding/SmartCommand 指定，避免把建筑与单位混进同一个未经业务验证的权重表。

## 确定性与持久化

- Production Catalog v6 / Production Command Log v8 保存 AutoTargetPriority；
- Replay Package v17 保存初始 Profile；
- Hot Snapshot v17 与 State Hash v18 保存当前 TargetLockRemaining；
- 搜索和重选继续按 `(tick + unitId)` 错峰，决胜顺序使用稳定 Unit ID。

`combat-target-selection` 验证：

- Tick 30 仍保持初始目标；
- 强语义优势目标在 Tick 60、锁定期结束后切换；
- 总分只改善 771 的同类候选不会引发抖动；
- 玩家显式锁回原目标后，Priority 10 候选也不能覆盖；
- 评分为 `10000 / -4100 / -4871`，强目标包含 Priority 1、Bonus Match 和 Armed Threat；
- Package v17 / Hot v17 恢复最终 Hash 完全一致。

下一阶段若继续战斗移动，应处理近战、远程、静止和移动单位之间的推挤质量与接触优先级；目标评分本身到此保持固定，不扩展脚本条件树。
