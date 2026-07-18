# Warcraft III 道具、技能与建筑配置接入审计

审计日期：2026-07-18。结论先行：当前不能称为“全部接入完成”。数据导出层已经能
完整索引原始对象，但玩法编译、建筑能力和表现阶段仍只覆盖子集。运行时必须区分
“JSON 可读取”和“行为/表现已验收”，不能再用 `missing=0` 代替完成度。

## 1. 可量化覆盖

| 范围 | 原始对象/家族 | 已分类 | 当前结论 |
| --- | ---: | ---: | --- |
| Ability 全量 | 801 / 415 | 134 / 51 | 667 个 rawcode 尚未分类 |
| 单位实际引用 | 461 / 285 | 74 / 43 | 242 个引用家族尚未分类 |
| Item Ability | 234 / 129 | 44 / 19 | 190 个物品技能尚未分类 |
| 当前人族运行时 | 44 / 43 | 44 / 43 | 33 项仍为 `blocked` |
| Item 对象 | 273 | 273 已导出 | 仅藏宝室 9 件已接玩法 |
| 当前人族建筑 Ability 绑定 | 原始多类 | 2 | 目前只有主城/城堡的战斗号召 |

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
- MissileArt 不再伪装成 Impact 特效；
- caster/target/buff attachment、count 和 effect sound 字段进入表现定义，后续不会再
  因适配丢字段。

尚未完成且不能隐藏的部分：

- Ability 事件流没有独立 `Release/ProjectileFlight` 阶段，真实导弹速度、弧度、自导和
  命中交接尚未实现；
- attachment 配置已保留，但外部模型还没有绑定到 MDX 骨骼/attachment transform，
  血魔法师球体等 `sprite,first` 挂件仍未达到最终正确性；
- 循环 EffectSound 尚未和 Buff/引导实例做一对一清理；
- 33 个当前人族 Ability 仍只有原型或阻塞状态，不能因按钮出现而视为完成。

## 4. 建筑 Ability 的实际缺口

人族建筑原始配置还包含 `Abds/Abdl`、`Argl`、`Arlm`、`Aall/Apit`、`Adts`、
`Afbt`、`AHta` 等对象。当前只有 `Amic` 战斗号召进入 AbilitySystem 的建筑绑定。
其中集结点、资源交回和商店已由独立模块承载，但缺少 rawcode 到委托测试的覆盖链接；
塔的侦测、反馈伤害和神秘之塔 Reveal 仍未完整进入建筑运行时。建筑武器也尚未由
通用 CombatStore 作为攻击者驱动，因此“塔能显示攻击数值”不等于塔的攻击行为已完成。

下一阶段必须先增加建筑能力/武器的确定性状态（冷却、目标、科技门槛、攻击事件和
快照/哈希），再开放命令按钮，不能只把原始 rawcode 塞进 command card。

## 5. 验收门槛

- `dotnet build rts-demo-1.csproj`：0 warning / 0 error；
- `--war3-human-ui-self-test`：要求 towerAnimations、abilityPresentation、items 全部 true；
- `--war3-rts-smoke`：要求 `success=True`、`data_integration=True`、
  `ability_integration=True`、`shop=True`、`item_use=True`；
- 覆盖生成器：801/415、461/285、234/129 的数量不得回退；
- 塔升级视觉后续还需增加 first/second/third 的逐帧截图门禁；
- Missile/attachment 未完成前，相应条目不得标为 `implemented`。
