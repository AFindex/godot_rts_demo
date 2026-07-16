# Warcraft III 技能与升级编辑器数据

## 目录和生成命令

```powershell
cd D:\Godot\war3_assets
.\scripts\Export-ObjectEditorData.ps1
```

输出目录：

- `exports/ability_editor_data`：801 个 Ability 对象。
- `exports/upgrade_editor_data`：89 个 Upgrade 对象。

脚本默认同步到 `D:\Godot\projs\godot_rts_demo\assets\warcraft3\classic\data`。加 `-SkipGodotSync` 可只生成研究工作区数据。

## 来源和覆盖顺序

导出器只读取标准 `Units` 目录，不混入 `Custom_V0`、`Custom_V1` 或 `Melee_V0` 变体。覆盖顺序固定为：

1. `roc`
2. `tft`
3. `tft-locale`
4. `patch`

数值主体来自 `AbilityData.slk` / `UpgradeData.slk`；名称、提示、图标、特效和前置关系来自各族 `*AbilityFunc.txt`、`*AbilityStrings.txt`、`*UpgradeFunc.txt`、`*UpgradeStrings.txt`。manifest 的 `sources` 保存每个源文件的 SHA-256，每个对象的 `provenance.sourceFiles` 记录实际命中的覆盖层。

## 目录布局

```text
ability_editor_data/
  manifest.json
  README.md
  abilities/{race}/{id}.json

upgrade_editor_data/
  manifest.json
  README.md
  upgrades/{race}/{id}.json
```

manifest 的 `objects` 是完整索引，包含大小写敏感的四字符 ID、中文显示名、种族、相对路径、字节数和内容 SHA-256。对象文件 schema 分别为 `war3-ability-editor-data/v1` 和 `war3-upgrade-editor-data/v1`。

## 对象 JSON

两类对象共享以下顶层结构：

- `id` / `displayName`：对象 ID 和中文名称。
- `identity`：种族、等级数，以及 hero/item/class/global 等身份字段。
- `assets`：图标、施法者、目标、区域、弹道等资源引用数组；引用记录原路径、解析结果、来源层和 Godot 转换路径。
- `summary.levels`：逐级结构化名称、提示和常用数值。
- `editorData`：SLK 行的原始字符串字段。
- `profile`：Func/Strings 合并后的原始字符串字段。
- `provenance`：该对象实际涉及的源文件。

Ability 每级常用字段包括目标类型、施法时间、持续时间、英雄持续时间、冷却、耗魔、范围、作用区域、DataA-I、Buff ID 和 Effect ID。Upgrade 每级常用字段包括递增后的金币、木材、研究时间、图标和前置；`summary.effects` 保存最多四个 effect 的 type/base/modifier/code。

## 空值和数值策略

- `_`、`-` 和空字符串在 `summary` 中视为空值；原值仍保留在 `editorData/profile`。
- Upgrade 第 `n` 级费用按 `base + (n - 1) * mod` 计算。
- 逗号列表使用 CSV 规则解析，支持带引号且内部包含逗号的中文提示。
- 资源路径不改写源字符串；解析结果另存，避免丢失研究证据。

## 完整性结果

全量单位数据引用 333 个不同 Ability，全部能在 Ability 清单中解析。全量单位数据引用 87 个不同 Upgrade，其中 `Rewd` 在现有原始 Upgrade 表和文本中都不存在，是源数据自身的唯一悬空引用；导出器不会臆造对象。Godot Human 可玩子集引用的 27 个 Ability 和 20 个 Upgrade 均完整。

数据成功导出只表示可查询，不表示游戏已经实现对应技能或 Buff。运行时适配范围和已生效科技见 Godot 项目的 `docs/WAR3_RUNTIME_DATA_INTEGRATION.md`。
