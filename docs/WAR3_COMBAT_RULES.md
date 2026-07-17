# Warcraft III 基础攻防与普通武器规则

## 数据来源

本阶段以项目导出的经典 1.27a 数据为唯一平衡输入：

- `assets/warcraft3/classic/data/unit_editor_data/units/**`：单位/建筑护甲类型、
  两槽攻击、最小射程、区域半径、目标层和升级列表。
- `assets/warcraft3/classic/data/upgrade_editor_data/upgrades/human`：`Rhme`、`Rhar`、
  `Rhla`、`Rhra`、`Rhhb` 的逐级名称、费用、时间、图标、效果和前置。
- `ability_editor_data/abilities/human/Asth.json` 与 UnitWeapons 原始字段：特殊
  武器种类、Damage Loss、Spill Distance/Radius、Maximum Targets 和风暴战锤门槛。
- 源资产工作区 `D:\Godot\war3_assets\extracted\raw\classic\patch\Units\MiscGame.txt`：
  `DefenseArmor=0.06` 与七类攻击对八类护甲的精确倍率表。

核心模拟不读取这些路径或 rawcode。`War3GameplayDataAdapter` 在组合阶段把数据编译成
内容中立的 `CombatProfileSnapshot`、`CombatWeaponProfileSnapshot` 和
`BuildingTypeProfile`；运行时、AI、回放和 Godot 表现只消费这些不可变 profile。

## 伤害顺序

普通攻击的单次伤害按以下顺序计算：

1. 基础平均攻击加当前武器科技等级乘攻击升级增量。
2. 若命中原有 `BonusVs` 标签，再加标签伤害与对应升级增量。
3. 乘攻击类型对目标护甲类型的矩阵倍率。
4. 除 `Spells` 外，正护甲使用 `damage / (1 + armor × 0.06)`；负护甲使用
   `damage × (2 - 0.94 ^ -armor)`；`Spells` 只消费类型倍率，不消费数值护甲。
5. 多次攻击按稳定顺序逐次扣血，并以当前可用生命截断事件中的实际伤害。

矩阵列顺序为 Small、Medium、Large、Fortified、Normal、Hero、Divine、None：

| 攻击 | Small | Medium | Large | Fortified | Normal | Hero | Divine | None |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Normal | 1.00 | 1.50 | 1.00 | 0.70 | 1.00 | 1.00 | 0.05 | 1.00 |
| Pierce | 2.00 | 0.75 | 1.00 | 0.35 | 1.00 | 0.50 | 0.05 | 1.50 |
| Siege | 1.00 | 0.50 | 1.00 | 1.50 | 1.00 | 0.50 | 0.05 | 1.50 |
| Magic | 1.25 | 0.75 | 2.00 | 0.35 | 1.00 | 0.50 | 0.05 | 1.00 |
| Chaos | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| Spells | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 0.70 | 0.05 | 1.00 |
| Hero | 1.00 | 1.00 | 1.00 | 0.50 | 1.00 | 1.00 | 0.05 | 1.00 |

非 War3 Demo 内容默认使用 `Legacy` 攻击与护甲类型，继续执行旧的平减护甲和 0.5
最低伤害规则，因此本阶段没有静默改变已有测试关卡的平衡。

## 最小射程

武器保存最大与最小射程。距离使用攻击者/目标身体半径后的接触距离：

- 大于最大射程：继续原追击逻辑。
- 位于合法射程环：停止并进入 Windup/Fire/Cooldown。
- 小于最小射程：清除本次 Windup，保持原攻击目标，向目标反方向发出独立
  GroundPoint 后撤移动；离开盲区后同一订单自动恢复攻击。
- Hold：不离开原地，解除本次过近目标。

后撤使用独立移动目标种类，不能复用“追击目标身体”的到达半径；否则最大射程会让
单位误判自己已经到达而永远不移动。

## 三段区域伤害

`CombatWeaponAreaSnapshot` 保存 Full/Half/Quarter 三个有序半径和目标层：

- `distance <= full`：100%。
- `full < distance <= half`：50%。
- `half < distance <= quarter`：25%。
- 之外：0。

