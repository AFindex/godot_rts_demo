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
    SummonPhoenix
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

        Prototype("Adef", AbilityActivationKind.Toggle,
            War3AbilityCompilerKind.Defend);
        Pending("AInv", AbilityActivationKind.Passive,
            "依赖尚未落地的物品栏、拾取、丢弃和物品技能系统。");
        result.Add("Ahar", new War3AbilityBehaviorDescriptor(
            "Ahar", AbilityActivationKind.Passive, false,
            War3AbilityCompilerKind.None,
            War3AbilityRuntimeSupportStatus.Delegated,
            "采集由 EconomySystem 承载；仍需建立 rawcode 到委托测试的覆盖链接。"));
        Pending("Amil", AbilityActivationKind.Passive,
            "缺少农民寻路到大厅、民兵变身和到期还原行为。");
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
        Pending("Agyb", AbilityActivationKind.Passive,
            "缺少按科技切换武器目标层和启用对地攻击的能力。");
        Prototype("Agyv", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.DetectionAura);
        Prototype("Aflk", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.FlakCannons);
        Pending("Acha", AbilityActivationKind.Passive,
            "Channel 基础家族缺少数据化命令分派；Srtt 仍需蒸汽机车形态和武器组切换。");
        Prototype("Aroc", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.Barrage);
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

        Prototype("AHbz", AbilityActivationKind.ChannelPoint,
            War3AbilityCompilerKind.Blizzard);
        Prototype("AHwe", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.SummonWaterElemental);
        Prototype("AHab", AbilityActivationKind.Passive,
            War3AbilityCompilerKind.BrillianceAura);
        Prototype("AHmt", AbilityActivationKind.TargetPoint,
            War3AbilityCompilerKind.MassTeleport);
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
        Prototype("AHfs", AbilityActivationKind.ChannelPoint,
            War3AbilityCompilerKind.FlameStrike);
        Prototype("AHbn", AbilityActivationKind.TargetUnit,
            War3AbilityCompilerKind.Banish);
        Prototype("AHdr", AbilityActivationKind.ChannelUnit,
            War3AbilityCompilerKind.DrainMana);
        Prototype("AHpx", AbilityActivationKind.Instant,
            War3AbilityCompilerKind.SummonPhoenix);
        return result;
    }
}
