# Warcraft III 完整技能系统落地计划

## 1. 目标与完成定义

最终目标不是“能够读取 Ability JSON”，而是在确定性 RTS 运行时中完整承载经典版
Warcraft III 的单位、英雄、建筑、召唤物和物品技能，并能从原始 Object Editor 数据
追溯每一项行为、数值、前置、Buff、特效、音效和测试结果。

完整落地必须同时满足以下条件：

1. 801 个 Ability 对象、全部单位技能引用、Buff/Effect、AbilityMetaData 和相关资源
   都有结构化清单，没有无法解释的静默丢字段。
2. 每个 rawcode 都有明确状态：`implemented`、`delegated`、`presentation_only`、
   `not_applicable` 或 `blocked`；不允许用“JSON 已加载”代替玩法完成。
3. 普通单位、英雄、建筑、召唤物和物品都从导出绑定能力，不再维护阵营专用的
   rawcode 白名单。
4. 共享行为按 `baseCode`/行为家族实现；不同 rawcode 只提供参数和表现差异。
5. 技能的科技解锁、英雄学习、目标过滤、法力、冷却、引导、Buff、伤害类型、
   召唤生命周期、变身、物品和建筑交互都进入确定性模拟、快照、哈希和回放。
6. Godot 表现层正确消费施法阶段、投射物、闪电、Buff 挂点、区域效果、声音与
   动画，不把表现时序反向写入模拟。
7. 每个行为家族至少有数据契约测试和模拟结果测试；所有可玩 rawcode 都有自动
   生成的覆盖项，持续集成中覆盖率只能上升，不能静默回退。

## 2. 已确认的基线

- Ability 清单：801 条。
- 全部 837 个单位/建筑记录引用 461 个不同 Ability：普通 `abilList` 333 条，
  `heroAbilList` 128 条；全部能解析到 Ability 清单。
- 461 条单位引用归并为 285 个 `baseCode` 行为家族；801 条全量对象归并为
  415 个家族。
- Item Ability：234 条、129 个行为家族。
- 当前 Godot 编译 44 条人族候选、43 个行为家族、17 个单位绑定和 2 个建筑绑定；
  36 个家族具备玩法原型编译器，另外条目为空效果、委托或纯表现对象。
- `buff_effect_editor_data` 已结构化导出 247 个闭包对象：213 个 Buff、34 个
  Effect（含 profile-only 补全对象）。
- `ability_metadata` 已导出 755 个合并字段定义和 801 个逐技能字段绑定。
- 运行时覆盖审计确认：43 个已分类家族覆盖全量 103/801 条 rawcode；单位实际
  引用的 285 个家族仍有 242 个未分类。

## 3. 分层架构

### 3.1 原始数据层

保留 MPQ 覆盖顺序和来源哈希，读取：

- `AbilityData.slk`、`AbilityMetaData.slk`
- `AbilityBuffData.slk`、`AbilityBuffMetaData.slk`
- 各族 `*AbilityFunc.txt`、`*AbilityStrings.txt`
- `UnitAbilities.slk` 的 `abilList`、`heroAbilList` 和 `auto`
- `UpgradeData.slk` 及科技前置
- `WorldEditStrings.txt` 的本地化字段名
- ItemData、物品能力与物品使用规则

### 3.2 结构化导出层

输出一对象一 JSON 与 manifest：

- `ability_editor_data/`
- `ability_metadata/`
- `buff_effect_editor_data/`
- `unit_editor_data/`
- `upgrade_editor_data/`
- 后续的 `item_editor_data/`

`ability_editor_data/coverage-manifest.json` 记录 rawcode、baseCode、引用单位、种族、
Buff/Effect 闭包、召唤对象、科技前置、元数据字段、运行时支持状态和原因。

### 3.3 内容编译层

新增内容中立的行为注册表：

```text
rawcode -> baseCode -> behavior family -> typed parameters -> runtime profile
```

禁止在核心模拟中判断 `AHbz`、`Adef` 等 Warcraft rawcode。rawcode 仅存在于导入、
调试、事件和表现边界。行为处理器按家族注册，变体复用处理器并读取各自字段。

### 3.4 确定性模拟层

逐步补齐以下通用能力：

