# 比赛生命周期与玩家能力

S11-G 将胜负从 UI 或关卡脚本中的临时判断收敛为纯 C# `MatchSystem`。

## 状态

- `Setup`：兼容工具和不需要胜负的历史移动测试；不施加参赛者门禁。
- `Running`：`StartMatch` 只能在 Tick 0 调用一次，至少两个已注册且唯一的玩家。
- `Completed`：记录完成 Tick 和唯一胜者；同 Tick 无存活玩家时为平局。

玩家状态为 `Active / Defeated / Victorious`。比赛进行后，非参赛者、已失败玩家和终局后的所有玩家命令分别返回稳定的 `NotParticipant / PlayerDefeated / MatchCompleted`，覆盖采集、转场、建造、生产、研究、Move、AttackMove、Stop、Hold 和 SmartCommand。

## 出局规则

玩家必须曾经拥有至少一座活跃正式建筑，才进入可出局状态。这条 `EstablishedPresence` 门槛避免开局建筑尚未生成的初始化帧把玩家误判出局。

当前规则是：建立过存在后，活跃正式建筑归零即失败。失去 Town Hall 但仍有 Barracks 的玩家继续存活；这同时验证“关键经济能力”和“比赛生命”没有被错误写成同一个布尔值。

`PlayerCapabilitySnapshot` 对 UI 和 AI 暴露：活跃/完工建筑数、Town Hall、生产设施、研究设施、工人和作战单位数，并派生 Worker Production、Army Production、Any Production 与 Elimination Risk。能力统计由 Construction 提供无分配计数，不让消费者遍历内部实体。

## 持久化

Replay Package v14 保存 Tick 0 比赛清单；Hot Snapshot v14 保存阶段、参赛者、建立存在标记、失败 Tick、结束 Tick和胜者；State Hash v15 覆盖全部比赛以及单位/建筑伤害未来态。载荷读取会严格验证排序、玩家注册、状态组合和胜者唯一性。

`match-capability-elimination` 黑盒场景使用三名玩家和不同功能建筑，验证：

- 三方建立存在后比赛仍在进行。
- P2 失去 Town Hall 但保留 Barracks 时仍为 Active，同时能力快照报告无工人生产、有军队生产。
- P2 最后一座建筑摧毁后，正式命令返回 `PlayerDefeated`。
- P3 出局后 P1 成为唯一胜者；后续命令返回 `MatchCompleted`。
- Tick 0 比赛清单可以规范 round-trip 和前段完整回放；终局 Hot Snapshot 恢复后状态 Hash 精确一致。

Godot HUD 只读取 `MatchSnapshot` 显示阶段、各玩家建筑/生产能力和最终胜者，不参与判定。
