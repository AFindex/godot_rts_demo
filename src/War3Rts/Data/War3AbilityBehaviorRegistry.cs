using RtsDemo.Simulation;

namespace War3Rts.Data;

public enum War3AbilityRuntimeSupportStatus : byte
{
    Implemented,
    ImplementedGameplay,
    Delegated,
    PresentationOnly,
    NotApplicable,
    Blocked,
    Unclassified
}

public enum War3AbilityCompilerKind : byte
{
    None,
    Defend,
    Heal,
    InnerFire,
    Dispel,
    Invisibility,
    Polymorph,
    Slow,
    SpellSteal,
    Charm,
    MagicImmunity,
    Feedback,
    Flare,
    FragmentationShards,
    DetectionAura,
    FlakCannons,
    Barrage,
    Cloud,
    SiphonMana,
    Blizzard,
    SummonWaterElemental,
    BrillianceAura,
    MassTeleport,
    StormBolt,
    ThunderClap,
    Bash,
    Avatar,
    HolyLight,
    DivineShield,
    DevotionAura,
    Resurrection,
    FlameStrike,
    Banish,
    DrainMana,
    SummonPhoenix,
    MilitiaTransform,
    BuildingMilitiaCall,
    Repair,
    Ensnare,
    Cripple,
    Bloodlust,
    Rejuvenation,
    FaerieFire
}

public sealed record War3AbilityBehaviorDescriptor(
    string BaseCode,
    AbilityActivationKind Activation,
    bool AutoCastDefault,
    War3AbilityCompilerKind Compiler,
    War3AbilityRuntimeSupportStatus Status,
    string Reason)
{
    public bool HasPrototypeCompiler =>
        Compiler != War3AbilityCompilerKind.None;
}

/// <summary>
/// Content compiler registry keyed by Warcraft baseCode. Rawcode variants can
/// share a behavior family while retaining their own level data and visuals.
/// Status is intentionally conservative: a prototype effect is not considered
/// complete until its Warcraft parity tests pass.
/// </summary>
public static class War3AbilityBehaviorRegistry
{
    private static readonly Dictionary<string, War3AbilityBehaviorDescriptor>
        Values = Create();
    private static readonly Dictionary<string, War3AbilityBehaviorDescriptor>
        RawOverrides = CreateRawOverrides();

    public static IReadOnlyCollection<War3AbilityBehaviorDescriptor> All =>
        Values.Values;

    public static bool TryGet(
        string baseCode,
        out War3AbilityBehaviorDescriptor descriptor) =>
        Values.TryGetValue(baseCode, out descriptor!);

    public static War3AbilityBehaviorDescriptor Resolve(string baseCode) =>
        TryGet(baseCode, out var descriptor)
            ? descriptor
            : new War3AbilityBehaviorDescriptor(
                baseCode,
                AbilityActivationKind.Passive,
                false,
                War3AbilityCompilerKind.None,
                War3AbilityRuntimeSupportStatus.Unclassified,
                "尚未建立行为家族分类。原始数据仍保留，不生成臆测效果。");

    public static War3AbilityBehaviorDescriptor Resolve(
        string baseCode,
        string rawId) =>
        RawOverrides.TryGetValue(rawId, out var descriptor)
            ? descriptor
            : Resolve(baseCode);

    public static string StatusText(War3AbilityRuntimeSupportStatus status) =>
        status switch
        {
            War3AbilityRuntimeSupportStatus.Implemented => "implemented",
            War3AbilityRuntimeSupportStatus.ImplementedGameplay =>
                "implemented_gameplay",
            War3AbilityRuntimeSupportStatus.Delegated => "delegated",
            War3AbilityRuntimeSupportStatus.PresentationOnly =>
                "presentation_only",
            War3AbilityRuntimeSupportStatus.NotApplicable => "not_applicable",
            War3AbilityRuntimeSupportStatus.Blocked => "blocked",
            _ => "unclassified"
        };

