# 默认可玩对局

更新日期：2026-07-14

默认入口不再生成两堆无经济语义的测试单位，而是运行完整的 Player vs AI 对局。`PlayableSkirmishScenario` 是纯 C# 场景组合根，只声明地图、开局、资源簇和敌方 AI 配置；采集、施工、生产、科技、视野、战斗、胜负与寻路全部由正式运行时执行。

## 对局配置

- 世界范围 3120×1720，约为旧默认地图面积的 6.9 倍。
- 中央地形形成上下两条 180px 主通道，并保留两侧回转空间和外围扩张路线。
- 玩家 P1 位于左侧，敌方 P2 位于右侧；正常启动只显示玩家视野。
- 双方各有 1 座 Command Center、12 个 SCV、1800 Minerals、600 Vespene 和 30 初始人口上限。
- 全图 78 个资源节点：双方主矿、四处外围扩张和两组中央富矿；矿物储量为 6500～10000，气矿储量为 6000。
- 初始 SCV 自动分配主矿，但除此之外没有预生成军队、生产设施、科技或扩张。
- 敌方使用正式 `ModularSkirmishAiPolicy`：目标 24 农民、10 人军队出击，会建人口、兵营、气矿、研究设施与新基地，并通过正式玩家命令执行生产、研究、侦察、防守和进攻。

## 操作

- 左键点选/框选，右键 SmartCommand，`A + 右键` AttackMove，`Shift + 右键` 排队。
- 方向键或屏幕边缘移动镜头，滚轮缩放；小地图支持定位和下达命令。
- 选中农民后使用命令卡建造；快捷键 `B` 直接进入 Supply Depot 的正式放置模式。
- 选中建筑后通过命令卡生产、研究和设置 Rally。
- `Ctrl+#` 编组，`Shift+#` 追加，数字键召回；`R` 重新开始整局。

旧的 `B/X` 直接增删动态 Footprint 调试路径已经移除，避免可玩局绕过资源扣费、施工、所有权和比赛规则。

## 验收

纯 C# `PlayableSkirmishScenarioSelfTest` 在同一场景定义上运行 1800 Tick：地图 3120×1720、78 个资源点、双方 12 个初始农民；玩家矿量由 1800 增至 2166，敌方发展到 6 座设施，比赛保持 Running。

Godot 集成烟测另外验证运行时 NavMesh、摄像机、迷雾和表现组合：NavMesh 生成 18 个多边形并成功同步。为支持中央有障碍的地图，`GodotPathProvider` 不再假设世界中心必定可行走，而是确定性选择距离中心最近的合法同步探针。

启动阶段使用 `ValidatingFallbackPathProvider`：Grid fallback 已就绪时即可处理开局施工和移动，不等待 Godot NavigationServer 完成首个物理帧同步；NavMesh 就绪后自动成为首选查询源。组合 Provider 的 Ready 状态因此表示“至少存在可工作的正式路径源”，不会再因不同机器的 NavMesh 初始化时序导致起始 Command Center 无法完工。

30 秒实际玩家视角录像位于 `test_videos/20260713_012005/playable-skirmish-demo.webm`，1280×720、903 帧、AV1/WebM、CRF 32、preset 8。
