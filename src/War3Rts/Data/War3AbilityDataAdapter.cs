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
    string AlternateHotkey = "")
{
    public string[] AnimationNames { get; init; } = [];
    public string[] BuffModels { get; init; } = [];
    public string[] CasterAttachments { get; init; } = [];
    public string[] TargetAttachments { get; init; } = [];
    public string[] BuffAttachments { get; init; } = [];
    public int CasterAttachmentCount { get; init; }
    public int TargetAttachmentCount { get; init; }
    public int BuffAttachmentCount { get; init; }
    public string EffectSound { get; init; } = string.Empty;
    public string EffectSoundLooped { get; init; } = string.Empty;
}

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
                ManaProfile(unitData),
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
                    definition.TypeId, abilityIds,
                    ManaProfile(buildingData)));
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
        return War3AbilityBehaviorRegistry.Resolve(baseCode, data.Id).Compiler is
            War3AbilityCompilerKind.BuildingMilitiaCall or
            War3AbilityCompilerKind.DetectionAura or
            War3AbilityCompilerKind.Feedback or
            War3AbilityCompilerKind.Flare;
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
            data.Identity.HeroLevelSkip,
            ProjectileProfile(data));
    }

    private AbilityProjectileProfile ProjectileProfile(
        War3ObjectEditorData data)
    {
        if (Models(data.Profile, "Missileart").Length == 0)
            return default;
        var speed = ParseProfileFloat(data.Profile, "Missilespeed");
        var arc = ParseProfileFloat(data.Profile, "Missilearc");
        var homing = (Value(data.Profile, "MissileHoming") ?? string.Empty)
            .Trim().Equals("1", StringComparison.Ordinal);
        return new AbilityProjectileProfile(
            Distance(speed), Math.Clamp(arc ?? 0f, 0f, 4f), homing);
    }

    private static float? ParseProfileFloat(
        IReadOnlyDictionary<string, string> profile,
        string key) => float.TryParse(
        Value(profile, key), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var value) &&
        float.IsFinite(value) && value >= 0f
            ? value
            : null;

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
                    MovementSpeedMultiplier: Math.Clamp(
                        RequiredPositiveData(level, "C", compiler),
                        0.1f, 1f)))),
            War3AbilityCompilerKind.Heal => One(Heal(
                RequiredPositiveData(level, "A", compiler))),
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
                Radius: Distance(level.Area),
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
                AbilityStatusFlags.AttackDisabled)),
            War3AbilityCompilerKind.Slow => One(Status(
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                duration,
                modifier: new AbilityStatModifier(
                    MovementSpeedMultiplier: Math.Clamp(
                        RequiredPositiveData(level, "A", compiler),
                        0.1f, 1f),
                    AttackCooldownMultiplier: 1f / MathF.Max(
                        0.1f,
                        1f - RequiredPositiveData(level, "B", compiler))))),
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
                Value: -RequiredPositiveData(level, "A", compiler),
                SecondaryValue: MathF.Max(0f, b),
                DamageKind: AbilityDamageKind.Magic,
                HeroValue: -RequiredPositiveData(level, "C", compiler),
                HeroSecondaryValue: MathF.Max(0f, d))),
            War3AbilityCompilerKind.Flare => One(new AbilityEffectProfile(
                AbilityEffectKind.Reveal, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtTarget, AbilityRelationFilter.Self,
                Radius: Distance(level.Area), Duration: duration)),
            War3AbilityCompilerKind.FragmentationShards => AttackBands(
                Distance(level.Area), Distance(a), Distance(b),
                RequiredPositiveData(level, "C", compiler),
                RequiredPositiveData(level, "D", compiler),
                RequiredPositiveData(level, "E", compiler)),
            War3AbilityCompilerKind.DetectionAura => One(Aura(
                AbilityEffectSelector.Caster, AbilityRelationFilter.Self,
                modifier: new AbilityStatModifier(
                    DetectionRangeAdd: Distance(RequiredPositive(
                        level.Range, "range", compiler, level.Level))))),
            War3AbilityCompilerKind.FlakCannons => AttackBands(
                Distance(level.Area), Distance(a), Distance(b),
                RequiredPositiveData(level, "C", compiler),
                RequiredPositiveData(level, "D", compiler),
                RequiredPositiveData(level, "E", compiler)),
            War3AbilityCompilerKind.Barrage => One(AttackSplash(
                RequiredPositiveData(level, "A", compiler),
                Distance(RequiredPositive(
                    level.Area, "area", compiler, level.Level)),
                Math.Max(1, (int)MathF.Round(
                    RequiredPositiveData(level, "C", compiler))))),
            War3AbilityCompilerKind.Cloud => One(Status(
                AbilityEffectSelector.AreaAtTarget, AbilityRelationFilter.Enemy,
                duration, AbilityStatusFlags.AttackDisabled,
                radius: Distance(level.Area))),
            War3AbilityCompilerKind.SiphonMana => One(new AbilityEffectProfile(
                AbilityEffectKind.Damage, AbilityEffectTiming.ChannelPulse,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                Value: RequiredPositiveData(level, "A", compiler), Interval: 1f,
                DamageKind: AbilityDamageKind.Magic)),
            War3AbilityCompilerKind.Blizzard => One(new AbilityEffectProfile(
                AbilityEffectKind.Damage, AbilityEffectTiming.ChannelPulse,
                AbilityEffectSelector.AreaAtTarget,
                AbilityRelationFilter.Any,
                Value: RequiredPositiveData(level, "B", compiler),
                Radius: Distance(level.Area),
                Interval: 1f,
                DamageKind: AbilityDamageKind.Magic,
                PulseCount: Math.Max(
                    1, (int)MathF.Round(
                        RequiredPositiveData(level, "A", compiler))),
                MaximumTotalValue: MathF.Max(0f, Data(level, "F")),
                BuildingValueMultiplier: Math.Clamp(d, 0f, 1f),
                VisualCount: Math.Max(
                    1, (int)MathF.Round(
                        RequiredPositiveData(level, "C", compiler))))),
            War3AbilityCompilerKind.SummonWaterElemental => One(new AbilityEffectProfile(
                AbilityEffectKind.Summon, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtCaster, AbilityRelationFilter.Self,
                MaximumTargets: Math.Max(1, (int)MathF.Round(
                    RequiredPositiveData(level, "A", compiler))),
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
                    1, (int)MathF.Round(
                        RequiredPositiveData(level, "A", compiler))),
                VisualCount: Math.Max(
                    1, (int)MathF.Round(
                        RequiredPositiveData(level, "A", compiler))),
                ClusteredPlacement: c >= 0.5f)),
            War3AbilityCompilerKind.StormBolt => One(Status(
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                duration, AbilityStatusFlags.Stunned,
                value: RequiredPositiveData(level, "A", compiler),
                damageKind: AbilityDamageKind.Magic)),
            War3AbilityCompilerKind.ThunderClap => One(Status(
                AbilityEffectSelector.AreaAtCaster,
                AbilityRelationFilter.Enemy | AbilityRelationFilter.Neutral,
                duration,
                modifier: new AbilityStatModifier(
                    MovementSpeedMultiplier:
                        RequiredPositiveData(level, "C", compiler),
                    AttackCooldownMultiplier: 1f /
                        RequiredPositiveData(level, "D", compiler)),
                value: RequiredPositiveData(level, "A", compiler),
                radius: Distance(level.Area),
                damageKind: AbilityDamageKind.Magic)),
            War3AbilityCompilerKind.Bash => One(new AbilityEffectProfile(
                AbilityEffectKind.ApplyStatus, AbilityEffectTiming.AttackHit,
                AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                Value: RequiredPositiveData(level, "C", compiler),
                Duration: duration,
                Interval: RequiredPositiveData(level, "A", compiler),
                Status: AbilityStatusFlags.Stunned,
                DamageKind: AbilityDamageKind.Physical)),
            War3AbilityCompilerKind.Avatar => One(Status(
                AbilityEffectSelector.Caster, AbilityRelationFilter.Self,
                duration,
                AbilityStatusFlags.MagicImmune,
                new AbilityStatModifier(
                    AttackDamageAdd: RequiredPositiveData(
                        level, "C", compiler),
                    ArmorAdd: RequiredPositiveData(level, "A", compiler),
                    MaximumHealthAdd: RequiredPositiveData(
                        level, "B", compiler)))),
            War3AbilityCompilerKind.HolyLight =>
            [
                Heal(RequiredPositiveData(level, "A", compiler)) with
                {
                    ExcludedUnitTraits = AbilityUnitTraits.Undead
                },
                new AbilityEffectProfile(
                    AbilityEffectKind.Damage, AbilityEffectTiming.Impact,
                    AbilityEffectSelector.Primary, AbilityRelationFilter.Enemy,
                    Value: RequiredPositiveData(level, "A", compiler) * 0.5f,
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
                MaximumTargets: Math.Max(1, (int)MathF.Round(
                    RequiredPositiveData(level, "A", compiler))))),
            War3AbilityCompilerKind.FlameStrike => FlameStrikeEffects(
                level, duration, heroDuration),
            War3AbilityCompilerKind.Banish => One(Status(
                AbilityEffectSelector.Primary, AbilityRelationFilter.Any,
                duration,
                AbilityStatusFlags.Banished |
                AbilityStatusFlags.AttackDisabled,
                new AbilityStatModifier(
                    MovementSpeedMultiplier:
                        RequiredPositiveData(level, "A", compiler)))),
            War3AbilityCompilerKind.DrainMana =>
                One(new AbilityEffectProfile(
                    AbilityEffectKind.TransferMana,
                    AbilityEffectTiming.ChannelPulse,
                    AbilityEffectSelector.Primary, AbilityRelationFilter.Any,
                    Value: FirstPositiveData(level, compiler, "E", "B"),
                    Interval: RequiredPositiveData(level, "C", compiler))),
            War3AbilityCompilerKind.SummonPhoenix => One(new AbilityEffectProfile(
                AbilityEffectKind.Summon, AbilityEffectTiming.Impact,
                AbilityEffectSelector.AreaAtCaster, AbilityRelationFilter.Self,
                MaximumTargets: Math.Max(1, (int)MathF.Round(
                    RequiredPositiveData(level, "A", compiler))),
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
                    UnitForm: MilitiaForm(level, duration))),
            _ => ImmutableArray<AbilityEffectProfile>.Empty
        };
    }

    private AbilityUnitFormProfile MilitiaForm(
        War3ObjectLevel level,
        float duration)
    {
        var source = HasDataText(level, "A") && HasDataText(level, "B")
            ? level
            : FindUnitFormLevel();
        var normalId = RequiredDataText(
            source, "A", War3AbilityCompilerKind.MilitiaTransform);
        var alternateId = RequiredDataText(
            source, "B", War3AbilityCompilerKind.MilitiaTransform);
        if (!unitTypes.TryGetValue(normalId, out var normal) ||
            !unitTypes.TryGetValue(alternateId, out var alternate))
            throw new InvalidOperationException(
                $"Militia form units {normalId}/{alternateId} are not bound.");
        return new AbilityUnitFormProfile(
            normal, alternate, RequiredPositive(
                duration > 0f ? duration : source.Duration,
                "duration", War3AbilityCompilerKind.MilitiaTransform,
                source.Level),
            BuildingFunctionKind.TownHall);
    }

    private War3ObjectLevel FindUnitFormLevel()
    {
        foreach (var entry in abilities.Entries.OrderBy(
                     value => value.Id, StringComparer.Ordinal))
        {
            if (!abilities.TryGet(entry.Id, out var candidate)) continue;
            var baseCode = string.IsNullOrWhiteSpace(candidate.Identity.BaseCode)
                ? candidate.Id
                : candidate.Identity.BaseCode;
            if (War3AbilityBehaviorRegistry.Resolve(baseCode, candidate.Id)
                    .Compiler != War3AbilityCompilerKind.MilitiaTransform)
                continue;
            var level = candidate.Summary.Levels.FirstOrDefault(value =>
                HasDataText(value, "A") && HasDataText(value, "B"));
            if (level is not null) return level;
        }
        throw new InvalidDataException(
            "A militia unit-form ability with JSON DataA/DataB was not found.");
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
        const War3AbilityCompilerKind compiler =
            War3AbilityCompilerKind.FlameStrike;
        var fullDamage = RequiredPositiveData(level, "A", compiler);
        var fullInterval = RequiredPositiveData(level, "B", compiler);
        var partialDamage = Data(level, "C");
        var partialInterval = partialDamage > 0f
            ? RequiredPositiveData(level, "D", compiler)
            : 1f;
        var buildingMultiplier = Math.Clamp(Data(level, "E"), 0f, 1f);
        var maximumDamage = MathF.Max(0f, Data(level, "F"));
        fullDuration = Math.Clamp(
            RequiredPositive(
                fullDuration, "hero duration", compiler, level.Level),
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

    private static float RequiredPositiveData(
        War3ObjectLevel level,
        string key,
        War3AbilityCompilerKind compiler) => RequiredPositive(
        Data(level, key), $"Data{key}", compiler, level.Level);

    private static float FirstPositiveData(
        War3ObjectLevel level,
        War3AbilityCompilerKind compiler,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Data(level, key);
            if (value > 0f) return value;
        }
        throw new InvalidDataException(
            $"{compiler} level {level.Level} requires one positive JSON field: " +
            string.Join(", ", keys.Select(value => $"Data{value}")));
    }

    private static float RequiredPositive(
        float? value,
        string field,
        War3AbilityCompilerKind compiler,
        int level)
    {
        if (value is > 0f and var result && float.IsFinite(result))
            return result;
        throw new InvalidDataException(
            $"{compiler} level {level} requires positive JSON {field}.");
    }

    private static float RequiredNonNegative(
        float? value,
        string field,
        War3AbilityCompilerKind compiler,
        int level)
    {
        if (value is >= 0f and var result && float.IsFinite(result))
            return result;
        throw new InvalidDataException(
            $"{compiler} level {level} requires non-negative JSON {field}.");
    }

    private static bool HasDataText(
        War3ObjectLevel level,
        string key) =>
        level.Data.TryGetValue(key, out var value) &&
        !string.IsNullOrWhiteSpace(value) && value is not "_" and not "-";

    private static string RequiredDataText(
        War3ObjectLevel level,
        string key,
        War3AbilityCompilerKind compiler)
    {
        if (HasDataText(level, key)) return level.Data[key].Trim();
        throw new InvalidDataException(
            $"{compiler} level {level.Level} requires JSON Data{key}.");
    }

    private static UnitManaProfile ManaProfile(War3UnitData data)
    {
        var mana = data.Summary.Mana;
        var maximum = mana.Effective is > 0f
            ? mana.Effective.Value
            : mana.Maximum is > 0f
                ? mana.Maximum.Value
                : 0f;
        var initial = maximum <= 0f
            ? 0f
            : mana.Initial is > 0f
                ? MathF.Min(maximum, mana.Initial.Value)
                : maximum;
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
        => AdaptSummon(level,
            War3AbilityCompilerKind.SummonWaterElemental);

    private AbilitySummonProfile Phoenix(War3ObjectLevel level)
        => AdaptSummon(level, War3AbilityCompilerKind.SummonPhoenix);

    private AbilitySummonProfile AdaptSummon(
        War3ObjectLevel level,
        War3AbilityCompilerKind compiler)
    {
        var objectId = level.SummonedUnitId?.Trim();
        if (string.IsNullOrWhiteSpace(objectId))
            throw new InvalidDataException(
                $"{compiler} level {level.Level} requires a JSON summonedUnitId.");
        if (!units.TryGet(objectId, out var source))
            throw new InvalidDataException(
                $"{compiler} level {level.Level} references missing unit JSON {objectId}.");
        _ = RequiredPositive(source.Summary.Movement.CollisionSize,
            "summoned unit collisionSize", compiler, level.Level);
        _ = RequiredPositive(source.Summary.Movement.Speed,
            "summoned unit movement speed", compiler, level.Level);
        _ = RequiredPositive(source.Summary.HitPoints.Effective,
            "summoned unit effective hit points", compiler, level.Level);
        _ = RequiredPositive(source.Summary.Sight.Day,
            "summoned unit day sight", compiler, level.Level);
        var sourceAttack = source.Summary.Combat.Attacks.FirstOrDefault(
            value => value.Enabled);
        if (sourceAttack is null)
            throw new InvalidDataException(
                $"{compiler} level {level.Level} summoned unit {objectId} has no enabled JSON attack.");
        _ = RequiredPositive(sourceAttack.Damage.Average,
            "summoned unit average attack damage", compiler, level.Level);
        _ = RequiredPositive(sourceAttack.Cooldown,
            "summoned unit attack cooldown", compiler, level.Level);

        var name = string.IsNullOrWhiteSpace(source.DisplayName)
            ? objectId
            : source.DisplayName;
        var radius = policy.MinimumUnitRadius;
        var speed = policy.MinimumMovementSpeed;
        var clearance = MovementClearance.FromPhysicalRadius(radius);
        var movement = new UnitMovementProfileSnapshot(
            0, name, radius, speed,
            speed * policy.AccelerationMultiplier,
            clearance.Class, clearance.NavigationRadius);
        var combat = new CombatProfileSnapshot(
            1f, 0f, 0f, 0f, 1f, 0f, 0f,
            CombatPositioningKind.Melee, 0f,
            CombatAttribute.Biological);
        var fallback = new UnitTypeProfile(
            0, name, movement, combat, false);
        var definition = new War3UnitDefinition(
            0, objectId, name, string.Empty,
            string.Empty, string.Empty, string.Empty);
        var adapted = new War3GameplayDataAdapter(units, policy)
            .ApplyUnitProfile(definition, fallback);
        return new AbilitySummonProfile(
            objectId, adapted.Movement, adapted.Combat,
            adapted.Perception,
            RequiredNonNegative(
                level.Duration, "duration", compiler, level.Level));
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
        var buffModels = RelatedModels(
            buffs, "Targetart", "Specialart", "Effectart");
        return new War3AbilityDefinition(
            profile.Id, profile.RawId, profile.Name, profile.Description,
            profile.IconPath, profile.Hotkey,
            Models(data.Profile, "Casterart"),
            Models(data.Profile, "Targetart"),
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
            NormalizeHotkey(Value(data.Profile, "Unhotkey")))
        {
            AnimationNames = AnimationCandidates(data.Profile),
            BuffModels = buffModels,
            CasterAttachments = AttachmentPaths(data.Profile, "Casterattach"),
            TargetAttachments = AttachmentPaths(data.Profile, "Targetattach"),
            BuffAttachments = RelatedAttachments(buffs, "Targetattach",
                "Specialattach"),
            CasterAttachmentCount = Integer(data.Profile, "Casterattachcount"),
            TargetAttachmentCount = Integer(data.Profile, "Targetattachcount"),
            BuffAttachmentCount = RelatedAttachmentCount(
                buffs, "Targetattachcount", "Specialattachcount"),
            EffectSound = Value(data.Profile, "Effectsound") ?? string.Empty,
            EffectSoundLooped = Value(data.Profile, "Effectsoundlooped") ??
                                  string.Empty
        };
    }

    private string[] RelatedAttachments(
        IEnumerable<string> objectIds,
        params string[] keys)
    {
        var output = new List<string>();
        foreach (var objectId in objectIds.Distinct(StringComparer.Ordinal))
        {
            if (!buffEffects.TryGet(objectId, out var related)) continue;
            foreach (var key in keys)
                output.AddRange(AttachmentPaths(related.Profile, key));
        }
        return output.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private int RelatedAttachmentCount(
        IEnumerable<string> objectIds,
        params string[] keys)
    {
        var count = 0;
        foreach (var objectId in objectIds.Distinct(StringComparer.Ordinal))
        {
            if (!buffEffects.TryGet(objectId, out var related)) continue;
            foreach (var key in keys) count = Math.Max(count, Integer(related.Profile, key));
        }
        return count;
    }

    private static string[] AnimationCandidates(
        IReadOnlyDictionary<string, string> profile)
    {
        var tokens = TokenList(profile, "Animnames");
        if (tokens.Length == 0)
            return ["Spell", "Spell Slam", "Spell Channel", "Attack"];
        var normalized = tokens.Select(Title).ToArray();
        return new[] { string.Join(' ', normalized) }
            .Concat(normalized)
            .Concat(new[] { "Spell", "Attack" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] TokenList(
        IReadOnlyDictionary<string, string> profile,
        string key) => (Value(profile, key) ?? string.Empty)
        .Split(',', StringSplitOptions.TrimEntries |
                    StringSplitOptions.RemoveEmptyEntries)
        .Where(value => value is not "_" and not "-")
        .ToArray();

    internal static string[] AttachmentPaths(
        IReadOnlyDictionary<string, string> profile,
        string key)
    {
        var paths = profile
            .Where(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                           pair.Key.StartsWith(key, StringComparison.OrdinalIgnoreCase) &&
                           int.TryParse(pair.Key.AsSpan(key.Length), out _))
            .Select(pair => new
            {
                Index = pair.Key.Length == key.Length
                    ? 0
                    : int.Parse(pair.Key.AsSpan(key.Length),
                        NumberStyles.Integer, CultureInfo.InvariantCulture),
                Path = string.Join(',', pair.Value
                    .Split(',', StringSplitOptions.TrimEntries |
                                StringSplitOptions.RemoveEmptyEntries)
                    .Where(value => value is not "_" and not "-"))
            })
            .Where(value => value.Path.Length > 0)
            .OrderBy(value => value.Index)
            .Select(value => value.Path)
            .ToArray();
        var declaredCount = Integer(profile, key + "count");
        return declaredCount > 0
            ? paths.Take(declaredCount).ToArray()
            : paths;
    }

    private static int Integer(
        IReadOnlyDictionary<string, string> profile,
        string key) => int.TryParse(
        Value(profile, key), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string Title(string value) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            value.Trim().ToLowerInvariant());

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
