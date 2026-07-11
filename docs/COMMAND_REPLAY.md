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

状态 Hash 格式版本 2 当前覆盖：

- Tick、世界边界、静态障碍、导航 revision 和按 ID 排序的动态建筑 footprint。
- 动态建筑下一稳定 ID，以及狭口的 LastDirection、ClearTicks、BurstTicks、等待计时等私有未来态。
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

当前测试每 30 Tick 采样一次。基线重复执行到 840 Tick 后得到状态 Hash `995B000A2787A5AE`；把最后一条命令目标偏移 90px 后，首次采样分歧位于 Tick 360，最终 Hash 为 `AE823DDB88EE4022`。

## 5. 性能

状态 Hash 不在每个模拟 Tick 强制执行，实际使用应按诊断周期采样。当前机器 Release 基准中，单次完整 Hash 平均耗时：

| 场景 | Hash 平均耗时 |
|---|---:|
| 256 单位移动 | 0.888ms |
| 512 单位移动 | 1.469ms |
| 1000 单位移动 | 1.663ms |
| 128 总单位持续战斗 | 0.151ms |
| 256 总单位持续战斗 | 0.859ms |

基准对 Hash 设置独立门槛，避免后续扩大状态覆盖时悄然引入不可接受的诊断成本。

## 6. Replay Package v2

Replay Package 在命令日志之外保存：

- Navigation、Gameplay Profile、Clearance Bake 的格式版本与稳定 Hash。
- 模拟容量、初始状态 Hash、稳定顺序的单位完整移动/战斗配置。
- 初始动态建筑 ID、Bounds 和放置 revision。
- 初始玩家账本、资源节点、DropOff、工人注册关系与初始工作状态。
- 后续建筑放置/移除世界命令、经济命令日志和既有单位命令日志。

重建时先严格匹配三类资源身份，再按清单创建建筑、单位和经济状态，并要求初始状态 Hash 完全一致。每个 Tick 固定应用世界命令、经济命令、单位命令，然后推进模拟，消除输入域之间的隐式顺序。

录制必须在 Tick 0、首条玩法命令之前启动。初始动态建筑允许先完成一轮连续放置，但不能在启动录制前先移除并留下 ID/revision 空洞；这种输入会明确拒绝，而不是生成无法精确重建的包。

当前验收包包含 8 个单位、1 个初始建筑、2 条动态建筑命令和 2 条单位命令；规范载荷 690 字节，包 Hash `800F3FAE85F87D40`。独立重建运行至 Tick 720 后，最终状态 Hash 与原运行同为 `AC0D861311EB0D52`。

## 7. Checkpoint v1

Checkpoint 使用 36 字节规范载荷保存格式版本、状态 Hash 格式版本、目标 Tick、Package Hash 和目标状态 Hash。恢复时先验证 Package 绑定，从 Tick 0 确定性 seek 到目标 Tick，并要求状态 Hash 完全匹配，然后继续使用已经推进到正确游标的同一个 Package Runner。

当前普通验收从 Tick 240 恢复到 Tick 720，后半段 17 个周期采样与连续运行完全相同；checkpoint Hash 为 `BE7D34BD9720DB76`，最终状态为 `AC0D861311EB0D52`。未知版本、截断数据和篡改后的目标状态 Hash 均被拒绝。

状态 Hash v2 额外使用 32 个单位的双向狭口场景，在交通租约活跃时于 Tick 240 建立 checkpoint，恢复后到 Tick 780 的 19 个采样全部一致，最终状态为 `083EDE1CDD642993`。这覆盖了 v1 未包含的私有交通状态。

Checkpoint 会记录状态 Hash 格式版本，因此 v1 checkpoint 不会被误当作 v2 使用；升级后必须重新生成，旧文件只作为历史诊断材料保留。

这一层固定了不泄漏内部 SoA 数组的上层契约，但恢复成本仍是 O(checkpoint Tick)，不是直接内存快照。

## 8. 热快照 v2

热快照在 Tick 240 深拷贝当前运行态，并在恢复时直接写回新模拟实例，不执行 Tick 0～239。当前覆盖：

- Unit SoA 全部未来态、每单位路径点/游标和高层路线 waypoint。
- Combat SoA 的生命、目标、AttackMove 意图、攻击槽、前摇与冷却。
- 活动命令、Shift 队列环形缓冲、完成/溢出计数和稳定序列号。
- 动态建筑 revision、next ID 和全部 footprint。
- 玩家账本、资源节点余量/占用、DropOff，以及工人的工作阶段、目标、货物和计时。
- 狭口私有交通租约与诊断快照。
- 路径预算、移动组/命令序列号、动态失效计数和待处理路径请求。

恢复后根据 snapshot Tick 直接定位 Replay Package 的世界、经济和单位命令游标，再继续运行。原战斗验收仍覆盖 AttackMove 接敌、未消费 Shift 命令、活动路径、动态建筑和狭口状态；新增经济验收在 Tick 240 的活跃采集循环中直接恢复，到 Tick 900 与连续运行完全一致。

## 9. 持久化热快照格式 v2

头部绑定快照格式、状态 Hash 格式、Package Hash、Tick、声明状态 Hash 和模拟容量；正文按逻辑状态保存 Unit、Combat、Economy、Queue、World、Choke 和私有请求数据。当前战斗场景载荷为 3,869 字节，6 工人经济场景载荷为 2,627 字节；大小随实体与路径状态变化，不作为协议常量。

Shift 队列按执行顺序编码，反序列化时可重建为新的物理环形布局，因此持久化格式不依赖 `_heads` 或底层数组排列。路径和高层路线使用有界点数，容量、单位数、建筑数、狭口数和待处理请求均有读取上限。

同一快照 round-trip 后生成相同规范字节，并能直接恢复。未知格式、旧状态 Hash 版本、截断载荷、尾部多余数据、结构非法正文、Package 不匹配和正文单字节篡改都会被拒绝。`CanonicalBytes` 可由上层直接写入文件或网络存储，模拟层不绑定具体文件系统 API。

## 10. 经济命令边界与未完成项

`EconomyCommandLogSnapshot` 格式 1 只记录外部玩家意图：Gather 与 Refinery operational 变化。Return Cargo、自动转矿和内部 Move/Stop 是确定性派生状态，不重复写入单位命令日志。这样回放不会把一次 Gather 展开成两次移动命令。

Replay Package v2 与热快照 v2 已保存当前全部采集经济未来态。正式建筑实体已经在 S11-C1 出现，因此下一格式会加入 Build/Cancel/Resume、建筑清单与施工未来态；在此之前活动施工明确拒绝录制/热快照，避免静默漏存。Train/Rally 仍随 S11-D 的正式生产实体加入。

当前仍只承诺相同运行时精确确定性，不承诺跨 CPU/.NET/平台 Lockstep。服务端权威和表现插值留到真正进入联机阶段再决策。

## 11. 验证入口

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test command-log-replay

F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test command-replay-divergence

F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test replay-package-world

F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test replay-checkpoint-resume

F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test replay-checkpoint-choke

F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test replay-hot-snapshot

F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --visual-test economy-replay-persistence
```

状态 Hash v2 录像是经济加入前的历史基线；Hash v3 对应经济格式 v2。当前状态 Hash v4 额外覆盖正式建筑生命周期，由 69 项全量回归验证；经济与建筑专用录像分别位于 `test_videos/20260711_163809/` 和 `test_videos/20260711_170403/`。
