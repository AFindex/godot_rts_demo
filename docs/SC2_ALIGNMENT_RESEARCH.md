# StarCraft II 操作与玩法细节对齐研究

更新日期：2026-07-13

## 1. 文档目的

这不是“照抄 SC2 数值”的愿望清单，而是本项目后续实现和验收的行为基线。重点是玩家能直接感知、会影响寻路/经济/建造正确性、并且容易因为底层重构而退化的细节。

每条结论使用以下证据等级：

- **A：官方明确**：Blizzard Game Guide 或 Blizzard 官方 SC2 API 直接描述。
- **B：稳定社区共识**：Liquipedia/长期竞技资料明确描述，并与官方语义一致。
- **C：实机假设**：论坛、玩家实验或视频能支持，但没有足够权威资料描述完整边界。必须先做当前版本 SC2 实机矩阵，再冻结为本项目合同。

对齐状态：

- **已对齐**：当前正式模拟和黑盒测试已经覆盖核心语义。
- **部分对齐**：主路径存在，但边界或时序仍不同。
- **未对齐**：当前行为与研究目标明显不同。
- **待实机**：不能只凭二手资料决定实现。

## 2. 结论摘要

当前 Demo 在“大量农民稳定往返”和“返矿到基地最近边缘”上已经走对了方向，但建造预放置仍有一个高优先级差距：

1. 采矿/返矿途中应当关闭**单位—单位**碰撞，而且是双向忽略；建筑、地形、不可通行 Footprint 仍然阻挡。
2. 关闭碰撞必须来自 Harvest/Return Cargo 订单语义，不能变成 Worker 类型的永久属性。Stop、Hold、Attack、普通 Move、施工等订单要恢复正常碰撞。
3. 建筑放置至少要拆成“光标预览、已接受的施工意图/幽灵、施工开始后的硬 Footprint”三个阶段。当前实现下单即创建硬 Footprint，时序过早。
4. 当前放置预检把所有活单位都视为硬失败 `UnitOverlap`。SC2 对己方单位、可见敌人、后来进入的单位、隐藏/潜地敌人有不同表现；其中“己方单位被要求让开”有较强社区证据，但精确时序必须实机确认。
5. SC2 建造中的建筑没有伤害减免。当前 Demo 无论施工阶段还是完工阶段都使用建筑基础护甲和升级护甲，明确不对齐。
6. SC2 的 Placement Grid、单位 Pathing Grid、建筑 Footprint、Near Resource、Power/Creep 和动态单位占位是不同层。不能继续用单个 `TryPlaceBuilding` 布尔式思维承载所有语义。

因此，下一阶段最值得做的不是继续调 Steering，而是先建立“施工意图与硬占地分离”的协议和独立黑盒测试。

## 3. 农民采矿与碰撞

### 3.1 采集循环

