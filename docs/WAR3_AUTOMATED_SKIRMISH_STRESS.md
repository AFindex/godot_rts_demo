# War3 自动运营遭遇战压测

## 目标

`--war3-auto-skirmish-stress` 启动一个仅供性能诊断使用的双边自动遭遇战。
双方都使用正式的 `ModularSkirmishAiPolicy` 和正式命令入口，覆盖：

- 农民采集和重新分配；
- 补农民、补人口、建造、扩张；
- 生产战斗单位、研究科技；
- 侦察、防守、编队进攻和持续补兵；
- 建筑占地、寻路、视野、战斗、投射物和表现同步。

测试模式不会替换或修改生产目录中的单位、建筑、科技和资源价格。无限金钱通过测试控制器定期给双方钱包补足；加速通过每个物理帧执行多个完整模拟 tick 实现。没有启动参数时，不创建控制器，也不进入多 tick 分支。

正式模块化 AI 当前只判断“是否已有农场”，在人口封顶后不会补第二批农场。为了让长压继续覆盖生产和交战，测试控制器在人口余量不超过 2 时，为双方各发出一条正式 `IssueConstruction` 农场命令。它仍会经过正常扣费、选址、占地、寻路、施工和完成流程；补建策略及选址只存在于该测试类中。

## 推荐运行方式

一键无渲染运行并把完整日志写入 `reports/war3_auto_skirmish_*.log`：

```powershell
.\tools\war3\Run-War3AutomatedSkirmishStress.ps1
```

加上 `-Rendered` 可观察实际表现同步和渲染爆卡；`-SampleSeconds 120 -TicksPerPhysicsFrame 1` 可按正常模拟速度采集更长的周期。

也可以直接调用 Godot：

```powershell
godot --path . res://war3_rts/War3Rts.tscn -- `
  --war3-auto-skirmish-stress `
  --war3-profile-seconds=60 `
  --war3-auto-skirmish-spike-ms=8
```

该模式会自动启用运行时 profiler；默认预热 2 秒、采样 20 秒、8 ms 作为单模拟 tick 的爆卡阈值，采样结束后自动退出。

无渲染回归：

```powershell
godot --headless --path . res://war3_rts/War3Rts.tscn -- `
  --war3-auto-skirmish-stress `
  --war3-profile-warmup=1 `
  --war3-profile-seconds=10
```

## 压力参数

| 参数 | 默认值 | 说明 |
| --- | ---: | --- |
| `--war3-auto-skirmish-ticks-per-frame=` | 4 | 每物理帧的完整模拟 tick 数，范围 1–32 |
| `--war3-auto-skirmish-bank=` | 250000 | 双方金矿和木材的补足目标 |
| `--war3-auto-skirmish-bank-refresh=` | 30 | 钱包补足间隔（模拟 tick） |
| `--war3-auto-skirmish-workers=` | 14 | 每方 AI 农民目标 |
| `--war3-auto-skirmish-army=` | 8 | 发起进攻所需军队规模 |
| `--war3-auto-skirmish-decision-interval=` | 6 | 每方 AI 决策间隔 |
| `--war3-auto-skirmish-attack-interval=` | 90 | 进攻命令刷新间隔 |
| `--war3-auto-skirmish-status-interval=` | 300 | 运营状态日志间隔 |
| `--war3-auto-skirmish-spike-ms=` | 8 | 单模拟 tick 爆卡阈值（毫秒） |

整帧/物理帧仍使用 `--war3-profile-spike-ms=`，默认 25 ms；它与单 tick 阈值分开，避免多 tick 加速时把正常的批量开销误报为每 tick 爆卡。

需要复现正常速度的周期性爆卡时，把 `ticks-per-frame` 设为 1；需要尽快跑过完整科技、生产和交战周期时使用默认值 4。不要用提高单 tick 的 `delta` 替代多 tick，因为 AI、寻路队列和周期系统以 tick 计数，放大 `delta` 无法真实加速这些触发器。

## 日志和判读

- `WAR3_AUTO_SKIRMISH_STATUS`：双方农民、军队、建筑、产能、科技设施、钱包和人口快照。
- `WAR3_AUTO_SKIRMISH_TICK_SPIKE`：精确到单模拟 tick 的爆卡记录；同时给出当 tick 的运营事件、AI 捕获/决策/执行、建造、生产、科技、战斗、寻路、避障、碰撞和视野耗时。
- `WAR3_RUNTIME_PHYSICS_SPIKE`：一个物理帧（可能包含多个模拟 tick）的总开销。
- `WAR3_RUNTIME_SPIKE`：包含表现同步、渲染和 GC 的整帧爆卡。
- `WAR3_RUNTIME_PROFILE_SUMMARY`：爆卡 tick 数、爆卡连续帧数和爆卡 burst 数。
- `auto_skirmish_spike_burst_interval_ticks`：相邻爆卡 burst 的 tick 间隔分布；若 p50/p95 很集中，通常说明是周期系统触发，而不是随机 GC 或系统调度。

`activities` 会标记 `BankTopUp`、`Gather`、`Construction`、`Production`、`Research`、`Scouting`、`Combat` 和 `StatusSample`。它用于把周期爆卡与运营事件直接关联；空事件 tick 仍然爆卡时，应优先查看路径、视野、战斗和 GC 字段。
