using System.Collections.Immutable;
using System.Globalization;
using RtsDemo.Simulation;

namespace War3Rts.Data;

public sealed record War3AbilityDefinition(
    int AbilityId,
    string ObjectId,
    string Name,
    string Description,
    string IconPath,
    string Hotkey,
    string[] CasterModels,
    string[] TargetModels,
    string[] EffectModels,
    string[] MissileModels,
    string[] BuffIds,
    string[] EffectIds,
    int RequiredHeroLevel,
    int HeroLevelSkip,
    string AlternateName = "",
    string AlternateDescription = "",
    string AlternateIconPath = "",
    string AlternateHotkey = "");

public sealed record War3AbilityImportResult(
    AbilityCatalogSnapshot Catalog,
    War3AbilityDefinition[] Definitions,
    int RequestedCount,
    string[] MissingObjectIds,
    string[] UnresolvedRequirementIds,
    int BehaviorFamilyCount,
    int PrototypeCount,
    string[] UnclassifiedBaseCodes)
{
    public string LogLine =>
        $"ability_runtime={Catalog.Count}/{RequestedCount} " +
        $"unit_bindings={Catalog.Bindings.Length} " +
        $"building_bindings={Catalog.BuildingBindings.Length} " +
        $"families={BehaviorFamilyCount} " +
        $"prototype={PrototypeCount} missing={MissingObjectIds.Length} " +
        $"requirement_missing={UnresolvedRequirementIds.Length} " +
        $"unclassified={UnclassifiedBaseCodes.Length} " +
        $"hash={Catalog.StableHashText}";
}

