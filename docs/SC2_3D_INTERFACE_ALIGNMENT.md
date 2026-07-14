# 3D Demo：StarCraft II 操作界面与 Rally 对齐专项

## 目标与边界

本专项把 3D Demo 的操作界面从“调试按钮集合”改造成稳定的 RTS 操作壳，并补齐 Rally。它只连接现有纯 C# 模拟能力，不修改寻路、碰撞、战斗判定、经济规则或 AI 决策。

边界固定为两条单向数据流：

```text
Godot 输入 / HUD 点击 -> 语义化 UI Intent -> RtsEncounter3DDemo -> 现有 RtsSimulation 命令
RtsSimulation + 当前选择 -> 不可变 UI Snapshot -> HUD / World Presenter
```

HUD 不保存 `RtsSimulation`，World Presenter 不下达命令；目录、可用性和选择子组由独立 adapter 组合。后续换皮、改布局、换图标、增加本地化，不要求修改模拟层。

## 调研结论

1. SC2 的常规布局是右上资源，底部左侧小地图、底部中间选择/单位信息、底部右侧命令区。相关界面图的文字说明明确标注了这四个区域：[界面布局图说明](https://www.researchgate.net/figure/A-StarCraft-screenshot-of-a-Protoss-base-with-annotations-The-interface-heads-up_fig4_278641976)。本 Demo 采用相同的信息层级，但不复制 Blizzard 美术资产。
2. 命令按钮有快捷键提示；框选、`Ctrl+单击`、双击同类型、`Shift` 增减选择、Tab 切换子组、数字控制组和双击数字聚焦都是 SC2 的核心操作层语义：[Blizzard Special Control](https://news.blizzard.com/en-us/article/4552955/game-guide-special-control)、[Blizzard Simplified Controls](https://news.blizzard.com/en-us/article/6640645/game-guide-simplified-controls)。
3. 小地图位于左下，显示可见单位/建筑和视口；左键移动镜头，选中单位后可在小地图下达移动或攻击命令：[Liquipedia Minimap](https://liquipedia.net/starcraft2/Minimap)。
4. 所有产兵建筑都能设置 Rally。可用命令按钮进入目标模式，也可选择建筑后直接右键目标；同类型多建筑可以统一训练和统一 Rally：[Blizzard Buildings Guide](https://news.blizzard.com/en-us/article/4488317/game-guide-buildings)。
5. 基地 Rally 到矿物或气矿会让新工人直接采集；Rally 可指向友军单位。目标在出兵前死亡则不执行，出兵后死亡则前往最后位置。底层模拟已经覆盖这些规则，本专项只暴露输入和反馈：[Blizzard Buildings Guide](https://news.blizzard.com/en-us/article/4488317/game-guide-buildings)。
6. SC2 允许单位和生产建筑存在于同一控制组；右键军队时，单位收到场景命令，生产建筑同时更新 Rally：[Blizzard Special Control](https://news.blizzard.com/en-us/article/4552955/game-guide-special-control)。3D Demo 因此不把“单位右键”和“建筑右键”设计成互斥分支。

## 本轮实现规格

### HUD

- 战场顶部不再放全宽横条；资源、人口和时间压到右上。
- 底部固定三段：左侧小地图与控制组，中间选择详情/生命/生产队列/Rally 状态，右侧固定 3×5 命令卡。
- 命令卡只消费 `CommandCardSnapshot`，按钮槽位和快捷键由 UI layout 映射；禁用原因进入 tooltip/status，不暴露模拟对象。
- 多选时显示总数与类型子组；Tab/Shift+Tab 切换活动子组，命令仅作用于活动子组。
- 去掉全知敌军统计。状态信息只用于命令确认、失败原因和当前目标模式。

### Rally

- Town Hall 与 Production 建筑显示 `Set Rally` 命令。
- 选中生产建筑后右键地面、资源或友军单位可直接设置；按 `Y` 或点击命令卡进入显式目标模式。
- 多个有效生产建筑一次设置同一 Rally；混合选择时单位照常收到 Smart Command，生产建筑同时设置 Rally。
- 选中建筑时显示从建筑矩形边缘到目标的 Rally 线、目标环和方向箭头；友军单位目标的位置实时跟随。
- 中央信息面板显示 `Not set`、共同目标或 `Mixed rally`，并显示聚合生产队列。

### 鼠标、选择与镜头配套

- 单击选择；Shift 单击增减；框选；Ctrl 单击或双击选择当前镜头内同类型单位。
- Tab 切换混合选择子组。
- `Ctrl+0..9` 覆盖控制组，`Shift+0..9` 添加，`0..9` 召回，双击数字聚焦。
- `F1` 选择/循环空闲工人；Backspace 循环己方基地。它们只改变本地选择与镜头。
- 右键命令、Attack Move、Build、Rally 使用不同颜色的短时反馈；右键或 Escape 取消目标模式。

## 验收门槛

- HUD、adapter、world presenter 之间只有 snapshot/intent，没有 HUD 读取模拟或 presenter 下命令。
- Rally 地面/资源/友军、多建筑、混合单位+建筑右键都能从 3D Demo 触发。
- 资源 Rally 后的新工人继续由现有底层流程自动采矿。
- Debug/Release 构建无警告；全量 self-test、3D smoke、AV1 录像和媒体校验通过。
- 录像必须能看清底部三段 HUD、右上资源、Rally 目标模式和世界 Rally 线。

## 明确不做

- 不改寻路、局部避障、拥堵恢复、生产出口和采矿分配。
- 不复制 SC2 商标、字体、图标或贴图；只对齐信息架构、交互层级和科幻控制台视觉语言。
- 不在本轮增加新兵种、技能树、建筑玩法或网络同步协议。
