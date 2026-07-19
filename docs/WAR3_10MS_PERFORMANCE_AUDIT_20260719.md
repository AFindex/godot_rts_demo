# War3 800 单位 10 ms 性能审计（2026-07-19）

## 结论

当前 800 单位压力场景已经消除了动态占用格候选爆炸、视野整块撤销/重建、单位 Transform 多次跨 native 写入等可安全修复的热点。寻路、战斗、视野业务回归通过，但距离 10 ms 仍存在表现层架构差距：

- 全动画、全特效压力模式：平均 `118.855 ms`，P95 `173.375 ms`。
- 生产级 LOD、关闭 VSync、Safe 渲染线程：平均 `22.775 ms`，P95 `38.475 ms`。
- 同一生产级 LOD 下，模拟平均 `6.201 ms`，GPU 平均 `4.613 ms`；两者已经合计 `10.814 ms`，尚未计入主线程表现同步和引擎调度。

因此，继续做局部 C# 微优化不足以把 800 单位全细节从约 119 ms 压到 10 ms。下一阶段必须让单位动画和战斗特效进入 GPU 批处理，同时继续把模拟预算降到 3–4 ms。

## 本轮已完成的安全优化

1. 动态占用格约束查询只扫描拟议圆盘覆盖的格子，不再按“移动扫掠 AABB + 全局最大建筑跨度”扩大候选范围。建筑 footprint 已经写入其覆盖的每个格子，因此不改变精确碰撞判定。压力场景候选检查降至平均 `6.431`、P95 `13`，`CollectConstraintCandidates` 已从 EventPipe 顶层热点消失。
2. 单位视野跨格移动改成两个有序 cell 列表的 merge-diff，只更新边缘变化格，不再撤销并重加整个视野圆。`AddVisionCells` / `RemoveVisionCells` 已从 EventPipe 顶层热点消失。
3. 单位移动和朝向合并为一次 `Transform` 写入；朝向比较和建筑世界坐标使用托管缓存，避免每单位每帧 native getter。
4. 每帧只取一次相机 frustum，800 个单位/建筑点测试在托管侧完成。
5. 特效粒子与 ribbon point 改为值类型，移除逐粒子对象分配；同一特效模拟步内共享 Skeleton 全局变换读取。
6. 动画序列 loop mode 只在实际变化时写入，不在每次重播时重复跨 native 设置。
7. 新增仅用于 profiling 的 `--war3-profile-production-presentation`。默认 800 压测仍保持全动画、全特效，不改变玩家从启动页运行的压力场景语义。

## 当前热点分类

### 模拟（平均约 6.2 ms）

- steering / 邻居处理仍是最大可控模拟项；全细节采样中约 `1.94 ms`。
- visibility 在优化后约 `1.10 ms`，剩余主要是视野 cell 收集和增量差分。
- `IsSegmentFree` 仍会扫描线段扫掠 AABB 内的格子，可评估改成带半径的栅格 DDA，但必须用现有回放与路径矩阵证明精确等价。
- collision 已降至约 `0.41 ms`，不再是首要矛盾。

### 表现与渲染

- 全细节视角约 `3,250` 平均 draw calls，P95 接近 `4,878`。
- 主要托管/native 边界热点为 Skeleton/骨骼发射器变换、CPU 粒子 ArrayMesh 重建、Mesh commit、AnimationPlayer 序列启动与材质应用。
- 全细节下 engine process 平均约 `70.1 ms`；生产级 LOD 下仍约 `16.1 ms`，说明瓶颈不只在模拟。
- `--render-thread separate` 在本项目实际启动时触发 Godot fatal 越界并退出，当前不能作为优化方案。
- 将隐藏 LOD actor 从 SceneTree detach 的实验只从 `22.775 ms` 变化到约 `22.5 ms`，属于噪声且造成退出资源泄漏，已完全回退。

## 达到 10 ms 的建议路线

### 阶段 A：GPU 单位动画批处理（首选）

- 按单位类型、队伍材质、动画 clip 建立 VAT/动画纹理与 MultiMesh 批次。
- instance 数据保存位置、朝向、动画 clip、相位、队伍色和必要的受击状态。
- 英雄、选中单位、近距离交互单位保留完整 ModelActor/Skeleton，远处普通单位使用 GPU 批次。
- 伤害帧、脚步、弹道发射等事件继续由模拟时间轴驱动，不能依赖 GPU 动画回调。
- 验收门槛：生产级 800 单位表现层 CPU 小于 `2 ms`，draw calls 小于 `300`，路径/攻击事件 hash 不变。

### 阶段 B：GPU 特效池

- 静态模型、billboard、粒子分别按材质批处理；禁止每个 emitter 每帧 `ClearSurfaces/AddSurfaceFromArrays`。
- 骨骼挂点在近景保留精确读取，远景使用单位根节点或预烘焙挂点轨迹。
- 验收门槛：800 单位持续交战时特效 CPU 小于 `1 ms`，不新增持续分配和周期 GC 峰值。

### 阶段 C：模拟降至 3–4 ms

- steering 分频必须按稳定 hash 分桶，并对接敌、窄口、即将碰撞单位保持高频，避免出现“过一会才动”。
- `IsSegmentFree` 采用保守 supercover/DDA 候选枚举，再运行原有精确几何判定。
- 视野圆 cell 模板按半径与地形层预计算，运行时只平移并做边缘差分。
- 所有改动必须通过重复 Attack-Move、动态建筑绕行、密集编队、地形视野和回放 hash 验证。

## 本轮验证

- `dotnet build`：0 warning，0 error。
- 战斗规则、Ability、地形视野、目的地 clearance 自测通过。
- 重复 Attack-Move：`48/48` 推进，命令合并 `11759/11759`。
- 动态建筑绕行：`20/20` 到达，零重叠、零不可达。
- 密集编队：`80/80` 到达，零重叠、零不可达。
- `alliance-shared-vision` 的 verify 入口本轮单独运行超过 120 秒未退出；专项地形/共享视野自测已通过。该入口性能或退出条件需要另案检查，不能把超时当成业务断言失败。

## 证据文件

- `reports/war3_hotspot_full_after_20260719.log`
- `reports/war3_production_lod_800_novsync_safe_20260719.log`
- `reports/dotnet/war3_20260719_112825_hotspot_pass2_pid63668/managed.summary.md`
- `reports/war3_production_lod_800_novsync_separate_20260719.log`
