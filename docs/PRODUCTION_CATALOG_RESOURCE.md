# Production Catalog Resource

`data/demo_production_catalog.tres` 是 Unit Type 和 Production Recipe 的编辑时来源。
Godot 层只负责把 `RtsProductionCatalogResource` 转成不可变纯 C#
`ProductionCatalogSnapshot`；生产队列、回放、热快照和状态 Hash 不引用 Godot 对象。

## v6 数据

Unit Type 声明稳定连续 ID、名称、半径、速度、加速度、完整战斗 Profile 和 Worker
语义。Recipe 通过 Unit Type ID 引用目录中的单位，并声明合法 Producer Building Type、
Minerals/Vespene/Supply 成本、生产时间、取消退款率，以及零到多个“已完成建筑类型 × 数量”前置条件。

v6 的战斗 Profile 包含 Armor、属性位集、AttacksPerVolley、BonusVs/BonusDamage、基础/加成每级武器升级量、ProjectileSpeed、CanMoveDuringWindup/CanMoveDuringCooldown 和 0～10 的 AutoTargetPriority。速度为 0 表示瞬时命中，大于 0 使用确定性跟踪投射物；移动字段默认 false；自动优先级默认 0，只用于 AttackMove/Stop/Hold 的有限目标评分。属性支持 Light、Armored、Biological、Mechanical、Structure、Massive 的合法组合，攻击段数限制为 1～32。

当前 Demo 包含：

- Marine：Barracks，50 Minerals，1 Supply，3 秒。
- Marauder：Barracks，100 Minerals / 25 Vespene，2 Supply，5 秒。
- SCV：Command Center，50 Minerals，1 Supply，3.5 秒，并注册为经济工人。

转换器根据物理半径重新推导 Movement Class 和 Navigation Radius，不允许编辑资产写入
互相矛盾的派生净空数据。Recipe 的 Unit Type ID 必须落在目录内，最终快照还会验证连续
ID、重复名称、移动/战斗约束、Producer、成本、人口、工期、退款率和重复/非法前置条件。加载到主场景时还会与 Building Type Catalog 交叉校验引用范围。

当前规范 Hash 为 `6A33CA0648280068`。`ProductionCatalogDiff` 分别统计 Unit Type
和 Recipe 变化数量；单独修改一个生产时间稳定报告 `0/1`。

## 工作流

主场景通过 `ProductionCatalogAsset` 绑定独立 `.tres`。正常启动使用场景引用，验证命令
故意清除该引用并通过 `CacheMode.Replace` 重新读取文件，覆盖 Fresh Load 路径。

```powershell
.\tools\validate_production_catalog.ps1
.\tools\generate_demo_production_catalog.ps1
```

生成命令会用纯 C# Demo 目录覆盖示例资产；日常平衡调整应在 Inspector 修改 Resource。
`production-catalog-resource-runtime` 黑盒场景只消费加载快照，从 Barracks 和 Command
Center 生产三种单位，验证战斗 Profile、Worker 注册、双资源和人口结果。

Production Command Log v8、Replay Package/Hot Snapshot v17 和 State Hash v18 保存完整战斗字段、武器移动约束、自动优先级、目标锁定与活跃投射物。Replay Package 的 Train 命令保存当时解析后的完整 Recipe/Unit Type/Requirements，因此之后修改
Resource 不会篡改已有录像。