- 伤害/治疗分类：物理、魔法、通用、攻击类型、护甲关系、魔抗、魔法易伤。
- 完整目标标签：古树/非古树、树木、守卫、物品、桥、墙、残骸、工兵、玩家等。
- Buff：正负面、魔法/物理、可驱散、唯一组、最高等级覆盖、叠加、光环滞留。
- 状态：沉默、缴械、定身、诱捕、睡眠、旋风、净化、诅咒、毒、疾病、燃烧等。
- 属性：生命/魔法恢复、闪避、暴击、反伤、吸血、攻击范围、视野、目标层等。
- 投射物/闪电/链式/弹射/冲击波/锥形/多段区域和持续区域。
- 召唤、尸体、复活、变身、装载、吞噬、建筑和树木交互。
- 英雄技能点、学习等级、终极技能、科技前置、自动施法开关与命令记录。
- 物品栏、拾取/丢弃、使用、充能、被动属性和物品技能。

所有新增状态必须进入热快照、状态哈希、确定性命令日志和回放。

### 3.5 Godot 表现层

- 施法开始、发射、飞行、命中、持续、结束分别消费事件。
- Buff/Effect 使用自己的对象定义，而不是从 Ability 的第一个模型猜测。
- 支持 attachment point/count、模型高度、朝向、缩放、多个 SpecialArt。
- 支持真实投射物轨迹、homing、arc、speed 和 LightningEffect 链路。
- 循环特效和循环声音绑定到运行时实例，结束/驱散/死亡时可靠清理。
- UI 支持技能学习、科技锁定、自动施法、开关态、充能、Buff 图标和已解析提示。

## 4. 里程碑与验收门槛

### M1：数据闭环与审计基线

- 导出 `heroAbilities`、`summonedUnitId`、技能前置和身份字段。
- 导出 AbilityMetaData 与本地化字段名。
- 导出 Buff/Effect 对象和 profile-only 闭包。
- 修复 Profile 资源字段大小写导致的资源漏解析。
- Godot 能严格加载新增目录。
- 生成覆盖 manifest，确认 461 个单位引用无悬空，当前数据缺口显式列出。

验收：导出脚本、目录严格加载、C# 构建和数据自测全部通过。

### M2：编译器与运行时基础语义

- 以 baseCode 为键的行为注册表替换 rawcode switch。
- 扩充目标标签、伤害/治疗类型、Buff 分类与叠加规则。
- 科技前置、英雄学习和自动施法改为确定性玩家命令。
- 建立按行为家族生成的测试矩阵。

验收：当前 44 条不再依赖 rawcode 行为分支，原始字段消费情况可审计。

### M3：当前人族正确性

- 修正当前 44 条的近似或错误实现。
- 接入当前 13 个建筑 profile 的能力。
- 完成民兵、蒸汽机车、凤凰、水元素、塔侦测、商店和资源交回链路。
- 接入英雄学习和 15 条科技门槛。

验收：人族可玩单位、建筑和召唤物引用全部为 implemented/delegated，且不存在
未解释的空效果。

### M4：四族与中立单位

- 按共享家族优先级接入兽族、亡灵、暗夜和中立/野怪能力。
- 优先实现高复用家族：光环、属性 Buff、召唤、形态、尸体、链式和区域引导。

验收：四族标准对战单位及建筑的能力覆盖 100%，战役变体可复用同一行为家族。

### M5：物品系统

- 导出 ItemData 与物品绑定。
- 实现背包、地面物品、拾取/丢弃、商店、使用、充能、冷却和被动技能。
- 接入 234 条 Item Ability，并区分复用家族与物品专用家族。

验收：全部物品技能有明确支持状态，常规对战物品完整可用。

### M6：战役、特殊对象与表现收口

- 接入战役英雄、特殊单位、运输、海军、地形永久修改等低复用行为。
- 完成全部 Buff/Effect 挂点、投射物、闪电、动画和技能音频。
- 清理 profile-only、源版本差异和明确不适用的编辑器内部对象。

验收：801 条 Ability 都有终态分类；所有可实例化对象可在测试场景验证。

## 5. 覆盖状态规范

每条 Ability 必须使用以下状态之一：

