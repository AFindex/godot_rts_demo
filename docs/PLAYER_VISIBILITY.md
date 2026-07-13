# 玩家视野与命令权限

S11-F2 把所有权、可见信息和玩家命令权限收敛到纯 C# 模拟边界。Godot、脚本 AI 和胜负系统不再通过遍历 `UnitStore`、`CombatStore` 或 Construction 内部集合自行判断玩家能知道什么。

## 状态模型

- `Combat.Teams[unit]` 仍是单位的权威 Owner Player ID；建筑使用 `GameplayBuildingSnapshot.PlayerId`。
- Player ID `0` 保留为中立实体；正式玩家范围为 `1..15`。
- 视野使用 32 世界单位的规则网格。单位提供 224 半径视野，普通建筑提供 256，Town Hall 提供 320。
- `Visible` 每 Tick 根据当前存活单位和非终结建筑重新计算，不重复持久化。
- `Detection` 是与 `Visible` 分离的派生网格：只有 `DetectionRange > 0` 的存活单位写入；它不探索地图，也不单独持久化。
- 单位权威 Profile 独立声明 `UnitConcealmentKind.None/Cloaked/Burrowed` 与 `DetectionRange`。敌方隐蔽单位必须同时处于普通视野与侦测范围内才可公开。
- `Explored` 是单调持久状态，以压缩 bitset 保存，参与 State Hash、热快照和恢复校验。
- Replay Package 必须从未探索的 Tick 0 开始；随后探索完全由初始实体和版本化命令确定性重建。

## PlayerView 合同

`CreatePlayerView(playerId)` 返回深拷贝只读快照：

- 己方存活单位和建筑始终可查询。
- 普通敌方与中立动态实体只有在当前 `Visible` 时才返回；Cloaked/Burrowed 敌方单位还必须处于 `Detection` 内。离开任一范围后不继续泄露位置、生命或施工状态。
- 己方隐蔽单位始终返回 `ConcealedOwn`；已侦测敌方返回 `ConcealedDetected`；未侦测敌方没有可查询快照。
- 未探索资源点不返回。
- 已探索但当前不可见的资源点只返回稳定 ID、种类和位置；`KnownRemaining = -1`，不泄露实时储量或 operational 变化。
- 快照同时包含 `Hidden/Explored/Visible` 单元格，供世界雾层、Minimap、AI 感知和诊断共同消费。

Godot 的实体绘制、点击命中和 Minimap 标记只消费 PlayerView。敌方生产/研究队列只对所有者显示；Debug DynamicFootprint 在玩家视图启用时不再绕过雾层绘制。

### 施工放置边界

- `PreviewConstruction` 与 `IssueConstruction` 使用 PlayerKnown 评估：己方动态单位是允许先建立 Reservation 的软占位；当前可见敌军参与 `UnitOverlap`；当前不可见的敌军单位、Gameplay Building 和 Reservation 不改变预览或命令接受码。
- 地形、世界边界和非 Gameplay 的地图占地仍按地图已知信息处理。全局 Connectivity Guard 使用 Authority 导航图，因此不在 PlayerKnown 预览运行，只在 Builder 到场的 Hard Commit 前执行。
- 到场后的 Authority 重验可以等待玩家不可见的阻挡，但 `PlayerBuildingViewSnapshot.ConstructionStatus` 只发布 `None / ClearingFriendlyUnits / KnownOccupant / WaitingForClearance`，不发布 blocker ID、阵营或隐藏原因。
- 该合同已覆盖普通战争迷雾以及 Cloak/Burrow/Detection 的未侦测/已侦测矩阵。Ally/共享视野尚未实现；能力开关、扫描、能量/研究和种族例外也不在本层伪造。

## 玩家命令入口

`IssuePlayerMove / IssuePlayerAttackMove / IssuePlayerStop / IssuePlayerHold / IssuePlayerSmartCommand` 在写入正式命令日志之前统一验证：

- 玩家已注册；
- 选择非空且所有单位存活；
- 每个单位都属于发令玩家；
- 实体目标有效且敌我关系正确；
- 敌方建筑当前可见；敌方单位当前可见，若为 Cloaked/Burrowed 还必须已侦测。

失败返回稳定 `PlayerOrderCommandCode`，不会部分执行、不会写日志。系统派生命令和历史回放继续使用解析后的底层命令入口，避免把权限检查重复执行两次。

## 持久化与验证

- 当前 Replay Package：v27，初始单位清单包含隐蔽类型与侦测范围。
- 当前 Hot Snapshot：v26，Combat SoA 保存隐蔽类型与侦测范围；Detection grid 由单位位置重建。
- 当前 State Hash：v27，包含单位隐蔽类型、侦测范围以及原有探索 bitset。
- 解码严格拒绝非法网格、过量玩家、无序/重复 Player ID、长度错误和非零 padding bits。

`player-visibility-authority` 黑盒场景覆盖：隐藏敌人不可查询与不可攻击、敌方单位不可被玩家 1 控制、侦察后单位/建筑/资源可见、返回后敌方动态实体再次隐藏、资源保留已探索但未知实时储量，以及完整回放与探索中的热恢复精确一致。

`construction-player-known-placement` 覆盖友军单位内预放置、可见敌军拒绝、隐藏敌军不影响 Preview/Issue、Builder 到场后的 Authority 等待、可见性过滤反馈和解除阻挡后的完工；测试只使用正式业务门面。

`concealment-detection-construction` 覆盖可见地面上的未侦测 Burrowed 单位、PlayerView 与攻击权限、PlayerKnown 预览、Authority 到场等待、通用反馈、Detector 进入后的 `ConcealedDetected`、目标命令开放、解除阻挡后完工，以及 Burrowed/普通单位接触豁免。Package、Hot Snapshot 与最终 State Hash 精确往返。

专项录像位于 `test_videos/20260713_204122/`，格式为 AV1/WebM、1280×720、212 帧。
