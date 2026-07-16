# war3_rts

地图制作、版本化资产格式、运行时 catalog/选图流程与扩展方式见
[`docs/WAR3_MAP_EDITOR.md`](../docs/WAR3_MAP_EDITOR.md)。进入本场景时会先枚举
`res://war3_rts/maps`；选图后，地形、导航、资源/PCG 与出生点均来自所选包。

正式的人族 Warcraft III RTS 模块。玩法复用项目的确定性模拟、寻路、经济、
建造、生产、战斗与 AI；人族数据、经典模型、动画、特效、HUD 和肖像由
`src/War3Rts` 独立组合。

运行时玩法数值读取
`assets/warcraft3/classic/data/{unit,ability,upgrade}_editor_data/manifest.json`，
并按需加载每个 object id 的 JSON。Human 的稠密类型 ID 保持稳定，JSON 路径和 War3 object id
不会进入模拟状态；缺失或损坏的记录会按对象回退到原有内置配置。字段映射、
尺度策略和扩展边界见
[`docs/WAR3_RUNTIME_DATA_INTEGRATION.md`](../docs/WAR3_RUNTIME_DATA_INTEGRATION.md)。

战场使用 6400×3840 的权威 `TerrainMapSnapshot`：双方主基地位于对称的
一级圆角高台，面向中央各有一条 War3 坡道；中央低地保留连续起伏、
双网格地表和四处中立金矿。导航、建造、单位贴地、鼠标落点与经典
cliff/CliffTrans 表现读取同一份地形。

`src/War3Rts/pcg` 提供独立、确定性的地编 PCG 基础设施：PCG32 随机流、
带密度和可变最小间距的撒点器、连续哈希噪声，以及战场树林配方。当前配方
生成 252 棵可采集/可阻挡的额外树木，并将基地、坡口、主战线、金矿采集圈
和连接通路作为排除区；左右镜像保证对战公平，林核高密、边缘渐疏。经济
节点、导航障碍和 3D 表现共同消费同一份 PCG 布局。

操作：

- 左键选择，拖动框选，Shift 追加选择。
- 右键移动、攻击或采集；中键拖动镜头，滚轮缩放。
- `M/A/S/H/B`：移动、攻击移动、停止、保持、建筑菜单。
- `F` 聚焦当前选择，`Home` 返回主基地，`Esc` 取消当前模式。

生产与研究队列使用原版 Human `BuildQueueBackdrop`：当前项目显示为上方大图标和
原版进度条，等待项目按顺序显示在下方槽位。训练使用单位图标，研究会按下一
科技等级切换原版升级图标；鼠标悬停显示状态与退款，点击任一图标可取消对应
订单。UI 只消费 `War3QueueItemSnapshot`，实际取消、退款、回放记录仍由生产和
科技系统负责。

自动验收：

- `--war3-rts-smoke`：验证采集、建造、战斗、AI、HUD、五项生产队列、研究队列、取消交互、高台和坡道通行。
- `--war3-rts-capture`：输出带 HUD 的玩家基地截图。
- `--war3-rts-terrain-capture`：隐藏 HUD，从低地正面输出玩家高台与坡口截图。
- `--war3-rts-pcg-capture`：隐藏 HUD，输出整张地图的 PCG 树林俯视验收图。

离线地图缓存：

- `tools/generate_war3_map_cache.ps1 -MapId lordaeron_crossroads -GodotExe <Godot console 路径>`
  会执行一次完整权威初始化，并在地图目录生成 `map.w3cache.json`。
- 缓存包含展开后的 Terrain/Object 地图、Small/Medium/Large Clearance Bake、
  初始模拟 Hot Snapshot，以及玩家/敌方 worker 和资源节点 ID。
- 运行时会优先读取缓存；源 manifest/地图文件、地图/地形/导航、玩法目录、
  Clearance、快照格式或恢复后 StateHash 任一不匹配时，会自动回退到完整初始化。
- 修改 `War3HumanScenario` 的初始布局或 AI 启动契约时，需要递增
  `War3OfflineMapCache.ScenarioBootstrapVersion` 并重新生成缓存。
