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

## Bake-only 安全提交

`RtsSimulation.TryCommitClearanceBake` 对 `RefreshPathingCaches` 候选执行两阶段事务：

1. `GridPathProvider`（或 `ValidatingFallbackPathProvider` 的 Grid fallback）验证源导航哈希、世界边界、Cell/行列布局和三档导航半径。
2. `BuildingConnectivityGuard` 独立执行相同验证。
3. 两边全部通过后才替换 Bake，清空三档 Grid Snapshot 与放置基线缓存。
4. 正在 `Moving/WaitingForPath` 的单位按 MovementGroup 批量重规划。

提交期间不会替换 Navigation、Gameplay Profile、StaticWorld、单位 SoA 或战斗状态。以下情况返回稳定拒绝码且保持旧 Bake：

- 没有版本化基线 Bake。
- Navigation 源哈希或 Grid 布局不匹配。
- Path Provider 不支持缓存替换。
- 命令日志或 Replay Package 正在录制。

提交后启动 Replay Package 录制时，Manifest 的 Bake Hash 必须等于当前活动 Bake，避免把新运行态绑定到旧资源身份。

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
- `clearance-bake-live-commit` 在 8 个单位移动途中提交同源 Bake，8 个单位全部重规划并最终到达；随后错误导航哈希候选被拒绝，reload 计数保持 1。
- StraightLine Provider 和录制中提交分别返回 `UnsupportedPathProvider` 与 `RecordingActive`。

## 后续

下一层可以增加文件变更监听和自动触发 Bake-only 提交；Navigation/Profile 变更仍通过明确的场景重建流程迁移允许保留的游戏状态。当前不会对运行中的 SoA 或战斗状态做部分原地修改。
