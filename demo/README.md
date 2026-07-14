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
