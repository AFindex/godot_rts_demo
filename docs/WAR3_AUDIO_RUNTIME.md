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
| 语义策略 | `War3WorldAudioController` / `War3AudioAudiencePolicy` | 把“选择、确认、命中、死亡、生产完成”转换成 Warcraft Cue，并按本地玩家身份决定是否可听 |
| Godot 播放 | `GodotWar3AudioPlayback` | ResourceLoader、2D/3D 播放器池、抢占、循环句柄、流缓存 |
| 音乐播放 | `GodotWar3MusicPlayer` | 独立加载播放列表，在 Music Bus 上顺序播放，不占用 SFX 播放池 |
| 混音配置 | `War3AudioMixSettings` / `War3AudioBusInstaller` | 音量持久化、Bus 创建与分组 |
| 项目桥接 | `War3Rts.Audio.cs` | 独立消费模拟事件流，解析项目单位 ID，不侵入确定性模拟 |

模拟层不知道 Cue、音频文件、Godot 节点或 Bus；播放器也不知道单位、武器和战斗规则。之后接技能、天气或剧情音频时，应继续只向语义控制器提供事实和业务生命周期。

## 当前事件映射

| 运行时事实 | Cue 规则 | 空间类型 |
| --- | --- | --- |
| 点击 HUD 命令 | `InterfaceClick` | 2D |
| 选中单位 | `{voiceSet}What` | 3D |
| 移动、停止、保持命令成功 | `{voiceSet}Yes` | 3D |
| 攻击命令成功 | `{voiceSet}YesAttack` | 3D |
| 生产完成 | `{voiceSet}Ready` | 3D |
| 单位死亡 | `{voiceSet}Death` | 3D |
| 攻击命中 | `{weaponSoundFamily}{targetImpactMaterial}` | 3D |

选择音只在用户实际选择入口触发，不挂在通用的 `RefreshSelection` 上；命令语音只在模拟接受命令后触发。战斗和玩法事件使用音频层自己的 sequence cursor，不会夺走 HUD、特效或诊断模块的事件。

## 受众与所有权策略

音效是否可听分成两层判断，不能只靠距离：

- **本地玩家私有语音**：`Selection`、`Command`、`AttackCommand`、`UnitReady` 只有声源单位属于本地玩家时才播放。敌方单位生成、被 AI 下令或被其他玩家操作时，不会向本地玩家播放应答语音。
- **世界事件**：`Impact`、`Death`、`Ability` 等事件可以来自任意阵营；它们是否最终可听由声源位置、监听相机和 3D 距离共同决定。
- **系统事件**：UI 与音乐没有单位所有者，分别通过 UI 与 Music Bus 播放。

这条规则集中在 `IWar3AudioAudiencePolicy`，模拟和 UI 不需要各自复制“是不是本地玩家”的判断。未来接入观战、回放或分屏时，可以替换策略，而不需要改 Cue 解析和播放器。

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
4. 从 music 分类建立 Human 播放列表；
5. 写出包含 Cue、音乐、文件数和字节数的 runtime manifest；
6. 对输出目录做根路径和目录名保护后才清理旧结果。

当前生成结果为 140 个 Cue、4 首 Human 音乐、395 个音频文件、52,671,864 字节。

## 验证

编译：

```powershell
dotnet build .\rts-demo-1.csproj --no-restore
```

让 Godot 导入运行包：

```powershell
godot --headless --editor --path . --quit-after 600
```

构建脚本会安全重建运行包，因此旧 `.import` 旁车文件也会被删除。这里保留约 600 帧，让编辑器完成文件扫描和 395 个音频导入；等待窗口过短会在日志里出现 `Scan thread aborted`，随后运行时会把尚未导入的资源报告为缺失。

快速音频冒烟会校验完整目录、`hfoo` 绑定、Footman 语音、死亡、UI、武器材质命中以及 Godot AudioStream 导入：

```powershell
godot --headless --path . .\war3_rts\War3Rts.tscn -- --war3-audio-smoke
```

通过标志：

```text
WAR3_AUDIO_SMOKE PASS cues=4352 units=837 checked=5 runtime=395 music=4
```

## 后续接入边界

- 技能：读取 `audio_refs/abilities`，由施法开始、持续、命中、结束四类业务事件控制，循环音必须持有并释放 loop handle。
- 环境：地图/天气系统只发布环境状态；Ambience 控制器负责交叉淡入淡出，不在单位 Tick 中播放。Lordaeron 等原始环境层主要是 MIDI + DLS，Godot 不能直接按现有 WAV/MP3 路径导入；需先做可重复的离线合成，再接入环境状态机。
- 音乐：Human 播放列表和独立 Music 播放器已接入；下一步补胜利、失败、交战状态切换与曲目交叉淡入淡出。
- 动画事件：模型播放器消费 `animation_event_map.json`，按动画时间轴发请求；不要每帧扫描动画名称。
- UI 设置：绑定 `War3AudioMixSettings`，保存后重新应用 Bus；不要逐个遍历播放器改音量。
- 性能：保持有界 voice pool 和流缓存，后续按类别增加并发限制与 NODUPEUSERNAMES 冷却策略。
