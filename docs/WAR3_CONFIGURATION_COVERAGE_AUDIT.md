# Warcraft III 道具、技能与建筑配置接入审计

审计日期：2026-07-18。结论先行：当前不能称为“全部接入完成”。数据导出层已经能
完整索引原始对象，但玩法编译、建筑能力和表现阶段仍只覆盖子集。运行时必须区分
“JSON 可读取”和“行为/表现已验收”，不能再用 `missing=0` 代替完成度。

## 1. 可量化覆盖

| 范围 | 原始对象/家族 | 已分类 | 当前结论 |
| --- | ---: | ---: | --- |
| Ability 全量 | 801 / 415 | 137 / 53 | 664 个 rawcode 尚未分类 |
| 单位实际引用 | 461 / 285 | 76 / 45 | 240 个引用家族尚未分类 |
| Item Ability | 234 / 129 | 45 / 20 | 189 个物品技能尚未分类 |
| 当前人族运行时 | 47 / 45 | 47 / 45 | 34 项仍为 `blocked` |
| Item 对象 | 273 | 273 已导出 | 仅藏宝室 9 件已接玩法 |
| 当前人族建筑 Ability 绑定 | 原始多类 | 6 个建筑 / 9 个槽位 | 战斗号召、魔法岗哨、反馈、显示已进入配置绑定 |

权威机器可读报告是 `reports/war3_ability_runtime_coverage.json`。其中
`implemented_gameplay`、`delegated`、`presentation_only`、`blocked` 和
`unclassified` 是不同状态；只有读取成功不提升状态。

## 2. 本轮已经去除的硬编码

### 2.1 物品目录与数值

新增 `item_editor_data/manifest.json` 和 273 个一物品一 JSON 文件。导出源按
`roc -> tft -> tft-locale -> patch` 合并：

- `ItemData.slk`
- `ItemFunc.txt`
- `ItemStrings.txt`
- 物品引用的 `ability_editor_data`

神秘藏宝室不再维护 C# 商品表。运行时先读取 `hvlt.Makeitems` 决定商品及顺序，
再从 Item JSON 读取名称、图标、按钮坐标、价格、库存、补货时间、充能、是否消耗、
科技/主城前置和冷却组，最后从 Ability JSON 读取施法时间、持续时间、范围、区域、
`DataA..I`、目标标签和创建单位。审计由此发现旧表把 `mcri` 与 `plcl` 的原始顺序
写反，现已按配置纠正。

已接入玩法的 9 件藏宝室物品仍只是 273 件物品中的子集。地面掉落、拾取、主动丢弃、
出售、随机掉落表和其余 190 个未分类 Item Ability 尚未完成，因此不能宣称“所有道具
均已接入”。

### 2.2 建筑升级模型与动画

哨塔、防御塔、炮塔和神秘之塔共用
`Buildings\\Human\\HumanTower\\HumanTower.mdx`。原始模型变体不是四条模型路径，
而由 `UnitFunc.Animprops` 选择：

- `hwtw`：无属性，基础哨塔；
- `hgtw`：`upgrade,first`；
- `hctw`：`upgrade,second`；
- `hatw`：`upgrade,third`。

旧适配器丢弃了 `Animprops`，世界表现又只按 Keep/Castle 分支，所以升级过程和完成后
都会退回基础塔。现已将该字段贯通到 `War3BuildingDefinition`，并由
`War3AnimationPropertyResolver` 生成升级、待机、工作、攻击候选和建筑肖像候选，
不判断建筑 rawcode。

阶段契约如下：

1. `Completed(source)`：唯一视觉所有者是现有 `BuildingVisual.Actor`；
2. `Upgrading(target)`：同一 Actor 读取目标建筑的 `Animprops`，由权威升级进度驱动
   `Birth Upgrade ...`，不创建第二套塔模型；
3. `Completion handoff`：模拟先切换 BuildingType，表现读取目标定义；模型路径相同则
   保留 Actor，只切换序列，路径不同才加载一次新模型；
4. `Completed(target)`：待机/工作/肖像继续使用目标 `Animprops`；
5. `Destroyed/interrupted`：建筑生命周期接管死亡表现，升级阶段不再拥有视觉。

这消除了“逻辑已升级、视觉仍是哨塔”和重复模型闪现。当前自动检查覆盖 first/second/
third 配置、目标序列候选和建筑肖像候选。

