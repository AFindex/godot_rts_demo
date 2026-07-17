# Warcraft III 技能运行时接入

## 结果与数据源

当前可玩的人族内容已接入数据驱动技能运行时。启动日志中的
`WAR3_ABILITY_DATA` 会报告实际导入数量、单位绑定数量、缺失 rawcode
和稳定哈希；当前基线为：

```text
ability_runtime=43/43 bindings=16 families=42 prototype=34 missing=0 unclassified=0
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
   持续时间、范围、目标过滤和 Data A/B/C/D，并转换为内容中立配置。
2. `AbilityCatalogSnapshot` 保存不可变技能目录、等级效果和单位类型绑定，
   同时生成稳定二进制与哈希。
3. `AbilitySystem` 是固定帧权威层，负责法力、冷却、施法/引导、自动施法、
   Buff、光环、状态、召唤物、显隐和技能事件。
4. `War3WorldPresenter` 与 `War3RtsHud` 只消费只读快照和事件，负责按钮、
   法力条、冷却、目标指示、动画和原始特效模型。

核心模拟不引用 War3 路径或 raw JSON。以后接入其他种族时，只需增加内容适配，
不需要把种族规则写进移动、战斗或 HUD。

## 已支持的玩法语义

- 单位/点/自身/建筑目标，友军、敌军、中立、存活、死亡、空中、地面、
  生物、机械、英雄、非英雄、无敌和可伤害过滤。
- 法力消耗与恢复、冷却、施法前摇、引导、周期触发和中断。
- 治疗、伤害、法力吸取、驱散、Buff 转移、控制权转移、传送、复活、
  召唤和区域视野揭示。
- 眩晕、无敌、魔免、隐形、变形、放逐、禁攻和禁移。
- 移速、攻击间隔、攻击力、护甲、最大生命、回魔和侦测范围修正。
- 被动光环和攻击命中触发；随机触发由事件序号确定，不使用非确定随机数。
- 治疗、心灵之火和减速等默认自动施法，按稳定单位 ID 选择第一个合法目标。
- 水元素和凤凰生命周期；召唤物保留 rawcode，并使用对应导出模型、肖像、
  图标和投射物表现。
- 每等级科技/建筑前置进入不可变技能目录；施法、被动光环和攻击触发都会在
  确定性模拟中检查前置，未满足时返回 `RequirementsNotMet`。
- 英雄技能初始等级为 0；英雄以等级 1、技能点 1 出生，通过学习命令校验
  `RequiredHeroLevel`、`HeroLevelSkip`、技能点和最高等级后升级技能。

当前人族的普通单位技能与四名英雄技能栏均从导出的 `abilities` 和
`heroAbilities` 绑定，不再维护英雄 rawcode 手写补表。采集、修理、
建造、物品栏、集结点等 Warcraft “能力对象”属于经济、建造、物品或命令模块，
不会在技能运行时重复实现；纯科技标记或纯模型挂件会保留在目录中，但不伪造
伤害数值。

## UI 与操作

选择具备主动技能的单位后，命令卡会在空槽显示技能按钮。按钮提供：

- 中文名称、导出说明、等级、法力、冷却和施法距离；
- 法力不足或冷却期间禁用；
- 剩余冷却数字和 Toggle 状态标记；
- 瞬发/开关技能直接执行，单位技能进入选目标模式，点技能进入地面选点模式。

肖像下方会显示蓝色法力条；选择信息会显示施法中状态、控制状态与活动 Buff。
施法者播放 Spell 系列动画，技能事件按 Caster/Target/Effect/Missile art 字段
生成原始 War3 特效。表现层只读事件，因此不会改变模拟结果。

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
```

外部模块应通过 `RtsSimulation` 的三个 `Issue*` 入口下达施法、学习和自动施法
开关，以便所有权、比赛状态和命令录制统一生效。`TrySetLevel`、
`TrySetAutoCast` 与 `TryAdvanceHeroLevel` 只用于受信任的初始化/英雄经验模块，
不应由 HUD 直接调用。

## 存档、回放和验证

技能目录和运行状态已进入：

- 热快照及其二进制编解码；
- 模拟状态哈希；
- 回放初始 manifest 和施法命令日志；
- 运行时事件流（事件可由回放重建，不写入快照）。

专项测试命令：

```powershell
godot --headless --path . -- --ability-self-test
godot --headless --path . -- --war3-ability-data-self-test
godot --headless --path . -- --generate-war3-ability-runtime-coverage
godot --headless --path . war3_rts/War3Rts.tscn -- --war3-rts-smoke
```

第一项验证自动治疗、伤害、无敌阻伤/到期、事件生命周期与热快照哈希；第二项会
在真实地图里施放水元素和雷霆一击，并验证 `ability_integration=True`、召唤物
rawcode、动画及技能特效消费链。

## 扩展其他种族

导出目录共索引 801 个技能对象；461 个单位引用和 285 个行为家族记录在数据覆盖
manifest 中。机器生成的运行时报告当前确认：42 个已分类家族覆盖 102 条 rawcode；
单位引用家族仍有 243 个未分类。当前人族运行时编译 43 项、42 个家族，其中
34 个家族有玩法原型编译器。当前 15 条有科技门槛的技能使用 12 个科技对象，
均已映射到 15 项运行时人族科技目录；生产建筑可以同时承担训练和研究职责。
报告把“已分类”“有原型”“已完成”分开统计，
禁止用成功加载冒充语义完成。

原始目标列表共有 27 种 token，已全部解析和保留。树木、残骸、守卫、古树、
物品等 10 种仍缺少运行时实体/特征语义，影响 119 条全量技能、当前人族 12 条；
这些缺口逐条写入覆盖报告，不会再被当作普通单位目标静默放宽。

接入兽族、亡灵、暗夜精灵或中立单位时，应增加共享 `baseCode` 行为家族并复用同一
`AbilitySystem`。遇到变身、尸体召唤、物品栏或地形永久修改等新语义时，应新增
内容中立 effect kind 和世界接口，避免在核心层判断具体 rawcode。
