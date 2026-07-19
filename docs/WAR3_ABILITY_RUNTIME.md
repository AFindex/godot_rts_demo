# Warcraft III 技能运行时接入

## 结果与数据源

当前可玩的人族内容已接入数据驱动技能运行时。启动日志中的
`WAR3_ABILITY_DATA` 会报告实际导入数量、单位绑定数量、缺失 rawcode
和稳定哈希；当前基线为：

```text
ability_runtime=47/47 unit_bindings=17 building_bindings=6 families=45 prototype=40 missing=0 unclassified=0
```

原始编辑器数据位于：

- `assets/warcraft3/classic/data/ability_editor_data/manifest.json`
- `assets/warcraft3/classic/data/ability_editor_data/coverage-manifest.json`
- `assets/warcraft3/classic/data/ability_editor_data/abilities/<race>/<rawcode>.json`
- `assets/warcraft3/classic/data/ability_metadata/`
- `assets/warcraft3/classic/data/buff_effect_editor_data/`
- `assets/warcraft3/classic/data/unit_editor_data/`
- `reports/war3_ability_runtime_coverage.json`

技能图标、施法者特效、目标特效、效果模型和投射物模型仍引用导出的 War3 虚拟
路径，由 `War3RuntimeAssets` 解析到外部资源包，不复制第二份模型。Ability 引用的
Buff/Effect 会继续解析自己的 Art 字段，因此 `AHbz -> XHbz` 等间接效果不再丢失。

## 模块边界

接入分成四层，彼此不依赖 HUD：

1. `War3AbilityBehaviorRegistry` 按 `baseCode` 选择激活方式和语义编译器；
   `War3AbilityDataAdapter` 读取变体自己的等级、法力、冷却、施法时间、
   持续时间、范围、目标过滤和 Data A-I，并转换为内容中立配置；允许为 0 的必需
   Data 字段也必须在 JSON 中存在且是有限数值，缺失/`-`/非法文本不会伪装成 0。
2. `AbilityCatalogSnapshot` 保存不可变技能目录、等级效果、单位类型绑定和建筑
   类型绑定，同时生成稳定二进制与哈希。
3. `AbilitySystem` 是固定帧权威层，负责单位/建筑法力、冷却、施法/引导、自动施法、
   Buff、光环、状态、召唤物、显隐、Released/Projectile/Impact 和技能事件。
4. `War3WorldPresenter` 与 `War3RtsHud` 只消费只读快照和事件，负责按钮、
   法力条、冷却、目标指示、动画和原始特效模型。

核心模拟不引用 War3 路径或 raw JSON。以后接入其他种族时，只需增加内容适配，
不需要把种族规则写进移动、战斗或 HUD。

## 已支持的玩法语义

- 单位/点/自身/建筑目标，友军、敌军、中立、存活、死亡、空中、地面、
  生物、机械、英雄、非英雄、无敌和可伤害过滤。
- 守卫、古树/非古树、非工兵和玩家控制单位特征；特征从 `UnitBalance.type`
  编译进单位类型绑定，不在技能核心判断 Warcraft rawcode。
- 法力消耗与恢复、冷却、施法前摇、引导、周期触发和中断。
- 治疗、伤害、法力吸取、驱散、Buff 转移、控制权转移、传送、复活、
  召唤和区域视野揭示。
- 眩晕、无敌、魔免、隐形、变形、放逐、禁攻和禁移。
- 移速、攻击间隔、攻击力、护甲、最大生命、生命回复、回魔和侦测范围修正。
- 被动光环和攻击命中触发；随机触发由事件序号确定，不使用非确定随机数。
- 治疗、心灵之火和减速等默认自动施法，按稳定单位 ID 选择第一个合法目标。
- 水元素和凤凰生命周期；召唤物 rawcode、生命、攻击、移动、碰撞、视野和持续时间
  都来自技能/单位 JSON。JSON 持续时间 0 表示永久召唤（凤凰），不会套用代码默认值。
- 每等级科技/建筑前置进入不可变技能目录；施法、被动光环和攻击触发都会在
  确定性模拟中检查前置，未满足时返回 `RequirementsNotMet`。
- 英雄技能初始等级为 0；英雄以等级 1、技能点 1 出生，通过学习命令校验
  `RequiredHeroLevel`、`HeroLevelSkip`、技能点和最高等级后升级技能。
- 物理、魔法和通用伤害分类：物理伤害经过护甲，魔法伤害受魔免拦截，通用伤害
  绕过魔免；分类同时适用于直接伤害、命中附伤和状态附带伤害。
