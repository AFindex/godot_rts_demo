# 命令队列、Control Group 与 SmartCommand

更新日期：2026-07-11

## 语义来源与当前边界

这一层参考 Blizzard 的 [Simplified Controls](https://news.blizzard.com/en-us/article/6640645/game-guide-simplified-controls) 与 [Special Control](https://news.blizzard.com/en-us/article/4552955/game-guide-special-control)：Shift 追加顺序命令，Ctrl+数字覆盖编组，Shift+数字添加，数字键召回。

Control Group 只保存选择集合，不持有共享命令。命令始终进入每个单位自己的队列。

## 每单位命令队列

- `UnitCommandQueueStore` 使用固定 SoA 环形队列，每单位最多 16 条待执行命令。
- 支持 Move、AttackMove、AttackTarget、Stop 和 Hold 的活动命令记录。
- Shift+SmartCommand 追加；不带 Shift 的命令清空全部待执行命令并立即替换。
- Stop/Hold 当前作为终止命令：立即清空队列，不参与 Shift 追加。
- 队列满时拒绝新命令并累加 overflow，不分配新容器、不覆盖旧命令。
- 同一次选择发出的命令带相同 SequenceId；单位按个体进度完成前序命令。
- 同 SequenceId 且恰好在同 Tick 到期的单位会重新批量发令，继续复用群组槽位、共享 Portal 路线和路径预算。
- 死亡会清空该单位队列；Control Group 召回时过滤死亡单位。

## Control Group

- Ctrl+0～9：覆盖对应组。
- Shift+0～9：把当前选择添加到对应组，不改变当前选择。
- 0～9：召回仍存活的成员，按稳定 unit ID 排序。
- 0～9 在 0.35 秒内再次召回同一组：把镜头定位到当前存活成员的中心。
- 编组内部为固定容量布尔索引，没有 HashSet、Node 或 Godot 对象。

尚未实现 Alt 移出/窃取编组和建筑/单位混合子组。

## 选择过滤与双击

- `SelectionFilter` 只接收稳定候选快照，不读取 Godot Node、UnitStore 或选择框状态。
- 点选按命中距离选择友军；同距离使用稳定 Unit ID。
- 框选过滤死亡单位和非玩家阵营，结果按 Unit ID 排序。
- 双击单位会选择当前相机可见区域内相同 `TypeId` 的存活友军。
- `TypeId` 由上层实体/Gameplay Profile 提供；选择系统不使用半径或速度猜测单位类型。

## 相机

- 屏幕四边 18px 边缘滚动；方向键提供等价键盘移动。
- 滚轮缩放范围 1.0～2.4，缩放前后的光标世界坐标保持不变。
- 平移速度按缩放反比调整，并在世界边界内夹紧。
- 编组双击以存活成员平均位置定位，不改变当前缩放。
- `OperationCameraController` 使用纯 `System.Numerics`，Godot `Camera2D` 只做显示适配。

## SmartCommand

```text
地面             → Move
友军单位位置     → Move
敌军单位         → AttackTarget
A 修饰的任意目标 → AttackMove
```

AttackTarget 使用现有战斗槽、追击与攻击结算，但锁定目标死亡后停止，不像 AttackMove 那样自动重选敌或恢复路线。

Godot Demo 中普通命令显示单圈反馈，Shift 队列显示双圈反馈；敌军锁定攻击和 AttackMove 使用红色反馈。

## 黑盒验收

- `queued-waypoints`：依次完成三段 Move，两个 Shift 命令均被记录为完成。
- `queued-command-replace`：即时命令清空两条待执行命令并到达新目标。
- `queued-capacity-limit`：16/16 待执行，额外两条得到 2 次显式 overflow。
- `control-group-recall`：Ctrl 覆盖 4 人、Shift 添加 2 人、召回 6 人并全部到达。
- `smart-command-sequence`：友军位置 Move → 敌军 AttackTarget → 地面 Move，目标死亡且两个 Shift 命令完成。
- `operation-selection-camera`：稳定点选、可见同类型双击、友军框选、光标锚定缩放、边缘滚动和编组双击定位全部通过。

## 下一阶段

接入 Minimap 世界缩略图、当前视口框、点击定位和 Minimap SmartCommand；Minimap 继续复用现有相机控制器与 SmartCommand，不建立第二套选择或命令语义。
