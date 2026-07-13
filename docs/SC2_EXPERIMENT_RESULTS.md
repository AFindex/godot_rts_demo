# StarCraft II 施工占位实验结果

更新日期：2026-07-13

## 结论边界

本文件只记录会改变施工占位策略的证据和可复现实验。当前机器未安装 StarCraft II，因此本轮没有伪造“当前客户端实机结果”。在线资料可以冻结建筑最终不能与单位重叠、敌方单位能够阻止开工等边界；它们不足以确认 Hold、采矿任务和盟友单位是否会被建造命令强制改派。

因此当前状态分成两层：

- **SC2 事实层**：有来源的结论按 A/B/C 级记录；未被当前客户端实机录像确认的动作保持 `Unknown`。
- **项目策略层**：使用集中、可替换的 `ConstructionBlockerPolicy`。未知项默认 `Wait`，不会静默改写玩家或盟友订单。

## 在线证据台账

| 结论 | 证据 | 等级 | 可冻结范围 |
|---|---|---:|---|
| 建筑最终放置区域不能与单位或其他建筑重叠 | [Liquipedia Buildings](https://liquipedia.net/starcraft2/Buildings) | B | Hard Footprint 提交前必须清空动态单位 |
| 玩家可用工人站在目标位置阻止敌方施工 | [Liquipedia Harassment](https://liquipedia.net/starcraft2/Harassment) | B | 可见敌军不能被施工系统自动推开；精确失败/等待时序仍未知 |
| Hold 的公开语义是留在原地，不移动追击 | [Blizzard Basic Unit Controls](https://news.blizzard.com/en-us/article/4552956/game-guide-basic-unit-controls) | A | Hold 与普通 Idle 必须是不同订单；资料没有说明建筑命令能否覆盖 Hold |
| 可见敌人或隐蔽阻挡者可能让工人到场后停住而无法开工 | [SC2 Forum](https://us.forums.blizzard.com/en/sc2/t/scv-left-click-not-building/9637)、[Arqade burrowed placement test](https://gaming.stackexchange.com/questions/6342/do-burrowed-units-block-building-construction) | C | 支持 PlayerKnown/Authority 分层；不能据此冻结提示、退款和等待时长 |
| 己方普通单位在蓝图内可能收到自动让位动作 | [SCV displacement discussion](https://www.reddit.com/r/starcraft/comments/rabj30) | C | 支持保留 `MovableFriendly → BeginEviction` 项目策略；仍需实机确认订单覆盖细节 |

没有足够证据的项目：采矿/返矿 SCV 是否会被建筑命令打断、Hold 是否会被强制移动、盟友是否会收到跨玩家让位命令、多个玩家同时预订同一位置的精确胜负、阻挡超时与退款/提示时序。

## 当前项目策略矩阵

| Authority 阻挡分类 | 当前动作 | SC2 对齐状态 | 设计理由 |
|---|---|---|---|
| `MovableFriendly`（Idle/Stop/Move） | `BeginEviction` | C 级支持，待实机 | 使用系统临时覆盖层让位，Hard Commit 后恢复原 Move/Shift 队列 |
| `FriendlyHold` | `Wait` | `Unknown` | 不违反 Hold 的公开“保持位置”语义 |
| `FriendlyEconomyTask` | `Wait` | `Unknown` | 不打断 Gather/Return Cargo；避免施工后台破坏经济循环 |
| `FriendlyAssignedBuilder` | `Wait` | 项目安全策略 | 不抢占另一个施工生命周期的 Builder |
| `FriendlyOtherOrder` | `Wait` | `Unknown` | 不静默覆盖攻击、技能或其他业务订单 |
| `AuthorityAlly` | `Wait` | `Unknown` | 阵营关系允许识别，但建造方无权给盟友下 Move |
| `AuthorityEnemy` | `Wait` | 最终阻挡已确认，时序未知 | 可见敌军在 PlayerKnown 预览拒绝；晚到/隐藏敌军由 Authority 阻挡，不被自动移动 |

这张表是可替换策略，不是对 SC2 内部算法的宣称。未来实机证据只允许修改策略映射和对应黑盒期望，不应重写 Reservation、PlayerKnown/Authority、Eviction Planner 或订单临时覆盖层。

## 本项目工程实验

正式黑盒场景：`construction-blocker-policy-matrix`。

场景只通过稳定业务门面执行 Hold、Gather、Build、PlayerMove、Preview 和 Cancel，并读取建筑、订单、经济与玩家可见快照；不访问 `RtsSimulation`、`ConstructionSystem`、Steering 或路径内部数据。

覆盖结果：

- 己方 Idle 占位触发系统临时撤离，建筑完成；
- 己方 Hold 保持 `BlockedAtStart`，不产生施工撤离，玩家解除后完成；
- 采矿 SCV 保持 `Gathering` 和原 Resource Node，施工取消也不改变采矿订单；
- 晚到盟友与敌军均保持原权限边界，不产生施工撤离；各自 Owner 移走后建筑完成；
- 已知盟友和可见敌军在 Preview 阶段均返回 `UnitOverlap`；
- 五路终态为 `Completed / Completed / Canceled / Completed / Completed`；
- 初始态、9 个跨阶段 Replay 检查点、最终 Replay Package v28 和阻挡态 Hot Snapshot v27 全部 State Hash 精确一致。

专项录像：`test_videos/20260713_214756/construction-blocker-policy-matrix.webm`，1280×720、362 帧、AV1/WebM、CRF 32、preset 8、2,431,925 字节。

## 尚待当前客户端实机执行

只保留会改变策略表的最小矩阵：

1. 自己 SCV：Idle、Hold、Gathering、ReturningCargo；记录原订单是否被替换、恢复或取消。
2. 自己战斗单位：Idle 与 Hold；记录 Preview、命令接受和自动 Move。
3. 盟友 Idle/Hold；确认建造方是否能触发盟友让位，以及由哪一方拥有该订单。
4. 可见敌军、下单后进入的敌军、未侦测/已侦测 Burrowed；记录等待、失败、提示和退款。
5. 上述关键项各补一个 1/8/32 单位与 Shift 三建筑时序，不扩展无关数值实验。

每个格子必须附客户端版本、地图/模式、原始录像和订单面板变化。没有这些材料时继续标记 `Unknown`；当前保守策略已经可玩且有门禁，不继续添加启发式。