- Buff 实例保留导出的 Buff rawcode、正/负面、魔法/物理驱散类别和
  Refresh/Replace/Stack 规则。相同 Buff rawcode 默认跨施法者刷新而不重复叠加，
  驱散和法术偷取只处理类别匹配的实例。
- 单位等级编译为击杀经验赏金；普通武器与技能击杀都把经验分给 240 世界单位内
  的同阵营存活英雄，并按稳定 unit ID 处理不能整除的余数。标准英雄累计经验阈值
  为 0/200/500/900/1400/2000/2700/3500/4400/5400，升级增加一个技能点。
- 被动光环也遵守 Buff rawcode 唯一组：相同光环不叠加，保留数值更强的实例；
  同强度按稳定施法者 ID 决胜。
- 驱逐魔法会从 Data B 读取召唤伤害，只伤害区域内敌对召唤物；魔法吸吮从
  Data E/C 读取每次转移量与间隔，并根据友敌关系自动决定法力流向。
- effect 可为英雄目标提供替代数值与必需/排除单位特征；Feedback 因此使用
  普通/英雄两套燃魔参数，并从 Data E 对召唤目标追加配置伤害；Holy Light 因此
  区分亡灵伤害与非亡灵治疗。
- 心灵之火完整消费 Data A/B/C/D：攻击倍率、护甲、独立自动施法范围和每秒生命
  回复都进入通用目录；自动施法只在 `AutoCastRange` 内选择目标。放逐完整消费
  Data A/B，分别形成移动速度和攻击间隔修正，不依赖英雄或技能 rawcode 分支。
- 攻击附伤支持互斥的同心伤害环。Flak 使用 7/6/5 三段伤害；旧版
  Fragmentation 的 275/250 半径倒置按原始优先级处理，不会让同一目标重复吃到
  多段附伤。
- 持久区域效果独立于施法者当前动作存在，保存阶段延迟、脉冲间隔、剩余次数和
  关系分组伤害上限。Flame Strike 已按 Data A-F 编译全伤/部分伤害阶段、建筑
  折减和六目标等价伤害上限；中途热恢复不会重复或跳过脉冲。
- 计数型引导严格按配置波数结束。Blizzard 使用 6/8/10 波、每波 6/7/10 个表现
  碎片、150/200/250 伤害上限，并对建筑造成 50% 伤害；友军、敌军和中立单位
  分别计算伤害上限。
- 群体传送只接受非自身友军地面单位或友军建筑，消费 Data B 的 3 秒可打断延迟，
  从施法者周围按距离和稳定 unit ID 选择最多 24 个单位，并通过统一目的地槽位
  分配器生成无重叠落点。
- 可逆单位形态不是普通 Buff。`Amil` 把农民与民兵的完整 profile 编译进技能目录，
  正向/反向都先寻路到最近己方已完成城镇大厅；接触后同步切换移动、战斗、视野、
  工人权限和能力绑定。民兵 45 秒到期自动返程，也支持“回到工作”提前返程。
- `Amic` 是独立的建筑施法入口，不借用单位施法者。主城和城堡从原始建筑
  `abilList` 绑定它，开启时按导出的 2000 范围（运行时 533.33）稳定筛选己方农民，
  并把施令建筑固定为接触目标；关闭时召回范围内民兵，同时取消仍在奔赴该建筑的
  农民。城镇大厅 `htow` 按当前 1.27a 数据不拥有此能力。
- `Ahrp` 修理把 Data A-E 分别编译为资源成本率、修理耗时率、协助建造成本率、
  协助建造速率和海上修理距离。建筑读取自己的造价/建造时间，机械单位读取 Unit
  JSON 的 `repairGold/repairLumber/repairTime`；持续作业按最大生命与当前生命/进度
  累计比例扣费。手动目标和自动寻找受损友军目标共用同一执行路径；修满、资源
  不足、工人死亡/离开范围或收到新命令都会停止。活动修理作业参与热快照、状态
  哈希和回放初态。
- 单位可以保存多组武器与空中/地面/建筑目标层。`Agyb`、`Srtt` 委托到通用
  武器组，`Rhgb`/`Rhrt` 在科技完成后解锁对应副武器；`Aflk`/`Aroc` 只在
  对空武器命中空中目标后触发，不会随地面攻击误触发。

当前人族的普通单位技能与四名英雄技能栏均从导出的 `abilities` 和
`heroAbilities` 绑定，不再维护英雄 rawcode 手写补表。修理由通用 Ability effect
发起、由内容中立 Construction/Economy 接口执行；采集、建造、物品栏、集结点等
Warcraft “能力对象”则委托到各自模块，不在技能核心重复实现。纯科技标记或纯模型
挂件保留在目录中，但不伪造伤害数值。

