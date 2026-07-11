# 命令队列、Control Group 与 SmartCommand

更新日期：2026-07-12

## 语义来源与当前边界

这一层参考 Blizzard 的 [Simplified Controls](https://news.blizzard.com/en-us/article/6640645/game-guide-simplified-controls) 与 [Special Control](https://news.blizzard.com/en-us/article/4552955/game-guide-special-control)：Shift 追加顺序命令，Ctrl+数字覆盖编组，Shift+数字添加，数字键召回。

Control Group 只保存选择集合，不持有共享命令。命令始终进入每个单位自己的队列。

## 每单位命令队列

- `UnitCommandQueueStore` 使用固定 SoA 环形队列，每单位最多 16 条待执行命令。
- 支持 Move、AttackMove、AttackTarget、AttackBuilding、Stop 和 Hold 的活动命令记录。
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
资源点           → 工人 Gather；非工人 Move
等待施工者的己方建筑 → 最低 ID 合格工人 Resume；其余单位 Move
A 修饰的任意目标 → AttackMove
```

AttackTarget 使用现有战斗槽、追击与攻击结算，但锁定目标死亡后停止，不像 AttackMove 那样自动重选敌或恢复路线。

资源与友方建筑上下文由纯 C# 模拟层拆分混合选择，不由 Godot UI 猜测单位能力。拆分前先验证全部工人采集条件，避免一组选中单位只执行一半；续建按稳定 Unit ID 选择唯一施工者。Gather/Resume 会清空对应工人的旧单位队列和旧经济工作，连续运行与 Replay Package 重放走同一正式入口。

当前固定单位队列只保存移动/战斗 `UnitOrder`，尚不能保存未来 Tick 才解析的经济/施工意图。Shift+资源或 Shift+待续建建筑因此明确返回 `QueuedContextCommandUnsupported`，不会静默改成 Move、立即执行或产生部分副作用。跨域任务队列需要独立版本化格式后再开放。

Godot Demo 中普通命令显示单圈反馈，Shift 队列显示双圈反馈；敌军锁定攻击和 AttackMove 使用红色反馈。

## Minimap 与 UI 解耦

Minimap 按三层组合，表现层可以高频换皮、改布局或加入动效，而不修改模拟和命令语义：

- `MinimapSnapshot`、`MinimapTransform`、`MinimapInteractionResolver` 是纯 C# 数据与规则，只表达世界边界、可见区域、障碍、标记和世界坐标意图。
- `RtsMinimapControl` 是纯表现 Control，只消费快照并发出 `FocusRequested` / `SmartCommandRequested`；它不持有 `RtsSimulation`，也不直接发命令。
- `RtsDemo` 是薄组合层，负责从现有世界生成快照，并把世界坐标意图接到 `OperationCameraController` 和既有 SmartCommand 解析。

当前支持静态障碍、友军/敌军、选择高亮、不同 footprint 建筑和视口框；左键或左键拖动定位镜头，右键发出 SmartCommand，A/Shift 修饰语义与主视图一致。

## 黑盒验收

- `queued-waypoints`：依次完成三段 Move，两个 Shift 命令均被记录为完成。
- `queued-command-replace`：即时命令清空两条待执行命令并到达新目标。
- `queued-capacity-limit`：16/16 待执行，额外两条得到 2 次显式 overflow。
- `control-group-recall`：Ctrl 覆盖 4 人、Shift 添加 2 人、召回 6 人并全部到达。
- `smart-command-sequence`：友军位置 Move → 敌军 AttackTarget → 地面 Move，目标死亡且两个 Shift 命令完成。
- `smart-command-gameplay-context`：乱序混合选择右键矿点后两名工人采集、Marine 移动；右键待续建建筑稳定选择最低 ID 工人，Shift 上下文明确拒绝，Package 的 2 条经济、2 条施工、3 条单位命令重放后 Hash 一致。
- `operation-selection-camera`：稳定点选、可见同类型双击、友军框选、光标锚定缩放、边缘滚动和编组双击定位全部通过。
- `minimap-interaction`：世界/面板坐标往返、视口框、定位意图、SmartCommand 意图和边界外拒绝全部通过，并录制真实 Minimap Control。

## 当前收口

操作表现基础闭环已覆盖选择、相机、编组定位和 Minimap。Alt 编组操作、混合选择子组、命令卡、皮肤与动画都留给实际游戏需求驱动；它们不再作为移动内核 Demo 的默认后续阶段。