### 2.3 技能数值、召唤对象与法力

受支持技能编译器不再在 JSON 缺字段时偷偷套用某个经典单位的数值。治疗量、伤害、
波数、减速倍率、召唤数量等必需字段均读取 `summary.levels[].data`；缺失或非法时，导入
自检以 `InvalidDataException` 失败。召唤物 rawcode 只读 `summonedUnitId`，生命、攻击、
移速、碰撞和视野再从该单位 JSON 读取。持续时间为 0 的凤凰按永久召唤处理，不再用
代码中的 60 秒替代。

单位和建筑法力读取各自 Unit JSON 的 effective/maximum、initial 和 regeneration；已删除
按英雄 rawcode 补最大法力和按英雄身份强改初始法力的分支。演示场景的完整性判断也由
固定“44 个技能”改为 `Catalog.Count == RequestedCount`。

物品栏不再按英雄身份返回 6 格，也不再在 HUD 判断人族 `Rhpm` 或保留“两格背包”
常量。`War3InventoryDataAdapter` 按 `AInv` 行为家族读取 Ability JSON：DataA 是容量，
DataB..E 分别是死亡掉落、可使用、可取得、可丢弃；科技门槛使用同一 Ability requirement
编译结果。由此英雄 `AInv` 的 6/0/1/1/1 与普通单位 `Aihn` 的 2/1/0/1/1 都是数据结果。
商店购买检查 `CanGetItems`，物品按钮和运行时检查 `CanUseItems`，非法字段不再被 clamp
或静默修正，而会让内容导入失败。地面物品实体、主动拾取/丢弃、死亡掉落及其回放/
热快照仍未完成，所以 `AInv` 家族继续诚实标记为 blocked。

这里保留的代码映射只有“baseCode 对应哪一种通用行为编译器”，例如 Heal、Flare、
Summon。它决定怎样解释 JSON 字段，不保存任何单位/技能的内容数值。

## 3. 技能表现审计与本轮修复

旧表现适配把 Ability、Buff、Effect、Missile 的模型合并成四个平面数组，导致：

- `Animnames` 被忽略，所有施法都尝试通用 `Spell`；
- BuffArt 同时在命中时生成、又按持续 Buff 生成；
- MissileArt 在 Impact 点作为爆炸瞬间生成；
- attachment point/count、循环音效的来源关系丢失。

现已完成：

- 保留并解析 `Animnames`，例如暴风雪使用 `Stand Channel`，风暴之锤使用
  `Spell Throw`；引导期间不会再被通用动作覆盖；
- Ability 的 Caster/Target/Effect、持续 BuffArt 和 MissileArt 分开持有；
- 持续 Buff 只由 Buff 实例生命周期拥有；
- `Started -> Released -> ProjectileFlight -> Impact -> Ended/Interrupted` 已成为权威事件
  生命周期；投射物位置、速度、自导目标和实例 id 进入状态、哈希与热快照；
- MissileArt 由 ProjectileFlight 独占，JSON `Missilespeed/Missilearc/MissileHoming` 驱动
  移动与弧线，不再伪装成 Impact 特效；
- caster/target/buff attachment 和 count 已进入表现消费。`sprite,first` 被保留为一条
  复合路径，`Targetattach1..5` 按声明顺序展开；overhead/head/chest/hand/foot/origin 和
  sprite 序号根据宿主模型实际高度定位，不再全部叠在地面原点；
- effect sound 字段进入表现定义，后续不会再因适配丢字段。

尚未完成且不能隐藏的部分：

- 当前挂点解释器能给出稳定的语义位置，但尚未把每条路径绑定到导出 GLB 的精确
  Skeleton3D bone transform；需要逐模型保留 MDX attachment-object 到 glTF bone 的映射，
  才能让手持、坐骑和多 sprite 挂件达到像素级一致；
- 循环 EffectSound 尚未和 Buff/引导实例做一对一清理；
- 34 个当前人族 Ability 仍只有阻塞/委托/表现状态，不能因按钮出现而视为完成。

## 4. 建筑武器与被动能力（2026-07-18 第二轮）

此前建筑适配器只读取生命、护甲和占地，同一份 Unit JSON 中的攻击、视野和 Ability
被直接丢弃。现已增加 Godot 无关的 `BuildingCombatProfileSnapshot` 与独立
`BuildingCombatSystem`，不使用塔 rawcode 分支，也不把建筑伪装成负数 unit id。

