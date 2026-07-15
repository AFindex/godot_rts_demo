# StarCraft II 地形系统对齐研究

本文只冻结能够由公开资料、SC2 实机表现或本项目代码验证的规则。无法确认的编辑器内部实现不冒充事实；后续若需要逐帧对齐，由专门的 SC2 客户端实验补证据。

## 1. 可靠事实

### 1.1 高地、悬崖和坡道属于玩法，不只是美术

暴雪的地图元素说明明确指出：坡道把通路压缩成容易防守的狭窄入口；低地单位没有高地视野时不能攻击高地单位。高低地因此同时影响路线、视野和交战，不允许只在 3D 中把模型抬高而让模拟仍按平地处理。

来源：[Blizzard Game Guide: Map Elements](https://news.blizzard.com/en-us/article/4546768/game-guide-map-elements)

SC2 4.13 编辑器更新把最大悬崖层数从 3 提升到 15。项目数据格式因此保留 `0..15`，但正式对战地图通常只需要少数清晰层级。

来源：[StarCraft II 4.13.0 PTR Patch Notes](https://news.blizzard.com/en-us/article/23471116/starcraft-ii-4-13-0-ptr-patch-notes)

### 1.2 路径种类与地表贴图是两套数据

同一份官方更新加入 Ground、Shallow Water、Deep Water，以及 Ground、Float、Amphibious、Flying 的通过组合：

- 普通地面单位：Ground + Shallow Water。
- 浮水单位：Shallow Water + Deep Water。
- 两栖单位：Ground + Shallow Water + Deep Water。
- 飞行单位：除 Air Pathing Blocker 外不受地面种类限制。

这说明泥土、金属、草地等视觉材质不能直接决定能否行走。项目必须允许“看起来不同但玩法相同”，也允许“贴图相同但额外刷了不可走/不可建区域”。

来源：[StarCraft II 4.13.0 PTR Patch Notes](https://news.blizzard.com/en-us/article/23471116/starcraft-ii-4-13-0-ptr-patch-notes)

### 1.3 可走、可建造、允许菌毯、遮挡视野必须分开

SC2 4.13 新增 `Ramp No Build`，并明确说明此前坡道不可建造是硬编码行为，更新后才允许由地形类型配置。这直接证明“能走”不等于“能造”。官方地图补丁还反复单独提到 Unbuildable Plate、Disallow Creep、Air Pathing Blocker、地形与 Pathing 不匹配等问题。

来源：[StarCraft II 4.13.0 PTR Patch Notes](https://news.blizzard.com/en-us/article/23471116/starcraft-ii-4-13-0-ptr-patch-notes)、[2020 Season 1 Versus Update](https://news.blizzard.com/en-us/article/23331593/2020-season-1-versus-update)

### 1.4 战争迷雾和特殊遮挡不是同一件事

官方说明区分：

- 未探索区域：全黑。
- 探索过但当前无视野：灰色，地形和已知建筑仍可见，敌军移动和新建筑不可见。
- 当前视野：单位和实时状态可见。
- Smoke / Undergrowth：内部与外部之间额外互相遮挡。

本项目已有 Hidden / Explored / Visible，但目前只是按半径刷格子，没有悬崖方向视野，也没有地形遮挡区域。它们属于后续地形视野阶段，不能在第一版里假装已经完成。

来源：[Blizzard Game Guide: Map Elements](https://news.blizzard.com/en-us/article/4546768/game-guide-map-elements)

### 1.5 地形美术必须服从辨识度和性能

暴雪公开地图制作流程指出，地图最后会进行性能检查，并依据热力图删除复杂物体、灯光或简化美术。正式 RTS 地形不应使用“每格一个 Godot 节点”的实现；地表、崖壁和装饰必须分块批处理，玩法数据也不能依赖装饰模型碰撞。

来源：[How Does Blizzard Make Maps?](https://news.blizzard.com/en-us/article/20984531/how-does-blizzard-make-maps)

## 2. Godot 4.7 的技术边界

Godot 的 `TileMapLayer` 适合 2D 编辑和自动拼接视觉地块，但官方文档指出其逐格导航存在实际限制，推荐设计完成后烘焙为更优化的 NavigationMesh；同一个 2D 导航图上也不能把多层导航面上下重叠。

来源：[Godot Using TileMaps](https://docs.godotengine.org/en/stable/tutorials/2d/using_tilemaps.html)、[TileMapLayer](https://docs.godotengine.org/en/4.5/classes/class_tilemaplayer.html)

`GridMap` 可以编辑 3D 网格并携带网格、碰撞和导航数据，但如果直接把每个格子的 Godot 导航和碰撞当作权威，会破坏本项目已有的纯 C# 确定性模拟、回放和 headless 测试。

来源：[Godot GridMap](https://docs.godotengine.org/en/4.0/classes/class_gridmap.html)

因此本项目采用：

```text
纯 C# 地形快照（权威）
├─ 寻路读取：能否通过、坡道跨层、移动类型
├─ 建造读取：是否平整、是否允许建造
├─ 视野读取：高度层、遮挡区域（后续）
├─ 战斗读取：攻击双方高度和视野（后续）
└─ Godot 适配：批量网格、材质、编辑器工具、鼠标拾取
```

Godot 只显示和编辑同一份数据，不通过物理碰撞反向决定模拟结果。

## 3. 本项目必须对齐的合同

### 地形格

每格分别保存：

- 悬崖层级 `0..15`。
- 表面材质 ID。
- Ground / Shallow Water / Deep Water / Air Blocked。
- Buildable。
- Ramp 和上升方向。
- Blocks Vision。
- Blocks Creep。

这些字段不能互相推导。例如浅水默认可供地面单位通行但不可建造；金属地面可以和泥土地面拥有完全相同的路径规则。

### 悬崖和坡道

- 不经过坡道时，地面单位不能跨越不同悬崖层。
- 坡道连续连接相邻两层，不允许一次跨多层。
- 单位圆形占地必须完整放在可通过区域内，不能让中心点可走就把半个身体挂在崖边。
- 建筑占地必须全部位于同一平整层；默认不能建在坡道上。
- 飞行单位不受悬崖影响，但受 Air Blocker 影响。

### 视野与战斗

- 探索状态继续使用现有三态模型。
- 低地对高地不能只凭距离获得视野；高地对低地规则单独测试。
- Smoke / Undergrowth 使用区域进出关系，不伪装成普通圆形视野缩短。
- 是否允许攻击仍由“目标实际可见”决定，不能仅比较高度后直接禁止攻击。

### 表现

- 地表按材质和区块合并为少量 Mesh Surface。
- 崖壁由层级边界生成，不使用每格独立方块。
- 单位、建筑、资源、弹道、选择圈、命令标记都查询地面高度。
- 鼠标使用地形表面拾取，不能继续固定与 `Y=0` 平面相交。
- 装饰物默认纯表现；只有显式 Footprint 才影响路径、建造或视野。

## 4. 不能冒充已对齐的内容

当前公开资料不足以冻结以下 SC2 精确内部参数：

- 低地单位在坡道边缘获得高地视野的精确距离和格子规则。
- 炮弹跨悬崖时的逐武器命中/丢失目标例外。
- 所有 Doodad 的遮挡、可选择和碰撞组合。
- SC2 地形贴图自动混合的内部 shader 和权重压缩格式。
- 每种单位在水面、桥梁和特殊路径层上的全部例外。

这些内容需要真实客户端场景实验后再进入强制实现，不凭印象补参数。
