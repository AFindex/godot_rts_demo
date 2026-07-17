# Warcraft III 音频运行时

本项目把 Warcraft III 的原始音频分成三层，避免玩法代码直接依赖文件名，也避免 Godot 导入全部 600+ MiB 研究素材。

```text
SoundInfo / 单位编辑器数据
        ↓ 离线导出
完整 Audio Catalog（Cue、语音族、单位/技能绑定）
        ↓ Build-War3AudioRuntime.ps1 按玩法裁剪
Godot Runtime Pack（实际 WAV/MP3）
        ↓ 运行时解析
语义控制器 → 播放池 → Audio Bus
```

## 数据与资源位置

- 完整导出源：`D:\Godot\war3_assets\exports\audio_catalog`
- 项目内完整目录：`assets/warcraft3/classic/data/audio_catalog`
- 原始覆盖音频：`assets/warcraft3/classic/audio`
- Godot 首批运行包：`assets/generated/warcraft3_audio`
- 运行包清单：`assets/generated/warcraft3_audio/runtime_manifest.json`

完整目录和音频文件属于可再生成的本地研究资产，均被 `.gitignore` 排除；代码和构建脚本可以正常提交。

## 分层职责

| 层 | 代码 | 职责 |
| --- | --- | --- |
| 数据目录 | `War3AudioCatalog` | 加载 manifest、单位绑定，惰性读取 Cue，确定性选择随机样本与音高 |
| 语义策略 | `War3WorldAudioController` / `War3AudioAudiencePolicy` | 把“选择、确认、命中、死亡、生产、技能和系统反馈”转换成 Warcraft Cue，并按本地玩家身份决定是否可听 |
| 时间轴事件 | `War3ModelActor` / `War3WorldPresenter` | 在模型动画跨过 `SNDX` 帧时发布事件，并保留声源单位、阵营和世界位置 |
| Godot 播放 | `GodotWar3AudioPlayback` | ResourceLoader、2D/3D 播放器池、抢占、循环句柄、流缓存、防连发和语义并发限制 |
| 音乐播放 | `GodotWar3MusicPlayer` | 独立加载播放列表，在 Music Bus 上顺序播放，不占用 SFX 播放池 |
| 混音配置 | `War3AudioMixSettings` / `War3AudioBusInstaller` | 音量持久化、Bus 创建与分组 |
| 项目桥接 | `War3Rts.Audio.cs` | 独立消费模拟事件流，解析项目单位 ID，不侵入确定性模拟 |

模拟层不知道 Cue、音频文件、Godot 节点或 Bus；播放器也不知道单位、武器和战斗规则。之后接技能、天气或剧情音频时，应继续只向语义控制器提供事实和业务生命周期。

## 当前事件映射

| 运行时事实 | Cue 规则 | 空间类型 |
| --- | --- | --- |
| 点击 HUD 命令 | `InterfaceClick` | 2D |
| 被拒绝的建筑放置 | `InterfaceError` | 2D |
| 选中单位 | `{voiceSet}What` | 3D |
| 移动、停止、保持命令成功 | `{voiceSet}Yes` | 3D |
| 攻击命令成功 | `{voiceSet}YesAttack` | 3D |
| Shift 追加路点 | `WayPoint` | 2D |
| 集结点设置成功 | `RallyPointPlace` | 2D |
| 建筑放置成功 | `PlaceBuildingDefault` | 2D |
| 建筑正式开工 | `ConstructingBuildingDefault` | 3D |
| 建筑完工 | `JobDoneSoundHuman` | 2D |
| 生产完成 | `{voiceSet}Ready` | 3D |
| 人族科技完成 | `UpgradeCompleteHuman` | 2D |
| 单位死亡 | `{voiceSet}Death` | 3D |
| 攻击命中 | `{weaponSoundFamily}{targetImpactMaterial}` | 3D |
| 模型 `SNDX` 时间轴 | `eventCode → animation_event_map.json → Cue` | 3D |
| 技能开始 | `abilityId → Effectsound` | 3D |
| 技能持续/结束 | `Effectsoundlooped` → loop handle → stop | 3D |

选择音只在用户实际选择入口触发，不挂在通用的 `RefreshSelection` 上；命令语音只在模拟接受命令后触发。战斗和玩法事件使用音频层自己的 sequence cursor，不会夺走 HUD、特效或诊断模块的事件。

## 受众与所有权策略

音效是否可听分成两层判断，不能只靠距离：

- **本地玩家私有语音和反馈**：`Selection`、`Command`、`AttackCommand`、`UnitReady`、`Notification` 只有声源单位或事件属于本地玩家时才播放。敌方单位生成、被 AI 下令、设置 Rally、完成研究或被其他玩家操作时，不会向本地玩家播放应答和系统反馈。
- **世界事件**：`Impact`、`Death`、`Ability` 等事件可以来自任意阵营；它们是否最终可听由声源位置、监听相机和 3D 距离共同决定。
- **系统事件**：UI 与音乐没有单位所有者，分别通过 UI 与 Music Bus 播放。

