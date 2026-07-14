# Warcraft III 资源实验室

`War3AssetLab.tscn` 是抽取资源在 Godot 中的运行时验收场景。它直接读取
`assets/warcraft3/classic/` 中的 GLB、PNG 与 `.war3.json`，提供：

- 中文名、分类、种族与原始路径检索，单位条目显示原始 CommandButton 图标；
- 独立的横向种族筛选行；
- 静态模型、骨骼动画、透明贴图与材质预览；
- 按 Warcraft 序列重新计算 Geoset 可见性，避免 Stand 同时显示尸体/Decay 网格；
- 并列动画按钮、播放速度和可拖动时间轴；
- ParticleEmitter2 和 RibbonEmitter 的 Godot 运行时重建；
- ParticleEmitter2/Ribbon 按 Skeleton3D 骨骼 ObjectId 对齐，保留原始发射面与方向；
- 单位关联肖像、原版种族肖像框、待机/说话切换和 MDX 原始肖像镜头；
- 左键环绕、中键平移、滚轮缩放、镜头归位，以及模型原点 XYZ 坐标轴；
- 网格和特效开关。

运行：

```powershell
godot --path . res://demo/war3/War3AssetLab.tscn
```

自动冒烟检查：

```powershell
godot --headless --path . res://demo/war3/War3AssetLab.tscn -- --war3-assets-smoke
```

重新生成肖像镜头目录（默认读取 `D:\Godot\war3_assets`）：

```powershell
node tools/build_war3_portrait_camera_catalog.mjs
```

Web 资源目录服务运行时，可重新生成中文名、种族与单位图标映射：

```powershell
node tools/build_war3_display_catalog.mjs
```

资源目录带 `.gdignore`，这是刻意的：Godot 不会为约 2.7 GB 原始导出物再生成
一份导入缓存。场景使用 `GltfDocument`、`Image` 和 JSON 直接从文件系统加载。
因此导出 PCK 时需要把该目录作为外部资源目录随程序一起分发，或另行移除
`.gdignore` 并建立正式导入管线。

当前运行时覆盖最常见的 ParticleEmitter2 与 RibbonEmitter。少量旧式
`ParticleEmitter` 会发射另一份 MDL 模型，`EventObject` 还可能触发声音、脚印、
Splat 或模型生成；实验室会标出这些数据但暂不替它们生成实例。对应元数据、
嵌套模型和音频均已保留，后续可以继续接事件调度器。