## UI 与操作

选择具备主动技能的单位后，命令卡会在空槽显示技能按钮。按钮提供：

- 中文名称、导出说明、等级、法力、冷却和施法距离；
- 法力不足或冷却期间禁用；
- 剩余冷却数字和 Toggle 状态标记；
- 从 Ability profile 的 `Orderon/Orderoff` 识别自动施放能力；右键切换时按
  `Art/Unart` 和当前状态更新图标/高亮，并对当前单位子组统一下达配置化开关命令；
- 瞬发/开关技能直接执行，单位技能进入选目标模式，点技能进入地面选点模式。

选择主城或城堡时，命令卡直接显示“战斗号召”；开启后按钮按原始 `Unart`、
`Untip`、`Unubertip`、`Unhotkey` 切换为“回到工作”。事件保留建筑施法者身份，
所以 `TownHallCallToArms` 以建筑位置作为 3D emitter，并继续经过本地玩家听众过滤。

建筑主动技能与单位技能使用同一 Preview/Issue 校验。神秘之塔“显示”从 JSON 读取
法力、冷却、施法距离、区域、持续时间和 `Rhse` 前置；按钮进入点目标模式并显示范围/
区域预览，命中后创建可过期的 Reveal/Detect 区域。

物品栏容量与权限也按 Ability 行为数据解析，不按英雄/单位身份分支。`AInv/Aihn`
的 DataA..E 分别进入容量、死亡掉落、可使用、可取得和可丢弃配置，科技前置复用技能
requirement 编译结果；当前商店购买与物品按钮已经强制执行 CanGet/CanUse。地面物品、
按距离拾取、主动丢弃、死亡掉落、库存/冷却及地面实例均已进入确定性快照，
`AInv/Aihn` 已标记为 `implemented_gameplay`。

照明弹 `Afla` 的 JSON DataB 作为生效延迟进入施法时间轴；没有 DataB 的 `AHta`
保持即时生效，两个 Reveal 变体不共享内容常量。

肖像下方会显示蓝色法力条；选择信息会显示施法中状态、控制状态与活动 Buff。
施法者播放 JSON `Animnames` 候选。技能按 Caster/Target/Effect/Missile art 分阶段
生成原始 War3 特效，Missile 读取 JSON 速度、弧度和自导；caster/target/buff 挂点路径
与 count 也由表现层消费。表现层只读事件和投射物快照，因此不会改变模拟结果。

## 运行时 API

```csharp
simulation.Abilities.ConfigureCatalog(catalog);
var result = simulation.IssueAbility(
    playerId,
    casterUnit,
    abilityId,
    AbilityCastTarget.Unit(targetUnit, targetPosition));

var state = simulation.Abilities.Observe(casterUnit);
var buffs = simulation.Abilities.ObserveBuffs(casterUnit);
var summons = simulation.Abilities.ObserveSummons();
simulation.IssueLearnAbility(playerId, casterUnit, abilityId);
simulation.IssueSetAbilityAutoCast(playerId, casterUnit, abilityId, enabled);
simulation.IssueBuildingAbility(
    playerId,
    casterBuilding,
    abilityId,
    AbilityCastTarget.Point(worldPosition));
```

外部模块应通过 `RtsSimulation` 的四个 `Issue*` 入口下达单位施法、建筑施法、
学习和自动施法开关，以便所有权、比赛状态和命令录制统一生效。`TrySetLevel`、
`TrySetAutoCast` 与 `TryAdvanceHeroLevel` 只用于受信任的初始化/英雄经验模块，
不应由 HUD 直接调用。

## 存档、回放和验证

技能目录和运行状态已进入：

- 热快照及其二进制编解码；
- 模拟状态哈希；
- 回放初始 manifest 和施法命令日志；
- 运行时事件流（事件可由回放重建，不写入快照）。

当前二进制版本为 Ability Catalog 25、Unit Command Log 9、Production Command
Log 16、Production Catalog 12、State Hash 48、Hot Snapshot 50、Replay Package 49。
持久区域、精确剩余波数以及单位形态的接近目标、阶段和剩余时间均参与状态哈希与
二进制往返；建筑开关态和建筑施法命令也参与相同链路。旧开发版快照与回放会被
明确拒绝，不会按新布局误读。民兵保留工人登记但临时禁用采集的合法状态也已纳入
热快照严格校验。

专项测试命令：

```powershell
godot --headless --path . -- --ability-self-test
godot --headless --path . -- --war3-ability-data-self-test
godot --headless --path . -- --building-upgrade-self-test
godot --headless --path . -- --generate-war3-ability-runtime-coverage
godot --headless --path . war3_rts/War3Rts.tscn -- --war3-rts-smoke
```

