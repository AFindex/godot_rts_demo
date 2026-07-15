# Warcraft III 地形资源适配专项

## 1. 专项目标

本专项不另造一套与现有系统竞争的“War3 地形模拟”。现有 SC2 风格地形已经把高度层、坡道、通行类型、建造、视野、动态占用和高层路线贯通到同一份 `TerrainMapSnapshot`；War3 适配作为表现与导入层接在它外面：

```text
War3 原始数据 / 已导出资源
├─ Terrain.slk / CliffTypes.slk / Water.slk
├─ war3map.w3e / war3map.wpm / war3map.shd
├─ TerrainArt/*.png 图集
├─ ReplaceableTextures/Cliff/*.png
├─ ReplaceableTextures/Water/*.png
└─ Doodads/Terrain/*.glb
                  │
                  ▼
War3 导入与表现适配层
├─ tileset 目录与语义 Surface 映射
├─ 地表图集采样、边缘混合、随机变体
├─ 崖壁材质与可选经典 cliff mesh
├─ 水面动画、岸线和深浅水表现
└─ doodad 表现与显式 footprint
                  │
                  ▼
现有权威 TerrainMapSnapshot
├─ 高度 / 坡道
├─ Ground / Shallow / Deep / Air
├─ Buildable / Vision / Creep
├─ Clearance / Topology / Choke
└─ 回放、热恢复和稳定哈希
```

美术主题更换不得改变权威地形哈希、单位路线、建造结果或高地视野。

## 2. 已确认的现状

### 2.1 项目已有能力

- `TerrainMapSnapshot` 已保存 0～15 高度层、语义表面、地面/浅水/深水/空中阻挡、可建造、坡道、视野和菌毯限制。
- `Rts3DTerrainPresenter` 已按 Surface 批量生成地表，并按相邻高度生成崖壁；没有每格一个 Godot 节点。
- Clearance Bake、Small/Medium/Large 拓扑、动态建筑封路、坡道路线、高低地视野、编辑器资源和 12 张预制已经接通。
- 原表现器此前只给 Surface 使用纯色；崖壁 UV 只有平面坐标，没有垂直高度，因此不能正确使用真实崖壁贴图。

### 2.2 已导出的 War3 地形资源

当前项目资源目录中已经存在：

- 20 个 `TerrainArt` 目录、161 张地表 PNG。
- 26 张经典悬崖 PNG。
- 315 帧/张水面 PNG。
- 577 个 Terrain Doodad GLB，其中 310 个名称属于 cliff 相关模型。
- Lordaeron Summer 已包含 Dirt、DirtGrass、DirtRough、Grass、GrassDark、Rock 六种地表图集。

本专项直接消费这些结构化导出结果，不在运行时读取 MPQ/BLP/MDX。

### 2.3 War3 表现规则调研结论

