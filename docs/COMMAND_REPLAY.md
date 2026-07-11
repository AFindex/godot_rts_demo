# 确定性命令日志与回放

这一阶段提供的是“相同初始状态、相同运行时、相同命令日志”的精确回放与分歧定位骨架。它用于回归测试、录像复核和后续联机方案验证，不宣称当前已经具备跨 CPU、跨 .NET 版本或跨平台的 Lockstep 能力。

## 1. 已实现边界

- `SimulationCommandRecorder` 在命令真正进入模拟层时记录已解析的外部单位命令。
- `SimulationCommandLogSnapshot` 使用格式版本 1、固定小端序和规范单位 ID 顺序进行序列化。
- `SimulationCommandReplay` 在对应 Tick 的 `Step` 之前重新注入命令。
- `SimulationStateHasher` 对影响未来模拟的状态做精确 64 位 FNV-1a Hash。
- `SimulationReplayTrace` 周期采样状态 Hash，并报告两次执行的首次分歧 Tick。
- 测试只通过 `MovementTestRig` 的业务接口录制和回放，不读取路径、Steering 或 SoA 内部数组。

当前记录的命令是 `Move / AttackMove / AttackTarget / Stop / Hold`，包括目标点、是否排队和稳定排序后的单位 ID。Shift 队列自动出队、追击更新、重新寻路等派生行为不会重复写入日志，因为它们必须由模拟状态自然复现。

Control Group 和 SmartCommand 属于输入解析层：它们在进入模拟层前被解析成具体单位集合和 `UnitOrder`。因此回放不依赖当时的选择框、编组或 UI 状态。

## 2. 规范日志格式 v1

头部包含 magic、格式版本和条目数；每条记录包含：

```text
Tick + OrderKind + Queued + TargetX + TargetY + UnitCount + sorted UnitIds
```

读取时拒绝不支持的版本、截断数据、非法 Tick、非法命令类型、非有限目标点、空单位集合、乱序或重复 ID，以及尾部多余数据。同一逻辑日志必须生成 byte-identical 的规范字节和相同稳定 Hash。

当前验收夹具记录 7 条命令，规范载荷为 274 字节，日志 Hash 为 `160A423C68D3AF41`。它同时验证完整 round-trip、未知版本拒绝和截断载荷拒绝。

## 3. 状态 Hash 覆盖

格式版本 1 当前覆盖：

- Tick、世界边界、静态障碍、导航 revision 和按 ID 排序的动态建筑 footprint。
- 单位存活、位置、速度、目标、移动配置、移动模式、路径/高层路线游标、目标槽位和各恢复状态机。
- 战斗生命、阵营、攻击配置、目标、前摇/冷却、AttackMove 恢复意图和战斗占位。
- 每单位活动命令、逻辑顺序的待执行队列和稳定序列号。
- 狭口交通租约、等待/通过状态、路径请求队列、路径预算和动态失效队列。

浮点数按原始 IEEE-754 bits Hash，不做容差或显示取整。选择状态、调试显示和命令录制器自身不影响未来模拟，因此不进入 Hash。

## 4. 正确的回放顺序

回放必须从相同的地图数据、Gameplay Profile、Clearance Bake、单位生成顺序和初始建筑状态开始。每个固定 Tick 执行：

```text
注入 command.Tick == simulation.Tick 的全部命令
-> simulation.Step()
-> 到采样边界时计算 StateHash
```

当前测试每 30 Tick 采样一次。基线重复执行到 840 Tick 后得到状态 Hash `0745CD63FEF92D2C`；把最后一条命令目标偏移 90px 后，首次采样分歧位于 Tick 360，最终 Hash 为 `3987BC62C2F24A60`。

## 5. 性能

状态 Hash 不在每个模拟 Tick 强制执行，实际使用应按诊断周期采样。当前机器 Release 基准中，单次完整 Hash 平均耗时：

| 场景 | Hash 平均耗时 |
|---|---:|
| 256 单位移动 | 0.750ms |
| 512 单位移动 | 1.449ms |
| 1000 单位移动 | 1.587ms |
| 128 总单位持续战斗 | 0.136ms |
| 256 总单位持续战斗 | 0.660ms |

基准对 Hash 设置独立门槛，避免后续扩大状态覆盖时悄然引入不可接受的诊断成本。

## 6. Replay Package v1

Replay Package 在命令日志之外保存：

- Navigation、Gameplay Profile、Clearance Bake 的格式版本与稳定 Hash。
- 模拟容量、初始状态 Hash、稳定顺序的单位完整移动/战斗配置。
- 初始动态建筑 ID、Bounds 和放置 revision。
- 后续建筑放置/移除世界命令，以及既有单位命令日志。

重建时先严格匹配三类资源身份，再按清单创建建筑和单位，并要求初始状态 Hash 完全一致。每个 Tick 固定先应用世界命令、再应用单位命令，消除两类输入之间的隐式顺序。

录制必须在 Tick 0、首条玩法命令之前启动。初始动态建筑允许先完成一轮连续放置，但不能在启动录制前先移除并留下 ID/revision 空洞；这种输入会明确拒绝，而不是生成无法精确重建的包。

当前验收包包含 8 个单位、1 个初始建筑、2 条动态建筑命令和 2 条单位命令；规范载荷 690 字节，包 Hash `5229BB45B72D5D04`。独立重建运行至 Tick 720 后，最终状态 Hash 与原运行同为 `E6CA47E1FC090065`。

## 7. Checkpoint v1

Checkpoint 使用 36 字节规范载荷保存格式版本、状态 Hash 格式版本、目标 Tick、Package Hash 和目标状态 Hash。恢复时先验证 Package 绑定，从 Tick 0 确定性 seek 到目标 Tick，并要求状态 Hash 完全匹配，然后继续使用已经推进到正确游标的同一个 Package Runner。

当前验收从 Tick 240 恢复到 Tick 720，后半段 17 个周期采样与连续运行完全相同；checkpoint Hash 为 `A83117D9561B50E2`，最终状态仍为 `E6CA47E1FC090065`。未知版本、截断数据和篡改后的目标状态 Hash 均被拒绝。

这一层固定了不泄漏内部 SoA 数组的上层契约，但恢复成本仍是 O(checkpoint Tick)，不是直接内存快照。

## 8. 明确未完成项

E4.2 需要保存并直接恢复影响未来模拟的完整运行时状态，避免从 Tick 0 重演。Replay Package 目前记录矩形动态建筑，但尚未覆盖未来可能出现的单位生产/销毁、非矩形建筑或玩家/随机种子等玩法状态。

跨平台 Lockstep、服务端权威和表现插值仍需在直接快照边界稳定后再决策。

## 9. 验证入口

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test command-log-replay

F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test command-replay-divergence

F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test replay-package-world

F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test replay-checkpoint-resume
```

对应规范录像位于 `test_videos/20260711_090654/`、`test_videos/20260711_094134/` 和 `test_videos/20260711_110031/`。
