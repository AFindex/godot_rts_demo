# Resource 热重载与差异诊断

更新日期：2026-07-11

## 当前边界

本阶段实现“重新加载、原子校验、差异与应用策略”，不直接修改运行中的模拟对象：

```text
Godot .tres --CacheMode.Replace--> 三类纯 C# Snapshot
                                  |
                                  v
                     RuntimeResourceSetSnapshot
                                  |
                     全集校验 + Diff + Impact
                                  |
               None / RefreshPathingCaches / RebuildSimulation
```

Navigation 或 Gameplay Profile 改变会返回 `RebuildSimulation`。只有 Bake 改变且仍绑定同一 Navigation 时返回 `RefreshPathingCaches`。上层必须先得到完整有效的候选资源集，不能逐个替换当前资源。

## 原子校验

`RuntimeResourceSetSnapshot.TryCreate` 要求：

- Navigation、Gameplay Profile 和 Clearance Bake 都已经分别通过格式验证。
- Bake 的 `SourceNavigationHash` 必须等于候选 Navigation 的稳定哈希。
- 任一资源加载失败或身份不匹配，整个候选集被拒绝。

Godot 侧 `RuntimeResourceSetLoader.TryLoadFresh` 使用 `ResourceLoader.CacheMode.Replace` 绕过旧缓存，然后立即转换为纯 C# 快照。加载器不持有模拟，也不决定 UI 表现。

## 差异报告

`RuntimeResourceReloadPlan` 提供：

- Navigation：世界边界、障碍、Portal、Edge、Choke 的变更数量。
- Gameplay Profile：单位与建筑 Profile 的变更数量。
- Clearance Bake：Bake 内容及源导航身份是否改变。
- 最终影响等级。

`RtsResourceReloadControl` 只消费 Reload Plan，显示哈希、计数和影响等级；它不加载文件、不应用资源，也不访问 `RtsSimulation`。

## 生成式测试资源

测试资源与正式 `data/` 分离，保存在 `test_resources/hot_reload/`：

- `navigation_variant.tres`：在开放区域增加 1 个 48×48 障碍。
- `gameplay_variant.tres`：只把 Unit Profile 0 的最大速度增加 16。
- `clearance_variant.tres`：从变体 Navigation 重新生成，源哈希严格匹配。

重新生成：

```powershell
.\tools\generate_hot_reload_test_resources.ps1
```

资源由正式 Converter 和 `ResourceSaver` 生成，不维护手写 `.tres` 副本。

## 验收

`resource-hot-reload` 验证：

- 三个变体 `.tres` 通过 Fresh Load，哈希与纯 C# 预期完全一致。
- Diff 为 1 个障碍、1 个单位 Profile、0 个建筑 Profile，影响为 `RebuildSimulation`。
- 只替换同源 Bake 时影响为 `RefreshPathingCaches`。
- 新 Navigation 与旧 Bake 混用时整个候选集被拒绝。
- 独立诊断 Control 的 AV1/WebM 录像通过。

## 后续

下一层可以增加文件变更监听和安全提交器：Bake-only 变更重建 Grid/Connectivity 缓存；Navigation/Profile 变更通过明确的场景重建流程迁移允许保留的游戏状态。当前不会对运行中的 SoA、路径请求或战斗状态做部分原地修改。