直接目标只结算一次。命中点随后按稳定单位 ID、稳定建筑 ID 扫描其他敌对目标；
每个目标重新消费自身护甲类型、有效护甲和标签，不复制主目标最终伤害。区域攻击保持
原武器科技等级与攻击类型，但清空子伤害的 Area，避免递归溅射。弹道会把完整区域
快照写入运行时实例，所以移动目标的实际命中点、热恢复和回放结果一致。

当前区域目标层支持 GroundUnit、AirUnit、Building、Tree、Debris、Item、Wall、Ward。
目标层只描述武器可命中的类别，不决定实体归属：Ward 仍是带 `AbilityUnitTraits.Ward`
的单位；Tree/Debris/Item/Wall 进入独立 `CombatObjectStore`。直接攻击、三段区域伤害、
Line/Bounce 传播均复用同一目标层过滤和稳定 ID 顺序。

## 非单位普通武器目标

`CombatObjectStore` 是内容中立的可破坏物边界，profile 保存稳定 ID、种类、矩形边界、
生命、护甲、护甲类型、属性、可选所有者、资源节点链接和动态导航 footprint 链接。
它没有 War3 rawcode，也不复用 Construction 的生产、建造或升级状态。

- Tree：正式 War3 地图中的 280 棵树逐一链接既有 Economy Resource Node；采伐和普通
  武器共享同一剩余量，任一路径归零都会同步使另一侧死亡。
- Ward：仍走 UnitStore/Ability trait，因此单位可见性、阵营关系、死亡和技能逻辑保持
  原有语义；它不会同时出现在 CombatObjectStore。
- Debris/Item/Wall：运行时类型和序列化协议已就绪；当前 Lordaeron Crossroads 没有
  这些实际地图实例，因此不会为了覆盖率生成伪实体。
- 树木不再写进不可变静态导航障碍。场景初始化时为每棵树建立动态 footprint；被攻击或
  采伐到 0 时派生移除 footprint、局部刷新路线并唤醒被导航阻挡的单位。该派生移除不写
  第二条 World Command，回放由原攻击/采集命令重算，避免同一 footprint 被删除两次。

显式 `AttackObject` 订单沿用命令结构中的通用资源/对象 ID 槽位，但具有独立 Order Kind；
命令合同、队列、玩家权限、热恢复和回放都会校验对象 ID。War3 的 Attack 目标模式点击
树木时下达此订单；普通右键树木仍保持农民采集、非农民移动的 Smart Command 语义。

## 直线传播与弹射

全量 837 个对象、644 个启用攻击槽的审计结果如下：

| 原始武器种类 | 攻击槽数 | 运行时语义 |
| --- | ---: | --- |
| normal | 311 | 直接命中 |
| missile | 241 | 单目标弹道 |
| msplash | 71 | 三段区域弹道 |
| artillery | 11 | 三段区域炮击 |
| instant | 6 | 立即命中 |
| mbounce | 2 | 稳定最近目标弹射 |
| mline | 1 | 主目标之后的直线传播 |
| aline | 1 | 当前数据以三段区域为主，Spill Distance 为 0 |

特殊基础对象只有 `hgry` 狮鹫骑士、`esen` 女猎手、`ensh` 娜萨和 `ebal` 投刃车。
通用 profile 不保存 rawcode，而保存 `Line/Bounce`、距离、半径、伤害损失、最大目标数、
目标层和可选距离科技。

- Line：从攻击者到主目标确定方向，只扫描主目标之后、`LineDistance` 长且
  `Radius` 宽的走廊；按纵向距离、目标种类和稳定 ID 排序，主目标不重复结算。
- Bounce：每一跳从上一个命中点选择半径内最近且未命中的合法敌对目标；距离相同按
  目标种类和稳定 ID 决胜。
- 第 N 个次级目标使用 `(1 - DamageLossFactor)^N`，每个目标仍独立结算自身护甲。
- 次级伤害清空 Area 与 Propagation，不能再产生递归溅射或指数弹射。