- `implemented`：运行时与表现均达到当前数据定义。
- `implemented_gameplay`：玩法完成，仍有明确记录的表现缺口。
- `delegated`：行为由经济、建造、物品、运输等独立模块实现，并有对应测试链接。
- `presentation_only`：纯模型挂件或编辑器表现对象，无独立玩法行为。
- `not_applicable`：仅编辑器/旧版本内部对象，附来源证据。
- `blocked`：原始数据或引擎能力不足，必须记录阻塞字段和下一步。

禁止使用 `loaded` 或 `missing=0` 作为完成状态。

## 6. 测试策略

1. 导出完整性：数量、哈希、引用闭包、字段类型和路径安全。
2. 内容编译：每个行为家族至少一个正例，所有 rawcode 能编译或有终态原因。
3. 模拟语义：目标合法性、数值、持续时间、打断、驱散、叠加和死亡交互。
4. 确定性：重复执行、热快照恢复、状态哈希、回放重建。
5. 表现冒烟：模型、挂点、投射物、循环实例、声音和清理。
6. 阵营验收：按标准对战单位/建筑/英雄/召唤物生成覆盖报告。

## 7. 当前执行批次

M1 已完成：导出、严格加载、引用闭包、MetaData、Buff/Effect 和数据自测均已落地。

当前执行 M2：

1. 已完成单位普通/英雄技能的动态收集，不再使用人族技能白名单。
2. 已建立 `baseCode` 行为注册表，当前 43 个家族全部有显式状态和原因。
3. 已将 34 个玩法原型改为语义编译器枚举分派，适配器不再按 Warcraft rawcode
   选择行为；rawcode 只保留数据、调试和表现身份。
4. 已生成 `reports/war3_ability_runtime_coverage.json`，覆盖全量、单位引用、物品和
   当前运行时四个切片，并接入严格自测。
5. 已完成目标单位特征、伤害分类、Buff 规则、英雄学习、经验升级、自动施法命令
   和科技前置的确定性语义；上述状态均进入目录、热快照、哈希和二进制存档。

最新进度：27 种原始目标 token 已全部进入配置，`notself`、`ward`、`player`、
`ancient/nonancient`、`nonsapper` 已进入运行时校验；只剩树木、残骸、桥、物品、
墙 5 种必须由独立世界实体承载的 token，影响 57 条逐对象记录。当前 15 条带前置的人族
技能所依赖的 12 个科技已全部进入稠密科技目录和施法/被动/攻击触发校验，
`requirement_missing=0`。英雄技能初始未学习、等级/技能点、学习门槛、学习命令、
自动施法开关命令、命令日志、热快照和 HUD 学习按钮已经落地；单位等级编译为
经验赏金，普通战斗与主动技能击杀都能在范围内按稳定 ID 均分经验并提升英雄等级、
技能点。伤害已区分物理/魔法/通用，Buff 保留 rawcode 身份、正负面、魔法/物理
驱散类别和刷新/替换/独立叠加规则。

M2 基础语义已完成，下一批按覆盖报告进入 M3 人族正确性，不再新增不可审计的
临时分支。5 种独立世界实体 token 在 M3 的采集/修理链路和 M5 物品系统中分别闭环。

M3 第一批已开始：

1. 被动光环按 Buff rawcode 唯一组解析，同组只应用最强实例，相同强度由最低
   施法者 unit ID 稳定胜出，避免多名大魔法师/圣骑士把同类光环重复叠加。
2. `Adis` 已消费 Data B，对敌方召唤单位施加 200 点魔法驱散伤害，友方召唤物
   不受该伤害；区域 Buff 驱散继续按正负面和魔法/物理类别筛选。
3. `AHdr` 已改用 Data E（30/60/90）和 Data C 汲取间隔；新增通用
   `TransferMana`，敌方目标为目标→施法者，友方目标为施法者→目标，转移量受
   双方法力与上限共同约束。
4. Feedback 已分别消费普通单位 Data A/B 与英雄 Data C/D；effect 支持英雄目标
   替代数值，首级普通单位烧 20 魔、英雄烧 4 魔，不再把英雄当普通单位。
5. 单位 `undead` 分类和 effect 必需/排除特征已落地；Holy Light 只治疗非亡灵
   友军，只伤害亡灵敌军。