    private static Dictionary<string, War3AbilityBehaviorDescriptor> Create()
    {
        var result = new Dictionary<string, War3AbilityBehaviorDescriptor>(
            StringComparer.Ordinal);

        void Pending(
            string id,
            AbilityActivationKind activation,
            string reason) => result.Add(
            id,
            new War3AbilityBehaviorDescriptor(
                id, activation, false, War3AbilityCompilerKind.None,
                War3AbilityRuntimeSupportStatus.Blocked,
                reason));

        void Gameplay(
            string id,
            AbilityActivationKind activation,
            War3AbilityCompilerKind compiler,
            string reason) => result.Add(
            id,
            new War3AbilityBehaviorDescriptor(
                id, activation, false, compiler,
                War3AbilityRuntimeSupportStatus.ImplementedGameplay,
                reason));

        void Delegated(
            string id,
            AbilityActivationKind activation,
            string reason) => result.Add(
            id,
            new War3AbilityBehaviorDescriptor(
                id, activation, false, War3AbilityCompilerKind.None,
                War3AbilityRuntimeSupportStatus.Delegated,
                reason));

        Delegated("AIrg", AbilityActivationKind.Instant,
            "物品持续生命/魔法恢复由 War3ItemEffectRuntime 承载；持续时间、区域和 DataA/DataB 来自 Ability JSON。");
        Pending("Amec", AbilityActivationKind.Instant,
            "机械小玩艺已可生成侦察单位，但原版随机 critter 选择与完整单位 profile 尚未数据化。");
        Delegated("AIhe", AbilityActivationKind.Instant,
            "物品即时生命恢复由 War3ItemEffectRuntime 承载，恢复量和冷却来自 Ability JSON。");
        Delegated("AIma", AbilityActivationKind.Instant,
            "物品即时魔法恢复由 War3ItemEffectRuntime 承载，恢复量和冷却来自 Ability JSON。");
        Delegated("AItp", AbilityActivationKind.TargetUnit,
            "回城卷轴由物品模块承载，施法时间、区域和目标前置来自 Item/Ability JSON。");
        Delegated("AIbl", AbilityActivationKind.TargetPoint,
            "象牙塔由物品与建造模块协作承载，创建单位和建造时间来自 Ability JSON。");
        Delegated("AIfb", AbilityActivationKind.Passive,
            "火焰之球由物品与 CombatStore 武器 profile 承载，伤害和区域来自 Ability JSON。");
        Delegated("ANsa", AbilityActivationKind.TargetUnit,
            "避难权杖由物品模块承载，范围、冷却、恢复延迟和每秒生命来自 Ability JSON。");

        Gameplay("Adef", AbilityActivationKind.Toggle,
            War3AbilityCompilerKind.Defend,
            "减伤、输出、移速、投射/魔法偏转概率与倍率、失误率和科技需求均由 JSON 编译；开关状态已接入确定性战斗结算、热载与回放。");
        Gameplay("AInv", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.None,
            "AInv/Aihn 的容量、死亡掉落、使用、取得、丢弃标志和科技前置均由 JSON/元数据编译；商店、HUD、地面物品、距离拾取、主动丢弃、死亡散落及确定性运行时快照已落地。");
        result.Add("Ahar", new War3AbilityBehaviorDescriptor(
            "Ahar", AbilityActivationKind.Passive, false,
            War3AbilityCompilerKind.None,
            War3AbilityRuntimeSupportStatus.Delegated,
            "采集由 EconomySystem 承载；仍需建立 rawcode 到委托测试的覆盖链接。"));
        Gameplay("Amil", AbilityActivationKind.Toggle,
            War3AbilityCompilerKind.MilitiaTransform,
            "农民/民兵双向形态、最近己方城镇大厅接触、45 秒到期还原和工人权限切换已落地。");
        Gameplay("Amic", AbilityActivationKind.Toggle,
            War3AbilityCompilerKind.BuildingMilitiaCall,
            "主城/城堡按原始 2000 范围号召农民、指定施令建筑接触、关闭时提前复工与建筑命令回放已落地。");
        Gameplay("AIta", AbilityActivationKind.TargetPoint,
            War3AbilityCompilerKind.Flare,
            "目标点、范围、持续时间、冷却、科技前置和揭露特效均由 Ability JSON 驱动；建筑与物品共用 Reveal 效果语义。");
        Gameplay("Arep", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Repair,
            "资源成本率、修理耗时率、协助建造成本/速率和海上修理距离均由 JSON 编译；建筑/机械单位持续修理、自动寻找受损目标、比例扣费、在建协作、取消、热载与回放已落地。");
        Gameplay("Aens", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Ensnare,
            "普通/英雄持续时间、空军坠落时间/高度、近战攻击范围、目标层、科技前置与 Bena/Beng 表现均由 JSON 编译；目标禁行并临时按地面层承受攻击。");
        Gameplay("Acri", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Cripple,
            "移动、攻击速度和攻击力削减、普通/英雄持续时间、目标层、消耗冷却、科技前置与 Bcri 表现均由 JSON 编译。");
        Gameplay("Ablo", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Bloodlust,
            "攻击/移动速度增益、体型表现系数、持续时间、目标层、消耗冷却、科技前置与 Bblo 表现均由 JSON 编译并接入自动施法。");
        Gameplay("Arej", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Rejuvenation,
            "总生命/魔法恢复量按 JSON 持续时间换算为确定性恢复速率；完整资源标志、无目标要求、目标层、消耗冷却与 Brej 表现均保留。");
        Gameplay("Afae", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.FaerieFire,
            "护甲削减、持续时间、始终自动施法标志、目标层、消耗冷却、科技前置与 Bfae 表现均由 JSON 编译；负护甲和跟随目标的共享视野已落地。");
        Gameplay("Ahea", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Heal,
            "生命恢复量、目标规则、魔法/冷却、自动施法和表现事件均由 Ability JSON 编译并通过运行时治疗验收。");
        Gameplay("Ainf", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.InnerFire,
            "攻击倍率、护甲、自动施法距离、生命恢复、持续时间和 Buff 均由 Ability JSON 编译并通过热载往返验收。");
        Gameplay("Adis", AbilityActivationKind.TargetPoint,
            War3AbilityCompilerKind.Dispel,
            "范围、召唤物伤害、目标层、魔法消除、消耗和科技门槛均来自 Ability JSON；法力损失 DataA=0 由严格字段门槛验证。");
        Gameplay("Aivs", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Invisibility,
            "目标规则、持续时间、消耗、科技门槛、隐形状态和 Buff 表现均由 Ability JSON 编译；当前过渡 DataA=0 由严格字段门槛验证。");
        Gameplay("Aply", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Polymorph,
            "最大目标等级、地面/空中/两栖/水中替换单位、替换移动速度、非英雄有机目标、持续时间、科技需求和 Bply/模型表现均由 JSON 驱动；变形后可移动但不能攻击或施法，解除后恢复原模型与属性。");
        Gameplay("Aslo", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Slow,
            "移动/攻击速率、普通/英雄持续时间、目标规则、自动施法、科技门槛与 Buff 均由 Ability JSON 驱动。");
        Gameplay("Asps", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.SpellSteal,
            "700 范围/搜索区域、消耗冷却、任意关系和魔法 Buff 类型由 JSON 驱动；敌方增益转给施法者，友方减益按距离与单位 ID 稳定选择敌方接收者，且可处理无敌目标。");
        Gameplay("Acmg", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Charm,
            "召唤物限定、最大单位等级、基础耗蓝与 DataB×DataC×目标当前生命的动态耗蓝、关系目标、科技需求和 Bcmg 表现均由 JSON 驱动；控制权与热载/回放统一落地。");
        Gameplay("Amim", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.MagicImmunity,
            "常驻魔法免疫由 Ability JSON 绑定为不可驱散被动状态；当前附加伤害系数 DataA=0 由严格字段门槛验证。");
        Gameplay("Afbk", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.Feedback,
            "单位与建筑反馈的法力燃烧、伤害系数、英雄折减、召唤物伤害和 Afbt 变体均由 Ability JSON/武器绑定驱动。重新装填与建筑命中已验收。");
        Gameplay("Afla", AbilityActivationKind.TargetPoint,
            War3AbilityCompilerKind.Flare,
            "侦察类型、0.8 秒延迟、范围、持续时间、超远射程、冷却、科技门槛和揭露表现均来自 Ability JSON；Visibility 统一消费检测揭露源。");
        Gameplay("Afsh", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.FragmentationShards,
            "全伤/中圈/外圈半径和三段伤害、目标层与 Rhfs 门槛均来自 Ability/武器 JSON，并由互斥伤害环测试覆盖。");
        result.Add("Agyb", new War3AbilityBehaviorDescriptor(
            "Agyb", AbilityActivationKind.Passive, false,
            War3AbilityCompilerKind.None,
            War3AbilityRuntimeSupportStatus.Delegated,
            "飞行机器炸弹由 CombatStore 武器组承载；Rhgb 解锁第二武器的对地/建筑目标层。"));
        Gameplay("Agyv", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.DetectionAura,
            "侦察类型与 900 范围由 Ability JSON 编译为常驻检测感知，战争迷雾按玩家关系和隐形状态统一判定。");
        Gameplay("Adts", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.DetectionAura,
            "建筑侦测范围与科技前置由 Ability JSON 编译进建筑感知 profile，战争迷雾系统按玩家科技等级启用。");
        Gameplay("Aflk", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.FlakCannons,
            "高射火炮按空中攻击命中、Rhfc 前置和互斥 7/6/5 伤害环触发。");
        Pending("Acha", AbilityActivationKind.Passive,
            "通用 Channel 基础家族缺少数据化命令分派；Srtt 已通过逐 rawcode 委托覆盖排除。");
        Gameplay("Aroc", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.Barrage,
            "弹幕攻击由 Rhrt 解锁对空武器，并只在空中目标命中后触发 9 目标范围伤害。");
        Gameplay("Asth", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.None,
            "Asth 仅声明 Rhhb 科技门槛；弹射目标层、次数、半径和逐跳伤害衰减来自狮鹫骑士武器 JSON，由 CombatStore 的通用 Bounce 传播、科技门槛和热载快照承载。");
        Gameplay("Aclf", AbilityActivationKind.TargetPoint,
            War3AbilityCompilerKind.Cloud,
            "范围、持续时间、攻击阻断类型、失误/速率字段、科技需求和 Bclf/Xclf 表现均由 JSON 编译；敌方建筑禁攻状态已接入建筑战斗、热载与回放。");
        Gameplay("Amls", AbilityActivationKind.ChannelUnit,
            War3AbilityCompilerKind.SiphonMana,
            "对空目标、每秒伤害、普通/英雄引导时长、射程、冷却、消耗和双端表现 Buff 均由 Ability JSON 驱动并由可打断引导承载。");
        result.Add("Asph", new War3AbilityBehaviorDescriptor(
            "Asph", AbilityActivationKind.Passive, false,
            War3AbilityCompilerKind.None,
            War3AbilityRuntimeSupportStatus.PresentationOnly,
            "血魔法师球体挂件；需要 attachment point/count 表现实例。"));

        Gameplay("AHbz", AbilityActivationKind.ChannelPoint,
            War3AbilityCompilerKind.Blizzard,
            "波数、碎片表现数量、关系分组伤害上限、建筑折减和可打断引导已数据化。表现仍由事件层消费。");
        Gameplay("AHwe", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.SummonWaterElemental,
            "每级召唤数量、hwat/hwt2/hwt3 完整单位 profile、60 秒寿命、消耗冷却和 Buff 表现均由 JSON 编译；召唤/驱散/热载已验收。");
        Gameplay("AHab", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.BrillianceAura,
            "每级法力恢复、900 范围、友军目标、Buff 与英雄学习数据均由 JSON 编译；当前百分比标志 DataB=0 由严格字段门槛验证。");
        Gameplay("AHmt", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.MassTeleport,
            "友军地面单位/建筑目标、施法延迟、附近单位上限和无重叠群组落点已落地。");
        Gameplay("AHtb", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.StormBolt,
            "每级伤害、普通/英雄眩晕、目标、射程、弹道、消耗冷却和 BPSE 表现均由 Ability JSON 驱动。");
        Gameplay("AHtc", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.ThunderClap,
            "每级范围/伤害、移动与攻击速率、普通/英雄持续时间和 Buff 均由 JSON 编译；当前指定目标伤害 DataB=0 严格验证。");
        Gameplay("AHbh", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.Bash,
            "每级确定性命中概率、物理附伤、普通/英雄眩晕、目标层和 BPSE 物理 Buff 均由 JSON 驱动；当前倍率/失误字段为 0 并严格验证。");
        Gameplay("AHav", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.Avatar,
            "护甲、生命、攻击、持续时间、消耗冷却、免疫和体型表现均由 JSON/Buff 事件驱动；当前额外魔法减伤 DataD=0 严格验证。");
        Gameplay("AHhb", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.HolyLight,
            "每级治疗量、亡灵半伤、友军/敌军/中立关系、非远古目标、射程和消耗冷却均由 JSON 编译。");
        Gameplay("AHds", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.DivineShield,
            "每级持续时间、消耗冷却、无敌状态和 BHds 表现均由 JSON 编译；当前不可主动取消 DataA=0 严格验证。");
        Gameplay("AHad", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.DevotionAura,
            "每级护甲、900 范围、友军关系、Buff 和英雄学习数据均由 JSON 编译；当前百分比标志 DataB=0 严格验证。");
        Gameplay("AHre", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.Resurrection,
            "复活数量、900 范围、死亡友军筛选、生命比例、消耗冷却和事件均由 JSON 驱动；当前复活无敌 DataB=0 严格验证。");
        Gameplay("AHfs", AbilityActivationKind.TargetPoint,
            War3AbilityCompilerKind.FlameStrike,
            "全伤与部分伤害阶段、间隔、关系分组伤害上限和建筑折减已数据化。");
        Gameplay("AHbn", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Banish,
            "移动/攻击速率、普通/英雄持续时间、任意关系有机目标、消耗冷却和 BHbn 表现均由 JSON 驱动；放逐单位免疫物理攻击但仍可施法并承受魔法伤害。");
        Gameplay("AHdr", AbilityActivationKind.ChannelUnit,
            War3AbilityCompilerKind.DrainMana,
            "每级转移量、1 秒脉冲、6 秒引导、任意关系流向、100% 超上限法力奖励、每秒 3 点衰减、消耗冷却和九组表现 Buff 均由 JSON 驱动；奖励状态已进入热载/回放哈希。");
        Gameplay("AHpx", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.SummonPhoenix,
            "召唤数量、hphx 完整单位 profile、永久寿命、消耗冷却和表现事件均由 JSON 编译；召唤生命周期与热载快照已覆盖。");
        return result;
    }

    private static Dictionary<string, War3AbilityBehaviorDescriptor>
        CreateRawOverrides() => new(StringComparer.Ordinal)
        {
            ["Srtt"] = new War3AbilityBehaviorDescriptor(
                "Acha", AbilityActivationKind.Passive, false,
                War3AbilityCompilerKind.None,
                War3AbilityRuntimeSupportStatus.Delegated,
                "蒸汽机车的 Srtt 变体标记由 CombatStore 武器组和 Rhrt 科技切换承载；不执行通用 Channel 行为。")
        };
}
