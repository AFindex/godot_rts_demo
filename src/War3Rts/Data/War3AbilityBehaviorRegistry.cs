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
    BuildingMilitiaCall
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
    private const string PrototypeReason =
        "已具备确定性玩法原型；仍需完成原始语义、目标、科技与表现一致性验收。";

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

        void Prototype(
            string id,
            AbilityActivationKind activation,
            War3AbilityCompilerKind compiler,
            bool autoCast = false) => result.Add(
            id,
            new War3AbilityBehaviorDescriptor(
                id, activation, autoCast, compiler,
                War3AbilityRuntimeSupportStatus.Blocked,
                PrototypeReason));

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

        Prototype("Adef", AbilityActivationKind.Toggle,
            War3AbilityCompilerKind.Defend);
        Pending("AInv", AbilityActivationKind.Passive,
            "依赖尚未落地的物品栏、拾取、丢弃和物品技能系统。");
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
        Pending("Arep", AbilityActivationKind.Passive,
            "缺少消耗资源的建筑/机械单位修理命令与自动施法行为。");
        Prototype("Ahea", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Heal, true);
        Prototype("Ainf", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.InnerFire, true);
        Prototype("Adis", AbilityActivationKind.TargetPoint,
            War3AbilityCompilerKind.Dispel);
        Prototype("Aivs", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Invisibility);
        Prototype("Aply", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Polymorph);
        Prototype("Aslo", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Slow, true);
        Prototype("Asps", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.SpellSteal);
        Prototype("Acmg", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Charm);
        Prototype("Amim", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.MagicImmunity);
        Prototype("Afbk", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.Feedback);
        Prototype("Afla", AbilityActivationKind.TargetPoint,
            War3AbilityCompilerKind.Flare);
        Prototype("Afsh", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.FragmentationShards);
        result.Add("Agyb", new War3AbilityBehaviorDescriptor(
            "Agyb", AbilityActivationKind.Passive, false,
            War3AbilityCompilerKind.None,
            War3AbilityRuntimeSupportStatus.Delegated,
            "飞行机器炸弹由 CombatStore 武器组承载；Rhgb 解锁第二武器的对地/建筑目标层。"));
        Prototype("Agyv", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.DetectionAura);
        Gameplay("Aflk", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.FlakCannons,
            "高射火炮按空中攻击命中、Rhfc 前置和互斥 7/6/5 伤害环触发。");
        Pending("Acha", AbilityActivationKind.Passive,
            "通用 Channel 基础家族缺少数据化命令分派；Srtt 已通过逐 rawcode 委托覆盖排除。");
        Gameplay("Aroc", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.Barrage,
            "弹幕攻击由 Rhrt 解锁对空武器，并只在空中目标命中后触发 9 目标范围伤害。");
        Pending("Asth", AbilityActivationKind.Passive,
            "缺少攻击弹射目标选择、次数和伤害衰减。");
        Prototype("Aclf", AbilityActivationKind.TargetPoint,
            War3AbilityCompilerKind.Cloud);
        Prototype("Amls", AbilityActivationKind.ChannelUnit,
            War3AbilityCompilerKind.SiphonMana);
        result.Add("Asph", new War3AbilityBehaviorDescriptor(
            "Asph", AbilityActivationKind.Passive, false,
            War3AbilityCompilerKind.None,
            War3AbilityRuntimeSupportStatus.PresentationOnly,
            "血魔法师球体挂件；需要 attachment point/count 表现实例。"));

        Gameplay("AHbz", AbilityActivationKind.ChannelPoint,
            War3AbilityCompilerKind.Blizzard,
            "波数、碎片表现数量、关系分组伤害上限、建筑折减和可打断引导已数据化。表现仍由事件层消费。");
        Prototype("AHwe", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.SummonWaterElemental);
        Prototype("AHab", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.BrillianceAura);
        Gameplay("AHmt", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.MassTeleport,
            "友军地面单位/建筑目标、施法延迟、附近单位上限和无重叠群组落点已落地。");
        Prototype("AHtb", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.StormBolt);
        Prototype("AHtc", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.ThunderClap);
        Prototype("AHbh", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.Bash);
        Prototype("AHav", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.Avatar);
        Prototype("AHhb", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.HolyLight);
        Prototype("AHds", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.DivineShield);
        Prototype("AHad", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.DevotionAura);
        Prototype("AHre", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.Resurrection);
        Gameplay("AHfs", AbilityActivationKind.TargetPoint,
            War3AbilityCompilerKind.FlameStrike,
            "全伤与部分伤害阶段、间隔、关系分组伤害上限和建筑折减已数据化。");
        Prototype("AHbn", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Banish);
        Prototype("AHdr", AbilityActivationKind.ChannelUnit,
            War3AbilityCompilerKind.DrainMana);
        Prototype("AHpx", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.SummonPhoenix);
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
