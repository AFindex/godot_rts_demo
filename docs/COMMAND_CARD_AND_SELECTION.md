# 混合选择子组与解耦命令卡

更新日期：2026-07-12

## 边界

S11-J1 把选择表现拆成三层：

1. 纯 C# `GameplaySelectionSnapshot` 接收稳定实体引用，按 Worker、CombatUnit、Building，再按 Type ID 和 Entity ID 规范分组。
2. `CommandCardComposer` 只把活动子组与业务候选合成为不可变 `CommandCardSnapshot`，不访问模拟、Godot Node 或 UI 状态。
3. `RtsCommandCardControl` 只绘制快照并发出 `CommandCardActionSnapshot` 意图；`RtsDemo` 薄绑定把意图交给现有正式命令。

UI 换布局、颜色、图标、动效或按钮排列时，不需要修改生产、研究、经济和单位命令规则。

## 选择规则

- 单位、工人和己方建筑可以同时存在于选择集中。
- 相同 Kind/Type ID 形成一个子组；子组和成员都有稳定排序。
- 重建快照时优先保留原活动子组；子组消失后稳定回退到第一组。
- Tab 正向切换，Shift+Tab 反向切换；命令卡动作只作用于当前活动子组。
- 点选或 Shift 点选建筑不再强制清空已有单位；框选仍只加入单位，避免无意框入大面积建筑。

## 当前动作

- Worker / CombatUnit：Move、Attack Move、Stop、Hold Position。
- 已完成生产建筑：Set Rally；按 Catalog 和正式 Availability 显示 Train；显示并取消当前生产订单。
- 已完成研究建筑：按 Technology Catalog 和正式 Availability 显示 Research；显示并取消当前研究订单。
- 未完成建筑：Cancel Construction。

按钮的 Enabled/Status 来自业务可用性结果，例如 `Success`、`SupplyBlocked` 或 `MissingPrerequisite`。Control 不自行计算资源、人口、前置或队列上限。

## J1/J2a/J2b1 收口与后续

J1 完成混合选择、活动子组、快照命令卡和已有即时动作。J2a 完成混合 Control Group 与 Alt 抢组。J2b1 已完成统一目标模式及 Move、Attack Move、Rally。以下留给 J2b2：

- Build 的建筑类型选择、放置预览、合法性反馈与确认。
- 多建筑同类型批量生产策略和多选队列聚合显示。
- 图标资产、快捷键重映射、tooltip 和最终皮肤。

## 验收

- `OperationPresentationSelfTest`：乱序/重复输入规范化为 4 个实体、3 个子组；正反循环、候选过滤、稳定排序和禁用原因通过。
- `operation-mixed-command-card`：2 Worker + 1 Combat Unit + 1 Barracks 形成 3 个子组；Barracks 命令卡完成 Train → Cancel → Train，并最终出生 1 个单位。
- `control-group-mixed-steal`：混合组保存 2 Worker + Marine + Barracks；Alt+2 抢走一个工人，Alt+Shift+2 再抢走第二个工人和建筑，最终两组分别稳定得到 `1u/0b` 与 `2u/1b`。
- `operation-target-command-mode`：两次 Shift Move 保持目标模式并形成队列，右键取消不发命令；Rally 和 Attack Move 分别走正式生产/玩家命令，3/3 单位到达。
- 真实 UI AV1/WebM 位于 `test_videos/20260712_024209/`。
- 混合编组 AV1/WebM 位于 `test_videos/20260712_030457/`。
- 目标模式 AV1/WebM 位于 `test_videos/20260712_034401/`。
