# 建筑升级运行时

## 已落地范围

当前已把经典版人族主基地升级链作为正式玩法协议接入：

```text
城镇大厅 htow --320 金/210 木、140 秒--> 主城 hkee
主城 hkee     --360 金/210 木、140 秒、需要国王祭坛 halt--> 城堡 hcas
```

费用不是手写平衡表，而是由目标建筑 JSON 的总成本减去源建筑总成本得到：
`hkee(705/415) - htow(385/205)` 与
`hcas(1065/625) - hkee(705/415)`。升级时间、中文名称、图标、生命、护甲、
人口供给和前置同样来自导出的单位编辑器数据。升级链只转换已完成建筑，保留稳定
`GameplayBuildingId`、占地和当前生命比例；不会删除旧建筑再创建新建筑。

## 模块边界

- `War3GameplayDataAdapter.CreateBuildingUpgrades` 只负责把 `Upgrade`、总成本、
  `Requires` 和目标 profile 编译成内容中立的 `BuildingUpgradeProfile`。
- `BuildingUpgradeCatalogSnapshot` 保存不可变目录、稳定规范字节和 Hash，并提供
  建筑类型祖先关系。主城/城堡因此可以继续满足“城镇大厅”生产者或建筑前置，
  不需要复制农民配方和科技条目。
- `BuildingUpgradeSystem` 负责校验、扣费、进度、75% 手动取消退款、完成转换和
  运行态恢复。它不依赖 Warcraft rawcode、Godot 节点或 HUD。
- `RtsSimulation` 是唯一命令入口，统一处理玩家/比赛状态、命令录制和事件发布。
  同一建筑在升级时不能生产、研究或施放建筑技能；有活动生产/研究队列时也不能
  开始升级。
- `War3Rts` 只把观察快照映射为命令卡和队列 DTO。取消按钮复用现有队列交互，
  玩法退款不在 UI 中重复实现。
- `War3WorldPresenter` 根据建筑当前类型和升级阶段选择原生模型序列；音频层只消费
  `BuildingUpgradeStarted/Completed` 事件。

## 表现和 UI

城镇大厅模型包含两套完整升级序列：

- 第一段：`Birth Upgrade First`、`Stand Upgrade First`、
  `Stand Work Upgrade First`；
- 第二段：`Birth Upgrade Second`、`Stand Upgrade Second`、
  `Stand Work Upgrade Second`。

升级中播放对应 `Birth Upgrade`，完成后根据是否在工作播放对应 `Stand` 或
`Stand Work`。建筑类型改变时 Presenter 会刷新模型实例，但选择、建筑 ID、占地、
队列位置和生命比例保持不变。上述六个序列已进入启动资产校验，避免资源换包后
静默退回错误动画。

命令卡显示目标建筑中文名、增量费用、升级时间和前置状态；升级期间队列面板显示
图标、进度、取消按钮及 75% 退款说明。完成主城后仍能训练农民；完成城堡后仍继承
城镇大厅生产/研究资格。

## 确定性与持久化

升级目录和活动订单进入以下协议：

- Production Command Log 11：`UpgradeBuilding` 与 `CancelBuildingUpgrade`；
- State Hash 36：目录、next order ID、建筑、玩家、完整 profile 和进度；
- Hot Snapshot 38：目录与活动订单二进制往返；
- Replay Package 37：初始升级 manifest 和命令回放。

恢复时会再次校验建筑所有权、完成态、源类型、profile 身份、进度范围和 next ID。
所有子系统恢复后还会校验升级/生产/研究互斥，拒绝同一建筑同时拥有两种活动队列的
损坏快照或回放包。

## API 与验证

```csharp
var profile = simulation.BuildingUpgrades.Catalog
    .TryForSource(buildingTypeId, out var value) ? value : default;
var result = simulation.IssueBuildingUpgrade(playerId, building, profile);
simulation.CancelBuildingUpgrade(playerId, result.OrderId);
```

专项测试：

```powershell
godot --headless --path . -- --building-upgrade-self-test
```

该测试覆盖两段数据、祭坛门槛、费用/退款、生产互斥、热快照二进制往返、生命比例、
类型继承、命令日志以及从命令重放后的最终状态 Hash。完整回归仍使用
`godot --headless --path . -- --self-test`。