Blizzard 说明农民会前往矿点、在矿点采集、携带资源返回基地并持续重复；单个矿点同一时刻只允许一个农民实际采集，其他农民会等待或自动寻找更合适的矿点。初次把一组农民右键到矿区时，它们会自动分散到未占用矿点。[Blizzard Resources Guide](https://news.blizzard.com/en-us/article/4488900/game-guide-resources)（A）、[Liquipedia Mining Minerals](https://liquipedia.net/starcraft2/Mining_Minerals)（B）

SC2 常规矿每趟携带 5，富矿每趟携带 7。标准基地通常有 8 片矿；长期有效饱和约为每片 2 个农民，第三个农民的边际收益明显下降。官方新手资料会用 2～3/片表达易懂的饱和范围，气矿则是 3 个农民。[Blizzard Resources Guide](https://news.blizzard.com/en-us/article/4488900/game-guide-resources)（A）、[Liquipedia Resources](https://liquipedia.net/starcraft2/Resources)（B）

Liquipedia 的测量值显示，一个农民占用矿点约 1.99 秒，随后约有 0.3571 秒离开停顿；下一个农民可在该停顿期间开始采集。实际收入还受到矿点到基地的路程和农民在矿点间重新选择的影响。这些数值适合用作校准目标，不应直接写死到通用经济内核。[Liquipedia Resources](https://liquipedia.net/starcraft2/Resources)（B）

当前状态：

- **部分对齐**：已有 `GoingToResource → WaitingForResource → Gathering → ReturningCargo` 循环、矿点容量、枯竭转矿和气矿三工人容量。
- Demo 当前常规携带量为 6、采集时间由场景配置，不是 SC2 5/1.99 秒；这是内容参数差异，不是架构缺陷。
- 当前单矿 `HarvesterCapacity` 可配置，适合表达“1 个实际采集者 + 多个已分配/等待者”，但 UI 的 IdealWorkers 和自动分配仍应明确区分“同时采集容量”和“经济饱和值”。

### 3.2 Mineral Walk：无单位碰撞，但建筑仍阻挡

Liquipedia 对 Mineral Walk 的定义非常明确：处于 Harvest 语义的农民可以穿过其他单位，但不能穿过建筑。[Liquipedia Mineral Walk](https://liquipedia.net/starcraft2/Mineral_Walk)（B）

社区的重复实验进一步表明，这不是“农民自身不推别人但别人仍推农民”的单向规则，而是只要接触对中有一方处于 Mineral Walk，双方单位碰撞就不应阻挡该穿行。农民可穿过友军、敌军和 Massive 单位；停止采集、攻击或 Hold 后，重叠单位会重新进入碰撞解算并散开。[Liquipedia Oddities](https://liquipedia.net/starcraft2/Oddities)（B）、[Mineral-walking discussion](https://www.reddit.com/r/starcraft/comments/f270la)（C）

推荐碰撞矩阵：

| 农民订单/阶段 | 单位—单位 | 建筑 Footprint | 静态地形 | 备注 |
|---|---:|---:|---:|---|
| 前往指定资源 | 忽略 | 阻挡 | 阻挡 | Mineral Walk 主路径 |
| 携货 Return Cargo | 忽略 | 阻挡 | 阻挡 | 社区测试支持，需继续实机录制 |
| 矿点内 Gathering | 不产生移动 | 阻挡 | 阻挡 | 不需要为静止阶段关闭全部碰撞 |
| WaitingForResource | 正常 | 阻挡 | 阻挡 | 避免等待农民形成永久可穿透堆叠 |
| 普通 Move/Patrol | 正常 | 阻挡 | 阻挡 | 不享受 Mineral Walk |
| Stop/Hold/Attack | 正常 | 阻挡 | 阻挡 | 恢复接触并有限解重叠 |
| 施工/修理 | 正常 | 阻挡 | 阻挡 | 不复用采矿豁免 |

当前状态：

- **核心已对齐**：`WorkerCollisionPolicy` 只对 `GoingToResource` 和 `ReturningCargo` 返回 true。
- Steering 和最终圆碰撞解算都消费同一个 `_unitCollisionSuppressed`；任一接触者被抑制时跳过成对修正，因此是双向忽略。
- `StaticWorld.ConstrainDisc` 和动态建筑 Footprint 仍然生效，农民不能穿建筑。
- `WaitingForResource`、`Gathering`、Idle、施工、普通移动和战斗不享受豁免。

仍需补测：

- 40 个农民穿过己方 Hold 单位、敌方小型单位和大型单位三组车道。
- 携货返矿穿行与显式 `Return Cargo` 穿行是否完全一致。
- Mineral Walk 中途 Stop/Attack 后，重叠农民必须有限散开且不能被弹过建筑。
- 狭窄矿线里“单位碰撞关闭、建筑碰撞开启”是否会造成贴建筑永久振荡。

### 3.3 Return Cargo 与最近基地

Return Cargo 只对携货农民可用；它会把资源送到最近的有效投递设施，然后恢复原采集任务。把携矿农民改派气矿时，先 Return Cargo 再采气可避免丢失当前携带的矿。[Liquipedia Micro](https://liquipedia.net/starcraft2/Micro_%28StarCraft%29)（B）、[SC2 Forum cargo behavior](https://us.forums.blizzard.com/en/sc2/t/when-a-worker-carries-5-minerals/1704)（C）

当前状态：

- **已对齐主路径**：自动返矿按距离选择最近有效 DropOff。
- Town Hall 使用建筑矩形半尺寸；移动目标是农民当前位置到“单位半径 + 安全余量”扩张矩形边界的最近点，不再走建筑中心或固定左右入口。
- `economy-mass-mining` 已验证 96 农民、4 基地、32 片矿，456 次返矿目标均落在合法最近边缘且无不可达。
- **未对齐**：玩家没有独立显式 Return Cargo 命令；携货农民被改派新资源时的 Cargo 保留、先投递再转场和 Shift 队列语义还没有完整合同。

需要注意一个待实机矛盾：Liquipedia Oddities 同时记录“完全用单位围住 Town Hall 会让农民无法交矿”。这可能意味着投递交互点、返回订单的穿行窗口或基地边缘可达性仍有额外条件，不能仅用“返程全程无单位碰撞”推导全部行为。应在 SC2 当前版本用 8/16/32 个 Hold 单位围基地做录像实验后再决定是否模拟。（C）

### 3.4 自动分矿、饱和与矿距

SC2 会让一组新下达采集的农民自动分散；被点中的矿已占用时，农民可能改选其他矿，除非当前采集者已接近完成。近矿比远矿效率更高，高手开局会手工拆分农民到近矿以减少第一轮拥堵。[Liquipedia Mining Minerals](https://liquipedia.net/starcraft2/Mining_Minerals)（B）

当前状态：

- **部分对齐**：AI 转场按矿点负载率分配，枯竭后寻找最近同类矿；普通玩家把多农民右键同一资源时，当前实现仍倾向把它们绑定到同一节点并在该节点排队。
- 建议把“玩家点击的资源”解释为首选节点而非永久唯一节点：在同一基地资源簇内，用 `active + assigned`、预计等待时间和额外路程做确定性选择。
- 自动改矿必须保留显式微操能力：玩家单独点某一片矿时应允许强制绑定至少一轮，不能每 Tick 为追求吞吐量而重排。

建议黑盒指标：第一轮入账离散度、每片矿 Assigned/Active、空闲等待总 Tick、两农民/三农民边际收益、近矿与远矿每分钟收入。

### 3.5 Rally 到资源

Blizzard 明确说明 Town Hall 的 Rally 指向矿物或气矿时，新农民出生后会自动开始采集；普通生产建筑也可 Rally 到地面、单位、运输或建筑目标。目标单位在出生前/后死亡时的回退行为不同。[Blizzard Buildings Guide](https://news.blizzard.com/en-us/article/4488317/game-guide-buildings)（A）

当前状态：

- **已对齐主路径**：`RallyTarget` 区分 Ground/ResourceNode/FriendlyUnit；新 Worker Rally 到资源进入正式 Gather。
- 目标单位失效保留最后位置的语义已经实现基础回退。
- 后续需要补“目标在单位出生前死亡”和“出生后死亡”两种时间边界测试。

## 4. 建筑预放置、动态单位和硬 Footprint

### 4.1 不应混为一层的五类数据

SC2 API 的 `GameInfo` 同时提供 `pathing_grid` 和 `placement_grid`；前者描述单位可走区域，后者描述结构可放置区域。API 还提供基于具体建造能力的 Placement Query，说明单纯查询静态格子不足以决定最终合法性。[Blizzard SC2 API GameInfo](https://blizzard.github.io/s2client-api/structsc2_1_1_game_info.html)（A）、[Blizzard SC2 API QueryInterface](https://blizzard.github.io/s2client-api/classsc2_1_1_query_interface.html)（A）

SC2 Editor 的 Footprint 资料进一步区分 Placement Check、Placement Apply、No Build、Near Resources、Resource Drop Off 和移动阻挡层。建筑需要 Footprint 同时参与放置和阻止单位穿越，但每种层并不等价。[SC2Mapster Footprints](https://sc2mapster.wiki.gg/wiki/Data/Footprints)（B）

本项目应保持以下分层：

1. `TerrainBuildability`：地图静态可建造格、边界、悬崖、水域。
2. `StaticPathing`：单位实际可走表面和 clearance。
3. `StructurePlacementFootprint`：与其他建筑、资源排斥层、Power/Creep 等规则比较。
4. `StructurePathingFootprint`：施工开始后加入导航与局部碰撞的硬占地。
5. `DynamicOccupants`：己方/盟友/敌方/隐藏单位，以及它们在预览、提交和施工开始时的不同处理。

当前 `BuildingPlacementValidator` 把 2～5 层压在一次 Validate 中，并在任何单位落入扩张矩形时返回 `UnitOverlap`。这对严格测试简单，但不足以表达 SC2 时序。

### 4.2 建议的三阶段施工协议

#### 阶段 A：光标预览

- 只产生表现快照，不扣资源、不写命令、不修改导航。
- 展示网格吸附、Footprint、资源排斥、静态地形、其他建筑、Power/Creep 和已知动态阻挡。
- 对己方可移动单位是否显示绿色、黄色还是红色，资料存在冲突，必须通过实机矩阵确认。
- 预览不得读取隐藏敌人的真实位置，否则会通过红色 Footprint 泄露战争迷雾信息。

#### 阶段 B：已接受施工意图 / Placement Ghost

- SCV 在真正开始施工前会走向目标；玩家资料反复描述这一阶段可显示绿色透明建筑幽灵。[Placement-model discussion](https://www.reddit.com/r/starcraft/comments/161xx3i)（C）
- 这一阶段应保存稳定的 Build Intent、目标 Footprint、资源扣费、Builder 和队列顺序。
- 它可以阻止同一玩家重复预订同一位置，但不应直接等价于完整硬建筑碰撞。
- 当前 Demo **未对齐**：`IssueConstruction` 成功时立即 `TryPlaceBuilding`，在工人尚未到达时就创建 Dynamic Footprint，其他单位开始绕行，Godot 也立刻画出实体施工建筑。

#### 阶段 C：施工开始 / Hard Commit

- Builder 抵达施工接近点后重新检查动态占用。
- 成功时原子把 Ghost 转成正式建筑、加入硬 Pathing Footprint，并开始生命/进度增长。
- 如果阻挡来自可移动己方单位，SC2 很可能给这些单位生成让位移动；玩家长期观察支持“在单位上放建筑会给阻挡单位 Move 命令”。[SCV displacement discussion](https://www.reddit.com/r/starcraft/comments/rabj30)（C）
- 如果阻挡来自可见敌人、后来进入的敌人或隐藏/潜地单位，施工不能开始。潜地单位阻止扩张是稳定竞技机制，且对建造方可能表现为预览绿色、提交后才失败。[Liquipedia Burrow](https://liquipedia.net/starcraft2/Burrow)（B）、[Arqade burrowed placement test](https://gaming.stackexchange.com/questions/6342/do-burrowed-units-block-building-construction)（C）
- 两个玩家竞争同一位置时，需要明确先到者/先开始者、失败通知、退款和命令队列后续行为；现有公开资料不足，列为实机项。

### 4.3 友军预占位：当前最重要的实机矩阵

公开资料存在表面冲突：Liquipedia Buildings 将“单位”列为不可建区域，但玩家重复观察到把建筑命令下在己方单位上时，单位会被要求让开。最可能的解释是“最终施工开始不能与单位重叠”，但光标预览和己方动态清场属于软约束。不能直接把论坛一句话当最终算法。

必须在当前 SC2 版本录制以下矩阵：

| Footprint 内对象 | 对象状态 | 预览颜色 | 命令是否接受 | Ghost 是否保留 | 开工时行为 |
|---|---|---|---|---|---|
| 自己 SCV | Idle | 待测 | 待测 | 待测 | 自动让位/失败 |
| 自己 SCV | Move 穿越 | 待测 | 待测 | 待测 | 继续穿越/改 Move |
| 自己 SCV | Hold | 待测 | 待测 | 待测 | 是否仍可推开 |
| 自己采矿 SCV | Harvest | 待测 | 待测 | 待测 | 订单是否被打断 |
| 自己战斗单位 | Idle/Hold | 待测 | 待测 | 待测 | 自动让位/失败 |
| 盟友单位 | Idle/Hold | 待测 | 待测 | 待测 | 是否可强制其移动 |
| 可见敌人 | Idle/Move | 待测 | 待测 | 待测 | 立即拒绝/到达失败 |
| 隐形/潜地敌人 | 未侦测 | 可能绿色 | 可能接受 | 待测 | 到达后失败 |
| Builder 自身 | 接近点内 | 待测 | 应接受 | 待测 | 选择合法施工侧 |
| 单位在下单后进入 | 任意 | 下单时合法 | 已接受 | 待测 | 重新验证/让位/失败 |

每次实验必须记录：资源何时扣除、SCV Order 队列、Ghost 出现/消失 Tick、单位是否收到新 Move、正式 Footprint 出现 Tick、失败提示、退款比例和 Shift 后续命令是否继续。

### 4.4 推荐的本项目施工状态机

```text
TargetingPreview                 // 表现态，不进权威状态
    ↓ 玩家确认
ReservedApproach                // 已扣费、保留意图、无硬 Footprint
    ├─ Builder 到达 → RevalidateDynamicOccupants
    │      ├─ 可移动友军 → EvictingFriendlyUnits（有界）
    │      ├─ 敌方/隐藏阻挡 → BlockedAtStart（通知/等待或失败）
    │      └─ 清空 → Constructing + Hard Footprint
    ├─ Builder 死亡/命令打断 → WaitingForBuilder
    └─ Cancel → 退款并释放 Reservation

Constructing
    ├─ ContinuousWorker：SCV 必须持续在场
    ├─ StartAndRelease：Probe 开始后离开
    ├─ ConsumeWorker：Drone 变为建筑
    ├─ Cancel/Destroyed
    └─ Completed
```

Reservation 与 Hard Footprint 必须使用不同 ID/不同集合。前者只解决命令竞争与表现，后者才影响 NavMesh、Grid fallback、Connectivity Guard 和单位最终碰撞。

为了确定性，友军让位不应给每个单位随机构造方向：

1. 从单位当前位置求建筑扩张矩形最近边；
2. 四边等距时按“原速度方向 → Builder 进入侧反方向 → 固定边序”打破平局；
3. 为多个单位按到边距离、Unit ID 排序分配边缘槽位；
4. 让位有 Tick 预算和失败状态，不能无限挤压；
5. 让位订单是系统派生命令，不污染玩家 Replay Command Log，但必须进入 Hot Snapshot/State Hash 的未来态。

### 4.5 当前实现差距

| 细节 | 当前实现 | 对齐判断 |
|---|---|---|
| 光标 Preview | 无副作用，共用正式校验 | 部分对齐 |
| 网格吸附 | 8px | 内容参数可调 |
| 单位重叠 | Preview/下单仍统一 `UnitOverlap`；开工会重新分类动态阻挡 | 部分对齐/待实机 |
| 隐藏敌人 | 放置校验读取全体 UnitStore | 未对齐，可能泄露信息 |
| 下单后 Ghost | 独立权威 Reservation + 不可变 Ghost 快照 | 已对齐架构时序 |
| 工人接近期间占地 | Reservation 不进入 Pathing，普通单位可穿越 | 已对齐架构时序 |
| 开工动态重检 | Builder 到场重新评估后才原子 Hard Commit | 主路径已对齐 |
| 友军让位 | 单个可移动己方单位确定性撤离；Builder 接近期临时忽略单位碰撞 | 部分对齐/待 E0 矩阵 |
| SCV 持续施工 | `ContinuousWorker` | 已对齐主路径 |
| Probe 开始后离开 | `StartAndRelease` | 只有策略骨架，无种族内容 |
| Drone 消耗/取消恢复 | 没有 `ConsumeWorker` | 未对齐，内容层后置 |
| 取消退款 | 75% | 已对齐；[Blizzard Common Mistakes](https://news.blizzard.com/en-us/article/4552958/game-guide-common-mistakes)（A） |
| 建造中生命增长 | 10%→100% 随进度增长 | 方向对齐，数值待校准 |
| 建造中护甲 | 使用完整基础/升级护甲 | 未对齐；官方说明施工中无伤害减免 |
| Builder 死亡/打断 | `WaitingForBuilder`，可续建 | 已对齐主路径 |
| Shift 连续造建筑 | Build 不进入正式 Shift 队列 | 未对齐；官方明确支持结构队列 |
| Refinery 绑定气矿 | 可见未启用气矿吸附并独占 | 已对齐主路径 |
| 全局连通保护 | 默认拒绝切断地图连通 | 非 SC2 原样机制，是本项目安全策略 |

“全局连通保护”不应伪装成 SC2 对齐项。SC2 明确允许玩家用建筑造墙；本项目 Connectivity Guard 当前为了防止 Demo 出现完全不可恢复地图而更保守。未来应把它拆为地图规则：允许战术墙、拒绝永久无解封图，或者只在编辑器/AI 放置启用。

### 4.6 建造中的伤害、取消和种族差异

Blizzard 明确说明施工中的建筑没有伤害减免；因此基础 Armor、建筑护甲升级或其他减伤不应在 `Approaching/Constructing/WaitingForBuilder` 阶段生效。[Blizzard Buildings Guide](https://news.blizzard.com/en-us/article/4488317/game-guide-buildings)（A）

取消未完成建筑返还 75% 矿和气；Zerg 取消建筑还会恢复 Drone，并可能暂时超过人口上限。[Blizzard Common Mistakes](https://news.blizzard.com/en-us/article/4552958/game-guide-common-mistakes)（A）、[Liquipedia Micro](https://liquipedia.net/starcraft2/Micro_%28StarCraft%29)（B）

种族施工方法：

- Terran：SCV 整段占用；离开、死亡或被打断会停工，可由 SCV 恢复。
- Protoss：Probe 只负责启动，建筑自行 Warp-in；依赖 Pylon Power 的建筑失去供能后不可工作，但仍可满足部分科技条件。
- Zerg：Drone 自身变成建筑；多数建筑依赖 Creep，取消可恢复 Drone。

来源：[Liquipedia Buildings](https://liquipedia.net/starcraft2/Buildings)（B）。

本项目当前阶段只做 Terran 风格内容是合理的；但通用协议应预留 `ContinuousWorker / StartAndRelease / ConsumeWorker`，不要用布尔 `BuilderRequired` 把种族差异写死。

## 5. 操作手感相关的其他对齐点

### 5.1 Move、Stop、Hold、AttackMove 必须是不同订单

Blizzard 对四种命令的差异有清晰定义：Move 忽略敌人继续前进；Stop 取消当前订单并允许重新自动接敌；Hold 不离开当前位置追击，只攻击射程内目标；AttackMove 遇敌接战，目标消失后继续路线。[Blizzard Basic Unit Controls](https://news.blizzard.com/en-us/article/4552956/game-guide-basic-unit-controls)（A）

当前 Demo 已分别建模这些订单，但继续测试时不能只看最终位置：还要验证接敌、目标死亡、射程外诱饵、队列取消和目标丢失后的状态迁移。

### 5.2 Shift 队列是玩法协议，不只是路径点列表

Blizzard 明确支持 Move、Attack、Stop、Hold、Patrol、装卸和施工等多类命令排队，并给出“建造完成后立刻回矿”的工人队列例子。[Blizzard Special Control](https://news.blizzard.com/en-us/article/4552955/game-guide-special-control)（A）

当前 Demo 已支持移动、战斗、Gather 和 ResumeConstruction 的确定性队列，但 Build 本身不能排队。建筑 Ghost/Reservation 引入后，Build Queue 应保存每一项的目标 Footprint 和解析后的 Building Profile，并在真正轮到该项时重验，而不是提前为所有队列项创建硬占地。

### 5.3 加速度是手感参数

SC2 地面战斗单位多数拥有近似瞬时加速，而 Worker 和空军有明确加速度；SCV/Probe/Drone 的公开加速度均约为 3.5 游戏距离单位/秒²。加速度和横向加速度会显著改变转向与微操反馈。[Liquipedia Speed](https://liquipedia.net/starcraft2/Speed)（B）、[Patch 5.0.12](https://liquipedia.net/starcraft2/Patch_5.0.12)（B）

当前 Demo 对所有单位使用连续加速模型，能产生平滑视觉，但未必符合 SC2 地面兵的立即响应。后续应把 `Acceleration` 与 `LateralAcceleration/TurnResponsiveness` 分开测量，不要仅调整 MaximumSpeed。

### 5.4 生产出口与 Rally

Blizzard 提醒建筑摆得太紧会让新单位卡在出口，并建议把 Rally 放在畅通侧；这说明生产出口不是“无条件瞬移到最近空点”。[Blizzard Buildings Guide](https://news.blizzard.com/en-us/article/4488317/game-guide-buildings)（A）

当前 Demo 搜索 12 个确定性出口，全部被堵时停在 `WaitingForExit`，不会丢单位或重复扣费。这是可靠的业务语义。若追求 SC2 手感，应进一步实测不同建筑的实际出生侧、Rally 对出口选择的影响、友军软挤压与敌军封口的差异。

## 6. 黑盒测试规划

所有对齐测试只能通过正式业务门面下命令并读取稳定快照，不能访问 `EconomySystem`、`ConstructionSystem`、Steering 或 NavMesh 私有数组。

### P0：应最先实现

#### `economy-mineral-walk-collision-matrix`

- 24 个采矿/返矿农民穿过友军、敌军、Hold 单位和大型单位。
- 同地图放置建筑墙，证明单位可穿、建筑不可穿。
- 中途 Stop/Attack，验证碰撞恢复和有界散开。
- 指标：穿越完成率、建筑穿透 0、最大重叠恢复 Tick、不可达 0。

#### `construction-soft-friendly-occupants`

- 小/中/大/主基地四种 Footprint 内放 1/8/32 个己方单位。
- 预览、提交、Ghost、让位、硬占地分别验收。
- 指标：接受码、Eviction 数、最大清场 Tick、错误弹出 0、单位未被推入建筑/地图外。
- 该测试必须等待 SC2 实机矩阵确认后才冻结期望。

#### `construction-hidden-enemy-blocker`

- 未侦测潜地阻挡不改变预览，Builder 到达后施工不应穿过敌人。
- 侦测前后对比失败提示、退款/等待策略、Fog 信息泄漏。
- 当前项目还没有隐形/潜地系统，先把测试列入协议，不用假实体作弊。

#### `construction-under-build-defense`

- 同一武器分别攻击 10%、50%、100% 进度建筑。
- 未完成建筑 Armor 必须为 0；完成 Tick 后才切换到类型护甲和科技护甲。

### P1：玩法收益高

- `economy-auto-patch-distribution`：8 片矿、12/16/24 农民，验证自动分散和近矿收益。
- `economy-explicit-return-cargo`：矿转气、跨基地转场、Shift 后续 Gather、携货类型保留。
- `construction-ghost-late-blocker`：下单后单位进入 Footprint，动态重验。
- `construction-queued-builds`：SCV 连续建 3 个不同尺寸建筑再回矿；中间一项失败不污染后续。
- `construction-race-methods`：Continuous/StartAndRelease/ConsumeWorker 三种策略合同。
- `production-exit-side-and-blocking`：Rally 四方向、友军/敌军封口和等待恢复。

### P2：内容层对齐

- Pylon Power、Creep、Add-on 空间和 Lift/Land。
- Burrow/Cloak/Detection 对放置与目标选择的影响。
- Gold Mineral、MULE、Extractor Trick、气矿不同种族设施。
- 这些不应阻塞当前纯 Terran Demo 的核心手感收口。

## 7. 防止进入无穷优化

对齐工作按三档收口：

1. **控制正确性**：订单语义、碰撞层、Footprint 时序、Fog 不泄漏、退款和确定性。必须做。
2. **经济/战术结果**：自动分矿、收入曲线、友军让位、施工阻挡。用有限场景和数值区间验收。
3. **像素级模仿**：SC2 精确加速度、动画帧、每种建筑特殊出口、种族全部例外。只有实际玩法暴露差异时再做。

每项机制最多先建立一个隔离矩阵测试和一个复杂对局测试。没有可复现失败，不继续增加启发式规则。

## 8. 建议实施顺序

1. 完成 SC2 当前版本的“建筑放在己方单位中”实机录像矩阵，冻结软/硬阻挡合同。
2. 把 `BuildingPlacementValidator` 拆为静态 Placement、动态 Start Validation 和 Hard Footprint Commit。
3. 新增权威 `ConstructionReservation`/Ghost；升级 Replay Package、Hot Snapshot 和 State Hash。
4. 实现确定性 Friendly Eviction，并添加 1/8/32 单位、多尺寸建筑测试。
5. 修复施工中 Armor=0。
6. 增加显式 Return Cargo 和 Cargo 转场队列。
7. 改进同基地自动分矿，再用真实收入曲线校准 5/7 携带量和采集时间。
8. 最后才考虑种族施工差异和内容层规则。

## 9. 主要资料

- [Blizzard：Resources Game Guide](https://news.blizzard.com/en-us/article/4488900/game-guide-resources)
- [Blizzard：Buildings Game Guide](https://news.blizzard.com/en-us/article/4488317/game-guide-buildings)
- [Blizzard：Basic Unit Controls](https://news.blizzard.com/en-us/article/4552956/game-guide-basic-unit-controls)
- [Blizzard：Special Control / Queuing](https://news.blizzard.com/en-us/article/4552955/game-guide-special-control)
- [Blizzard：Common Mistakes / 75% Refund](https://news.blizzard.com/en-us/article/4552958/game-guide-common-mistakes)
- [Blizzard SC2 API：GameInfo grids](https://blizzard.github.io/s2client-api/structsc2_1_1_game_info.html)
- [Blizzard SC2 API：Placement Query](https://blizzard.github.io/s2client-api/classsc2_1_1_query_interface.html)
- [Liquipedia：Mineral Walk](https://liquipedia.net/starcraft2/Mineral_Walk)
- [Liquipedia：Mining Minerals](https://liquipedia.net/starcraft2/Mining_Minerals)
- [Liquipedia：Resources and mining rates](https://liquipedia.net/starcraft2/Resources)
- [Liquipedia：Buildings](https://liquipedia.net/starcraft2/Buildings)
- [Liquipedia：Micro commands](https://liquipedia.net/starcraft2/Micro_%28StarCraft%29)
- [Liquipedia：Oddities](https://liquipedia.net/starcraft2/Oddities)
- [Liquipedia：Burrow](https://liquipedia.net/starcraft2/Burrow)
- [SC2Mapster：Footprint layers](https://sc2mapster.wiki.gg/wiki/Data/Footprints)

论坛和 Reddit 只用于发现实验边界，不作为单独冻结实现的依据。尤其是己方单位预占位、Hold 单位能否自动让位、Ghost 的碰撞时序和 Return Cargo 围基地行为，必须用当前 SC2 客户端重新录制确认。
