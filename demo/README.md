# Demo 场景目录

所有可运行演示场景统一放在这里；模拟、AI、测试和通用 Godot 适配器仍保留在 `src/`，不把产品代码复制进演示目录。

- `2d/RtsDemo2D.tscn`：原有完整 2D 可玩对局、启动页和测试中心。
- `3d/RtsEncounter3D.tscn`：复用相同纯 C# 模拟与敌方 AI 的 3D 可玩遭遇战。
- `3d/TerrainTraversal3D.tscn`：生产寻路栈驱动的悬崖绕行、坡道通行和浅水禁造专项演示。
- `3d/TerrainDynamicTopology3D.tscn`：Large 建筑封住近坡后走远坡、拆除后下一波恢复近坡的动态拓扑专项演示。
- `3d/TerrainVisionCombat3D.tscn`：高低地视野、空中观察、共享视野、烟雾遮挡和飞行中弹道专项演示。
- `terrain/TerrainAuthoringWorkspace.tscn`：可编辑 Godot Resource、正交笔刷、覆盖层、结构验证和不可变运行时导出的 T3 工作区/自动演示。
- 根目录 `Main.tscn`：仅为已有命令、文档和录像工具保留的兼容继承入口。

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe `
  --path . res://demo/3d/RtsEncounter3D.tscn

\.\tools\record_demo.ps1 -Demo 3d-encounter
```

3D 操作：左键点选/框选，Shift 单击增减，Ctrl 单击或双击选择镜头内同类型；右键会给单位下达智能命令并同时更新混选生产建筑的 Rally。`Y` 显式设置 Rally，`Tab` 切换选择子组，`Ctrl+0..9` 建组、`Shift+0..9` 添加、数字召回、双击数字聚焦；`F1` 选空闲农民，Backspace 循环基地。滚轮缩放、屏幕边缘滚屏、中键拖拽平移、`Alt+中键` 旋转。底部左侧小地图可移动镜头或下达命令，右侧固定 3×5 命令卡按选择上下文显示建造/生产/研究，`D` 显示选中单位的 MoveGoal 与 SlotTarget。