这条规则集中在 `IWar3AudioAudiencePolicy`，模拟和 UI 不需要各自复制“是不是本地玩家”的判断。未来接入观战、回放或分屏时，可以替换策略，而不需要改 Cue 解析和播放器。

## 模型时间轴音效

MDX 元数据中的 Event Object 名称以 `SNDX` 开头时，后四位是 `AnimLookups.slk` 的事件码。例如 Footman 的 `SNDXDFOO` 会解析为 `DFOO → FootmanDeath`。`War3ModelActor` 只负责在动画时间跨过事件帧时发出纯数据事件；表现器补上单位/建筑/弹道的世界位置和阵营；音频控制器最后才把事件码解析成 Cue。

时间轴支持循环动画跨尾帧后重新从零开始，也会在切换序列时重置游标。由建造进度强制 seek 的动画不会回放已经跨过的声音。死亡、消散和尸体腐烂序列仍由权威死亡事件播放一次，时间轴里的同类 Cue 会被忽略，避免“死亡事件立即播一次、模型再播一次”的重复。

## 技能生命周期

`War3WorldAudioController.StartAbility` 同时处理一次性 `Effectsound` 和持续 `Effectsoundlooped`：持续声音会强制建立 loop handle，并返回 `War3AbilityAudioSession`。施法完成、中断、单位死亡或技能被驱散时，调用方必须通过 `StopAbility` 释放会话。播放器的 `StopEmitter` 是单位被整体移除时的兜底。

模拟层现已提供独立的 `AbilityEventStream`，生命周期为：

```text
Started → Impact（可出现零到多次）→ Ended
        ↘ Interrupted
```

事件携带稳定 sequence、技能 ID、施法者、目标类型/编号、世界位置和结束原因。`Completed`、`Canceled`、`CasterDied`、`TargetInvalid` 由玩法系统决定，音频层不反查技能内部状态。当前正式接入源是主动隐蔽状态机：Burrow/Reveal 开始、过渡完成和施法中死亡分别产生开始、命中/完成和中断事件；黑盒场景同时验证完整施法与施法者死亡中断。

`War3Rts.Audio.cs` 用独立 cursor 消费技能事件，并按 `(casterUnit, abilityId)` 管理循环会话。重复开始会先收掉旧会话，`Ended`/`Interrupted` 会淡出对应 loop，单位死亡还会执行 emitter 级兜底。事件流是可由回放重新生成的派生事实，不进入快照和状态哈希；热恢复会清空旧事件，避免恢复前的声音被重放。

主动隐蔽使用内容中立的 `active-concealment` / `active-reveal` ID，目前原版目录没有与这两个业务 ID 同名的 `Effectsound`，因此它验证生命周期但不会硬套不相干的 Warcraft 音效。之后真正接入 `Ainf`、`AHdr` 等数据驱动技能时，发布原始 ability ID 即可自动解析一次性和循环 Cue。

## Audio Bus

运行时会幂等创建这些 Bus：

```text
Master
├── Music
├── SFX
│   ├── Combat
│   ├── Ability
│   └── World
├── Voice
├── UI
├── Ambience
└── Cinematic
```

默认音量设置保存在 `user://war3_audio_settings.json`。`War3AudioMixSettingsCodec` 只负责读写和数值校验，后续设置 UI 可以直接绑定它，不需要改目录、语义控制器或模拟。

当前 Human 音乐从完整资产清单单独建立播放列表，在 Music Bus 上顺序播放。音乐使用非空间 `AudioStreamPlayer`，不会因为镜头移动改变响度，也不会占用 SFX/Voice 播放池。

## 3D 距离与衰减

空间策略以 SoundInfo 的 `WANT3D` 为准。带世界坐标的 3D Cue 使用 `AudioStreamPlayer3D`；UI、音乐和没有世界坐标的请求使用 `AudioStreamPlayer`。当前激活的 RTS `Camera3D` 是监听器，Warcraft 距离统一乘以 `War3AudioCatalogPolicy.WorldDistanceScale`（当前为 `0.025`）转换为 Godot 世界距离。

SoundInfo 的距离字段映射如下：

| Warcraft 字段 | Godot/运行时含义 |
| --- | --- |
| `MinDistance` | `AudioStreamPlayer3D.UnitSize`，进入近距离满响度区域的尺度 |
| `MaxDistance` | 保留为原始衰减范围参考和诊断值 |
| `DistanceCutoff` | `AudioStreamPlayer3D.MaxDistance`，超过后完全不可听 |

