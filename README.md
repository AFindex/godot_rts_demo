# Godot 4.7 .NET RTS movement demo

完整实施状态见 [进度回顾与 TODO](docs/PROGRESS_AND_TODO.md)。实际玩法见 [S11 经济、建造与生产](docs/ECONOMY_AND_PRODUCTION.md)，比赛状态见 [比赛生命周期](docs/MATCH_LIFECYCLE.md)，AI 边界见 [RTS AI 架构](docs/AI_ARCHITECTURE.md)，AI 数据见 [AI Configuration Resource](docs/AI_CONFIGURATION_RESOURCE.md)，完整演示见 [双 AI 遭遇战测试关卡](docs/AI_ENCOUNTER_LEVEL.md)，科技数据见 [Technology Catalog Resource](docs/TECHNOLOGY_RESOURCE.md)，战斗移动见 [AttackMove 与战斗占位](docs/ATTACK_MOVE.md)、[武器移动约束](docs/WEAPON_MOVEMENT.md)、[自动目标评分](docs/TARGET_SELECTION.md) 和 [战斗接触优先级](docs/COMBAT_CONTACT.md)，弹道显示见 [战斗弹道表现边界](docs/COMBAT_PRESENTATION.md)，操作层见 [命令队列、编组与 SmartCommand](docs/OPERATION_LAYER.md) 和 [混合选择与命令卡](docs/COMMAND_CARD_AND_SELECTION.md)，确定性基础见 [命令日志与回放](docs/COMMAND_REPLAY.md)。导航资产见 [导航 Resource 格式](docs/NAVIGATION_RESOURCE_FORMAT.md)，单位/建筑数据见 [Gameplay Profile Resource](docs/GAMEPLAY_PROFILE_RESOURCE.md)，离线数据见 [Clearance Bake 格式](docs/CLEARANCE_BAKE_FORMAT.md)，多尺寸导航见 [Clearance 与 Movement Class](docs/CLEARANCE_AND_MOVEMENT_CLASS.md)，编辑器显示见 [多尺寸净空预览](docs/CLEARANCE_EDITOR_PREVIEW.md)，资源更新见 [Resource 热重载与差异诊断](docs/RESOURCE_HOT_RELOAD.md)，全局放置保护见 [Connectivity Guard](docs/GLOBAL_CONNECTIVITY_GUARD.md)。

正常启动首先进入中文启动页。可选择“进入 RTS 演示”，或打开测试中心浏览全部黑盒业务场景；测试中心支持分类和中英文检索，选中场景后会显示它验证的业务目标，并通过与命令行回归、自动录像相同的 `VisualTestCatalog` case id 启动。测试结束或手动停止后会返回目录，不会把测试世界写回默认演示运行时。

这是一个纯 C# 的 RTS 移动原型。模拟层不依赖 Godot Node/PhysicsBody，Godot 层只负责输入、绘制和 `NavigationServer2D` 路径查询。

## 首次克隆环境

- 使用 Godot 4.7 stable .NET/Mono 版本；普通非 .NET 版本不能加载本工程的 C# 脚本。
- 安装 x64 .NET 9 SDK；`global.json` 接受当前机器上最新的稳定 .NET 9 feature band。
- `NuGet.Config` 只引用公开 NuGet v3 源，不包含开发机器上的 Godot 安装绝对路径。

首次打开 Godot 前建议在仓库根目录执行：

```powershell
dotnet --list-sdks
dotnet restore
dotnet build
```

如果 Godot 曾在 C# 编译失败时导入过工程，关闭编辑器、删除本地 `.godot/` 后重新执行以上命令。大量 `Cannot instantiate C# script` 往往是程序集编译失败的后续错误，应优先查看 `dotnet build` 输出的第一条错误。

当前包含：