6. 攻击附伤支持互斥同心环。Flak 按 75/150/325 与 7/6/5 编译；1.27 的
   Fragmentation 100/275/250 与 25/18/12 按全伤→中伤→小伤优先级解析，因
   250 小伤圈被 275 中伤圈覆盖而不产生重复叠伤。该版本异常已由官方经典资料
   与 1.30 修订记录交叉确认。

M3 第二批已完成三个英雄技能闭环：

1. 新增内容中立的持久区域脉冲实例。Flame Strike 按 Data A-F 保存全伤/部分
   伤害、两个间隔、阶段延迟、建筑 75% 倍率和每次关系组伤害上限；实例进入
   热快照、回放初态和状态哈希。
2. Blizzard 不再把 Data D 建筑折减误当成 0.5 秒波间隔；现在按 6/8/10 个
   一秒波次执行，Data C 保留为 6/7/10 个表现碎片，Data F 作为每波
   150/200/250 伤害上限，Data D 作为 50% 建筑伤害。
3. Mass Teleport 从错误的地面点目标改为友军地面单位/建筑目标，消费 Data B
   的 3 秒施法延迟和 Data A 的 24 单位上限；传送成员按距离与稳定 ID 选择，
   目的地复用移动系统槽位分配器，避免全部单位堆在同一点。
4. 上述三个家族在覆盖报告中从 `blocked` 更新为 `implemented_gameplay`；表现仍
   通过技能事件流消费原始 Caster/Target/Effect/Area art，不反向控制模拟。

M3 第三批已完成单位侧 `Amil` 形态链路：

1. Ability Catalog 新增可逆 `UnitForm` 描述，完整内嵌普通/交替形态的移动、战斗、
   视野、工人身份和稠密类型 ID；模拟不读取 `hpea`/`hmil` rawcode。
2. 战斗号召与回到工作都会选择最近的己方已完成城镇大厅，单位必须实际寻路并接触
   建筑边界才切换；目标大厅失效时按稳定 ID 重新选择合法大厅。
3. 民兵形态消费原始 Data A/B 和 45 秒 Dur：速度、4 点护甲、攻击、模型、肖像、
   图标与能力绑定同步切换。到期自动返程，也可用导出的“回到工作”按钮提前返程。
4. 民兵期间原工人登记仍保留，但采集、返还资源和建造权限关闭；恢复农民后重新
   开启，因此不会通过复制/删除单位破坏稳定 unit ID、携带资源或选择状态。
5. 正在接近、民兵剩余时间和返程状态进入状态哈希、Ability 二进制状态和热快照；
   专项测试覆盖远距离接近、自动/手动还原和中途恢复一致性。

M3 第四批已完成建筑侧 `Amic` 号召链路：

1. Ability Catalog v12 增加建筑类型绑定；`hkee`/`hcas` 从原始 `abilList` 绑定
   `Amic`，`htow` 保持无此能力，主城/城堡作为不可直接建造的升级终态进入 13 个
   建筑 profile。
2. 建筑开关按导出的 2000 范围筛选己方农民/民兵，指定施令建筑为接触目标；关闭
   时也取消仍在奔赴该建筑的农民。Call/Uncall 名称、说明、图标和快捷键均来自原始
   `Art/Unart`、`Tip/Untip`、`Ubertip/Unubertip`、`Hotkey/Unhotkey`。
3. 新增独立建筑施法命令，不伪造 unit caster。命令日志允许且只允许该命令使用空
   单位列表；回放从建筑所有者重建命令，建筑开关态进入状态哈希、Ability runtime、
   Replay Package 36 与 Hot Snapshot 37。
4. Ability 事件携带 `CasterBuilding`；表现和音频从建筑边界中心解析世界位置与
   玩家，`TownHallCallToArms` 继续走 3D 距离衰减和听众过滤。
5. 专项测试覆盖范围内/外筛选、指定建筑接触、开启/关闭、工人权限恢复、命令日志
   二进制往返、重放状态哈希、热快照二进制和建筑事件生命周期。

M3 第五批已完成多武器与目标层链路：

