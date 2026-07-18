namespace RtsDemo.Presentation;

public sealed record TestShowcaseEntry(
    string Id,
    string Title,
    string Category,
    string Summary);

/// <summary>
/// Pure presentation metadata for the interactive test browser. It receives the
/// executable case ids from the test runtime and never creates a simulation.
/// </summary>
public static class TestShowcaseCatalog
{
    public const string AllCategories = "全部分类";

    private static readonly IReadOnlyDictionary<string, TestShowcaseEntry> Metadata =
        CreateMetadata();

    public static TestShowcaseEntry[] Build(IEnumerable<string> caseIds)
    {
        ArgumentNullException.ThrowIfNull(caseIds);
        var ids = caseIds.ToArray();
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("Test case ids are empty or duplicated.");
        var missing = ids.Where(id => !Metadata.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Missing test presentation metadata: {string.Join(',', missing)}");
        return ids.Select(id => Metadata[id]).ToArray();
    }

    public static string[] Categories(IReadOnlyList<TestShowcaseEntry> entries) =>
        [AllCategories, .. entries.Select(value => value.Category)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)];

    public static TestShowcaseEntry[] Filter(
        IReadOnlyList<TestShowcaseEntry> entries,
        string? query,
        string? category)
    {
        query = query?.Trim() ?? string.Empty;
        category = string.IsNullOrWhiteSpace(category)
            ? AllCategories
            : category;
        return entries.Where(entry =>
                (category == AllCategories || entry.Category == category) &&
                (query.Length == 0 ||
                 entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 entry.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static Dictionary<string, TestShowcaseEntry> CreateMetadata()
    {
        var values = new[]
        {
            M("semantic-construction-contact-matrix", "真实施工接触矩阵", "专项修复", "使用全部正式建筑尺寸和二十四个方向，独立核对工人接触矩形后才开工。"),
            M("semantic-construction-resume-matrix", "换工人继续施工矩阵", "专项修复", "五种正式建筑中断施工后由不同方向的新工人继续，验证暂停期、真实接触和原工人命令互不改写。"),
            M("semantic-real-refinery-cycle", "真实精炼厂采气循环", "专项修复", "从正式建造基地、采矿攒钱、建精炼厂到反复采气交货，不直接改内部状态。"),
            M("semantic-unreachable-queue-release", "不可达命令释放队列", "专项修复", "首条移动被整墙隔断后必须明确失败，并依次执行后面的两条 Shift 移动。"),
            M("semantic-follow-body-range", "跟随单位身体边界", "专项修复", "生产单位集结到友军时不追身体中心，目标移动后跟随，死亡后前往最后位置。"),
            M("semantic-production-exit-restoration", "生产整边出口与命令恢复", "专项修复", "连续封锁真实建筑边界，验证友军让位、敌军硬阻挡、解封出生和原命令恢复。"),
            M("group-move-terminal-stability", "编队移动终点稳定", "基础移动", "验证整队右键移动完成后速度归零，且长时间不再换槽、回拉或抖动。"),
            M("arrival-hard-stop", "抵达目标直接停稳", "基础移动", "验证无阻挡单位达到巡航速度后不在目标前主动减速，并从最后一个移动 Tick 直接切换为零速度。"),
            M("dynamic-blockage-priority-matrix", "通用阻塞与挤压优先级", "基础移动", "验证同级挤压、高推低、低级与 Hold 阻挡后三秒就地结束，以及热恢复一致。"),
            M("dynamic-blockage-continuous-waves", "跨波次拥挤集结", "终点协作", "验证大队、小队、双人和单人连续进入同一拥挤目标后都能稳定结束。"),
            M("friendly-building-radial-interaction", "建筑矩形径向交互", "操作", "验证单位从自身方向沿建筑中心射线接近真实矩形边缘，而不是被吸到四个面中点。"),
            M("combat-idle-auto-acquire", "空闲单位自动警戒", "战斗", "验证普通战斗单位无需 AttackMove 也会自动索敌、追击并攻击警戒范围内的敌人。"),
            M("attack-move-squad-slot-resume", "攻击移动编队续行", "战斗移动", "验证编队接敌后恢复各自落点，不会丢失槽位并重新挤向同一个中心点。"),
            M("frontend-test-browser", "启动页与测试中心", "操作界面", "验证启动入口、测试目录、分类搜索、中文业务说明，以及通过统一 case id 切换测试。"),
            M("single-unit", "单单位基础移动", "基础移动", "验证单个单位寻路、直接停稳、边界约束与最终无重叠。"),
            M("attack-move-engage-resume", "攻击移动接敌后续行", "战斗", "验证 AttackMove 偏离路线接敌、击杀目标后恢复原终点。"),
            M("combat-event-stream", "战斗事件序列", "战斗", "验证攻击起手、命中、摧毁事件的顺序、伤害和零丢失。"),
            M("combat-damage-matrix", "护甲与属性伤害矩阵", "战斗", "验证 Light/Armored 克制、护甲、多段伤害和目录数据。"),
            M("combat-projectile-flight", "确定性投射物飞行", "战斗", "验证跟踪弹道、移动目标、ProjectileId 和热恢复一致性。"),
            M("combat-projectile-presentation", "弹道表现解耦", "战斗表现", "验证 Bolt、Orb、Volley、拖尾与命中提示不进入权威状态。"),
            M("combat-mobile-fire", "移动射击约束", "战斗", "对比固定与移动武器的前摇、冷却移动和命令取消语义。"),
            M("combat-target-selection", "自动目标评分", "战斗", "验证目标优先级、属性克制、锁定滞后和玩家显式目标。"),
            M("combat-contact-priority", "战斗接触优先级", "战斗移动", "验证固定前摇、近战、冷却、移动射击单位的有序推挤阻力。"),
            M("combat-building-defense", "建筑护甲与防御科技", "战斗", "验证不同建筑护甲、Fortification 科技和正式建筑受击。"),
            M("attack-move-leash-resume", "攻击移动脱离追击", "战斗", "验证敌人离开 leash 后放弃追击并恢复原路线。"),
            M("attack-move-command-isolation", "Move 与 AttackMove 隔离", "战斗", "同图验证普通移动忽略敌人，攻击移动主动接敌。"),
            M("attack-move-repeat-continuity", "重复攻击移动不中断", "战斗移动", "在绕障碍行军与接敌交战期间连续重发同一 AttackMove，验证路径、编队槽位、攻击前摇和续行均不被重置。"),
            M("attack-move-cancel", "交战命令取消", "战斗", "验证 Stop/Hold 取消 AttackMove，且保留各自不同的索敌语义。"),
            M("combat-ranged-ring", "远程原地开火", "战斗移动", "验证已在射程内的远程单位直接开火，不会为了环形站位而整体重排。"),
            M("combat-stop-hold-acquire", "Stop/Hold 索敌差异", "战斗", "验证 Stop 可局部追击，Hold 只攻击当前射程内目标。"),
            M("combat-multi-retarget", "多人连续换目标", "战斗", "验证多人依次消灭多个目标并稳定恢复 AttackMove。"),
            M("queued-waypoints", "Shift 路点队列", "操作", "验证多个移动路点按顺序执行并正确完成队列。"),
            M("queued-command-replace", "队列替换", "操作", "验证非 Shift 新命令清除旧队列并替换活动任务。"),
            M("queued-capacity-limit", "命令队列容量", "操作", "验证 16 条硬上限和超出容量后的明确诊断。"),
            M("control-group-recall", "控制编组召回", "操作", "验证编组保存、召回和双击聚焦的稳定成员。"),
            M("control-group-mixed-steal", "混合编组与抢组", "操作", "验证单位建筑混编以及 Alt/Shift 抢组语义。"),
            M("smart-command-sequence", "SmartCommand 连续任务", "操作", "验证右键语义在移动、攻击和队列中的组合执行。"),
            M("smart-command-gameplay-context", "SmartCommand 玩法上下文", "操作", "验证敌人、资源、友军建筑和施工目标按能力正确拆分。"),
            M("smart-command-shift-worker-tasks", "工人跨域 Shift 任务", "操作", "验证 Move→采集→施工续建以及失效任务有界跳过。"),
            M("operation-selection-camera", "选择与相机", "操作界面", "验证点选、框选、同类双击、缩放、边缘滚屏与聚焦。"),
            M("operation-mixed-command-card", "混合选择命令卡", "操作界面", "验证单位建筑子组、Tab 切换和快照命令卡。"),
            M("operation-target-command-mode", "目标指令模式", "操作界面", "验证 Move、AttackMove、Rally 的目标预览、确认与取消。"),
            M("operation-build-placement-mode", "建筑放置模式", "操作界面", "验证非法位置保持预览、合法位置正式下达施工。"),
            M("operation-production-group-batch", "多建筑批量生产", "操作界面", "验证同类生产建筑聚合下单、队列显示和逐建筑取消。"),
            M("minimap-interaction", "小地图交互", "操作界面", "验证世界坐标映射、视口框、聚焦和右键命令。"),
            M("command-log-replay", "确定性命令日志", "回放", "验证规范序列化、重复执行和最终状态 Hash 一致。"),
            M("command-replay-divergence", "回放分歧定位", "回放", "修改一条命令后定位首次确定性状态分歧 Tick。"),
            M("replay-package-world", "完整回放包", "回放", "验证初始世界、动态建筑、命令日志和资源身份打包。"),
            M("replay-checkpoint-resume", "Checkpoint 中途恢复", "回放", "验证从中间 Tick 继续回放与完整运行一致。"),
            M("replay-checkpoint-choke", "狭口 Checkpoint", "回放", "验证交通方向、排空与队列未来态可以确定性恢复。"),
            M("replay-hot-snapshot", "运行时热快照", "回放", "验证直接恢复模拟热状态、规范载荷与篡改拒绝。"),
            M("economy-replay-persistence", "经济回放持久化", "回放", "验证采集、返还和工人阶段在回放及热恢复中一致。"),
            M("economy-explicit-return-cargo", "显式返还资源", "经济玩法", "验证返还资源、Stop 后投递、矿气双向改派、命令日志、回放和热恢复。"),
            M("economy-return-cargo-dropoff-loss", "投递点失效与恢复", "经济玩法", "验证携货工人改道备用投递点、无投递点等待、失败码和恢复后续投。"),
            M("open-field", "开放场大编队", "群体移动", "验证开放区域中多单位编队分槽、避让和整队抵达。"),
            M("dense-formation", "密集编队", "群体移动", "验证高密度起点下 Steering、碰撞和最终收敛。"),
            M("opposing-streams", "对向人流", "群体移动", "验证两股对向单位的避让侧选择与通行。"),
            M("crossing-streams", "交叉人流", "群体移动", "验证垂直交叉编队的局部避碰与恢复。"),
            M("command-replace", "移动命令中途替换", "基础移动", "验证旧路径结果不会覆盖新的移动目标。"),
            M("rapid-reissue", "快速重复发令", "基础移动", "验证连续改目标时命令版本隔离和有限路径请求。"),
            M("destination-convergence", "拥挤终点收敛", "终点协作", "验证槽位交换、让路、溢出位置和最终全员收敛。"),
            M("destination-outer-ring", "外围封闭终点", "终点协作", "验证外围先到单位不会永久挡住内部槽位持有者。"),
            M("destination-overtake", "终点速度超车", "终点协作", "验证快慢单位混合时局部重匹配与让路。"),
            M("destination-corner-mixed", "角落混合半径终点", "终点协作", "验证墙角和不同单位尺寸下的有界收敛。"),
            M("clearance-portal-choice", "多尺寸 Portal 选择", "净空导航", "验证小单位走窄路、大单位自动选择宽路。"),
            M("clearance-dynamic-gap", "动态窄缝净空", "净空导航", "验证建筑形成窄缝后 Small 可过、Large 明确不可达。"),
            M("building-footprint-sizes", "四档建筑尺寸", "建筑导航", "验证 Small/Medium/Large/Huge footprint 的实际占用。"),
            M("building-placement-rules", "建筑放置分层规则", "建筑导航", "分别验证静态 Placement、动态单位开工校验、Hard Footprint Commit，以及旧组合错误码优先级。"),
            M("building-connectivity-guard", "全局连通保护", "建筑导航", "验证会切断地图的建筑被拒绝，安全建筑可放置。"),
            M("building-size-navigation", "多尺寸建筑绕行", "建筑导航", "验证四档建筑加入后单位实际改道并抵达。"),
            M("gameplay-profile-resource-runtime", "玩法 Profile Resource", "数据工作流", "验证单位与建筑 Profile Resource 转换后驱动真实运行时。"),
            M("clearance-bake-resource-runtime", "Clearance Bake Resource", "数据工作流", "验证烘焙网格、分层连通和路径查询使用正式资源。"),
            M("clearance-editor-preview", "净空编辑器预览", "数据工作流", "展示三档障碍轮廓、Portal 资格和建筑 footprint。"),
            M("clearance-incremental-chunks", "增量净空 Chunk", "数据工作流", "验证局部重采样与全量分析严格一致。"),
            M("resource-hot-reload", "资源热重载差异", "数据工作流", "验证 Navigation/Profile/Bake 差异和重建影响等级。"),
            M("clearance-bake-live-commit", "Bake 在线提交", "数据工作流", "验证两阶段校验、原子替换和活动单位重规划。"),
            M("resource-file-watch-workflow", "资源文件监听", "数据工作流", "验证写入风暴去抖、有限重试和安全自动提交。"),
            M("building-connectivity-diff-preview", "放置连通差异面板", "数据工作流", "展示建筑放置前三档拓扑变化与受影响 Chunk。"),
            M("economy-dual-resource", "双资源状态机（局部）", "经济玩法", "仅验证纯经济状态机、容量、枯竭和 Operational 节点循环；不证明真实 Refinery 建筑可采。"),
            M("economy-auto-patch-distribution", "自动分矿与饱和槽位", "经济玩法", "同一次右键把 12/16/24/32 个普通农民确定性分散到八片矿，验证每片矿一个并发采集槽、两个理想分配位、等待队列以及回放和热恢复。"),
            M("economy-mule-independent-mining", "MULE 独立采矿通道", "经济玩法", "验证 MULE 与普通农民可在同一矿片并采、第二个 MULE 等待、八矿 MULE 自动分散且普通 16/16 饱和度不变。"),
            M("economy-assignment-lifecycle", "分矿生命周期", "经济玩法", "验证资源 Rally 填补最空矿片，以及矿片枯竭后 Active、Assigned、Waiting 收缩并在剩余矿簇确定性重分配。"),
            M("economy-mining-income-curve", "采矿边际收入曲线", "经济玩法", "使用 5 点携带量和 1.99 秒采集参数，对比近远矿以及每片 1/2/3/4 个普通农民的有限时间收入。"),
            M("economy-mineral-walk-collision-matrix", "采矿穿行碰撞矩阵", "经济玩法", "24 个农民在采矿与返矿途中穿过友军 Hold、敌军和大型单位，但仍绕过正式建筑；Stop、Hold、Attack 后恢复正常碰撞。"),
            M("economy-mass-mining", "大规模多基地采矿", "经济玩法", "96 个农民在四个基地与 32 片矿之间持续往返，验证最近投递边界、完整循环和高密度穿行。"),
            M("economy-expansion-saturation", "扩张与饱和度", "经济玩法", "验证双基地归属、工人转场、饱和度和基地摧毁。"),
            M("player-visibility-authority", "视野与命令权限", "比赛规则", "验证战争迷雾、探索记忆和不可见目标权限。"),
            M("construction-player-known-placement", "施工已知信息边界", "建造生产", "验证友军内预放置、可见敌军拒绝、隐藏敌军不改变预览，以及到场后的权威重验和统一反馈。"),
            M("concealment-detection-construction", "隐形侦测与施工阻挡", "比赛规则", "验证可见地面与单位侦测分层、埋地接触、权威施工阻挡、显形后命令权限及确定性恢复。"),
            M("active-burrow-detection-lifecycle", "主动潜地生命周期", "比赛规则", "验证潜地与解除过渡、视野缩减、行动限制、异层穿行、侦测授权、队列切换和确定性恢复。"),
            M("alliance-shared-vision-team-victory", "2v2 阵营与共享视野", "比赛规则", "验证 Self/Ally/Enemy 关系、盟友共享视野和侦测、友军伤害拒绝、盟友施工占位及联盟共同胜利。"),
            M("match-capability-elimination", "比赛失败与终局", "比赛规则", "验证建立存在、基地损失、玩家失败和胜负锁定。"),
            M("ai-modular-skirmish", "模块化 AI 完整对局", "AI 对局", "验证单 AI 从经济发展、科技扩张到摧毁敌方基地。"),
            M("ai-dual-runtime-replay", "双 AI 热恢复与回放", "AI 对局", "验证错峰双 AI 的策略未来态、热恢复和纯命令回放。"),
            M("ai-continuous-encounter", "双 AI 持续遭遇战", "AI 对局", "展示双方开局、发展、扩张、攀科技、战损补兵和持续互攻。"),
            M("construction-gameplay-buildings", "正式建筑施工", "建造生产", "验证五类建筑、施工暂停续建、取消退款和建筑摧毁。"),
            M("construction-reservation-hard-commit", "施工预占与硬提交", "建造生产", "验证施工幽灵不阻挡通行、阻止重复放置，到场后才原子提交硬占用。"),
            M("construction-multi-unit-eviction", "施工多单位确定性让位", "建造生产", "验证四档建筑、1/8/32 占位单位的唯一撤离槽、Hold 保守等待、订单保留和热恢复。"),
            M("construction-blocker-policy-matrix", "施工阻挡策略矩阵", "建造生产", "验证己方 Idle 自动让位，Hold 与采集任务保守等待，盟友和敌军由各自玩家解除，以及已知占位的预览拒绝。"),
            M("construction-queued-builds", "Shift 连续建造队列", "建造生产", "验证多尺寸建筑软预占、逐项开工重验、静态失效跳过、动态友军让位、单项取消、Builder 死亡和完成后回矿。"),
            M("construction-under-build-defense", "施工护甲完成边界", "建造生产", "验证施工期有效护甲恒为零，完成 Tick 后才应用建筑基础护甲与防御科技。"),
            M("building-type-resource-runtime", "建筑类型 Resource", "数据工作流", "验证建筑目录 Resource、尺寸、功能和运行时建造。"),
            M("production-replay-persistence", "生产热恢复", "回放", "验证生产中、等待出口和已出生三个阶段的恢复一致性。"),
            M("production-catalog-resource-runtime", "生产目录 Resource", "数据工作流", "验证兵种、配方、前置和战斗 Profile 从目录加载。"),
            M("production-rally-smart-targets", "Rally 智能目标", "建造生产", "验证地面、资源和友军 Rally 的正式目标协议。"),
            M("production-building-prerequisites", "生产建筑前置", "建造生产", "验证缺少前置时拒绝，建筑完成后生产开放。"),
            M("technology-research-upgrades", "研究与正式升级", "建造生产", "验证多级研究、互斥路线、取消和战斗数值生效。"),
            M("construction-replay-persistence", "施工回放持久化", "回放", "验证活动施工、Builder、资源账本和命令恢复。"),
            M("combat-attack-building", "单位攻击建筑", "战斗", "验证 SmartCommand 攻击建筑、摧毁、占用移除和热恢复。"),
            M("shared-target-reservations", "跨命令目标预留", "终点协作", "验证不同命令批次共享目标区域时仍分配唯一位置。"),
            M("stop-command", "Stop 命令", "基础移动", "验证 Stop 立即取消路径并在原地稳定停下。"),
            M("hold-command", "Hold 命令", "基础移动", "验证 Hold 保持位置且碰撞后不漂移。"),
            M("mixed-radii", "混合单位半径", "群体移动", "验证不同物理半径单位共同避让、分槽与抵达。"),
            M("boundary-target", "边界目标", "基础移动", "验证靠近地图边缘的目标会按单位半径安全约束。"),
            M("dynamic-local-invalidation", "动态局部失效", "动态导航", "验证新增建筑只使相交路径失效并局部重规划。"),
            M("dynamic-building-detour", "动态建筑绕行", "动态导航", "验证移动途中新增建筑后单位重新寻路绕行。"),
            M("dynamic-building-remove", "移除建筑恢复通路", "动态导航", "验证动态占用移除后路线恢复和单位继续抵达。"),
            M("dynamic-portal-reroute", "Portal 动态改道", "动态导航", "验证活动高层路线关闭后改走替代 Portal。"),
            M("dynamic-group-reroute", "编队共享动态改道", "动态导航", "验证同组单位共享一次高层替代路线而非逐个规划。"),
            M("navigation-resource-runtime", "导航 Resource 运行时", "数据工作流", "验证正式地图 Resource 的障碍、Portal、Edge 与 Choke。"),
            M("portal-choke", "Portal 狭口通行", "狭口交通", "验证编队经高层 Portal 路线进入单向狭口。"),
            M("reverse-choke", "反向狭口通行", "狭口交通", "验证相反方向进入同一狭口的路线和交通状态。"),
            M("bidirectional-choke-balanced", "均衡双向狭口", "狭口交通", "验证人数相近的双向队列公平轮换且无方向冲突。"),
            M("bidirectional-choke-asymmetric", "非对称双向狭口", "狭口交通", "验证少量与大量单位竞争时仍保证小队不会饿死。"),
            M("bidirectional-choke-waves", "多波次双向狭口", "狭口交通", "验证分批到达、排空切向和有界最大等待。"),
            M("hold-blocked-choke", "Hold 堵口", "狭口恢复", "验证固定阻挡存在时交通安全、排队与有限恢复。"),
            M("temporary-blocker-recovery", "临时阻挡恢复", "狭口恢复", "验证阻挡移除后单位经过恢复阶梯继续抵达。"),
            M("unreachable-recovery-limit", "不可达重试上限（底层）", "狭口恢复", "仅验证导航重试有界；不证明业务订单释放或 Shift 队列推进。"),
            M("large-group-192", "192 单位压力场", "性能压力", "验证 192 单位大编队的有限分配、碰撞和最终抵达。")
        };
        return values.ToDictionary(value => value.Id, StringComparer.Ordinal);
    }

    private static TestShowcaseEntry M(
        string id,
        string title,
        string category,
        string summary) => new(id, title, category, summary);
}
