# Building Type Resource

`data/demo_building_types.tres` 是施工业务参数的编辑时来源。Godot 层只负责把
`RtsBuildingTypeCatalogResource` 转成不可变的纯 C#
`BuildingTypeCatalogSnapshot`；`ConstructionSystem`、回放和状态 Hash 不引用 Godot
对象。

## v2 字段

每个类型具有稳定、从 0 连续的 ID，并声明名称、功能、尺寸、最小通行等级、双资源/
人口成本、施工时间、最大生命、完工人口、取消退款率、施工策略和气矿节点约束。
当前示例包含：

- Supply Depot：48×48，100 Minerals，4 秒，400 HP，+8 Supply。
- Barracks：112×80，150 Minerals，7 秒，1,000 HP。
- Command Center：160×120，400 Minerals，10 秒，1,500 HP，+15 Supply。
- Refinery：72×72，75 Minerals，5 秒，500 HP，必须绑定 Vespene 节点。

验证器会拒绝未知版本、空目录、非连续 ID、重复名称、无效数值/枚举和不一致的功能
契约。规范二进制使用固定字段顺序和 little-endian 基础类型，FNV-1a 64-bit Hash 用于
确定性比较。v2 新增 Armor、Attributes 和 ArmorUpgradePerLevel；Attributes 必须包含 Structure，可与 Mechanical 等属性组合。当前五类 Demo 建筑覆盖 0/1/2 基础护甲和每级 +1 Fortification，Hash 为 `8706BAAF85DDD1B7`。

Construction Command Log v2 保存解析后的完整建筑 Profile；Replay Package/Hot Snapshot v14 与 State Hash v15 保存并验证建筑防御未来态，因此资产调参不会篡改已有录像。
`BuildingTypeCatalogDiff` 以稳定 Hash 判断是否变化，并给出逐类型变化数量；单项造价
修改会稳定报告一个 Changed Type，供后续生产队列的安全重建策略消费。

## 编辑与运行时

主场景的 `BuildingTypesAsset` 指向独立 `.tres`。启动时打印
`RTS_BUILDING_TYPES PASS` 后，后续业务只消费快照。Resource 中的修改不改变历史回放：
Build 命令仍记录当时已经解析的完整 Profile。

```powershell
.\tools\validate_building_types.ps1
.\tools\generate_demo_building_types.ps1
```

生成脚本会用纯 C# Demo 定义覆盖示例资产；日常平衡调整应直接编辑 Resource。
`building-type-resource-runtime` 黑盒场景使用加载后的目录完成四种真实建筑的扣费、施工、
人口和 Refinery 生命周期，不依赖 Resource 转换器或 ConstructionSystem 内部结构。
