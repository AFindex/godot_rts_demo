# 玩家视野与命令权限

S11-F2 把所有权、可见信息和玩家命令权限收敛到纯 C# 模拟边界。Godot、脚本 AI 和胜负系统不再通过遍历 `UnitStore`、`CombatStore` 或 Construction 内部集合自行判断玩家能知道什么。

## 状态模型

- `Combat.Teams[unit]` 仍是单位的权威 Owner Player ID；建筑使用 `GameplayBuildingSnapshot.PlayerId`。
- Player ID `0` 保留为中立实体；正式玩家范围为 `1..15`。
- 视野使用 32 世界单位的规则网格。单位提供 224 半径视野，普通建筑提供 256，Town Hall 提供 320。
- `Visible` 每 Tick 根据当前存活单位和非终结建筑重新计算，不重复持久化。
- `Explored` 是单调持久状态，以压缩 bitset 保存，参与 State Hash、热快照和恢复校验。
- Replay Package 必须从未探索的 Tick 0 开始；随后探索完全由初始实体和版本化命令确定性重建。

## PlayerView 合同

`CreatePlayerView(playerId)` 返回深拷贝只读快照：

- 己方存活单位和建筑始终可查询。
- 敌方与中立动态实体只有在当前 `Visible` 时才返回；离开视野后不继续泄露位置、生命或施工状态。
- 未探索资源点不返回。
- 已探索但当前不可见的资源点只返回稳定 ID、种类和位置；`KnownRemaining = -1`，不泄露实时储量或 operational 变化。
- 快照同时包含 `Hidden/Explored/Visible` 单元格，供世界雾层、Minimap、AI 感知和诊断共同消费。

Godot 的实体绘制、点击命中和 Minimap 标记只消费 PlayerView。敌方生产/研究队列只对所有者显示；Debug DynamicFootprint 在玩家视图启用时不再绕过雾层绘制。

## 玩家命令入口

`IssuePlayerMove / IssuePlayerAttackMove / IssuePlayerStop / IssuePlayerHold / IssuePlayerSmartCommand` 在写入正式命令日志之前统一验证：

- 玩家已注册；
- 选择非空且所有单位存活；
- 每个单位都属于发令玩家；
- 实体目标有效且敌我关系正确；
- 敌方单位或建筑当前可见。

失败返回稳定 `PlayerOrderCommandCode`，不会部分执行、不会写日志。系统派生命令和历史回放继续使用解析后的底层命令入口，避免把权限检查重复执行两次。

## 持久化与验证

- Replay Package：v10。
- Hot Snapshot：v10，新增网格规格、有序 Player ID 和探索 bitset。
- State Hash：v11，包含网格规格、Player ID 和规范 bitset。
- 解码严格拒绝非法网格、过量玩家、无序/重复 Player ID、长度错误和非零 padding bits。

`player-visibility-authority` 黑盒场景覆盖：隐藏敌人不可查询与不可攻击、敌方单位不可被玩家 1 控制、侦察后单位/建筑/资源可见、返回后敌方动态实体再次隐藏、资源保留已探索但未知实时储量，以及完整回放与探索中的热恢复精确一致。
