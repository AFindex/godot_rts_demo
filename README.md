# Godot 4.7 .NET RTS movement demo

完整实施状态、已完成能力、已知限制和后续顺序见 [进度回顾与 TODO](docs/PROGRESS_AND_TODO.md)。导航资产结构和编辑流程见 [导航 Resource 格式](docs/NAVIGATION_RESOURCE_FORMAT.md)，终点 V2 见 [终点协作收敛](docs/DESTINATION_CONVERGENCE_V2.md)。

这是一个纯 C# 的 RTS 移动原型。模拟层不依赖 Godot Node/PhysicsBody，Godot 层只负责输入、绘制和 `NavigationServer2D` 路径查询。

当前包含：

- 60 Hz 固定 Tick、SoA 单位数据和命令版本隔离。
- 运行时烘焙 Godot 2D NavMesh，静态障碍路径查询按 Tick 限流。
- Portal 高层图 A*：起终点接入最近可见 Portal，再沿已标注拓扑选路。
- Godot Resource 表达世界边界、障碍、Portal、Edge 和 Choke，启动时转换为纯 C# 不可变快照。
- 导航数据具有格式版本、固定错误码、规范字节和稳定哈希；非法资产会拒绝启动。
- 群组只规划一次高层路线，每个单位再查询分段 NavMesh 路径。
- 动态路径失效按移动命令分组，同组受影响单位共享一次新的高层路线。
- 32 人以上编队在终点区域进行安全槽位交换，解除绕行和超越后的局部平衡。
- 终点 V2 独立跟踪短期停滞和目标区总驻留时间；已就位阻挡者可临时让路、等待、返回，所有有限恢复失败后再分配唯一 Overflow 槽位。
- 狭口 `Approaching / Traversing / Exiting` 状态机、稳定横向排序和多车道分配。
- 双向狭口方向租约、入口 admission、批次容量、排空切换和防饥饿等待。
- Hold 单位堵塞狭口检测；堵塞期间双端关闭，释放后安全恢复。
- 跨命令目标槽位预留，多个波次不会重复占用同一批落点。
- 有限卡死恢复阶梯：避让侧、局部路径、高层路线、直接 fallback、Unreachable。
- 动态建筑 footprint、16px 占用栅格、navigation revision 和局部路径失效。
- Godot 静态 NavMesh 路径会经过动态占用验证，必要时使用纯 C# 栅格 A* 绕行。
- 车道偏移会写入每单位路径点，避免出口处被共享中心 waypoint 回拉。
- 目标槽位分配、空间哈希、候选速度 Steering、TTC、避让侧记忆和三轮碰撞推挤。
- 框选、点选、右键移动、Stop、Hold，以及路径、槽位、Portal 和狭口调试显示。

## 运行

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe `
  --path C:\Users\chenhuaifei\Documents\Playground\godot_net_rts\rts-demo-1
```

控制：

```text
左键/框选：选择单位
右键：移动到目标区域
Space：全选
S：Stop
H：Hold Position
D：切换路径、槽位、Portal 和狭口调试显示
R：重置场景
B：在鼠标处放置动态建筑
X：移除最近放置的动态建筑
```

## 自动验证

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --self-test
```

测试覆盖开放场到达、双向人流、Portal A* 确定性、路线静态可通行、狭口车道和实际穿越。成功时输出 `RTS_SELF_TEST PASS` 并返回退出码 0。

## Release 性能基准

```powershell
.\tools\run_benchmarks.ps1
```

基准使用独立纯 C# `net9.0` Release 入口，直接复用 Simulation 源文件，避免 Godot 编辑器加载 Debug 程序集。预热 240 Tick 后测量 360 Tick，覆盖 256、512 和 1000 单位，并保存 JSON 到 `benchmark_results/`。

当前性能门槛：

- 256 单位：P95 不超过 4ms。
- 512 单位：P95 不超过 12.5ms。
- 1000 单位：P95 不超过 16.67ms。
- 所有规模：当前线程分配不超过 1KB/Tick。

当前机器的 Release 基线约为 1.36ms、4.51ms 和 8.84ms P95；1000 单位主要耗时为 Steering，其次为动态碰撞。

## 导航数据资产

主场景通过 Inspector 引用 `data/demo_navigation_map.tres`。该 Resource 可以直接编辑障碍、Portal、Edge 和 Choke，运行时不会把 Godot 对象传进模拟层。

验证当前资产：

```powershell
.\tools\validate_navigation_resource.ps1
```

从纯 C# Demo 夹具重新生成示例资产：

```powershell
.\tools\generate_demo_navigation_resource.ps1
```

生成命令会覆盖示例 `.tres`，用于重置 Demo 数据；日常地图编辑应直接在 Godot Inspector 中修改独立 Resource。当前示例资产的格式版本为 1，稳定哈希为 `B8441F9F1544B950`。

## 录制测试录像

一次录制全部单项测试：

```powershell
.\tools\record_tests.ps1
```

只录一个测试，或调整录像帧率：

```powershell
.\tools\record_tests.ps1 -Case portal-choke -Fps 30
```

当前包含 29 个黑盒业务场景：单单位移动、开放场编队、密集编队、对向人流、垂直交叉流、移动命令替换、快速连续改令、受干扰后的终点收敛、外圈先就位后内部预留释放、跨命令共享目标、Stop、Hold、混合半径、越界目标、动态建筑局部失效、途中放置绕行、完全封路后移除恢复、动态 Portal 改道、大编队活动 Portal 失效与共享改道、Godot Resource 到纯 C# 运行时快照、单向狭口、平衡双向狭口、非对称双向流量、连续波次、Hold 堵口恢复、临时包围恢复、不可达重试上限和 192 单位压力场景。

场景只通过稳定的测试业务接口生成单位、发送 `Move / Stop / Hold`、推进时间并读取位置和业务状态，不读取 `UnitStore`、路径点、Steering、Portal 或狭口状态机。底层实现变化时只需要维护 `MovementTestRig` 适配器。

脚本会向运行中的测试目录自动查询全部用例，不维护第二份硬编码列表。录像、对应 Godot 日志和 `manifest.json` 会保存在 `test_videos/<录制时间>/`。某项失败时仍会继续录制剩余用例，并在最后返回失败。录像使用 Godot Movie Maker 的固定帧率模式；模拟始终以 60 Hz 推进，因此适合逐版本对比。

生成自动验证截图：

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --path . -- --capture
```

## 当前边界与下一阶段

动态建筑、双向狭口、跨命令槽位、终点局部换槽、外圈封闭后的主动 Yielding 与 Overflow fallback、动态失效后的群组路线共享、有限卡死恢复，以及 Godot Resource → 纯 C# 导航快照链路已有可运行版本。当前继续完成终点 V2 的局部多单位重新匹配、SlotDepth 和边界场景；之后进入 Clearance 与 Movement Class。自动 Portal/Sector Baker、战斗、联机和确定性回放暂未实现。