- 60 Hz 固定 Tick、SoA 单位数据和命令版本隔离。
- 运行时烘焙 Godot 2D NavMesh，静态障碍路径查询按 Tick 限流。
- Portal 高层图 A*：起终点接入最近可见 Portal，再沿已标注拓扑选路。
- Godot Resource 表达世界边界、障碍、Portal、Edge 和 Choke，启动时转换为纯 C# 不可变快照。
- 导航数据具有格式版本、固定错误码、规范字节和稳定哈希；非法资产会拒绝启动。
- 群组只规划一次高层路线，每个单位再查询分段 NavMesh 路径。
- 动态路径失效按移动命令分组，同组受影响单位共享一次新的高层路线。
- 32 人以上编队在终点区域进行安全槽位交换，解除绕行和超越后的局部平衡。
- 大编队可对最多 24 个局部单位执行有预算的原子重匹配，并用进入方向秩序支持三单位以上循环换槽。
- 终点 V2 独立跟踪短期停滞和目标区总驻留时间；已就位阻挡者可临时让路、等待、返回，所有有限恢复失败后再分配唯一 Overflow 槽位。
- 狭口 `Approaching / Traversing / Exiting` 状态机、稳定横向排序和多车道分配。
- 双向狭口方向租约、入口 admission、批次容量、排空切换和防饥饿等待。
- Hold 单位堵塞狭口检测；堵塞期间双端关闭，释放后安全恢复。
- 跨命令目标槽位预留，多个波次不会重复占用同一批落点。
- 有限卡死恢复阶梯：避让侧、局部路径、高层路线、直接 fallback、Unreachable。
- 动态建筑 footprint、16px 占用栅格、navigation revision 和局部路径失效。
- Godot 静态 NavMesh 路径会经过动态占用验证，必要时使用纯 C# 栅格 A* 绕行。
- Small/Medium/Large 使用 6/8/12px 导航净空档位；Grid、Portal、Godot 路径复验和动态建筑共享同一尺寸语义。
- 建筑提供 32×32、64×48、112×80、160×120 四档 footprint；业务放置检查重叠、单位占用与局部假窄缝并返回稳定结果码。
- 建筑业务放置会按指定 Movement Class 比较放置前后连通分量，拒绝切断已有全局通路并返回 `DisconnectsNavigation`。
- 单位半径/速度/加速度和建筑尺寸/放置规则来自 Godot Gameplay Profile Resource，启动时转换为带稳定哈希的纯 C# 快照。
- `Main.tscn` 内置 `[Tool]` 多尺寸净空预览：同时显示三档障碍膨胀轮廓、Portal 宽度/可通行等级和四档建筑 footprint。
- Clearance Bake 将三档 walkable 位图、component ID 和 16×16-cell chunk 布局保存为带源导航哈希的版本化 Resource；静态运行时直接复用，单次动态 revision 只重采样 dirty chunks，多次累计变更安全回退全量分析。
- 增量 Connectivity 已接入 GridPathProvider：示例建筑加入/移除只重采样 512/3,080 cells，最终拓扑与全量分析逐 cell、逐 component ID 一致。
- 放置预检可生成独立的三档 Connectivity before/after 快照：同时报告分量变化、被阻塞 cell、split、断连 cell 和 dirty chunks；Godot 面板只消费该快照，不读取模拟或实现拓扑算法。
- Navigation/Profile/Bake 支持 `CacheMode.Replace` Fresh Load、原子资源集校验、逐类型差异和应用影响等级；导航/Profile 变化明确要求重建模拟。
- 同源且网格布局兼容的 Bake-only 候选可两阶段原子提交：同时替换 Grid/放置守卫缓存，并按 MovementGroup 重规划活动单位；录制中或错误导航哈希会拒绝。
- 默认文件监听将 Navigation/Profile/Bake 写入事件送入纯 C# 去抖状态机；完整 Fresh Load 后仅自动提交安全的 Bake-only 更新，Navigation/Profile 变化停在 `RebuildSimulation`，文件线程不调用 Godot API。
- 独立生成的热重载测试资源位于 `test_resources/hot_reload/`，不修改正式 Demo 数据。
- 车道偏移会写入每单位路径点，避免出口处被共享中心 waypoint 回拉。
- 目标槽位分配、空间哈希、候选速度 Steering、TTC、避让侧记忆和三轮碰撞推挤。
- 纯 C# AttackMove 状态机：错峰选敌、追击重寻路、前摇/冷却/伤害、leash、死亡清理和恢复原路线。
- 近战单位使用唯一接触槽并沿目标外圈分段就位；远程单位使用射程内唯一攻击环，交叉后可做严格降误差的局部换槽。
- Stop 会在局部索敌并追击，Hold 只攻击射程内目标且不离开原位置。
- 每单位固定 16 条 Shift 命令队列；同序列、同 Tick 到期的单位重新批量进入群组寻路。
- Control Group 支持单位/建筑混编：Ctrl+数字覆盖、Shift+数字添加、Alt+数字抢组覆盖、Alt+Shift+数字抢组添加；召回自动过滤死亡/销毁/失去控制的实体。
- SmartCommand 将地面/友军位置解析为 Move、敌军解析为锁定攻击，资源点按工人/非工人拆分为 Gather/Move，待续建己方建筑由稳定最低 ID 工人接管；Shift 可排队采集/续建任务，A 修饰统一解析为 AttackMove。
- 纯 C# SelectionFilter 支持稳定点选、友军框选和可见区域双击同类型选择。
- 相机支持边缘/方向键滚动、光标锚定缩放和编组数字键双击定位。
- Minimap 显示静态障碍、单位、不同 footprint 的建筑和当前视口框；左键/拖动定位，右键复用 SmartCommand。
- Minimap 使用纯 C# 快照/坐标/交互意图、独立 Godot Control 和薄业务绑定三层结构，换皮与动效迭代不进入模拟层。
- Worker、Combat Unit 和 Building 可形成稳定混合选择子组；独立命令卡 Control 只消费不可变快照，Train/Research/取消/Stop/Hold 意图复用正式业务命令。
- 命令卡 Move、Attack Move、Rally 进入统一目标模式；左键确认、右键/Escape 取消，Shift 可连续追加移动/攻击移动落点。目标光标由独立 Overlay 消费纯快照绘制。
- Worker 命令卡按 Building Type Catalog 展示 5 种实际建筑；Build 目标模式使用 8px 吸附、正式无副作用施工预览、红绿 footprint、稳定最近工人选择，并在确认时复用正式施工命令。
- S11 双资源经济支持 Minerals、Vespene、Supply 原子交易，矿脉单工人占用、Refinery 三工人容量、携带返还、枯竭转场、所有权校验和普通命令取消工作。
- 经济/建筑/生产/研究 UI 使用不可变 Snapshot；状态 Hash v18 覆盖生产、Rally、配方前置、科技等级、单位/建筑战斗字段、武器移动约束、目标锁定、活跃投射物及研究队列未来态。
- 版本化规范命令日志、固定 Tick 回放、精确状态 Hash 和首次分歧 Tick 定位。
- Replay Package 保存资源版本/Hash、初始单位与建筑清单，并按固定顺序重放动态建筑世界命令。
- 版本化 checkpoint 绑定 Package/状态 Hash，可确定性 seek 到中间 Tick 后继续精确回放。
- 状态 Hash v18 保存研究等级、完整科技 Profile、队列顺序、进度、单位/建筑战斗伤害、武器移动约束、目标锁定与投射物未来态。
- 进程内热快照可直接恢复 Unit/Combat SoA、路径、Shift 队列、动态占用、狭口租约和待处理请求，无需重演早期 Tick。
- 热快照具有版本化规范二进制编码，可直接保存到磁盘；未知版本、截断、正文篡改和错误 Package 均被拒绝。
- 战斗状态与移动路径分离；死亡保持稳定 unit ID，但从寻路邻居、碰撞、选择和建筑占用中移除。
- 框选、点选、右键移动、Stop、Hold，以及路径、槽位、Portal 和狭口调试显示。