第一项验证自动治疗、三类伤害与魔免、Buff 身份/驱散/叠加、单位目标特征、英雄
经验与升级、持久区域、关系分组伤害上限、精确波数、群组传送、单位形态的大厅
接近/自动与手动还原/工人权限/二进制往返，以及建筑范围号召、指定建筑接触、
提前复工、零单位列表建筑命令回放、建筑开关热快照、无敌阻伤/到期、事件生命周期
与热快照哈希；同时用独立配置样例验证自动施法半径、生命回复、召唤目标反馈伤害、
诱捕落地层、禁行、负护甲和显形状态，以及活动 Buff 的热快照继续运行哈希。第二项
严格审计 801 条数据闭包、当前 47 条人族编译结果，并额外枚举
`Aens/Acri/Ablo/Arej/Afae/Avul/Arsk/AEev/AOcr` 的 26 条跨种族 effect 变体，并验证
`Arsk` 的 3 条无数值 Aura 与 20 个真实单位绑定；Ainf/Afbk/AHbn/Adef/Aclf/Acmg/
Aply/Ahrp 与新增家族的标量均逐字段映射，同时验证 JSON 法力、召唤单位配置、复合挂点路径、双武器/目标层、
科技解锁和生产命令往返。最后一项场景冒烟会在真实地图里施放水元素和雷霆一击，
并验证 `ability_integration=True`、召唤物 rawcode、动画及技能特效消费链。

## 扩展其他种族

导出目录共索引 801 个技能对象；461 个单位引用和 285 个行为家族记录在数据覆盖
manifest 中。机器生成的运行时报告当前确认：62 个已分类家族覆盖 163 条 rawcode；
单位引用家族仍有 231 个未分类。当前人族运行时编译 47 项、45 个家族，其中
40 项有玩法编译器；状态为 43 `implemented_gameplay`、3 `delegated`、1
`presentation_only`、0 `blocked`、0 `unclassified`。当前 17 条有科技门槛的技能使用 13 个 requirement 对象，
均已映射到运行时人族科技/建筑目录；生产建筑可以同时承担训练和研究职责。
报告把“已分类”“有原型”“已完成”分开统计，
禁止用成功加载冒充语义完成。

原始目标列表共有 27 种 token，已全部解析和保留。守卫、古树、非古树、非工兵和
玩家控制目标已具备单位运行时语义；树木、残骸、桥、物品、墙 5 种仍需要独立
世界实体接口，影响 57 条全量技能。它们属于全量扩展切片，不再构成当前人族
运行时的 `blocked`；相关缺口仍逐条写入覆盖报告，不会被当作普通单位目标静默放宽。

接入兽族、亡灵、暗夜精灵或中立单位时，应增加共享 `baseCode` 行为家族并复用同一
`AbilitySystem`。遇到变身、尸体召唤、物品栏或地形永久修改等新语义时，应新增
内容中立 effect kind 和世界接口，避免在核心层判断具体 rawcode。

首个跨种族批次已经按该规则接入诱捕、残废、嗜血、生命恢复和精灵之火。`TryCompileAbility`
是完整种族目录和数据闭包测试共享的单 rawcode 编译入口；注册表不能再仅靠状态文字把
家族标成完成，所有导出变体必须实际编译并逐字段通过门禁。

自动施法的“支持切换”和“默认开启”也已拆开：前者读取 Ability JSON 的
`Orderon/Orderoff`，后者读取所属 Unit JSON 的 `defaultActiveAbility`。匹配同时接受精确
rawcode 与 baseCode，因此 `Afa2`/`Afae` 这类变体不需要特判；当前门禁确认牧师只默认
开启治疗、女巫默认开启减速、农民修理默认关闭。

第二个跨种族批次把命中修正拆成三条互不混用的内容契约：Deflect 只处理配置指定的
Pierce/Magic 偏转；Evasion 处理普通武器攻击的目标闪避；Critical Strike 处理攻击者
proc、倍率、附伤和该次 proc 的 Never Miss。所有概率来自 level Data，使用稳定的
tick、攻击者、目标、弹道 ID 和独立 salt 判定，所以热载和回放不会重新抽取结果。

`Arsk` 本身没有 Object Editor 数值字段。适配器把它编译为受同级 requirement 控制的
`AbilityStatusFlags.Resistant` Aura：目标校验对“仅非英雄”法术按英雄排除规则处理，
Buff 时长则选择同一 Ability JSON 的 `HeroDuration`。`Avul` 同样从能力绑定生成常驻 Aura；
建筑通过类型绑定查询被动状态，不需要把建筑伪装成单位或维护 rawcode 列表。