1. Combat 层新增最多八槽的内容中立武器组和地面单位/空中单位/建筑目标掩码；
   War3 当前两槽数据按 slot 稳定选择。旧单武器内容自动归一化，不要求其他阵营
   了解 Warcraft rawcode。
2. `hgyr` 的第二武器由 `Rhgb` 解锁对地/建筑，`hmtt` 的第二武器由 `Rhrt`
   解锁对空；`hmtm` 与 `hgry` 的两把默认武器按互斥目标层选择，不会对同一目标
   双重结算。攻击 Buff 作为武器外部修正保存，切槽不会覆盖基础数值。
3. 攻击事件触发会校验被动技能自己的目标 flags。`Aflk` 与 `Aroc` 只在空中目标
   命中时触发，并分别检查 `Rhfc`/`Rhrt`；地面攻击不再错误播放或结算防空附伤。
4. `Agyb` 和 rawcode `Srtt` 标记为委托到武器/科技模块；`Acha` 其他 Channel
   变体继续保持 blocked，覆盖报告不会为了一个变体错误宣称整个家族完成。
5. Upgrade JSON 的 `requirements` 进入科技目录；`Rhgb/Rhrt/Rhfc` 均保留
   `hcas` 城堡前置。武器组和当前 slot 已进入 Production Catalog 8、Production
   Command Log 10、Ability Catalog 13、State Hash 35、Replay Package 36 与
   Hot Snapshot 37；Godot Resource 和 HUD 同步显示/保留武器槽与目标层。
6. 专项数据门禁验证飞行机器、蒸汽机车、迫击炮和狮鹫双武器，科技前后选槽、
   行为终态以及生产命令往返；全量模拟回归继续验证热恢复和回放精确一致。

M3 第六批已完成建筑升级协议：

1. 新增内容中立的不可变建筑升级目录和确定性升级系统；`htow -> hkee -> hcas`
   从原始 `Upgrade`、总费用、建造时间与 `Requires` 编译，不在核心模拟判断 rawcode。
2. 两段增量费用分别为 320 金/210 木与 360 金/210 木；第二段严格要求 `halt`。
   订单与生产/研究互斥，手动取消按原版 profile 的 75% 退款，建筑死亡由统一经济
   生命周期清理。
3. 完成时保留稳定建筑 ID、占地和生命比例，并通过建筑类型祖先关系继承城镇大厅的
   生产、研究和前置资格；主城/城堡无需复制农民配方。
4. HUD 已接入升级按钮、费用/时间/前置提示、队列进度和取消；表现层使用模型原生
   `Birth/Stand/Stand Work Upgrade First/Second` 序列，类型切换时刷新 actor，音频
   消费正式开始/完成事件。
5. 升级订单进入 Production Command Log 11、State Hash 36、Replay Package 37 与
   Hot Snapshot 38；恢复额外拒绝同一建筑同时存在升级和生产/研究队列的损坏状态。
6. 专项测试覆盖数据、祭坛门槛、费用/退款、互斥、两段完成、生命比例、类型继承、
   命令往返、热恢复和最终回放 Hash。

下一批进入 War3 攻防类型矩阵和普通武器 `area`/`minimum-range` 字段，然后补单位
护甲科技的逐级效果。建筑升级实现细节见 `docs/BUILDING_UPGRADE_RUNTIME.md`。

本批半径语义参考：Blizzard 经典站点的
[Flying Machine](https://classic.battle.net/war3/human/units/flyingmachine.shtml) 与
[Mortar Team](https://classic.battle.net/war3/human/units/mortarteam.shtml) 数据页；
Fragmentation 后续半径修正由
[Warcraft III 1.30.0 记录](https://warcraft.wiki.gg/wiki/Warcraft_III/Patch_1.30.0)
交叉验证。Flame Strike 的六目标等价伤害上限参考
[Blood Mage](https://classic.battle.net/war3/human/units/bloodmage.shtml)，Blizzard 的
波数、五目标等价伤害上限、友伤和建筑折减，以及 Mass Teleport 的友军单位/建筑
目标和 24 单位上限参考
[Archmage](https://classic.battle.net/war3/human/units/archmage.shtml)。项目仍以当前
导出的 1.27a 对象数据为运行时输入，不套用新版本平衡值。