播放器使用 inverse-distance 衰减；播放请求在加载音频和占用 voice pool 之前还会做一次 `DistanceCutoff` 预裁剪。因此远处战斗音不仅听不到，也不会消耗解码与播放器并发。Footman 语音的原始 `3000 / 10000 / 100000` 当前对应 Godot 距离 `75 / 250 / 2500`。Doppler 暂时关闭，因为当前 RTS 镜头和步兵声源不需要车辆式的明显音高偏移。

`War3AudioAdmissionPolicy` 在资源加载前做第二层保护：同一 emitter + Cue 的短时间重复会被抑制；Animation、Ability、Impact 等语义分别有独立并发上限。当前 Animation 最多 16 路、Ability 12 路、Impact 24 路，底层世界播放器池仍保持 40 路总上限。循环音不使用短冷却，而由唯一 loop handle 防止同一 emitter 重复持有。

Godot 的 `AudioStreamPlayer3D.unit_size` 用于控制衰减尺度，`max_distance` 是硬截止距离；非空间音频则由 `AudioStreamPlayer` 播放。参见 [AudioStreamPlayer3D 官方文档](https://docs.godotengine.org/en/stable/classes/class_audiostreamplayer3d.html) 和 [Godot 音频流说明](https://docs.godotengine.org/en/stable/tutorials/audio/audio_streams.html)。

## 生成首批运行包

在项目根目录运行：

```powershell
powershell -ExecutionPolicy Bypass `
  -File .\tools\war3\Build-War3AudioRuntime.ps1
```

默认 `human-core` profile 覆盖当前玩法中的 16 个单位、11 个建筑及基础 UI 提示。脚本会：

1. 同步完整 Audio Catalog；
2. 根据单位绑定、语音族和武器材质关系收集可达 Cue；
3. 复制所需 WAV/MP3，并保持虚拟路径；
4. 从当前单位编辑器数据收集普通技能和英雄技能，并解析 `Effectsound` / `Effectsoundlooped`；
5. 扫描当前模型、弹道和特效元数据，只收集实际出现的 `SNDX` Cue；
6. 从 music 分类建立 Human 播放列表；
7. 写出包含 Cue、技能绑定、动画事件、音乐、文件数和字节数的 runtime manifest；
8. 对输出目录做根路径和目录名保护后才清理旧结果。

当前生成结果为 166 个 Cue、50 个 Human 可达技能绑定、27 个实际模型动画事件、4 首 Human 音乐、424 个音频文件、54,414,142 字节。新增的系统反馈包括 Rally、Shift 路点、建筑放置/开工/完工、界面错误和研究/升级完成；这些资源原本已在完整目录中，缺口来自 `human-core` 精简 profile 没有选入。

## 验证

编译：

```powershell
dotnet build .\rts-demo-1.csproj --no-restore
```

让 Godot 导入运行包：

```powershell
godot --headless --editor --path . --quit-after 600
```

构建脚本会安全重建运行包，因此旧 `.import` 旁车文件也会被删除。这里保留约 600 帧，让编辑器完成文件扫描和 424 个音频导入；等待窗口过短会在日志里出现 `Scan thread aborted`，随后运行时会把尚未导入的资源报告为缺失。

快速音频冒烟会校验完整目录、`hfoo` 绑定、Footman 语音、死亡、UI、Rally/路点/建筑/研究反馈、武器材质命中、Ainf/AHdr 技能生命周期、权威技能事件顺序、DFOO 时间轴映射、防连发/并发策略以及 Godot AudioStream 导入：

```powershell
godot --headless --path . .\war3_rts\War3Rts.tscn -- --war3-audio-smoke
```

通过标志：

```text
WAR3_AUDIO_SMOKE PASS cues=4352 units=837 abilities=801 animation_events=607 checked=15 runtime=424 runtime_abilities=50 runtime_animation_events=27 music=4
```

## 后续接入边界

- 技能：权威事件流、主动隐蔽来源、一次性/循环音频会话和中断回收已经接通；下一步建立通用法力、冷却、目标选择与 Buff 系统，并让 `Ainf`、`AHdr` 等实际技能发布同一合同。
- 环境：地图/天气系统只发布环境状态；Ambience 控制器负责交叉淡入淡出，不在单位 Tick 中播放。Lordaeron 等原始环境层主要是 MIDI + DLS，Godot 不能直接按现有 WAV/MP3 路径导入；需先做可重复的离线合成，再接入环境状态机。
- 音乐：Human 播放列表和独立 Music 播放器已接入；下一步补胜利、失败、交战状态切换与曲目交叉淡入淡出。
- 动画事件：当前 Human 单位、建筑、弹道和特效的 `SNDX` 已按时间轴播放；后续按新增种族 profile 扩展资源裁剪范围。
- UI 设置：绑定 `War3AudioMixSettings`，保存后重新应用 Bus；不要逐个遍历播放器改音量。
- 性能：已有语义并发限制、同 emitter 防连发、有界 voice pool 和流缓存；后续根据实战遥测调整上限，并补跨 emitter 的大规模同 Cue 聚合策略。
