# 3D 遭遇战 Demo

## 技术边界

这不是第二套 RTS。权威状态仍是 `RtsSimulation` 的二维地面模拟；`SimPlane3DTransform` 仅把 `(sim X, sim Y)` 映射到 Godot `(world X, 0, world Z)`，比例为 `0.025`。寻路、Steering、单位推挤、终点收敛、经济、施工、生产、科技、战斗和 AI 全部复用现有纯 C# 系统。

3D 表现层不创建 `CharacterBody3D`，也不把 Jolt 碰撞结果写回模拟。`Rts3DWorldPresenter` 只读取单位 SoA、建筑/资源快照和投射物快照；输入层把射线落点转换回模拟坐标并调用正式 API。因此换模型、材质、动画、地形或相机不会改变确定性玩法。

## 当前内容与操作

- 玩家与敌方 AI 各 12 个初始农民、双资源、多个扩张点和不同尺寸建筑；敌方会完整发展并持续进攻。
- 农民、普通兵、重甲兵分别用方块、球体和六边柱；阵营用蓝/红主色，角色识别不只依赖颜色。
- 五类建筑全部使用最朴素的 `BoxMesh`，严格按矩形 footprint 的 X/Z 尺寸表现；只用颜色和高度轻微区分功能，便于直接观察单位绕行、贴边、交互距离和卡位问题。矿物、气矿、障碍、施工高度、选择环和弹道仍由 presenter 同步。
- 左键点选/框选；Shift 单击增减选择，Ctrl 单击或双击选择当前镜头内同类型，Tab/Shift+Tab 切换活动子组。右键智能命令，`M` 移动、`A` 攻击移动、`S` 停止、`H` Hold、`F` 聚焦。
- 方向键移动，`,` / `.` 旋转，滚轮缩放。
- 鼠标靠近屏幕边缘会滚屏；中键拖拽平移，`Alt+中键` 拖拽旋转/俯仰，触控板 Pan/Magnify 同样可用。镜头目标、距离和角度都使用帧率无关平滑。
- 快捷键按当前 3×5 命令卡上下文显示：工人卡包含五类建筑，生产/科技建筑卡显示可用训练或研究，灰色按钮通过 tooltip 说明资源、人口或前置原因。
- Town Hall 与 Production 建筑支持 `Y` 显式 Rally；也可以选中建筑后直接右键地面、资源或友军单位。混选单位和生产建筑时，同一次右键分别下发 SmartCommand 和 Rally。多建筑统一更新，资源 Rally 会继续使用现有自动采矿流程。选中建筑后，Presenter 从真实矩形边缘绘制到目标的 Rally 线、目标环和箭头。
- `Ctrl+0..9` 覆盖控制组，`Shift+0..9` 添加，数字召回，双击数字聚焦；`F1` 循环空闲工人，Backspace 循环己方基地。这些都只改变本地操作选择和相机。
- HUD 采用右上资源条与底部三段布局：左侧小地图/控制组，中间选择详情/生命/队列/Rally，右侧固定 3×5 命令卡。`Rts3DHud` 只发意图，`Rts3DInterfaceAdapter` 只生成不可变快照，组合根才调用正式模拟 API；小地图左键移动镜头、右键下达上下文命令。
- `D` 切换寻路诊断：黄色线/十字是 `MoveGoal`，青色线/十字是最终 `SlotTarget`，只显示选中单位；建造模式用绿色/红色半透明长方盒显示真实 footprint 和放置结果。

## 性能与验证

当前容量 768，第一版采用“稳定实体 ID -> 缓存 MeshInstance3D”，Mesh 和 Material 按类型/阵营共享，每帧只更新 Transform 和存活集合。移动诊断线共用单个缓存 `ImmediateMesh`，建造预览也只复用一个节点，不按帧创建 Godot 对象。单位规模稳定后可把同形状同阵营批次替换为 MultiMesh，模拟接口不变。

当前使用纯 C# `GridPathProvider`，目的是证明 3D 表现不要求修改寻路核心。若以后地图需要真实高度层、桥下/桥上或体积导航，应新增分层地面拓扑或 3D 导航适配器，而不是让 Godot 物理成为第二权威。

`--demo-3d-smoke` 推进 1,800 Tick，验证玩家采集增长、敌方设施发展和 presenter 实体数。`tools/record_demo.ps1` 使用 900 Tick 自动巡视，并自动选择 Town Hall、设置矿物 Rally、建立控制组，门禁要求世界中实际存在 Rally Marker；输出遵守 AV1/WebM、CRF 32、preset 8 规范。专项证据和非目标见 [SC2 3D Interface Alignment](SC2_3D_INTERFACE_ALIGNMENT.md)。