- `Terrain.slk` 分离 tile ID、贴图目录、贴图文件以及 buildable/walkable/flyable 等数据，说明贴图身份与玩法规则需要显式映射，不能由文件名猜路径。
- `.w3e` 为 `(地图宽+1)×(地图高+1)` 个 **tilepoint** 保存地表类型，而不是为每个面格保存最终贴图。一个渲染格读取四个角点，因此这是独立于玩法面格的视觉控制网格，也就是本专项所称的“双网格”。
- 导出的 512×256 扩展地表图集实际分成 **8×4 个 64×64 子块**，不是 4×2 个 128 子块。左半边 0～15 是四角组合的 Alpha 过渡块，右半边 16～31 是完整地表变化。
- 每种非底层材质根据四角是否属于自己生成 4bit mask：右下 bit0、左下 bit1、右上 bit2、左上 bit3；mask 直接选择 0～15 子块。最低地表序号作为不透明底层，其余材质按序 Alpha over。
- HiveWE 的地表 shader 为每格保存最多四个“纹理 ID + 图集层”，按 Alpha 依次合成。其 `GroundTexture` 加载器也明确把图集左、右两个 4×4 区域分别上传成数组层 0～15 和 16～31。
- 完整块变化不是 0～15 等概率随机。当前 HiveWE 的扩展地表权重总和为 570：`0/16/0` 各 85，随后是 `1～3` 的 `10/4/1`，以及 `4～7`、`8～11`、`12～15` 三组 `85/10/4/1`。变化 16 特殊映射到左半区过渡块 15；本项目的自动迁移按坐标确定性地复现这套概率分布，W3E 导入则保留地图原有 variation。
- War3 悬崖并非普通垂直四边形。原始流程用 cliff/ramp MDX 选择不同拓扑块，再用 `Cliff0/1` 图集；第一阶段先让现有批量崖壁拥有正确的水平/垂直 UV，后续再增加经典 mesh 选择器。
- 经典悬崖文件名使用四角相对层高：取四角最低层为 `A`，按 **TL/TR/BR/BL** 顺序把高度差 0/1/2 编为 A/B/C，再拼成 `CliffsABCDn`；变化号超过该签名的最大模型时需要夹到最高可用变化。
- 本地导出的自然悬崖正好是 64 种非平坦签名、94 个 GLB；城市悬崖为 64 种、111 个 GLB。Lordaeron Summer 的 `CLdi` 与 `CLgr` 在 `CliffTypes.slk` 中都选择自然 `Cliffs` 模型目录，只通过 replaceable `Cliff0/1` 更换表面。
- 导出 GLB 保留原始 128×128 Warcraft 单元并在 `WarcraftRoot` 使用 0.01 缩放。运行时需要额外把水平 1.28 对齐项目格宽 `40×0.025=1.0`，把垂直 1.28 对齐 cliff level `48×0.025=1.2`，并执行 Warcraft XY→Godot XZ 的轴重映射；仅做 Y 轴旋转会把 TL 与 BR 错置。
- 原版 `ground_exists` 在左下 tilepoint 被标为 cliff/ramp 时会隐藏整块 ground quad，由 `CliffsABCDn` 模型同时接管不规则顶部、立面和脚边；模型不能叠在完整矩形地表之上，否则高侧地表必然越过 cliff 顶缘。本项目的玩法格与经典 tilepoint 相差半格，因此使用四分之一格覆盖图做等价裁切。
- 原版 `real_tile_texture` 的优先级为“邻近 cliff/ramp > blight > 普通 ground”，并把 cliff 类型声明的 `groundTile` 用在周边 tilepoint。Lordaeron Summer 的 Cliff0 对应 Dirt、Cliff1 对应 Grass；这也是 cliff 脚底裙边能与双网格地表衔接、而不是直接硬切的原因。
- 自然 `CliffTrans` 已导出 32 个、城市 `CityCliffTrans` 已导出 16 个。它们使用 A/B/C 与 H/L/X 混合签名并跨两个 tile；当前玩法坡道是面格方向字段，不等同于 W3E tilepoint ramp flag，因此必须先建立显式坡道视觉点网格，当前阶段继续使用无裂缝程序化侧壁回退。
- 水面是独立序列资源，不应混入 Ground Surface 图集。浅水/深水仍由玩法字段决定，水面材质只消费它们。
- `war3map.w3e` 是视觉地形导入来源；`war3map.wpm` 是路径数据来源。导入时两者进入不同字段，禁止用贴图或 cliff 外观反推通行。