## 运行

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe `
  --path C:\Users\chenhuaifei\Documents\Playground\godot_net_rts\rts-demo-1
```

控制：

```text
左键/框选：选择单位
双击单位：选择当前视野内同类型友军
右键：SmartCommand（地面/友军位置移动、敌军锁定攻击、工人采集资源、工人续建待施工建筑）
选中生产建筑后右键：设置 Ground/资源点/友军单位 Rally；新工人对资源目标自动采集
A + 右键：AttackMove 到目标区域
Shift + 右键：追加 SmartCommand 到每单位命令队列
Ctrl + 数字：覆盖对应 Control Group
Shift + 数字：添加到对应 Control Group
Alt + 数字：覆盖对应 Control Group，并把所选实体从其他组移出
Alt + Shift + 数字：添加到对应 Control Group，并把所选实体从其他组移出
数字：召回 Control Group
双击数字：召回并把镜头定位到 Control Group
鼠标滚轮：以光标为锚点缩放
屏幕边缘/方向键：移动镜头
Minimap 左键/拖动：定位镜头
Minimap 右键：SmartCommand（支持 A/Shift 修饰）
Space：全选
S：Stop
H：Hold Position
D：切换路径、槽位、Portal 和狭口调试显示
R：重置场景
B：在鼠标处轮换放置 Small/Medium/Large/Huge 动态建筑（业务合法性检查）
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

基准使用独立纯 C# `net9.0` Release 入口，直接复用 Simulation 源文件，避免 Godot 编辑器加载 Debug 程序集。预热 240 Tick 后测量 360 Tick，覆盖 256、512、1000 单位移动，以及双方持续 AttackMove 的 128/256 总单位场景，并保存 JSON 到 `benchmark_results/`。

当前性能门槛：

- 256 单位：P95 不超过 4ms。
- 512 单位：P95 不超过 12.5ms。
- 1000 单位：P95 不超过 16.67ms。
- 非战斗移动：当前线程分配不超过 1KB/Tick。
- 活跃投射物战斗 128/256 总单位：P95 不超过 4/8ms，分配不超过 8KB/Tick。

当前机器最近一次 Release 移动基线为 1.37ms、4.29ms 和 11.50ms P95；双方持续 AttackMove 且保有 417/163 个末帧活跃投射物的 128/256 总单位基准为 2.01/3.00ms P95。完整状态 Hash v18 在 1000 单位场景平均约 1.76ms。

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

导航几何修改后重新生成并验证 Clearance Bake：

```powershell
.\tools\generate_demo_clearance_bake.ps1
.\tools\validate_clearance_bake.ps1
```

当前 Bake 格式版本为 1，源导航哈希 `B8441F9F1544B950`，Bake 哈希 `A1BCA2BD6C885350`，规范载荷 38,215 字节。

重新生成与正式数据隔离的 Resource Reload 变体资源：

```powershell
.\tools\generate_hot_reload_test_resources.ps1
```

生成内容位于 `test_resources/hot_reload/`，覆盖 Navigation 障碍变化、单位 Profile 变化、与新导航匹配的 Clearance Bake，以及同 Navigation 的 Bake-only 自动提交候选。

## 录制测试录像

一次录制全部单项测试：

```powershell
.\tools\record_tests.ps1
```

只录一个测试，或调整录像帧率：

```powershell
.\tools\record_tests.ps1 -Case portal-choke -Fps 30
```

录像流水线先让 Godot Movie Maker 生成临时 AVI，再自动使用固定版本 FFmpeg 的
`libsvtav1` 编码为 AV1/WebM；只有 codec、分辨率和逐帧数量验证通过后才删除临时
AVI。默认使用 `CRF 32 / preset 8 / yuv420p / 无音轨`，也可以显式调整：

```powershell
.\tools\record_tests.ps1 -Case portal-choke -Fps 30 -Crf 32 -EncoderPreset 8
```

首次运行会下载并校验 FFmpeg 8.1.2 Full Build 到 `tools/.cache/`，工具二进制不进入
版本库。历史 AVI 可通过 `tools/compress_test_videos.ps1` 批量迁移。当前 85 段历史录像
已从 3,309,160,498 字节压缩到 228,515,601 字节（保留 6.91%，节省约 3.08GB）。
完整约定见 [测试录像与 FFmpeg 工具链](docs/VIDEO_RECORDING.md)。

当前包含 100 个黑盒业务场景，并覆盖启动页/测试中心、AI Resource/双 AI 自对战/成对热恢复/无 AI 回放、模块化对局 AI、双 AI 持续发展/扩张/科技/交战关卡、双资源经济、基地扩张/饱和度/工人转场、玩家视野/探索/命令权限、玩家能力/失败/终局、五类正式建筑、Build 放置预览、生产队列、同类多建筑批量生产/聚合队列/逐建筑取消、多建筑生产前置、正式研究等级/取消/互斥、Ground/资源/友军 Rally 及其回放热恢复、版本化数据、建筑战斗、确定性战斗事件流/投射物及解耦表现、固定/移动武器约束、自动目标评分与稳定锁定、战斗接触优先级、单位/建筑护甲、属性加成、多段攻击和攻防科技、Clearance、动态地图、Portal/狭口、AttackMove、混合编组/目标模式与操作层、Replay Package、checkpoint 和热快照。

场景只通过稳定的测试业务接口生成单位、发送 `Move / Stop / Hold`、推进时间并读取位置和业务状态，不读取 `UnitStore`、路径点、Steering、Portal 或狭口状态机。底层实现变化时只需要维护 `MovementTestRig` 适配器。

脚本会向运行中的测试目录自动查询全部用例，不维护第二份硬编码列表。AV1/WebM 录像、对应 Godot 日志和 `manifest.json` 会保存在 `test_videos/<录制时间>/`。某项失败时仍会继续录制剩余用例，并在最后返回失败。录像使用 Godot Movie Maker 的固定帧率模式；模拟始终以 60 Hz 推进，因此适合逐版本对比。

生成自动验证截图：

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --path . -- --capture
```

