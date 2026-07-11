# Resource 热重载与差异诊断

更新日期：2026-07-11

## 当前边界

本阶段完成“文件监听、去抖、重新加载、原子校验、差异与安全应用”：

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

## 文件监听与去抖

`RuntimeResourceFileWatcher` 默认监听正式 Navigation、Gameplay Profile 和 Clearance Bake 路径：

- `FileSystemWatcher` 回调运行在线程池，只把资源类型写入 `ConcurrentQueue`。
- Godot `_Process` 主线程排空队列，并交给纯 C# `ResourceReloadDebouncer`。
- 250ms 安静窗口合并编辑器一次保存产生的 Changed/Created/Renamed 连续事件。
- Fresh Load 遇到半写文件或临时 Navigation/Bake 不匹配时，每 250ms 重试，最多 5 次；新文件事件会开启新一代批次。
- 所有 `ResourceLoader`、Converter、差异分析和模拟提交只在 Godot 主线程执行。

候选全集有效后，`None` 只更新监听基线，`RefreshPathingCaches` 调用两阶段 Bake 提交，`RebuildSimulation` 只发布诊断并保留当前运行态。表现层 `RtsResourceWatchControl` 只消费不可变工作流快照。

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
- `clearance_bake_only_variant.tres`：保持正式 Navigation/Profile，只改变 Bake chunk 布局，用于安全自动提交。

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
- `resource-file-watch-workflow` 将 2 次 Bake 文件事件合并为 1 次 Fresh Load/提交，8 个活动单位全部重规划并 8/8 到达，reload 计数严格为 1。
- 纯 C# 状态机验证 3 个混合事件合批、半写文件延迟重试、第二次成功，以及达到最大次数后的终止。

## 格式版本策略

当前 Navigation/Profile/Bake 都只有格式 1。未知版本继续由 Validator 稳定拒绝，不做猜测式原地迁移；现有 Generator 是格式 1 的规范重建路径。未来真正引入格式 2 时，应新增显式 `v1 -> v2` 迁移器和固定夹具，而不是提前维护没有输入规范的兼容代码。

至此 S9 数据工作流闭环。Navigation/Profile 的运行时状态迁移、Sector/Portal Authoring Tool 和跨 chunk component 边界图是独立后续能力，不属于自动热重载的安全提交范围。
