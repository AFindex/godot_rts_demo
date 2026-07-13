# 阵营、外交与共享视野

本模块中的“阵营”指玩家之间的比赛关系，不是 Terran/Zerg/Protoss 种族内容。权威关系由纯 C# `PlayerDiplomacySystem` 提供，表现层和 AI 不再用 `ownerId != playerId` 猜测敌我。

## 领域合同

- 玩家相对关系固定为 `Own / Ally / Neutral / Enemy`，对应 SC2 官方 API 的 `Self / Ally / Neutral / Enemy`。
- 玩家 0 为 Neutral；未显式配置的正玩家各自属于独立默认联盟，保持历史 FFA 行为。
- `ConfigureAlliance(allianceId, sharedVision, players)` 只能在 Tick 0、比赛开始和录像开始前调用。当前阶段不提供中途结盟、背盟或控制权共享。
- 显式 alliance ID 使用 `1..999999`；默认 FFA alliance ID 使用保留区间，避免与显式队伍碰撞。
- Shared Vision 是联盟级设置。视野和 Detection 都从来源玩家传播给联盟成员，不复制权威单位，也不让 UI 直接访问其他玩家 Store。

SC2 官方 `ObservationInterface::GetUnits()` 将观测集合描述为全部盟友，以及当前可见的敌方和中立单位；`GetUnits(Alliance)` 也把 Ally 作为正式查询维度。当前项目据此把联盟关系放在 PlayerView 之前，而不是把共享视野做成 UI 特效。[SC2 API Unit](https://blizzard.github.io/s2client-api/classsc2_1_1_unit.html)、[SC2 API ObservationInterface](https://blizzard.github.io/s2client-api/classsc2_1_1_observation_interface.html)

## 消费边界

- `PlayerVisibilitySystem`：盟友视野源和 Detector 同时共享；盟友隐蔽单位发布为 `ConcealedAlly`，敌方仍要求普通视野与 Detection 同时成立。
- `PlayerViewSnapshot`：单位和建筑均发布玩家相对 `Relation`；Godot 只按关系着色，Own 蓝、Ally 青、Enemy 红。
- 命令与战斗：选择权仍严格属于 Owner；显式攻击和自动索敌只接受 `Enemy`，不会攻击不同 PlayerId 的盟友。
- SmartCommand：盟友单位/建筑进入 Friendly 语义，但不能替盟友续建或控制其实体。
- 施工：可见盟友占位在 PlayerKnown 预览中返回 `UnitOverlap`；到场 Authority 分类为 `AuthorityAlly`，默认策略稳定 `Wait`，绝不擅自移动盟友单位。盟友是否会在 SC2 中自动让位仍是 E0 实机项。
- 胜负：比赛只在剩余 Active 玩家属于同一 alliance 时结束；所有仍存活成员同时 `Victorious`，先出局的队友仍为 `Defeated`。多人联盟获胜时 `WinnerPlayerId=-1`，以 `WinnerAllianceId` 表示胜方。

## 确定性与门禁

联盟设置进入 Replay Package v28、Hot Snapshot v27 和 State Hash v28。载荷验证玩家顺序、ID 区间、每个显式联盟至少两名成员，以及联盟内一致的 Shared Vision 设置。

`alliance-shared-vision-team-victory` 是 2v2 稳定门面测试，覆盖：

- P1 读取 P2 的 Ally 关系、共享视野和共享 Detector；
- P2 的 Cloaked 单位以 `ConcealedAlly` 发布，P3 的 Burrowed 单位以 `ConcealedDetected` 发布；
- P1 攻击 P2 被拒绝，攻击已侦测 P3 成功；
- 盟友占据建筑位置时预览返回 `UnitOverlap`；
- P3/P4 淘汰后 P1/P2 同时胜利，胜者为 alliance 100；
- Tick 100 完整回放和 Tick 300 终局热恢复均与原运行 State Hash 精确一致。

当前不做：动态外交、资源/人口共享、盟友单位控制、盟友建筑施工协助、共享科技，以及 SC2 未经实机确认的盟友建筑让位策略。