## 当前边界与下一阶段

S11-E2 已收口：Academy、正式研究队列、多级/互斥科技，以及 Technology Godot Resource 数据工作流已经闭环；当前目录 v1 Hash 为 `8F9990031AA55B5E`。

S11-F1 已完成：Town Hall 完工/摧毁驱动基地与 DropOff 生命周期；资源按最近有效基地归属，公开快照提供矿气节点数、当前/理想工人数和饱和度；确定性工人转场进入 Economy Command Log v2。

S11-F2 已完成：32 单位确定性视野网格、探索 bitset、PlayerView 只读合同、正式玩家命令权限，以及 Godot 世界/Minimap/SmartCommand 防信息泄露边界已经闭环。

S11-G 已完成：版本化比赛清单、建立存在门槛、玩家能力快照、失败/胜利/平局状态和终局命令权限已经闭环，详见 [比赛生命周期](docs/MATCH_LIFECYCLE.md)。Replay Package/Hot Snapshot 当前为 v17，状态 Hash 为 v18。

S11-H1 模块化对局 AI 已完成：Economy/Build/Production/Technology/Scouting/Defense/Combat 七类 Planner、版本化 Blackboard、资源与单位占用仲裁、施工续建和执行反馈已经形成可持续对局。专用场景完成供给、矿气调度、五类建筑、扩张、科技、侦察和摧毁敌方基地，详见 [RTS AI 架构](docs/AI_ARCHITECTURE.md)。

