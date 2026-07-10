# Gameplay Profile Resource

更新日期：2026-07-10

## 1. 资产入口

主场景通过 `GameplayProfilesAsset` 引用 `data/demo_gameplay_profiles.tres`。该目录 Resource 包含：

- `UnitMovementProfileResource[]`
- `BuildingFootprintProfileResource[]`
- 格式版本

启动时经 `GameplayProfileResourceConverter` 转为纯 C# `GameplayProfileCatalogSnapshot`。模拟层不保存 Godot Resource、Godot Array 或 Godot Vector2。

当前示例资产：

| 单位 ID | 名称 | 半径 | 最大速度 | 加速度 | 派生等级 |
|---:|---|---:|---:|---:|---|
| 0 | Scout | 5.5 | 150 | 780 | Small / 6px |
| 1 | Marine | 7.5 | 128 | 720 | Medium / 8px |
| 2 | Heavy | 10 | 105 | 600 | Large / 12px |

| 建筑 ID | 名称 | 等级 | 尺寸 | 最低通行等级 |
|---:|---|---|---:|---|
| 0 | Pylon | Small | 32×32 | Medium |
| 1 | Barracks | Medium | 64×48 | Medium |
| 2 | Factory | Large | 112×80 | Medium |
| 3 | CommandCenter | Huge | 160×120 | Medium |

## 2. 验证与派生

纯 C# 快照验证：

- 格式版本必须为 1。
- Unit 和 Building ID 必须分别从 0 开始连续。
- 名称在整个目录内不区分大小写唯一。
- 半径、速度、加速度和建筑尺寸必须为有限正数。
- UnitPadding 必须为有限非负数。
- BuildingFootprintClass 和 MinimumPassageClass 必须是有效枚举。
- Resource 数组不允许 null 元素。

单位 Resource 不重复保存 Movement Class 和 Navigation Radius；它们由物理半径通过 `MovementClearance` 唯一派生，避免资产中出现“半径是 Large、标签却是 Small”的矛盾。

快照生成规范字节和 FNV-1a 稳定哈希。当前示例资产哈希为：

```text
5A0A6FD9EA3985BF
```

## 3. 运行时使用

- `RtsSimulation.AddUnit(position, profile)` 使用快照中的半径、速度和加速度。
- `RtsSimulation.TryPlaceBuilding(center, profile)` 使用快照中的尺寸、最低通行等级和 UnitPadding。
- Demo 出生单位在 Scout、Marine、Heavy 之间轮换。
- Demo 连续按 B 在 Pylon、Barracks、Factory、CommandCenter 之间轮换。

## 4. 验证命令

```powershell
.\tools\validate_gameplay_profiles.ps1
```

成功输出包括格式、哈希以及单位/建筑数量。全量自测还会验证规范字节稳定性、三档尺寸派生和非法非连续 ID 的固定错误码。

## 5. 黑盒场景

`gameplay-profile-resource-runtime` 使用实际加载的 Godot Resource 快照：

- 生成 3 种单位并验证可观察半径与资产一致。
- 生成 4 种建筑并通过业务放置检查。
- 3/3 单位到达，4/4 建筑成功，0 重叠、0 不可达。

场景只通过稳定业务接口读取 Profile 快照、Spawn、Move、PlaceBuilding 和观察结果，不读取 Resource 转换器或 UnitStore。

## 6. 后续

场景内 `[Tool]` 多尺寸 Preview 与全局 Connectivity 第一层已经完成：编辑器可显示单位净空、Portal 宽度资格、四档建筑 footprint 和所选 Movement Class 的连通分量；业务放置会拒绝切断已有分量。下一层是资源格式迁移、差异诊断和热重载。
