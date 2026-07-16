# Warcraft III 编辑器数据运行时接入

## 目标与数据路径

玩法数据来自以下三个只读目录：

- `res://assets/warcraft3/classic/data/unit_editor_data`：837 个单位/建筑对象。
- `res://assets/warcraft3/classic/data/ability_editor_data`：801 个技能对象。
- `res://assets/warcraft3/classic/data/upgrade_editor_data`：89 个升级对象。

目录中的 `manifest.json` 建立 837 个对象的索引，每个对象的完整数据按 `units/{race}/{category}/{id}.json` 独立保存。运行时先读取 manifest，只有在某个对象真正被玩法或工具查询时才读取其 JSON。

## 分层

接入刻意分成三层：

1. `War3UnitDataCatalog` 与 `War3ObjectDataCatalog` 是只读数据仓库。它们负责 schema 校验、安全路径解析、大小写敏感的四字符 object id 索引、按需加载与缓存，并保留完整原始字段。它们不依赖 Godot 节点、战斗系统或 UI。
2. `War3GameplayDataAdapter` 与 `War3TechnologyDataAdapter` 是换算与语义适配层。它们只把当前通用模块能表达的字段转换为 `UnitTypeProfile`、`BuildingTypeProfile`、`ProductionRecipeProfile`、`TechnologyProfile` 和美术定义。
3. `War3HumanContent` 是组合层。它维持原有稠密 `TypeId`，装配数据仓库与适配器，再把快照交给现有移动、战斗、经济、建造、生产、AI、回放、HUD 和表现模块。

核心模拟因此不会读取 JSON，也不会把 `hfoo`、`Hamg` 等 object id 写入模拟状态。回放、状态哈希、存档与 AI 继续只看到稳定的数值 profile 和稠密 ID。

## 已接入字段

| 运行时模块 | 导出字段 | 处理方式 |
| --- | --- | --- |
| 名称/UI | `displayName`、`level`、攻击/护甲类型 | 使用中文名称、对象等级及本地化的 War3 类型标签 |
| 模型/肖像/图标 | `assets.model`、`assets.portrait`、`assets.icon` | 解析为现有导出资产虚拟路径；缺失时用内置映射 |
| 飞行表现 | `summary.movement.flyingHeight` | 只用于 3D 模型展示高度 |
| 移动 | `speed`、`collisionSize` | 经集中缩放策略转换为模拟速度、物理半径和导航净空 |
| 生命/护甲 | `hitPoints.effective`、`armor.effective` | 直接进入战斗 profile；英雄使用已经折算属性后的有效生命/护甲 |
| 攻击 | 首个 `combat.attacks[].enabled` | 接入平均伤害、射程、警戒范围、攻击间隔、伤害点与武器类型 |
| 英雄攻击 | `heroAttributes.primary` 及对应初始属性 | 在骰子平均值上加入主属性，避免只得到裸武器骰子伤害 |
| 弹道 | `assets.missile`、原始 `Missilespeed` | 导弹/炮弹武器创建模拟弹道并驱动现有特效表现；instant 武器保持即时命中 |
| 单位生产 | `cost.gold/lumber/foodUsed/buildTime` | 接入经济扣费、人口与训练队列 |
| 建筑 | `cost`、`buildTime`、`hitPoints`、`foodProduced`、`armor` | 接入建造、经济、生命、人口与护甲 |
| 单位视野 | `summary.sight.day`、移动类型 | 昼间视野经世界尺度换算进入通用 `UnitPerceptionProfileSnapshot`；飞行单位使用 Elevated 地形观察模式 |
| 特殊美术 | `assets.specialEffect` | 通过现有 transient effect 路径在单位/建筑死亡或取消时播放（资源存在时） |
| 升级增量 | `armor.upgradeAmount`、攻击升级增量 | 接入 profile 的升级增量；导出为空时保留项目回退值 |
| 科技/UI | Upgrade 逐级名称、说明、图标、费用、时间、最大等级 | `Rhme`、`Rhar`、`Rhac` 映射为现有三项研究；命令卡显示原版中文数据 |
| 技能查询/UI | Ability 名称及单位 `abilities` 引用 | 选择面板显示当前可玩单位的技能摘要；完整逐级参数可经 catalog 查询 |
| 生产/研究队列 UI | `ProductionQueueSnapshot`、`ResearchQueueSnapshot` | 统一适配为 `War3QueueItemSnapshot`；显示原版队列边框、项目图标、实时进度、状态和退款，并按真实订单 ID 取消 |

`summary` 是游戏运行时的稳定契约；`editor.*` 仍可通过 `War3HumanContent.DataCatalog.TryGetEditorValue(...)` 查询，供未来技能、科技和编辑器模块使用。

## 尺度策略

策略集中在 `War3GameplayImportPolicy`，默认值为：

- 世界距离：`4 / 15`，用于碰撞半径、攻击射程、警戒范围和弹道速度。
- 移动速度：`4 / 9`，用于把 War3 移速转换到当前战场节奏。
- 加速度：转换后最大速度的 `5.5` 倍。
- 飞行显示高度：`0.0075`，只影响 3D 表现，不污染确定性 2D 模拟。
- 金币、木材、人口、生命、护甲、攻击周期、训练和建造秒数：保持导出值。

