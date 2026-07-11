# AI Configuration Godot Resource

S11-H2 将 AI 行为阈值从策略实现迁入版本化数据合同。纯 C# `AiConfigurationCatalogSnapshot v1` 是运行时权威格式；Godot Resource 只负责 Inspector 编辑和 Fresh Load 转换。

当前 `data/demo_ai_configurations.tres` 包含：

| Profile | Workers | Attack size | Decision | Scout | Attack | Defense radius |
|---|---:|---:|---:|---:|---:|---:|
| Standard | 10 | 6 | 12 Tick | 360 Tick | 240 Tick | 340 |
| Aggressive | 8 | 4 | 10 Tick | 240 Tick | 120 Tick | 380 |

每个 Profile 还声明单次最大意图数、人口缓冲。目录严格验证格式版本、连续 ID、唯一名称、数值范围和有限半径；规范字节生成稳定 Hash `509CED7A999A2BD0`。

`RtsAiConfigurationCatalogResource` 与 `AiDifficultyProfileResource` 可直接在 Inspector 编辑。`AiConfigurationResourceConverter` 使用 `CacheMode.Replace` Fresh Load，并只向 AI 层交付不可变快照。策略通过 `ModularAiConfig.FromProfile` 组合 Building/Production/Technology Catalog 与难度参数，Godot Resource 不进入模拟核心。

生成和 Fresh Load 验证入口：

```powershell
.\tools\generate_demo_ai_configurations.ps1
.\tools\validate_ai_configurations.ps1
```

双 AI 场景分别使用 Standard 和 Aggressive。Director 按 Player ID 稳定排序，并使用 12/10 Tick 周期与 0/5 offset 错峰；配置决定策略节奏，场景不复制阈值。