S11-H2 已完成：Standard/Aggressive 配置进入 [AI Configuration Resource](docs/AI_CONFIGURATION_RESOURCE.md)；双 AI 使用不同周期/offset 自对战。Tick 1,200 成对恢复后继续运行 AI，以及 Tick 0 只重放正式命令且不启动 AI，两条链都与连续运行最终 Hash 一致。

S11-H3 已完成：解耦的 [双 AI 持续遭遇战关卡](docs/AI_ENCOUNTER_LEVEL.md) 从 6 工人开局，自主完成双方基础设施、扩张、总 4 级科技和持续互攻；关卡编排只依赖稳定业务合同，不读取模拟内部 Store。

S11-I1/I2 已完成：资源点与己方建筑进入正式 SmartCommand 目标协议；混合选择按能力拆分，乱序选择稳定选择最低 ID 施工者。Shift 现在可排队 `Move → GatherResource` 与 `Move → ResumeConstruction`；未来 Tick 重新验证失败的任务会有界跳过并继续下一条命令，详见 [操作层](docs/OPERATION_LAYER.md)。

S11-J1 已完成：Worker、Combat Unit、Building 混合选择按稳定子组组织，Tab/Shift+Tab 切换活动子组；命令卡通过纯 C# Snapshot/Composer、独立 Godot Control 和薄意图绑定展示并执行 Stop/Hold、Train/Research 与取消。详见 [混合选择与命令卡](docs/COMMAND_CARD_AND_SELECTION.md)。

