# Demo 场景目录

所有可运行演示场景统一放在这里；模拟、AI、测试和通用 Godot 适配器仍保留在 `src/`，不把产品代码复制进演示目录。

- `2d/RtsDemo2D.tscn`：原有完整 2D 可玩对局、启动页和测试中心。
- `3d/RtsEncounter3D.tscn`：复用相同纯 C# 模拟与敌方 AI 的 3D 可玩遭遇战。
- 根目录 `Main.tscn`：仅为已有命令、文档和录像工具保留的兼容继承入口。

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe `
  --path . res://demo/3d/RtsEncounter3D.tscn

\.\tools\record_demo.ps1 -Demo 3d-encounter
```

3D 操作：左键点选/框选、右键智能命令、滚轮缩放、屏幕边缘滚屏、中键拖拽平移、`Alt+中键` 旋转；底部命令卡可建造/生产/研究，右下角小地图可移动镜头或下达命令，`D` 显示选中单位的 MoveGoal 与 SlotTarget。
