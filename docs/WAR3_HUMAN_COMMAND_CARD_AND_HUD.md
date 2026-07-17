# 人族 Command Card 与 HUD 数据接入

本轮目标是让可玩人族的命令面板和信息面板由 Warcraft III 原始对象数据驱动，并让按钮状态与运行时业务状态一致。

## 数据边界

- 单位/建筑按钮位置：`HumanUnitFunc.Buttonpos`，使用 `slot = y * 4 + x` 映射到 4×3 Command Card。
- 单位/建筑快捷键：`HumanUnitStrings.Hotkey`。
- 技能按钮位置：技能对象 `Profile.Buttonpos`；切换形态优先使用 `UnButtonpos`。
- 技能学习图标：技能对象 `Profile.ResearchArt`，不再把被动技能的 `PASBTN` 图标误当成学习图标。
- 科技按钮位置、快捷键和分级图标：升级对象的 `Buttonpos`、`Hotkey` 与逐级 `Art`。
- 建造、训练、研究和升级关系：单位对象的 `Builds`、`Trains`、`Researches` 与 `Upgrade`。

当前标准可玩人族目录为 17 个单位、16 个建筑、21 项科技。`--war3-human-ui-self-test` 会逐项检查这些关系、槽位冲突、英雄技能位置与物品栏资格。

## 标准命令位置

普通单位命令固定为：移动 `(0,0)`、停止 `(1,0)`、保持位置 `(2,0)`、攻击 `(0,1)`。单位技能按原始 `Buttonpos` 放置；农民的建造入口和生产建筑的集结点位于 `(3,1)`。

建造子页不再按数组顺序填充。11 个可建造人族建筑的原始位置恰好覆盖 slot 0–10，slot 11 保留返回按钮。训练单位、科技和建筑升级同样使用各自 rawcode 的原始坐标。

## 科技状态

研究按钮现在区分：

- `Ready`：前置、资源、队列和研究建筑都允许；
- `Unavailable`：缺资源、缺前置或建筑不可用；
- `Queued`：该科技已在玩家研究队列中；
- `Completed`：单级科技已完成或多级科技达到最高级。

多级科技完成一级后会切换下一等级图标和等级徽标；达到最高级后按钮禁用并显示“完成”。命令签名包含状态、徽标、图标与快捷键，因此研究完成会立即刷新样式。

此前遗漏的 `Rhan` 动物作战训练、`Rhri` 长管火枪、`Rhlh` 改进型伐木效率、`Rhse` 魔法岗哨已经接入；`Rhac` 加强型石工技术的研究建筑修正为伐木场。

## 哨塔三分支

农民建造的是 `hwtw` 哨塔，而不是完成态的 `hgtw` 防御塔。哨塔可原地升级为：

- slot 8：`hgtw` 防御塔，需要伐木场；
- slot 9：`hctw` 炮塔，需要车间；
- slot 10：`hatw` 神秘之塔，无额外建筑前置。

为支持这一点，通用建筑升级目录从“每种源建筑只有一个目标”扩展为“一个源建筑可有多个分支”。活动订单仍通过唯一 profile ID 校验，热快照、回放和状态哈希继续使用完整升级 profile。

## 英雄技能学习

英雄有未分配技能点时，普通 Command Card 的 slot 7 显示技能学习按钮。点击后进入学习子页，四个英雄技能按原始 `(0..3,2)` 排列。每个按钮显示当前/下一等级，并由技能点、英雄等级、等级间隔和最高等级共同决定状态。学习成功后扣除技能点并返回普通命令页。

已学习的主动技能、切换技能和被动技能都保留在普通页的原始位置；被动技能完整显示但不可点击。

## 信息、肖像与物品栏

- 攻击和护甲信息使用正式 Warcraft CommandButtons 图标；防御塔也从原始攻击数据展示攻击均值与攻击类型。
- 肖像血条、法力条显示“当前值 / 最大值”。
- 英雄中央信息区显示等级、当前经验、下一级经验和经验进度条。
- 英雄通过 `AInv` 获得 6 格物品栏。
- 研究 `Rhpm` 背包技能后，原始 `Upgrades` 中声明支持 `Rhpm` 的普通单位获得 2 格物品栏。
- 其他单位和所有建筑显示正式 Human inventory cover，不伪造可用物品格。

当前物品系统尚未实现拾取、丢弃和物品技能，因此这里只接入资格与空槽表现，不生成虚假物品状态。

底部界面布局直接使用原始 1600×512 Human console atlas 的像素坐标，再统一按 `320 / 512` 缩放。Command Card 的原始格距为 87 像素，不能按近似的 58 屏幕像素累加；物品栏使用原始两列三行边界。无物品栏资格时，`HumanUITile-InventoryCover` 以与底图完全相同的缩放和纵向原点覆盖六格，并放置在底图之上。英雄可使用 `--war3-rts-hero-capture` 生成专用布局验收截图。

## 寻路占地与选中圈

- 建筑不再使用手工估算矩形。运行时读取对象数据的 `UnitData.pathTex`，解析对应 TGA 中红色的不可行走像素边界；一个 Warcraft pathing cell 映射为 8 个模拟单位。
- 蓝色但没有红色的 pathing cell 只限制建造，不会被误算成单位不可行走的硬占地。
- 单位碰撞半径独立于攻击距离等世界距离换算，按原始 `collisionSize / 3` 导入，最小半径为 7。
- 选中圈的外半径直接等于运行时 `NavigationRadius`，不再用模型无关的 `radius × 2.8` 放大。
- 建筑升级前后的硬占地必须一致；哨塔三种分支以及城镇大厅的两级升级均由专项测试校验。

## 小地图

HUD 每次快照会把可见世界区域投影成 `CameraViewBounds`，小地图用双描边矩形显示当前镜头范围。正常模式使用普通箭头光标；只有 `MinimapSignalMode` 为真时才使用瞄准光标。当前玩法尚未开放信号模式，因此正常小地图不会再一直显示瞄准样式。

## 验证命令

```powershell
godot --headless --path . -- --war3-human-ui-self-test
godot --headless --path . -- --war3-spatial-sizing-self-test
godot --headless --path . -- --war3-navigation-traversal-self-test
godot --headless --path . -- --building-upgrade-self-test
godot --headless --path . -- --ability-self-test
godot --headless --path . res://war3_rts/War3Rts.tscn -- --war3-rts-smoke
```