建筑攻击阶段契约：

1. `Idle/Search`：建筑战斗系统拥有目标搜索，按 JSON 的 acquisition、target layers、
   minimum range 与科技门槛选武器；
2. `Windup`：同一 BuildingVisual 读取武器 DamagePoint，并用建筑 `Animprops` 解析攻击
   序列，不生成第二份塔模型；
3. `ProjectileFlight`：独立建筑投射物拥有 Missile 视觉，保存发射时的伤害和 on-hit
   快照；建筑 Actor 回到 cooldown，不继续拥有导弹；
4. `Impact`：只应用一次类型伤害、区域伤害和 on-hit，再发布表现事件；投射物视觉在
   命中交接后销毁；
5. `Upgrade/Destroy interruption`：建筑攻击状态被清空；已经发射的投射物保存发射时
   配置，目标失效则明确发布 expired。

已由 JSON 接入并验收：

- `hgtw` 防御塔：25 穿刺伤害、700 范围、0.9 秒周期、0.3 秒前摇和
  `GuardTowerMissile`；
- `hctw` 炮塔：两组目标武器、攻城伤害及 50/100/125 区域衰减；
- `hatw` 神秘之塔：9 点攻击与 `OrbOfDeathMissile`；
- `Adts` 魔法岗哨：900 侦测范围和 `Rhse` 科技门槛进入建筑感知 profile；
- `Afbt` 反馈：普通单位 24、英雄 12 的法力燃烧上限、倍率及召唤物伤害进入通用
  attack-hit effect；
- 建筑武器、前摇、冷却、目标、投射物和 on-hit 都进入状态哈希、热快照、施工命令、
  建筑升级及生产重放的二进制格式。

自动验证包含：塔实际攻击、前摇/发射/命中阶段、单次 Impact、投射物中途热快照恢复、
炮塔区域配置，以及神秘之塔命中女巫后法力与生命同时下降。不能再用“信息面板有攻击
数值”冒充运行时接入。

## 5. 建筑主动 Ability（本轮完成）与剩余委托

人族建筑原始配置还包含 `Abds/Abdl`、`Argl`、`Arlm`、`Aall/Apit`、`Adts`、
`Afbt`、`AHta` 等对象。建筑 Ability 现在按具体建筑实例维护 JSON 法力、回复、技能槽和
冷却；Preview/Issue 共用单位技能的目标、距离、科技、法力和冷却校验，命令面板显示
权威禁用原因、剩余冷却和法力。点目标建筑技能也使用统一目标模式、范围圈和区域圈。

神秘之塔 `AHta` 不由 rawcode 分支实现：行为注册表把其 baseCode `AIta` 编译为通用
Flare/Reveal，范围 900、持续 15 秒、冷却 180 秒、无限施法距离和 `Rhse` 前置都读取
JSON。Reveal/Detect 状态、建筑法力/冷却和目标进入状态哈希、命令回放及热快照；测试
覆盖前置失败、施放、生效、隐形探测、过期、冷却拒绝以及中途快照恢复。

`Abds/Abdl`、`Argl`、`Arlm`、`Aall/Apit` 中部分属于商店、集结点、资源交回或系统
委托。模块本身已有实现，但仍需逐 baseCode 建立“配置对象 -> 委托模块 -> 自动测试”
的覆盖证据，不能简单标成玩法完成。

## 6. 验收门槛

- `dotnet build rts-demo-1.csproj`：0 warning / 0 error；
- `--war3-human-ui-self-test`：要求 towerAnimations、abilityPresentation、items 全部 true；
- `--war3-combat-rules-self-test`：要求 building 与 feedback 的 staged/hot/hit 全部 true；
- `--ability-self-test`：要求 projectile snapshot 与 `permanent_summon=True`；
- `--war3-ability-data-self-test`：要求 `mana_json=True`、`summon_json=True`、
  `attachments_json=True`；
- `--war3-rts-smoke`：要求 `success=True`、`data_integration=True`、
  `ability_integration=True`、`shop=True`、`item_use=True`；
- 覆盖生成器：801/415、461/285、234/129 的数量不得回退；
- 塔升级视觉后续还需增加 first/second/third 的逐帧截图门禁；
- 精确骨骼挂点和循环音效所有权未完成前，相应表现条目不得标为最终验收。