War3 格式与算法参考：[HiveWE war3map.w3e Terrain](https://github-wiki-see.page/m/stijnherfst/HiveWE/wiki/war3map.w3e-Terrain)。本地核对源码为 HiveWE `src/base/terrain.ixx`、`src/resources/ground_texture.ixx` 与 `data/shaders/terrain.frag`；随机变化权重及 cliff/ramp 文件名生成固定核对于 [HiveWE terrain.ixx `2f001ee`](https://github.com/stijnherfst/HiveWE/blob/2f001ee16421dc9a9d6ff744dd032900e934f082/src/base/terrain.ixx)。

### 2.4 SC2 地形美术调研与本项目采用边界

可由暴雪公开资料确认的原则：

- 地图先完成玩法布局并锁定，再进入地形与环境美术阶段；美术不得妨碍单位辨识和玩法信息。这与本项目“`TerrainMapSnapshot` 权威、材质适配只做表现”的边界一致。
- 高低层需要由明度不同的 cliff 纹理主动强化，而不是仅依赖几何阴影；doodad 下方也应补画贴图，使其与地面相接。
- 最终还要按性能指标和热点检查清理或简化美术内容，因此混合应以固定材质批次实现，不能退化成每格节点或每格材质。
- SC2 的纹理控制同样不等于玩法格：官方补丁说明纹理集按独立的 8×8 terrain chunk 分配，而纹理笔刷在 chunk 使用的活动纹理集内绘制。因此两者都体现“几何/玩法数据与纹理控制数据分层”，但 SC2 不是照搬 War3 的四角 4bit 图集。

参考：

- Blizzard：[How Does Blizzard Make Maps?](https://news.blizzard.com/en-us/article/20984531/how-does-blizzard-make-maps)
- Blizzard：[Mastering Mapmaking: Part One](https://news.blizzard.com/en-us/article/20097658/creacion-de-mapas-buenas-practicas-de-diseno-y-rendimiento)
- Blizzard：[StarCraft II 4.13.0 PTR Patch Notes](https://news.blizzard.com/en-gb/article/23471116/starcraft-ii-4-13-0-ptr-patch-notes)
- Blizzard：[Heart of the Swarm 3.0 Patch Notes](https://news.blizzard.com/en-us/article/19913940/heart-of-the-swarm-3-0-patch-notes)

暴雪没有公开 SC2 当前地形 splat 权重的准确编码、压缩格式或内部 shader。因此本项目只能准确称为“SC2 风格连续权重混合”，不能宣称字节级复刻 SC2。当前采用以下可维护实现：

- 权威格子仍只有单一语义 `SurfaceId`。
- 网格构建时用同高程邻接格生成四通道连续顶点权重，不写入地形序列化和稳定哈希。
- shader 对权重插值并加入低强度确定性噪声，打散笔直等值线。
- 连续权重对照模式还使用同一坐标场生成低频 UV domain warp 和轻微宏观明度扰动，四层共用同一变形场，因此不会在材质交界产生四套互相漂移的纹理。该效果是“SC2 风格扩展”，不是对未公开 SC2 shader 的精确复刻。
- 双网格模式不施加任意旋转或随机扭曲，以免破坏 War3 作者绘制的 4bit 边缘；它通过原始完整块变化和过渡块自身的不规则边缘实现随机扰动。
- 跨越 cliff level 或不能在同一高度相接的坡面停止取样，避免草地/泥地从高台涂到崖壁和低地。
- 水面保持独立材质批次，不参与地表四通道混合。

## 3. 分阶段任务

### W0：基线审计（已完成）

- 冻结现有权威地形与表现器边界。
- 统计已导出地表、崖壁、水面和 Terrain Doodad。
- 用 `Terrain.slk`、`CliffTypes.slk` 与 HiveWE shader 核对 War3 图集、Alpha 层和 cliff 处理方式。

验收：形成本文档；任何实现不得改动模拟格式。

### W1：第一版可运行材质适配（已完成）

- 为 `Rts3DTerrainPresenter` 增加可插拔 `IRts3DTerrainMaterialProvider`。
- 地表 UV 改为“一套权威地形格对应一个 UV 单元”。
- 崖壁生成真实水平/垂直 UV，修复高度方向被压扁的问题。
- 增加 Lordaeron Summer 映射、扩展图集完整地表变体、Cliff0 岩壁采样和 Water00 动态水面。
- 增加独立 `War3TerrainShowcase3D` 验收场景，并加入地形专项入口。

验收：同一个 `demo_playable_terrain.tres` 在不改变哈希的情况下显示 War3 地表、悬崖和水面；原地形场景继续使用默认材质。

### W2：连续权重混合与 War3 多层地表

- **W2a 已完成：**增加可选 `IRts3DTerrainBlendMaterialProvider`；四种地表在一个材质批次中使用 RGBA 顶点权重连续混合。
- **W2a 已完成：**权重由同高程邻域高斯采样生成，shader 加入确定性边缘扰动；水面和悬崖不参与地表混合。
- **W2a 已完成：**测试场景接入项目现有 RTS 镜头，支持 WASD/方向键/边缘移动、中键拖动、`Alt+中键` 旋转、滚轮缩放和 Home/R/按钮归位。
- **W2b 运行时核心已完成：**增加纯表现 `TerrainVisualLayerMap`，尺寸为 `(Columns+1)×(Rows+1)`；每点保存视觉材质和变化号，每格从四角生成最多四层 mask，不进入权威 `TerrainCell`。
- **W2b 运行时核心已完成：**按 8×4、64px 子块解释扩展图集；支持 0～15 四角 Alpha 过渡块和 16～31 完整变化，按 War3 地表序号执行 Alpha over。
- **W2b 运行时核心已完成：**旧语义 Surface 可确定性迁移为 tilepoint 网格；悬崖交界优先高侧角点材质，来源玩法哈希保持 `9704B5847C5B2221`。
- **W2b 运行时核心已完成：**展示场景默认双网格，并可一键切换 SC2 风格连续权重做同图对照。
- **W2c 资源核心已完成：**`TerrainVisualLayerMap` 已有版本化规范二进制、来源玩法哈希、独立视觉哈希、尺寸/范围/尾随数据校验和 Godot `RtsTerrainVisualLayerResource` 包装；展示资源保存在 `data/demo_war3_terrain_visual_layers.tres`，视觉哈希为 `C7D80B640F3ED024`。
- **W2c 资源核心已完成：**展示场景优先加载作者视觉资源；只有资源缺失时才从旧 Surface 确定性迁移，资源存在但损坏或绑定错地图时明确报错，不静默覆盖。
- **W2c 随机扰动已完成：**War3 双网格采用 0～16 带权 variation；SC2 风格连续混合增加低频权重扰动、UV domain warp 与宏观明度差，全部按坐标确定且不进入玩法哈希。
- **W2c 编辑器待办：**增加“玩法 Surface / War3 Tilepoints / SC2 Weight Canvas”三个独立覆盖层，并继续扩展 blight/cliff tilepoint override 的作者字段。

验收：W2a 要求同高度草地、泥土、岩石形成连续边缘；W2b 要求四角组合能选择正确的 4bit 过渡块、完整块无透明洞、跨悬崖不串色；W2c 要求相同玩法地图换视觉资源后路线、建造和回放状态完全一致。

### W3：经典 cliff/ramp mesh 适配

- **第一子步已完成：**现有批量崖壁按高侧地表分批，草地高台使用 Cliff1，泥土/岩石等使用 Cliff0；水平与垂直 UV 分别按格宽和 cliff level 高度重复。
- **第一子步已完成：**地表混合在不同高程处断开，崖壁成为独立材质边界。
- **经典 cliff 核心已完成：**`TerrainCliffMeshLayout` 以玩法格中心为视觉 tilepoint，按 TL/TR/BR/BL 生成 A/B/C 签名，选择上层 Surface 和确定性变化；布局拥有独立视觉哈希与候选/选择/回退诊断。
- **经典 cliff 核心已完成：**`War3ClassicCliffMeshCatalog` 从导出包读取 64/94 个自然 cliff 资产并直接加载 GLB；不把大资源复制进 Godot import cache。
- **经典 cliff 核心已完成：**按“模型文件 + 上层 Surface”建立 MultiMesh 批次。当前展示图 142 个候选全部解析，合并为 17 个批次，不创建 142 个场景节点。
- **经典 cliff 核心已完成：**修正 128 水平单位、128 垂直单位、导出根变换和 Warcraft XY→Godot XZ 轴顺序；经典模型使用自身完整 atlas UV，程序化回退继续采样可重复岩壁区。
- **经典 cliff 核心已完成：**一个 cliff 边缘由相邻两个经典 tile 各覆盖一半；未解析、地图边界或部分覆盖时只为未覆盖半边生成程序化侧壁，避免洞、重复面和整边静默消失。
- **经典 cliff 接缝已完成：**`TerrainClassicCliffSeamMap` 把中心采样的经典 tile 映射为相邻四个玩法格的四个 quadrant；只裁掉模型实际接管的区域，消除高台矩形 mesh 越过不规则 cliff 顶缘的问题，并保持未解析/程序化回退格完整。
- **经典 cliff 接缝已完成：**双网格在运行时复制作者视觉层，并按 Cliff0→Dirt、Cliff1→Grass 应用 cliff-ground 优先过渡；作者资源、来源玩法哈希和 fallback 模式都不被修改。当前展示图裁切 568 个 quadrant、覆盖 414 个运行时过渡点。
- **经典 cliff 核心已完成：**展示场景可实时切换“经典模型 / 程序化回退”，权威地形与视觉层资源均不重建。
- **复杂拓扑压力图已完成：**展示场景保留原基准图，并增加交错山脊、嵌套盆地群岛、蛇形峡谷、64 签名矩阵和四层贴图编织场；每张图都是独立的地形快照与视觉层，只扩大验收覆盖，不修改 cliff/ground 渲染算法。
- **复杂拓扑压力图已完成：**支持 PageUp/PageDown 或按钮循环六张图、Home/R 重置适配后的镜头、F1 隐藏界面。五张压力图分别包含 429/522/703/440/761 个 cliff 候选，全部无缺失资产回退；签名矩阵覆盖 64 种非平坦 `CliffsABCD` 组合。
- **坡道视觉点网格待办：**从方向型 `TerrainCell` 生成 W3E 风格 corner ramp flags，再按 HiveWE 两格 A/B/C/H/L/X 规则选择 `CliffTrans`；在该映射完成前，坡道和大于两层的异常邻接保持程序化回退并输出计数。

验收：直崖、内外角、孤立高地、盆地和四方向坡道无裂缝；关闭经典 mesh 后玩法结果不变。

### W4：`.w3e` / `.wpm` 地图导入

- 实现版本化、带错误坐标的 W3E 读取器：地图边界、tileset、自定义 tile 列表、顶点高度、水位、地表层和 cliff 信息。
- W3E 转为现有编辑文档和独立 Visual Layer Map；不直接生成运行时节点。
- WPM 位映射到 Ground/Build/Air 等显式字段，并保留无法一一对应的原始标志供审计。
- 量化 War3 顶点连续高度到“基础高度 + SC2 风格 cliff level”；无法无损表达时输出诊断，不静默截断。
- 导入结果通过现有 TerrainAuthoring 验证后才能导出 `.tres`。

验收：至少三张不同 tileset 的原始 War3 地图完成 W3E/WPM→编辑资产→运行时快照→重新加载闭环，并保存稳定导入报告。

### W5：水面、阴影、Blight 与 Terrain Doodad

- 从 Water 数据选择 tileset、帧序列、颜色和深浅水参数；加入帧动画或纹理数组。
- 加入岸线/浅水过渡；水体仍只表现，移动类型读取现有 Pathing。
- `war3map.shd` 作为烘焙阴影表现层，不写入 BlocksVision。
- Blight 作为可选视觉覆盖层；`BlocksCreep` 继续保持独立玩法语义。
- Terrain Doodad 分为纯表现、移动 footprint、建造 footprint、视野 footprint 四种能力；默认纯表现。

验收：水面动画不改变深浅水通过矩阵；纯表现 doodad 不产生碰撞；显式 footprint 与回放/热恢复一致。

### W6：正式接入 `war3_rts`

- 为人族对战建立独立 War3 地形编辑资产，不直接复用展示图。
- 把出生点、金矿、树林和建筑区域迁移到地形锚点。
- War3 单位、建筑、资源、选择圈、弹道和特效继续查询权威地面高度。
- 小地图加入 War3 地表颜色、高低层轮廓和水域。
- 保留纯色诊断主题开关，用于快速区分玩法数据错误和美术错误。

验收：完整人族对战在有高低地、坡道、水面和 War3 材质的地图运行；采集、战斗、建造、集结点、AI 和回放全部通过。

## 4. 自动化与视觉门禁

- `war3-terrain-smoke`：所有 shader/纹理可加载、材质映射完整、权威地形哈希不变。
- `war3-terrain-capture`：固定相机截图，必须同时看到两种以上地表、至少一段崖壁和水面。
- `war3-terrain-continuous-capture`：使用同一地图和镜头输出连续权重对照图。
- `war3-terrain-cliff-capture`：使用近景固定镜头验收经典 cliff 接缝、角块、纹理、朝向和尺度。
- `war3-terrain-cliff-fallback-capture`：同一镜头关闭经典模型，输出程序化崖壁对照图。
- `war3-terrain-stress-capture --war3-terrain-preset=<id>`：按固定近景输出指定复杂拓扑图；可用 ID 为 `interlocked-ridges`、`nested-archipelago`、`serpentine-canyons`、`signature-matrix`、`material-weave`。
- `--generate-war3-terrain-visual-resource`：从旧语义 Surface 重新生成确定性的版本化视觉层 `.tres`；该命令始终重建，不复用已有输出。
- `terrain-visual-layer-self-test`：验证四角 bit 顺序、单材质完整 mask、0～16 带权变化、规范二进制/Godot 资源往返、来源地图绑定、声明哈希和非法 layer 拒绝逻辑。
- `terrain-cliff-layout-self-test`：验证 TL/TR/BR/BL 签名、变化确定性、上层 Surface、中心 tile 到四个玩法 quadrant 的覆盖映射、cliff-ground 运行时副本不修改作者视觉层、大于两层/坡道/缺失资产回退，以及导出目录 64 签名/94 模型完整性。
- `war3-terrain-stress-self-test`：验证五张压力图 ID/玩法哈希/视觉哈希互异、视觉层绑定正确、四种材质齐全、每张至少 100 个 cliff 候选且无回退，并验证签名矩阵命中全部 64 种模型签名。
- 连续混合视觉门禁：同高度地表不得出现整格矩形硬切；跨悬崖不得出现高侧地表向崖壁或低侧串色。
- 双网格视觉门禁：完整块必须从右半 4×4 区域采样且完全不透明；过渡块必须按四角 mask 选择左半 4×4 区域，V 方向与 W3E tilepoint 行方向一致。
- 镜头输入门禁：键盘平移、中键拖动、滚轮缩放与归位至少通过场景 smoke 和一次人工交互验收。
- 既有 terrain self-test 全部继续通过。
- Draw Call 以 Surface/材质批次增长，不随格子数线性增长。
- W2 以后增加视觉层稳定哈希；模拟哈希明确不含视觉资产路径。
- W3/W4 对所有无法映射的 cliff、tile、WPM 位输出计数和坐标，不允许静默回退。

## 5. 当前交付边界

目前完成 W0、W1、W2a、W2b、W2c 资源核心、W3 程序化回退和经典 cliff 核心：真实 War3 地表/经典模型悬崖/水面已跑在现有权威地形上；既可使用带低频随机扰动的 SC2 风格连续权重，也可使用 War3 tilepoint 双网格、4bit 过渡块、0～16 带权变化和多层合成；经典悬崖按 64 种四角签名自动选型并以 MultiMesh 批量渲染，测试场景可独立切换纹理与悬崖算法。

尚未宣称完成的内容包括：视觉层编辑器覆盖层、blight/cliff tilepoint override、`CliffTrans` 两格坡道视觉点映射以及 W3E/WPM 导入；它们继续属于 W2c 编辑器、W3 坡道子阶段和 W4。