建筑占地尺寸、建筑功能、生产者关系、前置建筑和工人身份仍采用项目映射。原因是这些字段属于当前导航网格与玩法模块的语义，不能由 War3 的模型尺寸或碰撞半径可靠推导。夜间视野数据已经保留，但当前项目尚无昼夜时钟，因此模拟只消费昼间值。

## 技能与升级导出

源工作区 `D:\Godot\war3_assets` 中执行：

```powershell
.\scripts\Export-ObjectEditorData.ps1
```

脚本按 `roc -> tft -> tft-locale -> patch` 覆盖顺序合并 `AbilityData.slk`、`UpgradeData.slk` 和各族 `Func/Strings`，输出“一对象一 JSON + manifest + 来源 SHA-256”，随后同步到本项目。单位导出表引用的 333 个技能全部可解析；全量单位升级引用中 `Rewd` 是原始数据内唯一找不到 Upgrade 记录的悬空 ID。当前 Human 可玩子集引用的 27 个技能和 20 个升级均完整。

## 科技效果边界

现有通用科技系统只支持每项科技一个固定费用/时间，以及武器等级和建筑护甲等级两个战斗消费点。因此本阶段采用以下明确映射：

| 稠密科技 ID | War3 ID | 已消费的运行时语义 |
| --- | --- | --- |
| 0 | `Rhme` | 近战武器升级；伤害增量由单位攻击 profile 消费 |
| 1 | `Rhar` | 名称、图标、说明、费用、时间、最大等级、研究队列已接入；单位护甲效果待通用模块扩展 |
| 2 | `Rhac` | 石工技术；由现有建筑护甲升级消费点生效 |

War3 的高等级研究费用和时间会递增，而当前 `TechnologyProfile` 是固定值。本阶段运行时使用第一级费用/时间，完整逐级值仍保留在 `War3TechnologyDefinition.Levels` 与 Upgrade JSON 中。UI 显示实际会扣除的运行时固定费用，避免把源数据的递增费用误报为已实现。

Ability 数据已经进入只读 catalog、引用校验和选择 UI，但项目尚无通用 Mana/Ability/Buff 系统，所以不能把“数据可查”描述成“技能可释放”。后续应独立实现 Ability runtime，再按技能基类/效果代码逐类适配。

## 原版队列界面对齐

经典资源 `UI/FrameDef/UI/InfoPanelBuildingDetail.fdf` 把建筑队列背景声明为 `BuildQueueBackdrop`，Human 皮肤在 `war3skins.txt` 中将其映射到 `UI/Widgets/Console/Human/human-unitqueue-border.blp`。该素材包含一个当前项目大槽和六个等待槽；进度条使用 `human-buildprogressbar-fill/border.blp`。当前通用生产和研究系统的最大队列长度都是 5，因此 UI 保留原版七槽布局，但只填充真实存在的五项。

HUD 不读取生产或科技内部集合。组合层把观察快照转换成队列 DTO：生产项目绑定单位图标和 `ProductionOrderId`，研究项目按当前科技等级绑定逐级 Upgrade 图标和 `ResearchOrderId`。点击队列图标触发 HUD 事件，场景再调用 `CancelProduction` 或 `CancelResearch`，所以退款、确定性命令记录和回放语义没有复制到 UI。

## 失败与回退

场景使用 `War3UnitDataCatalog.Open`。manifest 不存在、schema 不兼容、单个 JSON 损坏或 object id 未找到时，不会让整个 RTS 场景崩溃：

- manifest 整体失败：27 个当前可玩人族对象全部使用原有内置配置。
- 单对象失败：只回退该对象，其余对象继续使用导出数据。
- 启动日志输出 `WAR3_GAMEPLAY_DATA`，包含索引总数、已应用单位/建筑数和回退数。
- 启动日志输出 `WAR3_OBJECT_DATA`，分别报告 Ability/Upgrade 清单、可玩引用缺失和三项科技映射状态。
- `War3HumanContent.DataStatus` 可被调试 UI 或自动化测试读取。

严格工具或测试可调用 `War3UnitDataCatalog.Load`，它会对 manifest、schema、路径或重复 ID 错误直接抛异常。

## 当前边界与后续扩展

当前可玩 Human 模块绑定 16 个单位和 11 个建筑；仓库已经索引全部 837 个对象，因此新增种族不需要重写 JSON 加载器，只需为该种族建立新的稠密 ID、功能和生产关系组合层。

完整技能、升级和中文描述现已导出并建立运行时仓库。尚未接入模拟语义的主要字段是法力/回复、主动技能与 Buff、攻击目标掩码、War3 攻防类型矩阵、溅射半径、夜间视野，以及单位护甲科技。它们需要各自的通用模块与回放/状态哈希合同，不能直接塞进 War3 UI 或战斗脚本。

## 验证

1. 构建 C# 项目，确保 nullable 与 warnings-as-errors 全部通过。
2. 运行 `war3_rts/War3Rts.tscn`；日志应同时包含 `WAR3_GAMEPLAY_DATA` 和 `WAR3_OBJECT_DATA`，后者应为 `technology_applied=3 technology_fallbacks=0`。
3. 观察农民生命、单位训练费用/人口/时间、建筑费用/生命/建造时间、不同单位射程和弹道。
4. 使用现有 `--war3-rts-smoke` 冒烟流程；`data_integration=True` 会验证 manifest、可玩技能/升级引用、农民视野换算、飞行单位 Elevated 视野和科技消费点 ID。
