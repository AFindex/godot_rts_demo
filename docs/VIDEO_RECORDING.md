# 测试录像与 FFmpeg 工具链

更新日期：2026-07-11

## 固定约定

规范录像统一使用：

- 容器：WebM。
- 视频：AV1，FFmpeg `libsvtav1`。
- 质量：CRF 32。
- 编码速度：preset 8。
- 像素格式：`yuv420p`。
- 音频：删除；自动测试录像没有需要保留的业务音频。
- 元数据与章节：删除，减少非业务差异。

CRF 是固定质量模式，不为每段画面强行分配相同码率。静态背景会得到很小的文件，
192 单位压力场等高变化画面会获得更多码率，从而保住 HUD 细字、单位轮廓和路径线。

## FFmpeg 获取

`tools/get_ffmpeg.ps1` 固定下载 Gyan FFmpeg 8.1.2 Full Build。FFmpeg 官方 Windows
下载页把 Gyan 列为预编译 Windows 构建来源；Full Build 包含 `libsvtav1`，Essentials
Build 不包含该编码器。

- 固定资产：`ffmpeg-8.1.2-full_build.7z`。
- SHA-256：`0fff188997a499b5382e0f66e845d4556c48c54f0113ebed4853d556dbdd7059`。
- 缓存：`tools/.cache/ffmpeg/8.1.2/`。
- 解压：Windows 自带的 bsdtar/libarchive。

下载、哈希验证、解压和 `libsvtav1` 能力验证全部由脚本完成。缓存被 `.gitignore`
排除，因此仓库只保存可审查的下载规则，不分发约 167MB 的 GPLv3 工具二进制。

## 自动录制

```powershell
.\tools\record_tests.ps1
.\tools\record_tests.ps1 -Case minimap-interaction -Fps 30
```

每个用例依次执行：

```text
Godot Movie Maker → <case>.capture.avi
→ FFmpeg libsvtav1 → <case>.partial.webm
→ ffprobe 校验 codec / width / height / frame count
→ 原子改名为 <case>.webm
→ 删除临时 AVI
→ 写入 manifest.json
```

独立演示使用同一编码和校验链：

```powershell
.\tools\record_demo.ps1 -Demo 3d-encounter
.\tools\record_demo.ps1 -Demo terrain-traversal
.\tools\record_demo.ps1 -Demo terrain-vision
.\tools\record_demo.ps1 -Demo terrain-authoring
```

`terrain-traversal` 在编码前必须取得 `RTS_TERRAIN_DEMO_PASS`，Manifest 会保留到达数、非法穿崖、浅水放置结果、Bake 兼容性、自动拓扑三档区域数、坡道数量/宽度、高层入口点/Choke ID 和最小单位间距；业务失败时不产生可提交的 WebM。

`terrain-vision` 必须取得 `RTS_TERRAIN_VISION_DEMO_PASS`，并同时证明初始高地隐藏、无视野攻击拒绝、高架观察、弹道发射后丢失视野仍命中、盟友共享高地视野以及烟雾内外遮挡；任一断言失败都不会生成可提交结果。

`terrain-authoring` 必须取得 `RTS_TERRAIN_AUTHORING_DEMO_PASS`。结果需要同时证明初始地图可导出、表面笔刷不串改玩法字段、断裂坡道会在准确格子被拒绝、修复后 Vision/Buildable/Creep 可分别修改，以及运行时资源保存并重新加载后哈希一致。录像仍统一使用 AV1/WebM、CRF 32、preset 8。

Godot 或编码失败时保留临时 AVI 供诊断，最终命令返回失败；不会把 partial 文件当成
成功录像。Manifest 保存源/目标字节数、codec、container、CRF、preset 和业务测试结果。

## 单文件与历史迁移

```powershell
.\tools\compress_test_video.ps1 `
  -InputPath .\some-test.avi `
  -OutputPath .\some-test.webm `
  -DeleteSource

.\tools\compress_test_videos.ps1
.\tools\verify_test_videos.ps1
```

批量工具递归处理 `test_videos/`，逐段更新同目录 manifest，并生成
`test_videos/compression_report.json`。只有完整验证成功后才删除源文件。
`verify_test_videos.ps1` 会拒绝任何 AVI、partial 文件、非 AV1 流、空帧流和
manifest 文件名/大小不一致，可作为提交前门禁。

## 当前迁移指标

| 项目 | 数值 |
|---|---:|
| 历史录像 | 85 段 |
| AVI 总大小 | 3,309,160,498 B |
| AV1/WebM 总大小 | 228,515,601 B |
| 保留比例 | 6.91% |
| 节省空间 | 3,080,644,897 B |

抽样检查覆盖 192 单位压力场、Clearance Editor 细字/轮廓、多人战斗和 Minimap；
1280×720 HUD、路径线、单位边框和小地图细点均保持可辨认。

## 来源

- FFmpeg 下载与 Windows 构建入口：https://ffmpeg.org/download.html
- Gyan Windows Build 版本、许可与库清单：https://www.gyan.dev/ffmpeg/builds/
- FFmpeg `libsvtav1` 参数：https://ffmpeg.org/ffmpeg-codecs.html#libsvtav1
- SVT-AV1 FFmpeg 用法与 CRF/preset 说明：https://gitlab.com/AOMediaCodec/SVT-AV1/-/blob/master/Docs/Ffmpeg.md