/// <summary>
/// Maps exported Warcraft rawcodes and level Data fields into content-neutral
/// simulation effects. All scalar costs, durations and damage come from the
/// export; only behavior semantics and world-distance conversion live here.
/// </summary>
public sealed class War3AbilityDataAdapter(
    War3ObjectDataCatalog abilities,
    War3ObjectDataCatalog buffEffects,
    IWar3UnitDataCatalog units,
    War3GameplayImportPolicy policy,
    IReadOnlyDictionary<string, int> technologyIds,
    IReadOnlyDictionary<string, int> buildingIds,
    IReadOnlyDictionary<string, UnitTypeProfile> unitTypes)
{
    public War3AbilityImportResult Build(
        IReadOnlyList<War3UnitDefinition> definitions,
        IReadOnlyList<War3BuildingDefinition>? buildingDefinitions = null)
    {
        var requestedIds = CollectReferencedAbilityIds(
            definitions, buildingDefinitions ?? []);
        var profiles = new List<AbilityProfile>();
        var presentations = new List<War3AbilityDefinition>();
        var rawToDense = new Dictionary<string, int>(StringComparer.Ordinal);
        var missing = new List<string>();
        var unresolvedRequirements = new HashSet<string>(StringComparer.Ordinal);
        var families = new HashSet<string>(StringComparer.Ordinal);
        var unclassified = new HashSet<string>(StringComparer.Ordinal);
        var prototypeCount = 0;
        foreach (var rawId in requestedIds)
        {
            if (!abilities.TryGet(rawId, out var data))
            {
                missing.Add(rawId);
                continue;
            }
            var id = profiles.Count;
            var profile = AdaptProfile(id, data);
            var baseCode = string.IsNullOrWhiteSpace(data.Identity.BaseCode)
                ? data.Id
                : data.Identity.BaseCode;
            foreach (var requirement in data.Summary.Levels
                         .SelectMany(value => value.Requirements))
            {
                if (!technologyIds.ContainsKey(requirement) &&
                    !buildingIds.ContainsKey(requirement))
                    unresolvedRequirements.Add(requirement);
            }
            families.Add(baseCode);
            var behavior = War3AbilityBehaviorRegistry.Resolve(baseCode, data.Id);
            if (behavior.HasPrototypeCompiler) prototypeCount++;
            if (behavior.Status == War3AbilityRuntimeSupportStatus.Unclassified)
                unclassified.Add(baseCode);
            profiles.Add(profile);
            presentations.Add(AdaptPresentation(profile, data));
            rawToDense.Add(rawId, id);
        }

        var bindings = new List<UnitAbilityBindingProfile>();
        foreach (var definition in definitions.OrderBy(value => value.TypeId))
        {
            if (!units.TryGet(definition.ObjectId, out var unitData)) continue;
            var normalIds = unitData.Summary.Abilities
                .Where(rawToDense.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var normalSet = normalIds.ToHashSet(StringComparer.Ordinal);
            var heroIds = unitData.Summary.HeroAbilities
                .Where(rawToDense.ContainsKey)
                .Where(rawId => !normalSet.Contains(rawId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var entries = normalIds.Select(rawId =>
                    new UnitAbilityEntryProfile(
                        rawToDense[rawId],
                        1,
                        rawId.Equals(
                            unitData.Summary.DefaultActiveAbility,
                            StringComparison.Ordinal)))
                .Concat(heroIds.Select(rawId =>
                    new UnitAbilityEntryProfile(rawToDense[rawId], 0)))
                .ToImmutableArray();
            bindings.Add(new UnitAbilityBindingProfile(
                definition.TypeId,
                unitData.Identity.IsHero,
                ManaProfile(definition.ObjectId, unitData),
                entries,
                AdaptUnitTraits(unitData),
                Math.Max(1, unitData.Summary.Level ?? 1),
                ExperienceBountyForUnitLevel(unitData.Summary.Level ?? 1),
                HeroMaximumLevel: 10));
        }

        var buildingBindings = new List<BuildingAbilityBindingProfile>();
        foreach (var definition in (buildingDefinitions ?? [])
                     .OrderBy(value => value.TypeId))
        {
            if (!units.TryGet(definition.ObjectId, out var buildingData))
                continue;
            var abilityIds = buildingData.Summary.Abilities
                .Where(rawToDense.ContainsKey)
                .Select(rawId => rawToDense[rawId])
                .Distinct()
                .ToImmutableArray();
            if (!abilityIds.IsEmpty)
                buildingBindings.Add(new BuildingAbilityBindingProfile(
                    definition.TypeId, abilityIds));
        }

        return new War3AbilityImportResult(
            new AbilityCatalogSnapshot(
                profiles.ToArray(), bindings.ToArray(),
                buildingBindings.ToArray()),
            presentations.ToArray(), requestedIds.Length,
            missing.ToArray(),
            unresolvedRequirements.Order(StringComparer.Ordinal).ToArray(),
            families.Count, prototypeCount,
            unclassified.Order(StringComparer.Ordinal).ToArray());
    }

    private string[] CollectReferencedAbilityIds(
        IEnumerable<War3UnitDefinition> definitions,
        IEnumerable<War3BuildingDefinition> buildingDefinitions)
    {
        var unitAbilityIds = definitions.SelectMany(definition =>
            units.TryGet(definition.ObjectId, out var data)
                ? data.Summary.Abilities.Concat(data.Summary.HeroAbilities)
                : []);
        var buildingAbilityIds = buildingDefinitions.SelectMany(definition =>
                units.TryGet(definition.ObjectId, out var data)
                    ? data.Summary.Abilities
                    : [])
            .Where(IsRuntimeBuildingAbility);
        return unitAbilityIds.Concat(buildingAbilityIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private bool IsRuntimeBuildingAbility(string rawId)
    {
        if (!abilities.TryGet(rawId, out var data)) return false;
        var baseCode = string.IsNullOrWhiteSpace(data.Identity.BaseCode)
            ? data.Id
            : data.Identity.BaseCode;
        return War3AbilityBehaviorRegistry.Resolve(baseCode, data.Id).Compiler ==
               War3AbilityCompilerKind.BuildingMilitiaCall;
    }

    private AbilityProfile AdaptProfile(int id, War3ObjectEditorData data)
    {
        var behaviorId = string.IsNullOrWhiteSpace(data.Identity.BaseCode)
            ? data.Id
            : data.Identity.BaseCode;
        var behavior = War3AbilityBehaviorRegistry.Resolve(behaviorId, data.Id);
        var activation = behavior.Activation;
        var levelValues = data.Summary.Levels.Length == 0
            ? [new War3ObjectLevel { Level = 1 }]
            : data.Summary.Levels;
        var levels = levelValues.Select(level => AdaptLevel(
                behavior, level))
            .ToImmutableArray();
        var first = levelValues[0];
        var icon = Asset(data, "art") ??
                   Value(data.Profile, "Art") ?? string.Empty;
        var hotkey = NormalizeHotkey(first.Hotkey);
        return new AbilityProfile(
            id,
            data.Id,
            string.IsNullOrWhiteSpace(data.DisplayName) ? data.Id : data.DisplayName,
            CleanTooltip(first.ExtendedTooltip ?? first.Tooltip ?? string.Empty),
            icon,
            hotkey,
            activation,
            TargetFlags(behavior.Compiler, activation, first.Targets),
            data.Identity.Hero,
            behavior.AutoCastDefault,
            levels,
            data.Identity.RequiredHeroLevel,
            data.Identity.HeroLevelSkip);
    }

    private AbilityLevelProfile AdaptLevel(
        War3AbilityBehaviorDescriptor behavior,
        War3ObjectLevel level)
    {
        var duration = MathF.Max(0f, level.Duration ?? 0f);
        var heroDuration = MathF.Max(0f, level.HeroDuration ?? duration);
        var channel = ChannelSeconds(behavior.Compiler, level, duration);
        var effects = DecorateEffects(
            behavior.Compiler,
            level,
            Effects(behavior.Compiler, level, duration, heroDuration));
        return new AbilityLevelProfile(
            Math.Max(1, level.Level),
            MathF.Max(0f, level.ManaCost ?? 0f),
            MathF.Max(0f, level.Cooldown ?? 0f),
            CastSeconds(behavior.Compiler, level),
            channel,
            Distance(level.Range),
            Distance(level.Area),
            duration,
            heroDuration,
            effects,
            Requirements(level));
    }

    private ImmutableArray<AbilityRequirementProfile> Requirements(
        War3ObjectLevel level)
    {
        var requiredLevel = Math.Max(1, level.RequirementLevel ?? 1);
        return level.Requirements
            .Select(rawcode =>
            {
                if (technologyIds.TryGetValue(rawcode, out var technologyId))
                    return new AbilityRequirementProfile(
                        AbilityRequirementKind.TechnologyLevel,
                        technologyId,
                        requiredLevel);
                if (buildingIds.TryGetValue(rawcode, out var buildingId))
                    return new AbilityRequirementProfile(
                        AbilityRequirementKind.CompletedBuilding,
                        buildingId,
                        requiredLevel);
                return (AbilityRequirementProfile?)null;
            })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToImmutableArray();
    }

    private ImmutableArray<AbilityEffectProfile> Effects(
        War3AbilityCompilerKind compiler,
        War3ObjectLevel level,
        float duration,
        float heroDuration)
    {
        var a = Data(level, "A");
        var b = Data(level, "B");
        var c = Data(level, "C");
        var d = Data(level, "D");
        var e = Data(level, "E");
        return compiler switch
        {
            War3AbilityCompilerKind.Defend => One(new AbilityEffectProfile(
                AbilityEffectKind.ToggleStatus, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Caster, AbilityRelationFilter.Self,
                Modifier: new AbilityStatModifier(
                    MovementSpeedMultiplier: Math.Clamp(c > 0f ? c : 0.7f,
                        0.1f, 1f)))),
            War3AbilityCompilerKind.Heal => One(Heal(a > 0f ? a : 25f)),
            War3AbilityCompilerKind.InnerFire => One(Status(
                AbilityEffectSelector.Primary,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                duration,
                modifier: new AbilityStatModifier(
                    AttackDamageMultiplier: 1f + MathF.Max(0f, a),
                    ArmorAdd: MathF.Max(0f, b)))),
            War3AbilityCompilerKind.Dispel => One(new AbilityEffectProfile(
                AbilityEffectKind.Dispel, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtTarget, AbilityRelationFilter.Any,
                SecondaryValue: MathF.Max(0f, b),
                Radius: Distance(level.Area), MaximumTargets: 32,
                DamageKind: AbilityDamageKind.Magic,
                BuffDispelKind: AbilityBuffDispelKind.Magic)),
            War3AbilityCompilerKind.Invisibility => One(Status(
                AbilityEffectSelector.Primary,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                duration, AbilityStatusFlags.Invisible)),
            War3AbilityCompilerKind.Polymorph => One(Status(
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                duration,
                AbilityStatusFlags.Polymorphed |
                AbilityStatusFlags.AttackDisabled,
                new AbilityStatModifier(MovementSpeedMultiplier: 0.45f))),
            War3AbilityCompilerKind.Slow => One(Status(
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                duration,
                modifier: new AbilityStatModifier(
                    MovementSpeedMultiplier: Math.Clamp(a, 0.1f, 1f),
                    AttackCooldownMultiplier: b > 0f
                        ? 1f / MathF.Max(0.1f, 1f - b)
                        : 1f))),
            War3AbilityCompilerKind.SpellSteal => One(new AbilityEffectProfile(
                AbilityEffectKind.TransferBuff, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Any,
                BuffDispelKind: AbilityBuffDispelKind.Magic)),
            War3AbilityCompilerKind.Charm => One(new AbilityEffectProfile(
                AbilityEffectKind.TransferControl, AbilityEffectTiming.Impact,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy)),
            War3AbilityCompilerKind.MagicImmunity => One(Aura(
                AbilityEffectSelector.Caster, AbilityRelationFilter.Self,
                status: AbilityStatusFlags.MagicImmune)),
            War3AbilityCompilerKind.Feedback => One(new AbilityEffectProfile(
                AbilityEffectKind.Mana, AbilityEffectTiming.AttackHit,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                Value: -(a > 0f ? a : 20f),
                SecondaryValue: MathF.Max(0f, b),
                DamageKind: AbilityDamageKind.Magic,
                HeroValue: -(c > 0f ? c : a > 0f ? a : 20f),
                HeroSecondaryValue: d > 0f ? d : MathF.Max(0f, b))),
            War3AbilityCompilerKind.Flare => One(new AbilityEffectProfile(
                AbilityEffectKind.Reveal, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtTarget, AbilityRelationFilter.Self,
                Radius: Distance(level.Area), Duration: duration)),
            War3AbilityCompilerKind.FragmentationShards => AttackBands(
                Distance(level.Area), Distance(a), Distance(b),
                c > 0f ? c : 25f,
                d > 0f ? d : 18f,
                e > 0f ? e : 12f),
            War3AbilityCompilerKind.DetectionAura => One(Aura(
                AbilityEffectSelector.Caster, AbilityRelationFilter.Self,
                modifier: new AbilityStatModifier(
                    DetectionRangeAdd: Distance(level.Range ?? 900f)))),
            War3AbilityCompilerKind.FlakCannons => AttackBands(
                Distance(level.Area), Distance(a), Distance(b),
                c > 0f ? c : 7f,
                d > 0f ? d : 6f,
                e > 0f ? e : 5f),
            War3AbilityCompilerKind.Barrage => One(AttackSplash(
                a > 0f ? a : 25f,
                Distance(level.Area ?? 500f),
                Math.Max(1, (int)MathF.Round(c > 0f ? c : 9f)))),
            War3AbilityCompilerKind.Cloud => One(Status(
                AbilityEffectSelector.AreaAtTarget, AbilityRelationFilter.Enemy,
                duration, AbilityStatusFlags.AttackDisabled,
                radius: Distance(level.Area), maximumTargets: 32)),
            War3AbilityCompilerKind.SiphonMana => One(new AbilityEffectProfile(
                AbilityEffectKind.Damage, AbilityEffectTiming.ChannelPulse,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                Value: a > 0f ? a : 30f, Interval: 1f,
                DamageKind: AbilityDamageKind.Magic)),
            War3AbilityCompilerKind.Blizzard => One(new AbilityEffectProfile(
                AbilityEffectKind.Damage, AbilityEffectTiming.ChannelPulse,
                AbilityEffectSelector.AreaAtTarget,
                AbilityRelationFilter.Any,
                Value: b > 0f ? b : 30f,
                Radius: Distance(level.Area),
                Interval: 1f,
                DamageKind: AbilityDamageKind.Magic,
                PulseCount: Math.Max(
                    1, (int)MathF.Round(a > 0f ? a : 6f)),
                MaximumTotalValue: MathF.Max(0f, Data(level, "F")),
                BuildingValueMultiplier: Math.Clamp(d, 0f, 1f),
                VisualCount: Math.Max(
                    1, (int)MathF.Round(c > 0f ? c : 6f)))),
            War3AbilityCompilerKind.SummonWaterElemental => One(new AbilityEffectProfile(
                AbilityEffectKind.Summon, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtCaster, AbilityRelationFilter.Self,
                MaximumTargets: Math.Max(1, (int)MathF.Round(a > 0f ? a : 1f)),
                Summon: WaterElemental(level))),
            War3AbilityCompilerKind.BrillianceAura => One(Aura(
                AbilityEffectSelector.AreaAtCaster,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                Distance(level.Area),
                new AbilityStatModifier(ManaRegenerationAdd: MathF.Max(0f, a)))),
            War3AbilityCompilerKind.MassTeleport => One(new AbilityEffectProfile(
                AbilityEffectKind.Teleport, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtCaster,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                Radius: Distance(level.Area),
                MaximumTargets: Math.Max(
                    1, (int)MathF.Round(a > 0f ? a : 24f)),
                VisualCount: Math.Max(
                    1, (int)MathF.Round(a > 0f ? a : 24f)),
                ClusteredPlacement: c >= 0.5f)),
            War3AbilityCompilerKind.StormBolt => One(Status(
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                duration, AbilityStatusFlags.Stunned,
                value: a > 0f ? a : 100f,
                damageKind: AbilityDamageKind.Magic)),
            War3AbilityCompilerKind.ThunderClap => One(Status(
                AbilityEffectSelector.AreaAtCaster,
                AbilityRelationFilter.Enemy | AbilityRelationFilter.Neutral,
                duration,
                modifier: new AbilityStatModifier(
                    MovementSpeedMultiplier: c > 0f ? c : 0.5f,
                    AttackCooldownMultiplier: d > 0f ? 1f / d : 2f),
                value: a > 0f ? a : 60f,
                radius: Distance(level.Area), maximumTargets: 32,
                damageKind: AbilityDamageKind.Magic)),
            War3AbilityCompilerKind.Bash => One(new AbilityEffectProfile(
                AbilityEffectKind.ApplyStatus, AbilityEffectTiming.AttackHit,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                Value: c > 0f ? c : 25f,
                Duration: duration,
                Interval: a > 0f ? a : 20f,
                Status: AbilityStatusFlags.Stunned,
                DamageKind: AbilityDamageKind.Physical)),
            War3AbilityCompilerKind.Avatar => One(Status(
                AbilityEffectSelector.Caster, AbilityRelationFilter.Self,
                duration,
                AbilityStatusFlags.MagicImmune,
                new AbilityStatModifier(
                    AttackDamageAdd: c > 0f ? c : 20f,
                    ArmorAdd: a > 0f ? a : 5f,
                    MaximumHealthAdd: b > 0f ? b : 500f))),
            War3AbilityCompilerKind.HolyLight =>
            [
                Heal(a > 0f ? a : 200f) with
                {
                    ExcludedUnitTraits = AbilityUnitTraits.Undead
                },
                new AbilityEffectProfile(
                    AbilityEffectKind.Damage, AbilityEffectTiming.Impact,
                    AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                    Value: (a > 0f ? a : 200f) * 0.5f,
                    DamageKind: AbilityDamageKind.Magic,
                    RequiredUnitTraits: AbilityUnitTraits.Undead)
            ],
            War3AbilityCompilerKind.DivineShield => One(Status(
                AbilityEffectSelector.Caster, AbilityRelationFilter.Self,
                duration, AbilityStatusFlags.Invulnerable)),
            War3AbilityCompilerKind.DevotionAura => One(Aura(
                AbilityEffectSelector.AreaAtCaster,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                Distance(level.Area),
                new AbilityStatModifier(ArmorAdd: MathF.Max(0f, a)))),
            War3AbilityCompilerKind.Resurrection => One(new AbilityEffectProfile(
                AbilityEffectKind.Revive, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtCaster,
                AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
                Value: 1f, Radius: Distance(level.Area),
                MaximumTargets: Math.Max(1, (int)MathF.Round(a > 0f ? a : 6f)))),
            War3AbilityCompilerKind.FlameStrike => FlameStrikeEffects(
                level, duration, heroDuration),
            War3AbilityCompilerKind.Banish => One(Status(
                AbilityEffectSelector.Primary, AbilityRelationFilter.Any,
                duration,
                AbilityStatusFlags.Banished |
                AbilityStatusFlags.AttackDisabled,
                new AbilityStatModifier(
                    MovementSpeedMultiplier: a > 0f ? a : 0.5f))),
            War3AbilityCompilerKind.DrainMana =>
                One(new AbilityEffectProfile(
                    AbilityEffectKind.TransferMana,
                    AbilityEffectTiming.ChannelPulse,
                    AbilityEffectSelector.Primary, AbilityRelationFilter.Any,
                    Value: e > 0f ? e : b > 0f ? b : 15f,
                    Interval: c > 0f ? c : 1f)),
            War3AbilityCompilerKind.SummonPhoenix => One(new AbilityEffectProfile(
                AbilityEffectKind.Summon, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtCaster, AbilityRelationFilter.Self,
                MaximumTargets: Math.Max(1, (int)MathF.Round(a > 0f ? a : 1f)),
                Summon: Phoenix(level))),
            War3AbilityCompilerKind.MilitiaTransform => One(
                new AbilityEffectProfile(
                    AbilityEffectKind.TransformUnit,
                    AbilityEffectTiming.Impact,
                    AbilityEffectSelector.Caster,
                    AbilityRelationFilter.Self,
                    UnitForm: MilitiaForm(level, duration))),
            War3AbilityCompilerKind.BuildingMilitiaCall => One(
                new AbilityEffectProfile(
                    AbilityEffectKind.TransformUnit,
                    AbilityEffectTiming.Impact,
                    AbilityEffectSelector.AreaAtCaster,
                    AbilityRelationFilter.Self |
                    AbilityRelationFilter.Friendly,
                    Radius: Distance(level.Area),
                    UnitForm: MilitiaForm(
                        level, duration > 0f
                            ? duration
                            : MilitiaDurationSeconds()))),
            _ => ImmutableArray<AbilityEffectProfile>.Empty
        };
    }

    private float MilitiaDurationSeconds()
    {
        if (abilities.TryGet("Amil", out var militia) &&
            militia.Summary.Levels.FirstOrDefault() is { } level &&
            level.Duration is > 0f and var duration)
            return duration;
        return 45f;
    }

    private AbilityUnitFormProfile MilitiaForm(
        War3ObjectLevel level,
        float duration)
    {
        var normalId = DataText(level, "A", "hpea");
        var alternateId = DataText(level, "B", "hmil");
        if (!unitTypes.TryGetValue(normalId, out var normal) ||
            !unitTypes.TryGetValue(alternateId, out var alternate))
            throw new InvalidOperationException(
                $"Militia form units {normalId}/{alternateId} are not bound.");
        return new AbilityUnitFormProfile(
            normal, alternate, MathF.Max(0.1f, duration),
            BuildingFunctionKind.TownHall);
    }

    private static AbilityEffectProfile Heal(float value) => new(
        AbilityEffectKind.Heal, AbilityEffectTiming.Impact,
        AbilityEffectSelector.Primary,
        AbilityRelationFilter.Self | AbilityRelationFilter.Friendly,
        Value: value);

    private static AbilityEffectProfile Status(
        AbilityEffectSelector selector,
        AbilityRelationFilter relations,
        float duration,
        AbilityStatusFlags status = AbilityStatusFlags.None,
        AbilityStatModifier modifier = default,
        float value = 0f,
        float radius = 0f,
        int maximumTargets = 0,
        AbilityDamageKind damageKind = AbilityDamageKind.None) => new(
        AbilityEffectKind.ApplyStatus, AbilityEffectTiming.Impact,
        selector, relations, value, Radius: radius, Duration: duration,
        MaximumTargets: maximumTargets, Status: status, Modifier: modifier,
        DamageKind: damageKind);

    private static AbilityEffectProfile Aura(
        AbilityEffectSelector selector,
        AbilityRelationFilter relations,
        float radius = 0f,
        AbilityStatModifier modifier = default,
        AbilityStatusFlags status = AbilityStatusFlags.None) => new(
        AbilityEffectKind.ApplyStatus, AbilityEffectTiming.Aura,
        selector, relations, Radius: radius, Status: status, Modifier: modifier);

    private static AbilityEffectProfile AttackSplash(
        float damage,
        float radius,
        int maximumTargets) => new(
        AbilityEffectKind.Damage, AbilityEffectTiming.AttackHit,
        AbilityEffectSelector.AreaAtTarget,
        AbilityRelationFilter.Enemy | AbilityRelationFilter.Neutral,
        Value: damage, Radius: radius, MaximumTargets: maximumTargets,
        DamageKind: AbilityDamageKind.Physical);

    private static ImmutableArray<AbilityEffectProfile> AttackBands(
        float fullRadius,
        float mediumRadius,
        float smallRadius,
        float fullDamage,
        float mediumDamage,
        float smallDamage)
    {
        var output = ImmutableArray.CreateBuilder<AbilityEffectProfile>(3);
        AddBand(0f, fullRadius, fullDamage);
        AddBand(fullRadius, mediumRadius, mediumDamage);
        // Classic 1.27 Fragmentation Shards has quarter radius 250 below
        // half radius 275. Damage priority makes that quarter band empty.
        AddBand(MathF.Max(fullRadius, mediumRadius), smallRadius, smallDamage);
        return output.ToImmutable();

        void AddBand(float inner, float outer, float damage)
        {
            if (damage <= 0f || outer <= inner) return;
            output.Add(new AbilityEffectProfile(
                AbilityEffectKind.Damage, AbilityEffectTiming.AttackHit,
                AbilityEffectSelector.AreaAtTarget,
                AbilityRelationFilter.Enemy | AbilityRelationFilter.Neutral,
                Value: damage, Radius: outer,
                DamageKind: AbilityDamageKind.Physical,
                InnerRadius: inner));
        }
    }

    private ImmutableArray<AbilityEffectProfile> FlameStrikeEffects(
        War3ObjectLevel level,
        float duration,
        float fullDuration)
    {
        var fullDamage = Data(level, "A");
        if (fullDamage <= 0f) fullDamage = 15f;
        var fullInterval = Data(level, "B");
        if (fullInterval <= 0f) fullInterval = 0.33f;
        var partialDamage = Data(level, "C");
        var partialInterval = Data(level, "D");
        if (partialInterval <= 0f) partialInterval = 1f;
        var buildingMultiplier = Math.Clamp(Data(level, "E"), 0f, 1f);
        var maximumDamage = MathF.Max(0f, Data(level, "F"));
        fullDuration = Math.Clamp(
            fullDuration > 0f ? fullDuration : 2.67f,
            fullInterval,
            MathF.Max(fullInterval, duration));
        var partialDuration = MathF.Max(0f, duration - fullDuration);
        var fullPulses = Math.Max(
            1, (int)MathF.Floor(fullDuration / fullInterval) + 1);
        var partialPulses = partialDamage > 0f && partialDuration > 0f
            ? Math.Max(
                1, (int)MathF.Floor(partialDuration / partialInterval))
            : 0;
        var targetCap = maximumDamage > 0f
            ? MathF.Max(1f, MathF.Round(maximumDamage / fullDamage))
            : 0f;
        var output = ImmutableArray.CreateBuilder<AbilityEffectProfile>(2);
        output.Add(new AbilityEffectProfile(
            AbilityEffectKind.Damage, AbilityEffectTiming.PersistentPulse,
            AbilityEffectSelector.AreaAtTarget, AbilityRelationFilter.Any,
            Value: fullDamage, Radius: Distance(level.Area),
            Duration: fullDuration, Interval: fullInterval,
            DamageKind: AbilityDamageKind.Magic,
            PulseCount: fullPulses,
            MaximumTotalValue: maximumDamage,
            BuildingValueMultiplier: buildingMultiplier,
            VisualCount: 1));
        if (partialPulses > 0)
        {
            output.Add(new AbilityEffectProfile(
                AbilityEffectKind.Damage,
                AbilityEffectTiming.PersistentPulse,
                AbilityEffectSelector.AreaAtTarget,
                AbilityRelationFilter.Any,
                Value: partialDamage, Radius: Distance(level.Area),
                Duration: partialDuration, Interval: partialInterval,
                DamageKind: AbilityDamageKind.Magic,
                PulseCount: partialPulses,
                StartDelay: fullDuration,
                MaximumTotalValue: targetCap > 0f
                    ? partialDamage * targetCap
                    : 0f,
                BuildingValueMultiplier: buildingMultiplier,
                VisualCount: 1));
        }
        return output.ToImmutable();
    }

    private static ImmutableArray<AbilityEffectProfile> DecorateEffects(
        War3AbilityCompilerKind compiler,
        War3ObjectLevel level,
        ImmutableArray<AbilityEffectProfile> effects)
    {
        if (effects.IsDefaultOrEmpty) return effects;
        var buffId = level.BuffIds.FirstOrDefault() ?? string.Empty;
        var output = effects.ToArray();
        for (var index = 0; index < output.Length; index++)
        {
            var effect = output[index];
            if (effect.Kind is not (AbilityEffectKind.ApplyStatus or
                    AbilityEffectKind.ToggleStatus))
                continue;
            var identity = string.IsNullOrWhiteSpace(buffId)
                ? $"{compiler}:{level.Level}"
                : buffId;
            output[index] = effect with
            {
                BuffId = identity,
                BuffPolarity = Polarity(effect.Relations),
                BuffDispelKind = IsNonDispellable(compiler, effect)
                    ? AbilityBuffDispelKind.None
                    : compiler == War3AbilityCompilerKind.Bash
                        ? AbilityBuffDispelKind.Physical
                        : AbilityBuffDispelKind.Magic,
                BuffStacking = AbilityBuffStackingKind.Refresh
            };
        }
        return output.ToImmutableArray();
    }

    private static AbilityBuffPolarity Polarity(
        AbilityRelationFilter relations)
    {
        var friendly = (relations & (AbilityRelationFilter.Self |
                                     AbilityRelationFilter.Friendly)) != 0;
        var hostile = (relations & (AbilityRelationFilter.Enemy |
                                    AbilityRelationFilter.Neutral)) != 0;
        return friendly == hostile
            ? AbilityBuffPolarity.Neutral
            : friendly
                ? AbilityBuffPolarity.Beneficial
                : AbilityBuffPolarity.Harmful;
    }

    private static bool IsNonDispellable(
        War3AbilityCompilerKind compiler,
        in AbilityEffectProfile effect) =>
        effect.Timing == AbilityEffectTiming.Aura ||
        compiler is War3AbilityCompilerKind.Defend or
            War3AbilityCompilerKind.Avatar or
            War3AbilityCompilerKind.DivineShield;

    private static ImmutableArray<AbilityEffectProfile> One(
        AbilityEffectProfile effect) => [effect];

    private static AbilityTargetFlags TargetFlags(
        War3AbilityCompilerKind compiler,
        AbilityActivationKind activation,
        IEnumerable<string> values)
    {
        var tokens = values.Select(value => value.ToLowerInvariant()).ToArray();
        if (activation == AbilityActivationKind.Passive && tokens.Length == 0)
            return AbilityTargetFlags.None;
        if (compiler == War3AbilityCompilerKind.Resurrection)
            return AbilityTargetFlags.Self | AbilityTargetFlags.Dead;
        if (compiler == War3AbilityCompilerKind.MassTeleport)
            return AbilityTargetFlags.Unit | AbilityTargetFlags.Building |
                   AbilityTargetFlags.Friendly | AbilityTargetFlags.Alive |
                   AbilityTargetFlags.Ground | AbilityTargetFlags.Vulnerable |
                   AbilityTargetFlags.Invulnerable |
                   AbilityTargetFlags.NotSelf;
        if (activation is AbilityActivationKind.Instant or AbilityActivationKind.Toggle)
            return AbilityTargetFlags.Self | AbilityTargetFlags.Alive;
        if (activation is AbilityActivationKind.TargetPoint or
            AbilityActivationKind.ChannelPoint)
            return AbilityTargetFlags.Point;

        var flags = AbilityTargetFlags.Unit | AbilityTargetFlags.Alive;
        foreach (var token in tokens)
        {
            flags |= token switch
            {
                "self" => AbilityTargetFlags.Self,
                "friend" or "ally" => AbilityTargetFlags.Friendly,
                "enemy" => AbilityTargetFlags.Enemy,
                "neutral" => AbilityTargetFlags.Neutral,
                "structure" => AbilityTargetFlags.Building,
                "dead" => AbilityTargetFlags.Dead,
                "ground" => AbilityTargetFlags.Ground,
                "air" => AbilityTargetFlags.Air,
                "organic" => AbilityTargetFlags.Organic,
                "mechanical" => AbilityTargetFlags.Mechanical,
                "hero" => AbilityTargetFlags.Hero,
                "nonhero" => AbilityTargetFlags.NonHero,
                "invu" => AbilityTargetFlags.Invulnerable,
                "vuln" => AbilityTargetFlags.Vulnerable,
                "ward" => AbilityTargetFlags.Ward,
                "tree" => AbilityTargetFlags.Tree,
                "debris" => AbilityTargetFlags.Debris,
                "player" => AbilityTargetFlags.PlayerControlled,
                "notself" => AbilityTargetFlags.NotSelf,
                "ancient" => AbilityTargetFlags.Ancient,
                "nonancient" => AbilityTargetFlags.NonAncient,
                "nonsapper" => AbilityTargetFlags.NonSapper,
                "bridge" => AbilityTargetFlags.Bridge,
                "item" => AbilityTargetFlags.Item,
                "wall" => AbilityTargetFlags.Wall,
                "alive" => AbilityTargetFlags.Alive,
                _ => AbilityTargetFlags.None
            };
        }
        if ((flags & (AbilityTargetFlags.Self | AbilityTargetFlags.Friendly |
                      AbilityTargetFlags.Enemy | AbilityTargetFlags.Neutral)) == 0)
            flags |= AbilityTargetFlags.Self | AbilityTargetFlags.Friendly |
                     AbilityTargetFlags.Enemy | AbilityTargetFlags.Neutral;
        if (compiler == War3AbilityCompilerKind.Resurrection)
            flags |= AbilityTargetFlags.Dead;
        return flags;
    }

    private static float ChannelSeconds(
        War3AbilityCompilerKind compiler,
        War3ObjectLevel level,
        float duration) => compiler switch
    {
        War3AbilityCompilerKind.SiphonMana or
            War3AbilityCompilerKind.DrainMana =>
            MathF.Max(0.1f, duration),
        War3AbilityCompilerKind.Blizzard => MathF.Max(1f, Data(level, "A")),
        _ => 0f
    };

    private static float CastSeconds(
        War3AbilityCompilerKind compiler,
        War3ObjectLevel level) => compiler ==
            War3AbilityCompilerKind.MassTeleport
        ? MathF.Max(
            MathF.Max(0f, level.CastTime ?? 0f), Data(level, "B"))
        : MathF.Max(0f, level.CastTime ?? 0f);

    private float Distance(float? value) =>
        MathF.Max(0f, value ?? 0f) * policy.WorldDistanceScale;

    private static float Data(War3ObjectLevel level, string key)
    {
        if (!level.Data.TryGetValue(key, out var value) ||
            !float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var result) ||
            !float.IsFinite(result))
            return 0f;
        return result;
    }

    private static string DataText(
        War3ObjectLevel level,
        string key,
        string fallback) =>
        level.Data.TryGetValue(key, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;

    private static UnitManaProfile ManaProfile(
        string objectId,
        War3UnitData data)
    {
        var mana = data.Summary.Mana;
        var maximum = mana.Effective is > 0f
            ? mana.Effective.Value
            : mana.Maximum is > 0f
                ? mana.Maximum.Value
                : objectId switch
                {
                    "Hamg" => 285f,
                    "Hmkg" => 255f,
                    "Hpal" => 255f,
                    "Hblm" => 315f,
                    _ => 0f
                };
        var initial = maximum <= 0f
            ? 0f
            : mana.Initial is > 0f
                ? MathF.Min(maximum, mana.Initial.Value)
                : maximum;
        if (data.Identity.IsHero && initial < maximum * 0.6f)
            initial = maximum * 0.7f;
        return new UnitManaProfile(
            initial, maximum, MathF.Max(0f, mana.Regeneration ?? 0f));
    }

    internal static AbilityUnitTraits AdaptUnitTraits(War3UnitData data)
    {
        var table = data.Editor.FirstOrDefault(pair =>
            pair.Key.Equals(
                "UnitBalance", StringComparison.OrdinalIgnoreCase)).Value;
        if (table is null) return AbilityUnitTraits.None;
        var value = table.FirstOrDefault(pair =>
            pair.Key.Equals("type", StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(value)) return AbilityUnitTraits.None;
        var traits = AbilityUnitTraits.None;
        foreach (var token in value.Split(
                     ',', StringSplitOptions.RemoveEmptyEntries |
                          StringSplitOptions.TrimEntries))
        {
            traits |= token.ToLowerInvariant() switch
            {
                "ancient" => AbilityUnitTraits.Ancient,
                "sapper" => AbilityUnitTraits.Sapper,
                "ward" => AbilityUnitTraits.Ward,
                "undead" => AbilityUnitTraits.Undead,
                _ => AbilityUnitTraits.None
            };
        }
        return traits;
    }

    internal static int ExperienceBountyForUnitLevel(int level)
    {
        level = Math.Clamp(level, 1, 100);
        // Warcraft's standard unit experience progression starts at 25 and
        // increases its per-level delta by five (25, 40, 60, 85, ...).
        return checked((5 * level * level + 15 * level + 30) / 2);
    }

    private AbilitySummonProfile WaterElemental(War3ObjectLevel level)
    {
        var index = Math.Clamp(level.Level, 1, 3) - 1;
        var health = new[] { 525f, 675f, 900f }[index];
        var damage = new[] { 19f, 33f, 44f }[index];
        var movement = Movement(
            $"水元素 {level.Level}", 12f, 220f, 1_100f);
        var combat = new CombatProfileSnapshot(
            health, damage, 80f, 180f, 1.5f, 0.3f, 315f,
            CombatPositioningKind.Ranged, 0f,
            CombatAttribute.Biological, ProjectileSpeed: 380f);
        var objectId = level.SummonedUnitId ??
                       (index switch { 0 => "hwat", 1 => "hwt2", _ => "hwt3" });
        return AdaptSummon(
            objectId, movement, combat,
            UnitPerceptionProfileSnapshot.Standard,
            level.Duration is > 0f ? level.Duration.Value : 60f);
    }

    private AbilitySummonProfile Phoenix(War3ObjectLevel level)
    {
        var movement = Movement("火凤凰", 16f, 320f, 1_500f);
        var combat = new CombatProfileSnapshot(
            1_250f, 68f, 120f, 220f, 1.4f, 0.25f, 385f,
            CombatPositioningKind.Ranged, 1f,
            CombatAttribute.Biological, ProjectileSpeed: 420f);
        return AdaptSummon(
            level.SummonedUnitId ?? "hphx", movement, combat,
            UnitPerceptionProfileSnapshot.ElevatedObserver(300f), 60f);
    }

    private AbilitySummonProfile AdaptSummon(
        string objectId,
        UnitMovementProfileSnapshot movement,
        CombatProfileSnapshot combat,
        UnitPerceptionProfileSnapshot perception,
        float lifetime)
    {
        var fallback = new UnitTypeProfile(
            0, movement.Name, movement, combat, false)
        {
            Perception = perception
        };
        var definition = new War3UnitDefinition(
            0, objectId, movement.Name, "召唤单位",
            string.Empty, string.Empty, string.Empty);
        var adapted = new War3GameplayDataAdapter(units, policy)
            .ApplyUnitProfile(definition, fallback);
        return new AbilitySummonProfile(
            objectId, adapted.Movement, adapted.Combat,
            adapted.Perception, lifetime);
    }

    private static UnitMovementProfileSnapshot Movement(
        string name,
        float radius,
        float speed,
        float acceleration)
    {
        var clearance = MovementClearance.FromPhysicalRadius(radius);
        return new UnitMovementProfileSnapshot(
            0, name, radius, speed, acceleration,
            clearance.Class, clearance.NavigationRadius);
    }

    private War3AbilityDefinition AdaptPresentation(
        AbilityProfile profile,
        War3ObjectEditorData data)
    {
        var buffs = data.Summary.Levels
            .SelectMany(value => value.BuffIds)
            .Distinct(StringComparer.Ordinal).ToArray();
        var effects = data.Summary.Levels
            .SelectMany(value => value.EffectIds)
            .Distinct(StringComparer.Ordinal).ToArray();
        return new War3AbilityDefinition(
            profile.Id, profile.RawId, profile.Name, profile.Description,
            profile.IconPath, profile.Hotkey,
            MergeModels(
                Models(data.Profile, "Casterart"),
                RelatedModels(buffs.Concat(effects), "Casterart")),
            MergeModels(
                Models(data.Profile, "Targetart"),
                RelatedModels(buffs, "Targetart", "Specialart")),
            MergeModels(
                Models(data.Profile, "Effectart", "Specialart", "Areaeffectart"),
                RelatedModels(
                    effects, "Effectart", "Specialart", "Areaeffectart",
                    "Targetart")),
            MergeModels(
                Models(data.Profile, "Missileart"),
                RelatedModels(buffs.Concat(effects), "Missileart")),
            buffs, effects,
            data.Identity.RequiredHeroLevel,
            data.Identity.HeroLevelSkip,
            CleanTooltip(Value(data.Profile, "Untip") ?? string.Empty),
            CleanTooltip(Value(data.Profile, "Unubertip") ?? string.Empty),
            Value(data.Profile, "Unart") ?? string.Empty,
            NormalizeHotkey(Value(data.Profile, "Unhotkey")));
    }

    private string[] RelatedModels(
        IEnumerable<string> objectIds,
        params string[] keys)
    {
        var output = new List<string>();
        foreach (var objectId in objectIds.Distinct(StringComparer.Ordinal))
        {
            if (!buffEffects.TryGet(objectId, out var related)) continue;
            output.AddRange(Models(related.Profile, keys));
        }
        return output.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] MergeModels(params IEnumerable<string>[] values) =>
        values.SelectMany(value => value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? Asset(War3ObjectEditorData data, string key) =>
        data.Assets.TryGetValue(key, out var values) && values.Length > 0
            ? values[0].RequestedPath
            : null;

    private static string[] Models(
        IReadOnlyDictionary<string, string> values,
        params string[] keys) => keys
        .SelectMany(key => (Value(values, key) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries))
        .Where(value => value.Length > 0)
        .Select(value => value.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
            ? value[..^4] + ".mdx"
            : value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string? Value(
        IReadOnlyDictionary<string, string> values,
        string key)
    {
        foreach (var pair in values)
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        return null;
    }

    private static string NormalizeHotkey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var character = value.Trim().FirstOrDefault(char.IsLetterOrDigit);
        return character == default
            ? string.Empty
            : char.ToUpperInvariant(character).ToString();
    }

    private static string CleanTooltip(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var output = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '|' || index + 1 >= value.Length)
            {
                output.Append(value[index]);
                continue;
            }
            var command = char.ToLowerInvariant(value[index + 1]);
            if (command == 'n')
            {
                output.Append('\n');
                index++;
                continue;
            }
            if (command == 'r')
            {
                index++;
                continue;
            }
            if (command == 'c' && index + 9 < value.Length &&
                value.AsSpan(index + 2, 8).ToString().All(Uri.IsHexDigit))
            {
                index += 9;
                continue;
            }
            output.Append(value[index]);
        }
        return output.ToString().Trim().Trim('"');
    }
}
