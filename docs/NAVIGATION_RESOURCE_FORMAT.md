# Godot RTS 导航 Resource 格式

更新日期：2026-07-10

## 1. 数据流

```text
Godot Inspector / .tres
    ↓
RtsNavigationMapResource
    ↓ NavigationMapResourceConverter
NavigationMapSnapshot（纯 C#）
    ├─ StaticWorld
    ├─ PortalGraphRoutePlanner
    └─ ChokeController
```

`Godot.Resource` 只存在于 `src/Godot/Resources/`。模拟、寻路、验证、规范字节和稳定哈希都位于纯 C# 层。

主场景通过导出属性 `RtsDemo.NavigationMapAsset` 引用 `data/demo_navigation_map.tres`。资产缺失或验证失败时启动会返回非零退出码，不会静默使用另一张地图。

实现遵循 Godot 4.7 的 C# Resource 约束：自定义可创建类型使用 `[GlobalClass]` 和无参构造，子资源集合使用 `Godot.Collections.Array<T>`，运行时通过 ResourceLoader/GD.Load 加载。参考官方文档：[Resources](https://docs.godotengine.org/en/latest/tutorials/scripting/resources.html)、[C# exported properties](https://docs.godotengine.org/cs/4.x/tutorials/scripting/c_sharp/c_sharp_exports.html)、[ResourceLoader 4.7](https://docs.godotengine.org/en/4.7/classes/class_resourceloader.html)。

## 2. Resource 类型

### RtsNavigationMapResource

- `FormatVersion`：当前必须为 1。
- `WorldBounds`：世界可用矩形。
- `Obstacles`：静态轴对齐矩形障碍。
- `Portals`：高层图节点。
- `Edges`：无向 Portal 连接。
- `Chokes`：需要交通调度的狭长通道。

### NavigationPortalResource

- `Id`：必须从 0 开始，与数组下标完全一致。
- `Position`：Portal 中心点。
- `DisplayName`：调试名称，也参与稳定哈希。

### NavigationPortalEdgeResource

- `FromPortal`、`ToPortal`：节点 ID。
- `Width`：可通行宽度。
- `ChokeId`：`-1` 表示普通连接，否则引用 Choke。

### NavigationChokeResource

- `Id`：必须从 0 开始且稠密。
- `A`、`B`：通道两端，同时必须对应引用它的 Portal Edge 两端。
- `Width`：通道宽度。
- `ApproachDistance`：进入交通控制的提前距离。

## 3. 验证错误码

错误码数值已经固定；新增错误应使用新编号，不能重排现有值。

| 错误码 | 名称 | 含义 |
|---:|---|---|
| 1001 | UnsupportedFormatVersion | 不支持的数据版本 |
| 1002 | InvalidWorldBounds | 世界边界无效 |
| 1101 | InvalidObstacle | 障碍尺寸或数值无效 |
| 1102 | ObstacleOutsideWorld | 障碍超出世界 |
| 1201 | NonDensePortalId | Portal ID 与数组下标不一致 |
| 1202 | InvalidPortalPosition | Portal 坐标不是有限值 |
| 1203 | PortalOutsideWorld | Portal 超出世界 |
| 1204 | PortalInsideObstacle | Portal 位于静态障碍内 |
| 1301 | InvalidPortalEdge | Edge 节点引用或宽度无效 |
| 1302 | DuplicatePortalEdge | 出现重复无向 Edge |
| 1303 | InvalidEdgeChokeReference | Choke 引用越界 |
| 1401 | NonDenseChokeId | Choke ID 不稠密 |
| 1402 | InvalidChokeGeometry | Choke 几何或参数无效 |
| 1403 | ChokeOutsideWorld | Choke 端点超出世界 |
| 1404 | ChokeEdgeGeometryMismatch | Choke 端点与 Edge Portal 不对应 |
| 1501 | MissingResourceAsset | 资源不存在或无法加载 |
| 1502 | NullResourceElement | Resource 数组含空元素 |

## 4. 规范字节和哈希

验证通过后，快照按固定字段顺序写为小端规范字节：

```text
format
world bounds
obstacle count + obstacles
portal count + (id, position, UTF-8 name)
edge count + (from, to, width, choke)
choke count + (id, A, B, width, approach distance)
```

浮点数使用 IEEE 754 位模式，字符串使用 UTF-8 和 32 位长度。稳定哈希是规范字节的 FNV-1a 64。

当前 Demo：

- 规范数据：388 字节。
- 稳定哈希：`B8441F9F1544B950`。
- 相同输入重复构建必须得到 byte-identical 数据。
- 任意参与运行时的数据变化都必须改变哈希。

该哈希后续可以直接用于回放头、联机握手和地图版本检查，但当前尚未实现联机协议。

## 5. 编辑和验证流程

1. 在 Godot FileSystem 中打开或复制 `data/demo_navigation_map.tres`。
2. 在 Inspector 中编辑数组和子 Resource。
3. 在 `Main.tscn` 的 `RtsDemo.NavigationMapAsset` 指定目标资源。
4. 执行验证：

```powershell
.\tools\validate_navigation_resource.ps1
```

5. 执行完整黑盒测试：

```powershell
F:\my_work\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe `
  --headless --path . -- --self-test
```

重置示例资源：

```powershell
.\tools\generate_demo_navigation_resource.ps1
```

重置命令从纯 C# Demo 夹具重建 `.tres`，会覆盖现有示例资源。

## 6. 当前边界

- 只支持轴对齐矩形障碍。
- Portal 和 Choke 仍需人工布置，没有从 NavMesh 自动提取。
- 没有 Sector、Clearance Field 和 Movement Class。
- 没有 EditorPlugin 几何拖拽、连线和多尺寸通行预览。
- 没有格式迁移器；非版本 1 数据会明确拒绝。
- 运行中不热重载资源，需要重新启动场景。

下一阶段将加入 Clearance 与 Movement Class；自动 Baker 和编辑器几何预览留在后续 S9 工具阶段。