`hgry` 的基础 `mline` 保存 Spill Radius 50 和 Damage Loss 0.2，但 Spill Distance
为 0。适配器沿 `Asth -> Requires Rhhb -> Rhhb effect rasd=200` 的导出关系，把科技
13 编译为每级 200 原始距离；未研究时没有次级命中，研究风暴战锤后才启用直线传播。

## 确定性朝向与攻击起手

`MiscData.txt` 的全局 `AttackHalfAngle=0.5` 是单位朝向/起手约束，不是表现层字段。
`UnitStore` 现在保存当前/上一 tick 朝向和每秒转身弧度；移动单位朝速度方向转身，已锁定
攻击目标的单位优先朝单位中心或建筑中心转身。Godot 表现器只插值权威朝向，不再根据
动画或目标位置瞬间改写模型 Rotation。

经典对象编辑器 `TurnRate` 按每个约 0.03 秒内部帧的弧度解释，适配器因此编译为
`TurnRate / 0.03` rad/s；例如 Footman 的 0.6 编译为 20 rad/s。单位进入合法射程后：

1. 已在 Windup 的攻击继续完成，不因目标在起手后横移而被朝向窗口反复取消。
2. Cooldown 仍按原规则推进，同时继续朝当前目标转身。
3. 只有 Cooldown 为零且朝向误差不超过 `AttackHalfAngle` 才发布 `AttackStarted`，进入
   Windup/Fire；未转正时停止平移，不播放伪造的攻击起手，也不产生伤害或弹道。

非 War3 profile 默认使用 `pi` 半角和高速兼容转向，既有 Demo 不会因新增字段改变起手
节奏。War3 profile 明确写入 0.5 半角和原始单位 TurnRate。

## 科技归属

科技不是全军共享的单一“武器等级”。适配器读取每个单位的 `summary.upgrades`：

- `Rhme`：人族近战武器，稠密 ID 0。
- `Rhar`：人族铁甲，稠密 ID 1。
- `Rhla`：人族镶皮甲，追加稠密 ID 15。
- `Rhra`：人族远程武器，追加稠密 ID 16。

每个激活武器保存自己的伤害科技 ID；每个单位保存自己的护甲科技 ID 和每级增量。
飞行机器/蒸汽机车切换武器槽时会同时切换科技归属。英雄、农民和不在对应升级列表
中的单位不会因为玩家研究了无关科技而获得数值。

## 持久化版本

本阶段改变了确定性协议，版本如下：

- Building Type Catalog 3；Building Upgrade Catalog 2。
- Gameplay Profile Catalog 2；Production Catalog 12；Production Command Log 15。
- Ability Catalog 17；Simulation Command Log 9。
- State Hash 40；Replay Package 41；Hot Snapshot 42。

攻击/护甲类型、单位护甲科技、逐武器伤害科技、最小射程、区域半径/目标层、传播
类型/参数/距离科技、Facing/PreviousFacing/TurnRate/AttackHalfAngle，以及飞行中弹道
携带的攻击类型/区域/传播快照，以及 Combat Object profile/生命/资源链接/动态 footprint、
对象攻击订单和对象弹道都进入规范字节、状态哈希和二进制恢复校验。

## 验证

```powershell
dotnet build .\rts-demo-1.csproj --no-restore

D:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . demo/2d/RtsDemo2D.tscn -- `
  --war3-combat-rules-self-test

D:\Godot\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . war3_rts/War3Rts.tscn -- --war3-rts-smoke
```

专项测试固定验证 Normal→Medium、Pierce→Small、Siege→Fortified、Spells→Hero，
并验证 Footman/Rifleman/Mortar/Guard Tower/Barracks 的原始映射、两级铁甲后的有效护甲、
迫击炮三段伤害、最小射程后撤再开火、负 5 护甲、Line/Bounce 稳定传播、风暴战锤
研究前后对照、背对目标不能瞬间开火/转正后才能起手、原始 TurnRate 换算、朝向热恢复，
以及 Tree/Wall 目标层过滤、树木范围伤害、Ward 单位层、资源生命同步、导航 footprint
移除、对象事件、对象弹道/热恢复/完整回放和非默认 Production/Godot Resource 二进制往返。
