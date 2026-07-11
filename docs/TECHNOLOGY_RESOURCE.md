# Technology Catalog Resource

`data/demo_technology_catalog.tres` 是研究与升级的编辑时来源。Godot 只负责把
`RtsTechnologyCatalogResource` 转换为不可变的纯 C#
`TechnologyCatalogSnapshot`；研究队列、等级、回放和热快照不引用 Godot 对象。

## v1 数据契约

每项科技声明稳定连续 ID、名称、Research 建筑类型、双资源成本、研究时间、最高等级、
取消退款率、可选互斥组，以及零到多个建筑/科技等级前置。条件数组转换为
`ImmutableArray`，外部不能修改已经进入运行时的目录。

Demo 包含：

- Infantry Weapons：Academy，最高三级。
- Assault Doctrine：需要 Infantry Weapons 1，互斥组 1。
- Fortification Doctrine：需要 Infantry Weapons 1，同属互斥组 1。

当前格式为 v1，规范 Hash 为 `8F9990031AA55B5E`。

## 严格校验

- 拒绝空目录、未知格式、null Technology/Requirement、非连续 ID、重复名称和非法数值。
- Researcher 必须引用 Building Catalog 中的 Research 建筑。
- Building 前置必须落在 Building Catalog 内。
- Technology 前置只能引用更小的稳定 ID，因此目录天然无环；向后引用和自引用均拒绝。
- 同一科技不能重复声明同种目标条件。

正常启动使用 `Main.tscn` 绑定的 Resource；验证命令清除场景引用后使用
`CacheMode.Replace` Fresh Load，确保测试覆盖真实文件读取路径。

```powershell
.\tools\generate_demo_technology_catalog.ps1
.\tools\validate_technology_catalog.ps1
```

`technology-research-upgrades` 通过稳定测试 Facade 消费加载快照，覆盖等级、退款、前置、
互斥、完整回放和研究中热恢复。设施日志仍保存下令时解析后的完整 Technology Profile，
因此后续 Resource 调参不会篡改历史 Package。