S11-J2b2b 已完成：活动同类型生产建筑会按稳定 Building ID 批量 Train；命令卡聚合显示生产者数、可发令数和总排队数；取消按每座建筑最新的匹配配方订单各撤销一条。共享资源、人口和队列容量仍由正式生产 API 逐建筑重新验证。

S8-E1 已完成：固定容量 `CombatEventStream` 按 Tick/Sequence 发布 AttackStarted、Impact、TargetDestroyed；单位和建筑共用事件合同，Godot 表现、音效、弹道和诊断不再需要读取战斗状态机内部数组。

S8-E2a 已完成：伤害合同支持护甲、Light/Armored/Biological/Mechanical/Structure/Massive 属性、对属性加成、最多 32 段攻击、每级基础/加成升级量和 0.5 最低单段伤害；Production Catalog v3 可在 Inspector 编辑全部字段，Infantry Weapons 等级会实时进入正式命中与无副作用预览。

S8-E2b 已完成：Building Type Catalog v2 为五类不同尺寸建筑声明 Structure/Mechanical、0/1/2 基础护甲和每级防御增量；Fortification Doctrine 实时参与建筑预览与正式受击，单位和建筑完全复用同一伤害公式。

S8-E3a 已完成：固定容量确定性投射物使用稳定 ID，发射时冻结武器/科技载荷，命中时读取目标当前防御；跟踪移动目标，目标失效明确过期且不重定向。飞行状态进入 Replay Package/Hot Snapshot，Production Catalog 可配置 ProjectileSpeed。

S8-E3b 已完成：纯 C# `CombatPresentationComposer` 输出不可变飞行/拖尾/命中提示帧；独立 Godot Layer 和主题 Resource 负责 Bolt/Orb/Volley 的颜色、形状及命中脉冲。表现历史不进入权威状态，换皮不会修改战斗或回放。

S8-E4a 已完成：武器 Profile 独立声明前摇/冷却期间能否沿 AttackMove 移动；Move 可取消未完成前摇但不能重置已开始的冷却。Production Catalog v5、Package/Hot v16 与 Hash v17 完整保存该行为。

S8-E4b 已完成：自动索敌按距离、0～10 基础优先级、武器属性克制和目标攻击能力评分；0.75 秒锁定、语义优势和 2,500 分门槛共同防止目标抖动，玩家显式 AttackTarget 永不被覆盖。

S8-E5a 已完成：碰撞阶段从当前战斗状态派生固定前摇、近战接触、固定冷却、移动射击和普通五类角色；火力线可抗挤压但不存在永久刚体，通用碰撞解算器不依赖战斗内部状态。详见 [战斗接触优先级](docs/COMBAT_CONTACT.md)。

生产目录资产说明见 [Production Catalog Resource](docs/PRODUCTION_CATALOG_RESOURCE.md)。

科技目录资产说明见 [Technology Catalog Resource](docs/TECHNOLOGY_RESOURCE.md)。

玩家视野、探索状态和命令权限说明见 [Player Visibility](docs/PLAYER_VISIBILITY.md)。

建筑类型资产的字段、校验和生成流程见 [Building Type Resource](docs/BUILDING_TYPE_RESOURCE.md)。
