using War3Rts;
using War3Rts.Data;
using RtsDemo.Demos.War3;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Simulation;
using System.Globalization;

namespace RtsDemo.Tests;

public static class War3AbilityDataClosureSelfTest
{
    public static SelfTestResult Run()
    {
        try
        {
            var dataRoot = War3AssetPack.AbsolutePath("data");
            var units = War3UnitDataCatalog.Load(
                Path.Combine(dataRoot, "unit_editor_data"));
            var abilities = War3ObjectDataCatalog.LoadAbility(
                Path.Combine(dataRoot, "ability_editor_data"));
            var buffEffects = War3ObjectDataCatalog.LoadBuffEffect(
                Path.Combine(dataRoot, "buff_effect_editor_data"));
            var metadata = War3AbilityMetadataCatalog.Load(
                Path.Combine(dataRoot, "ability_metadata"));

            var normalReferences = new HashSet<string>(StringComparer.Ordinal);
            var heroReferences = new HashSet<string>(StringComparer.Ordinal);
            var errors = new List<string>();
            foreach (var entry in units.Entries)
            {
                if (!units.TryGet(entry.Id, out var unit))
                {
                    errors.Add($"unit:{entry.Id}");
                    continue;
                }
                foreach (var id in unit.Summary.Abilities)
                {
                    normalReferences.Add(id);
                    if (!abilities.Contains(id)) errors.Add($"ability:{id}");
                }
                foreach (var id in unit.Summary.HeroAbilities)
                {
                    heroReferences.Add(id);
                    if (!abilities.Contains(id)) errors.Add($"hero:{id}");
                }
            }

            foreach (var entry in abilities.Entries)
            {
                if (!abilities.TryGet(entry.Id, out var ability))
                {
                    errors.Add($"ability-json:{entry.Id}");
                    continue;
                }
                if (!metadata.TryGetBinding(entry.Id, out var binding) ||
                    !binding.BaseCode.Equals(
                        ability.Identity.BaseCode, StringComparison.Ordinal))
                    errors.Add($"metadata:{entry.Id}");
                foreach (var id in ability.Summary.Levels
                             .SelectMany(level => level.BuffIds.Concat(level.EffectIds))
                             .Distinct(StringComparer.Ordinal))
                {
                    if (!buffEffects.Contains(id))
                        errors.Add($"buff-effect:{entry.Id}->{id}");
                }
            }

            var relatedPresentation =
                buffEffects.TryGet("XHbz", out var blizzardEffect) &&
                blizzardEffect.Identity.Effect &&
                blizzardEffect.Assets.TryGetValue(
                    "effectArt", out var effectArt) &&
                effectArt.Any(value =>
                    value.RequestedPath.Contains(
                        "BlizzardTarget", StringComparison.OrdinalIgnoreCase));
            var militiaAlternatePresentation =
                War3HumanContent.TryAbility(
                    "Amil", out var militiaPresentation) &&
                militiaPresentation is not null &&
                militiaPresentation.AlternateName.Contains(
                    "回到工作", StringComparison.Ordinal) &&
                militiaPresentation.AlternateDescription.Contains(
                    "城镇大厅", StringComparison.Ordinal) &&
                militiaPresentation.AlternateIconPath.Contains(
                    "BacktoWork", StringComparison.OrdinalIgnoreCase) &&
                militiaPresentation.AlternateHotkey == "W";
            var metadataLocalized =
                metadata.TryGetBinding("Adef", out var defendMetadata) &&
                defendMetadata.Fields.Any(value =>
                    value.DataIndex == 6 &&
                    value.DisplayName.Contains("偏转概率", StringComparison.Ordinal));
            var summonIdsPreserved =
                abilities.TryGet("AHwe", out var waterElemental) &&
                waterElemental.Summary.Levels.SelectMany(value => value.UnitIds)
                    .SequenceEqual(["hwat", "hwt2", "hwt3"]);
            var runtimeImport = War3HumanContent.AbilityImportStatus;
            var runtimeCoverage = War3AbilityRuntimeCoverageAudit.Analyze(
                abilities,
                units,
                runtimeImport.Definitions.Select(value => value.ObjectId),
                War3HumanContent.Technologies.Select(value => value.ObjectId));
            var runtimeCompilerMatrix = runtimeImport.Catalog.Abilities
                .Select(profile =>
                {
                    if (!abilities.TryGet(profile.RawId, out var source))
                        return (Compiler: War3AbilityCompilerKind.None,
                            HasEffects: false);
                    var baseCode = string.IsNullOrWhiteSpace(
                        source.Identity.BaseCode)
                        ? source.Id
                        : source.Identity.BaseCode;
                    var behavior = War3AbilityBehaviorRegistry.Resolve(
                        baseCode, source.Id);
                    return (behavior.Compiler,
                        HasEffects: profile.Levels.All(level =>
                            !level.Effects.IsDefaultOrEmpty));
                })
                .Where(value => value.Compiler != War3AbilityCompilerKind.None)
                .ToArray();
            var innerFireTechnology = War3HumanContent.Technologies.Single(
                value => value.ObjectId == "Rhpt").TechnologyId;
            var runtimeRequirementsPreserved =
                runtimeImport.Catalog.TryFind("AHmt", out var massTeleport) &&
                massTeleport.HeroAbility &&
                massTeleport.RequiredHeroLevel == 6 &&
                runtimeImport.Catalog.TryFind("Ainf", out var innerFire) &&
                innerFire.Levels.All(level =>
                    level.Requirements.Contains(new AbilityRequirementProfile(
                        AbilityRequirementKind.TechnologyLevel,
                        innerFireTechnology,
                        2)));
            var heroLearningBindings =
                runtimeImport.Catalog.TryFind("AHwe", out var waterSkill) &&
                runtimeImport.Catalog.TryBinding(
                    War3HumanContent.Archmage, out var archmageBinding) &&
                archmageBinding.Hero &&
                archmageBinding.Abilities.Any(value =>
                    value.AbilityId == waterSkill.Id && value.Level == 0);
            var runtimeManaFromJson = runtimeImport.Catalog.Bindings.All(binding =>
            {
                var definition = War3HumanContent.Units[binding.UnitTypeId];
                if (!units.TryGet(definition.ObjectId, out var source))
                    return false;
                var expected = source.Summary.Mana.Effective is > 0f
                    ? source.Summary.Mana.Effective.Value
                    : source.Summary.Mana.Maximum is > 0f
                        ? source.Summary.Mana.Maximum.Value
                        : 0f;
                var expectedInitial = expected <= 0f
                    ? 0f
                    : source.Summary.Mana.Initial is > 0f
                        ? MathF.Min(expected,
                            source.Summary.Mana.Initial.Value)
                        : expected;
                var expectedRegeneration = MathF.Max(
                    0f, source.Summary.Mana.Regeneration ?? 0f);
                return MathF.Abs(binding.Mana.Initial - expectedInitial) < 0.001f &&
                       MathF.Abs(binding.Mana.Maximum - expected) < 0.001f &&
                       MathF.Abs(binding.Mana.RegenerationPerSecond -
                                 expectedRegeneration) < 0.001f;
            }) && runtimeImport.Catalog.BuildingBindings.All(binding =>
            {
                var definition = War3HumanContent.Buildings[
                    binding.BuildingTypeId];
                if (!units.TryGet(definition.ObjectId, out var source))
                    return false;
                var expected = source.Summary.Mana.Effective is > 0f
                    ? source.Summary.Mana.Effective.Value
                    : source.Summary.Mana.Maximum is > 0f
                        ? source.Summary.Mana.Maximum.Value
                        : 0f;
                var expectedInitial = expected <= 0f
                    ? 0f
                    : source.Summary.Mana.Initial is > 0f
                        ? MathF.Min(expected,
                            source.Summary.Mana.Initial.Value)
                        : expected;
                var expectedRegeneration = MathF.Max(
                    0f, source.Summary.Mana.Regeneration ?? 0f);
                return MathF.Abs(binding.Mana.Initial - expectedInitial) < 0.001f &&
                       MathF.Abs(binding.Mana.Maximum - expected) < 0.001f &&
                       MathF.Abs(binding.Mana.RegenerationPerSecond -
                                 expectedRegeneration) < 0.001f;
            });
            var runtimeSummonsFromJson =
                runtimeImport.Catalog.TryFind("AHwe", out var runtimeWater) &&
                abilities.TryGet("AHwe", out var sourceWater) &&
                runtimeWater.Levels.Zip(sourceWater.Summary.Levels)
                    .All(value => value.First.Effects.Length == 1 &&
                        SummonMatchesUnitJson(
                            units,
                            value.First.Effects[0].Summon,
                            value.Second.SummonedUnitId,
                            value.Second.Duration)) &&
                runtimeImport.Catalog.TryFind("AHpx", out var runtimePhoenix) &&
                abilities.TryGet("AHpx", out var sourcePhoenix) &&
                runtimePhoenix.Levels.Zip(sourcePhoenix.Summary.Levels)
                    .All(value => value.First.Effects.Length == 1 &&
                        SummonMatchesUnitJson(
                            units,
                            value.First.Effects[0].Summon,
                            value.Second.SummonedUnitId,
                            value.Second.Duration));
            var runtimePresentationAttachments =
                War3HumanContent.TryAbility("AHta", out var revealPresentation) &&
                revealPresentation is not null &&
                revealPresentation.CasterAttachments.SequenceEqual(["overhead"]) &&
                buffEffects.TryGet("Xfhl", out var sixPointEffect) &&
                War3AbilityDataAdapter.AttachmentPaths(
                        sixPointEffect.Profile, "Targetattach")
                    .SequenceEqual([
                        "sprite,first", "sprite,second", "sprite,fifth",
                        "sprite,third", "sprite,fourth", "sprite,sixth"
                    ]);
            var runtimeConfiguredScalars =
                abilities.TryGet("Ainf", out var sourceInnerFire) &&
                runtimeImport.Catalog.TryFind("Ainf", out var innerFireScalar) &&
                innerFireScalar.Levels.Zip(sourceInnerFire.Summary.Levels)
                    .All(pair =>
                        TryData(pair.Second, "A", out var attack) &&
                        TryData(pair.Second, "B", out var armor) &&
                        TryData(pair.Second, "C", out var autoCastRange) &&
                        TryData(pair.Second, "D", out var healthRegeneration) &&
                        pair.Second.Range is > 0f and var sourceRange &&
                        Nearly(pair.First.Effects.Single().Modifier
                            .AttackDamageMultiplier, 1f + attack) &&
                        Nearly(pair.First.Effects.Single().Modifier.ArmorAdd,
                            armor) &&
                        Nearly(pair.First.Effects.Single().Modifier
                            .HealthRegenerationAdd, healthRegeneration) &&
                        Nearly(pair.First.AutoCastRange,
                            autoCastRange * pair.First.Range / sourceRange)) &&
                abilities.TryGet("Afbk", out var sourceFeedback) &&
                runtimeImport.Catalog.TryFind("Afbk", out var feedbackScalar) &&
                feedbackScalar.Levels.Zip(sourceFeedback.Summary.Levels)
                    .All(pair =>
                        TryData(pair.Second, "E", out var summonedDamage) &&
                        Nearly(pair.First.Effects.Single().SummonedValue,
                            summonedDamage)) &&
                abilities.TryGet("AHbn", out var sourceBanish) &&
                runtimeImport.Catalog.TryFind("AHbn", out var banishScalar) &&
                banishScalar.Levels.Zip(sourceBanish.Summary.Levels)
                    .All(pair =>
                        TryData(pair.Second, "A", out var movementReduction) &&
                        TryData(pair.Second, "B", out var attackReduction) &&
                        Nearly(pair.First.Effects.Single().Modifier
                            .MovementSpeedMultiplier, movementReduction) &&
                        Nearly(pair.First.Effects.Single().Modifier
                            .AttackCooldownMultiplier,
                            1f / MathF.Max(0.1f, 1f - attackReduction)));
            var runtimeEffectSemantics =
                abilities.TryGet("Afla", out var sourceFlare) &&
                float.TryParse(
                    sourceFlare.Summary.Levels[0].Data["B"],
                    NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var expectedFlareDelay) &&
                runtimeImport.Catalog.TryFind("Afla", out var flare) &&
                MathF.Abs(flare.Levels[0].CastSeconds -
                          expectedFlareDelay) < 0.001f &&
                runtimeImport.Catalog.TryFind("AHta", out var towerReveal) &&
                towerReveal.Levels[0].CastSeconds == 0f &&
                runtimeImport.Catalog.TryFind("AHtb", out var stormBolt) &&
                stormBolt.Projectile.Enabled &&
                MathF.Abs(stormBolt.Projectile.Speed -
                    1000f * 4f / 15f) < 0.001f &&
                stormBolt.Projectile.Homing &&
                stormBolt.Levels.All(level =>
                    level.Effects.Length == 1 &&
                    level.Effects[0].DamageKind == AbilityDamageKind.Magic &&
                    level.Effects[0].BuffId == "BPSE" &&
                    level.Effects[0].BuffPolarity ==
                    AbilityBuffPolarity.Harmful &&
                    level.Effects[0].BuffDispelKind ==
                    AbilityBuffDispelKind.Magic) &&
                runtimeImport.Catalog.TryFind("AHbh", out var bash) &&
                bash.Levels.All(level =>
                    level.Effects[0].DamageKind ==
                    AbilityDamageKind.Physical &&
                    level.Effects[0].BuffDispelKind ==
                    AbilityBuffDispelKind.Physical) &&
                runtimeImport.Catalog.TryFind("Ainf", out var innerFireBuff) &&
                innerFireBuff.Levels.All(level =>
                    level.Effects[0].BuffId == "Binf" &&
                    level.Effects[0].BuffPolarity ==
                    AbilityBuffPolarity.Beneficial &&
                    level.Effects[0].BuffStacking ==
                    AbilityBuffStackingKind.Refresh) &&
                runtimeImport.Catalog.TryFind("Adis", out var dispelMagic) &&
                dispelMagic.Levels.All(level =>
                    level.Effects[0].BuffDispelKind ==
                    AbilityBuffDispelKind.Magic &&
                    level.Effects[0].DamageKind == AbilityDamageKind.Magic &&
                    level.Effects[0].SecondaryValue == 200f) &&
                runtimeImport.Catalog.TryFind("AHdr", out var drainMana) &&
                drainMana.Levels.All(level =>
                    level.Effects.Length == 1 &&
                    level.Effects[0].Kind == AbilityEffectKind.TransferMana &&
                    level.Effects[0].Value is 30f or 60f or 90f &&
                    level.Effects[0].Interval == 1f) &&
                runtimeImport.Catalog.TryFind("Afbk", out var feedback) &&
                feedback.Levels.All(level =>
                    level.Effects[0].Value == -20f &&
                    level.Effects[0].SecondaryValue == 1f &&
                    level.Effects[0].HeroValue == -4f &&
                    level.Effects[0].HeroSecondaryValue == 1f) &&
                runtimeImport.Catalog.TryFind("AHhb", out var holyLight) &&
                holyLight.Levels.All(level =>
                    level.Effects.Length == 2 &&
                    level.Effects[0].Kind == AbilityEffectKind.Heal &&
                    level.Effects[0].ExcludedUnitTraits ==
                    AbilityUnitTraits.Undead &&
                    level.Effects[1].Kind == AbilityEffectKind.Damage &&
                    level.Effects[1].RequiredUnitTraits ==
                    AbilityUnitTraits.Undead) &&
                runtimeImport.Catalog.TryFind("Aflk", out var flak) &&
                (flak.Targets &
                 (AbilityTargetFlags.Unit | AbilityTargetFlags.Air |
                  AbilityTargetFlags.Enemy)) ==
                (AbilityTargetFlags.Unit | AbilityTargetFlags.Air |
                 AbilityTargetFlags.Enemy) &&
                (flak.Targets & AbilityTargetFlags.Ground) == 0 &&
                flak.Levels.All(level =>
                    level.Effects.Select(value => value.Value)
                        .SequenceEqual([7f, 6f, 5f]) &&
                    level.Effects[0].InnerRadius == 0f &&
                    level.Effects[1].InnerRadius == level.Effects[0].Radius &&
                    level.Effects[2].InnerRadius == level.Effects[1].Radius) &&
                runtimeImport.Catalog.TryFind("Aroc", out var barrage) &&
                (barrage.Targets &
                 (AbilityTargetFlags.Unit | AbilityTargetFlags.Air |
                  AbilityTargetFlags.Enemy)) ==
                (AbilityTargetFlags.Unit | AbilityTargetFlags.Air |
                 AbilityTargetFlags.Enemy) &&
                (barrage.Targets & AbilityTargetFlags.Ground) == 0 &&
                runtimeImport.Catalog.TryFind("Afsh", out var fragments) &&
                fragments.Levels.All(level =>
                    level.Effects.Select(value => value.Value)
                        .SequenceEqual([25f, 18f]) &&
                    level.Effects[1].InnerRadius == level.Effects[0].Radius) &&
                runtimeImport.Catalog.TryFind("AHfs", out var flameStrike) &&
                flameStrike.Activation == AbilityActivationKind.TargetPoint &&
                flameStrike.Levels.All(level =>
                    level.ChannelSeconds == 0f &&
                    level.Effects.Length == 2 &&
                    level.Effects.All(effect =>
                        effect.Timing ==
                            AbilityEffectTiming.PersistentPulse &&
                        effect.DamageKind == AbilityDamageKind.Magic &&
                        effect.BuildingValueMultiplier == 0.75f) &&
                    level.Effects[0].PulseCount == 9 &&
                    level.Effects[1].PulseCount == 6 &&
                    level.Effects[1].StartDelay == 2.67f) &&
                flameStrike.Levels.Select(level =>
                        level.Effects[0].MaximumTotalValue)
                    .SequenceEqual([90f, 160f, 220f]) &&
                runtimeImport.Catalog.TryFind("AHbz", out var blizzard) &&
                blizzard.Activation == AbilityActivationKind.ChannelPoint &&
                blizzard.Levels.Select(level => level.ChannelSeconds)
                    .SequenceEqual([6f, 8f, 10f]) &&
                blizzard.Levels.Select(level =>
                        level.Effects.Single().PulseCount)
                    .SequenceEqual([6, 8, 10]) &&
                blizzard.Levels.Select(level =>
                        level.Effects.Single().VisualCount)
                    .SequenceEqual([6, 7, 10]) &&
                blizzard.Levels.Select(level =>
                        level.Effects.Single().MaximumTotalValue)
                    .SequenceEqual([150f, 200f, 250f]) &&
                blizzard.Levels.All(level =>
                    level.Effects.Single().Relations ==
                        AbilityRelationFilter.Any &&
                    level.Effects.Single().BuildingValueMultiplier == 0.5f) &&
                massTeleport.Activation == AbilityActivationKind.TargetUnit &&
                massTeleport.Levels.Single().CastSeconds == 3f &&
                massTeleport.Levels.Single().Effects.Single()
                    .ClusteredPlacement &&
                massTeleport.Levels.Single().Effects.Single()
                    .MaximumTargets == 24 &&
                (massTeleport.Targets &
                 (AbilityTargetFlags.Unit | AbilityTargetFlags.Building |
                  AbilityTargetFlags.Friendly | AbilityTargetFlags.Ground |
                  AbilityTargetFlags.NotSelf)) ==
                (AbilityTargetFlags.Unit | AbilityTargetFlags.Building |
                 AbilityTargetFlags.Friendly | AbilityTargetFlags.Ground |
                 AbilityTargetFlags.NotSelf) &&
                runtimeImport.Catalog.TryFind("Amil", out var militia) &&
                militia.Activation == AbilityActivationKind.Toggle &&
                militia.Levels.Single().Effects.Single().Kind ==
                    AbilityEffectKind.TransformUnit &&
                militia.Levels.Single().Effects.Single().UnitForm.Normal.Id ==
                    War3HumanContent.Peasant &&
                militia.Levels.Single().Effects.Single().UnitForm.Alternate.Id ==
                    War3HumanContent.Militia &&
                militia.Levels.Single().Effects.Single().UnitForm
                    .AlternateDurationSeconds == 45f &&
                militia.Levels.Single().Effects.Single().UnitForm
                    .RequiredBuildingFunction == BuildingFunctionKind.TownHall &&
                runtimeImport.Catalog.TryBinding(
                    War3HumanContent.Militia, out var militiaBinding) &&
                militiaBinding.Abilities.Any(value =>
                    value.AbilityId == militia.Id && value.Level == 1) &&
                runtimeImport.Catalog.TryFind("Amic", out var callToArms) &&
                callToArms.Activation == AbilityActivationKind.Toggle &&
                MathF.Abs(callToArms.Levels.Single().Area -
                          533.3333f) < 0.01f &&
                callToArms.Levels.Single().Effects.Single().Selector ==
                    AbilityEffectSelector.AreaAtCaster &&
                callToArms.Levels.Single().Effects.Single().UnitForm
                    .AlternateDurationSeconds == 45f &&
                runtimeImport.Catalog.TryBuildingBinding(
                    War3HumanContent.Keep, out var keepAbilities) &&
                keepAbilities.Abilities.Contains(callToArms.Id) &&
                runtimeImport.Catalog.TryBuildingBinding(
                    War3HumanContent.Castle, out var castleAbilities) &&
                castleAbilities.Abilities.Contains(callToArms.Id) &&
                !runtimeImport.Catalog.TryBuildingBinding(
                    War3HumanContent.TownHall, out _);
            var runtimeUnitTraits =
                units.TryGet("nwad", out var watcherWard) &&
                War3AbilityDataAdapter.AdaptUnitTraits(watcherWard) ==
                AbilityUnitTraits.Ward &&
                units.TryGet("eaoe", out var ancient) &&
                (War3AbilityDataAdapter.AdaptUnitTraits(ancient) &
                 AbilityUnitTraits.Ancient) != 0 &&
                units.TryGet("ngsp", out var sapper) &&
                (War3AbilityDataAdapter.AdaptUnitTraits(sapper) &
                 AbilityUnitTraits.Sapper) != 0 &&
                units.TryGet("ugho", out var undead) &&
                (War3AbilityDataAdapter.AdaptUnitTraits(undead) &
                 AbilityUnitTraits.Undead) != 0;
            var runtimeHeroExperienceData =
                War3AbilityDataAdapter.ExperienceBountyForUnitLevel(1) == 25 &&
                War3AbilityDataAdapter.ExperienceBountyForUnitLevel(2) == 40 &&
                War3AbilityDataAdapter.ExperienceBountyForUnitLevel(5) == 115 &&
                runtimeImport.Catalog.TryBinding(
                    War3HumanContent.Footman, out var footmanBinding) &&
                footmanBinding.UnitLevel == 2 &&
                footmanBinding.ExperienceBounty == 40 &&
                runtimeImport.Catalog.TryBinding(
                    War3HumanContent.Archmage, out var xpArchmageBinding) &&
                xpArchmageBinding.HeroMaximumLevel == 10;
            var production = War3HumanContent.CreateProductionCatalog();
            var flyingMachine = production.UnitType(
                War3HumanContent.FlyingMachine).Combat;
            var siegeEngine = production.UnitType(
                War3HumanContent.SiegeEngine).Combat;
            var mortarTeam = production.UnitType(
                War3HumanContent.MortarTeam).Combat;
            var gryphonRider = production.UnitType(
                War3HumanContent.GryphonRider).Combat;
            var weaponProfilesValid =
                flyingMachine.Weapons.Length == 2 &&
                flyingMachine.Weapons[0].EnabledByDefault &&
                flyingMachine.Weapons[0].TargetLayers ==
                    CombatTargetLayer.AirUnit &&
                !flyingMachine.Weapons[1].EnabledByDefault &&
                flyingMachine.Weapons[1].RequiredTechnologyId == 4 &&
                (flyingMachine.Weapons[1].TargetLayers &
                 (CombatTargetLayer.GroundUnit |
                  CombatTargetLayer.Building)) ==
                (CombatTargetLayer.GroundUnit |
                 CombatTargetLayer.Building) &&
                siegeEngine.Weapons.Length == 2 &&
                (siegeEngine.Weapons[0].TargetLayers &
                 CombatTargetLayer.Building) != 0 &&
                (siegeEngine.Weapons[0].TargetLayers &
                 (CombatTargetLayer.GroundUnit |
                  CombatTargetLayer.AirUnit)) == 0 &&
                !siegeEngine.Weapons[1].EnabledByDefault &&
                siegeEngine.Weapons[1].RequiredTechnologyId == 6 &&
                siegeEngine.Weapons[1].TargetLayers ==
                    CombatTargetLayer.AirUnit &&
                mortarTeam.Weapons.Length == 2 &&
                (mortarTeam.Weapons[0].TargetLayers &
                 CombatTargetLayer.GroundUnit) != 0 &&
                (mortarTeam.Weapons[1].TargetLayers &
                 CombatTargetLayer.Building) != 0 &&
                gryphonRider.Weapons.Length == 2 &&
                gryphonRider.Weapons.Any(value =>
                    value.TargetLayers == CombatTargetLayer.AirUnit);
            var weaponStore = new CombatStore(1);
            weaponStore.Register(
                0, 1, default, flyingMachine,
                UnitPerceptionProfileSnapshot.ElevatedObserver());
            var weaponSelectionValid =
                weaponStore.CanTarget(
                    0, CombatTargetLayer.AirUnit, _ => false) &&
                !weaponStore.CanTarget(
                    0, CombatTargetLayer.GroundUnit, _ => false) &&
                weaponStore.TrySelectWeapon(
                    0, CombatTargetLayer.GroundUnit,
                    technologyId => technologyId == 4) &&
                weaponStore.ActiveWeaponSlots[0] == 1 &&
                MathF.Abs(weaponStore.AttackDamage[0] - 7.5f) < 0.01f;
            var technologyCatalog = War3HumanContent.CreateTechnologyCatalog();
            var weaponTechnologyRequirementsValid = new[] { 4, 6, 7 }
                .All(technologyId => technologyCatalog.Technology(technologyId)
                    .Requirements.Contains(new TechnologyRequirementProfile(
                        TechnologyRequirementKind.CompletedBuilding,
                        War3HumanContent.Castle, 1)));
            var weaponBehaviorStatusValid =
                War3AbilityBehaviorRegistry.Resolve("Agyb", "Agyb").Status ==
                    War3AbilityRuntimeSupportStatus.Delegated &&
                War3AbilityBehaviorRegistry.Resolve("Acha", "Srtt").Status ==
                    War3AbilityRuntimeSupportStatus.Delegated &&
                War3AbilityBehaviorRegistry.Resolve("Aflk", "Aflk").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                War3AbilityBehaviorRegistry.Resolve("Aroc", "Aroc").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay;
            var flyingRecipe = production.Recipes.ToArray().Single(value =>
                value.UnitType.Id == War3HumanContent.FlyingMachine);
            var productionLog = new ProductionCommandLogSnapshot(
            [
                new RecordedProductionCommand(
                    0, ProductionReplayCommandKind.Train, 1,
                    new GameplayBuildingId(War3HumanContent.Workshop),
                    default, flyingRecipe, default, default, default)
            ]);
            var weaponCommandRoundTrip =
                ProductionCommandLogSnapshot.TryDeserialize(
                    productionLog.CanonicalBytes,
                    out var restoredProductionLog, out var productionValidation) &&
                productionValidation == ProductionCommandLogValidationCode.Success &&
                restoredProductionLog is not null &&
                restoredProductionLog.Entries.Single().Recipe.UnitType.Combat
                    .Weapons.AsSpan().SequenceEqual(
                        flyingMachine.Weapons.AsSpan());
            var productionResource =
                ProductionCatalogResourceConverter.FromSnapshot(production);
            var weaponResourceRoundTrip =
                ProductionCatalogResourceConverter.TryConvert(
                    productionResource,
                    out var restoredProduction,
                    out var resourceValidation) &&
                resourceValidation.IsValid && restoredProduction is not null &&
                restoredProduction.UnitType(War3HumanContent.FlyingMachine)
                    .Combat.Weapons.AsSpan().SequenceEqual(
                        flyingMachine.Weapons.AsSpan());
            var behaviorRegistryValid =
                War3AbilityBehaviorRegistry.All.Count == 53 &&
                runtimeImport.RequestedCount == 47 &&
                runtimeImport.BehaviorFamilyCount == 45 &&
                runtimeImport.PrototypeCount == 39 &&
                runtimeImport.UnclassifiedBaseCodes.Length == 0 &&
                runtimeImport.UnresolvedRequirementIds.Length == 0 &&
                runtimeCoverage.RegistryBaseFamilyCount == 53 &&
                runtimeCoverage.All.AbilityCount == 801 &&
                runtimeCoverage.All.BaseFamilyCount == 415 &&
                runtimeCoverage.All.ClassifiedBaseFamilyCount == 53 &&
                runtimeCoverage.UnitReferenced.AbilityCount == 461 &&
                runtimeCoverage.UnitReferenced.BaseFamilyCount == 285 &&
                runtimeCoverage.Items.AbilityCount == 234 &&
                runtimeCoverage.Items.BaseFamilyCount == 129 &&
                runtimeCoverage.CurrentRuntime.AbilityCount == 47 &&
                runtimeCoverage.CurrentRuntime.BaseFamilyCount == 45 &&
                runtimeCoverage.CurrentRuntime.PrototypeAbilityCount == 39 &&
                runtimeCoverage.CurrentRuntime.StatusCounts["unclassified"] == 0 &&
                runtimeCoverage.OrphanRegisteredBaseCodes.Length == 0 &&
                runtimeCoverage.Targeting.ExportedTokens.Length == 27 &&
                runtimeCoverage.Targeting.UnrecognizedTokens.Length == 0 &&
                runtimeCoverage.Targeting.RuntimeUnsupportedTokens.Length == 5 &&
                runtimeCoverage.Targeting.AbilitiesWithUnsupportedTokens == 57 &&
                runtimeCoverage.TechnologyRequirements
                    .AbilitiesWithRequirements == 93 &&
                runtimeCoverage.TechnologyRequirements
                    .CurrentRuntimeAbilitiesWithRequirements == 17 &&
                runtimeCoverage.TechnologyRequirements
                    .CurrentRuntimeRequirementIds.Length == 13 &&
                runtimeCoverage.TechnologyRequirements
                    .CurrentRuntimeResolvedRequirementIds.Length == 13 &&
                runtimeCoverage.TechnologyRequirements
                    .CurrentRuntimeUnresolvedRequirementIds.Length == 0 &&
                runtimeCompilerMatrix.Length == 39 &&
                runtimeCompilerMatrix.Select(value => value.Compiler)
                    .Distinct().Count() == 36 &&
                runtimeCompilerMatrix.All(value => value.HasEffects) &&
                runtimeRequirementsPreserved && heroLearningBindings &&
                runtimeEffectSemantics && runtimeConfiguredScalars &&
                runtimeUnitTraits &&
                runtimeHeroExperienceData && weaponProfilesValid &&
                weaponSelectionValid && weaponTechnologyRequirementsValid &&
                weaponBehaviorStatusValid && weaponCommandRoundTrip &&
                weaponResourceRoundTrip && runtimeManaFromJson &&
                runtimeSummonsFromJson && runtimePresentationAttachments;
            var references = normalReferences
                .Concat(heroReferences)
                .ToHashSet(StringComparer.Ordinal);
            var countsValid = units.Count == 837 && abilities.Count == 801 &&
                              buffEffects.Count == 247 &&
                              metadata.FieldCount == 755 &&
                              metadata.BindingCount == 801 &&
                              normalReferences.Count == 333 &&
                              heroReferences.Count == 128 &&
                              references.Count == 461;
            var passed = countsValid && errors.Count == 0 &&
                         relatedPresentation && militiaAlternatePresentation &&
                         metadataLocalized &&
                         summonIdsPreserved && behaviorRegistryValid &&
                         units.LoadErrors.Count == 0 &&
                         abilities.LoadErrors.Count == 0 &&
                         buffEffects.LoadErrors.Count == 0 &&
                         metadata.LoadErrors.Count == 0;
            return new SelfTestResult(
                passed,
                $"abilities={abilities.Count}, refs={references.Count}" +
                $"({normalReferences.Count}+{heroReferences.Count}), " +
                $"metadata={metadata.FieldCount}/{metadata.BindingCount}, " +
                $"buff_effect={buffEffects.Count}, errors={errors.Count}, " +
                $"presentation={relatedPresentation}, localized={metadataLocalized}, " +
                $"unit_ids={summonIdsPreserved}, " +
                $"runtime={runtimeImport.RequestedCount}/" +
                $"{runtimeImport.BehaviorFamilyCount}/" +
                $"{runtimeImport.PrototypeCount}/" +
                $"{runtimeImport.UnclassifiedBaseCodes.Length}, " +
                $"coverage={runtimeCoverage.All.ClassifiedAbilityCount}/" +
                $"{runtimeCoverage.All.ClassifiedBaseFamilyCount}, " +
                $"requirements={runtimeRequirementsPreserved}, " +
                $"hero_learning={heroLearningBindings}, " +
                $"effect_semantics={runtimeEffectSemantics}, " +
                $"configured_scalars={runtimeConfiguredScalars}, " +
                $"mana_json={runtimeManaFromJson}, " +
                $"summon_json={runtimeSummonsFromJson}, " +
                $"attachments_json={runtimePresentationAttachments}, " +
                $"unit_traits={runtimeUnitTraits}, " +
                $"hero_xp_data={runtimeHeroExperienceData}, " +
                $"weapons={weaponProfilesValid}/{weaponSelectionValid}/" +
                $"{weaponTechnologyRequirementsValid}/" +
                $"{weaponBehaviorStatusValid}/{weaponCommandRoundTrip}/" +
                $"{weaponResourceRoundTrip}" +
                $"[fly={string.Join('|', flyingMachine.Weapons.Select(value => $"{value.Slot}:{value.EnabledByDefault}:{value.RequiredTechnologyId}:{value.TargetLayers}"))};" +
                $"siege={string.Join('|', siegeEngine.Weapons.Select(value => $"{value.Slot}:{value.EnabledByDefault}:{value.RequiredTechnologyId}:{value.TargetLayers}"))};" +
                $"mortar={string.Join('|', mortarTeam.Weapons.Select(value => $"{value.Slot}:{value.TargetLayers}"))};" +
                $"gryphon={string.Join('|', gryphonRider.Weapons.Select(value => $"{value.Slot}:{value.TargetLayers}"))}], " +
                $"unit_unclassified=" +
                $"{runtimeCoverage.UnclassifiedReferencedBaseCodes.Length}" +
                (errors.Count == 0
                    ? string.Empty
                    : $", first={string.Join(',', errors.Take(8))}"));
        }
        catch (Exception exception)
        {
            return new SelfTestResult(
                false,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool TryData(
        War3ObjectLevel level,
        string key,
        out float value)
    {
        value = 0f;
        return level.Data.TryGetValue(key, out var text) &&
               float.TryParse(
            text, NumberStyles.Float, CultureInfo.InvariantCulture,
            out value) &&
               float.IsFinite(value);
    }

    private static bool Nearly(float left, float right) =>
        MathF.Abs(left - right) < 0.001f;

    private static bool SummonMatchesUnitJson(
        War3UnitDataCatalog units,
        in AbilitySummonProfile summon,
        string? expectedObjectId,
        float? expectedLifetime)
    {
        if (string.IsNullOrWhiteSpace(expectedObjectId) ||
            !summon.ObjectId.Equals(expectedObjectId, StringComparison.Ordinal) ||
            !units.TryGet(summon.ObjectId, out var source) ||
            source.Summary.Combat.Attacks.FirstOrDefault(value => value.Enabled)
                is not { } attack)
            return false;
        var policy = War3GameplayImportPolicy.Default;
        var expectedRadius = MathF.Max(
            policy.MinimumUnitRadius,
            (source.Summary.Movement.CollisionSize ?? 0f) *
            policy.UnitCollisionRadiusScale);
        return MathF.Abs(summon.LifetimeSeconds -
                         (expectedLifetime ?? 0f)) < 0.001f &&
               MathF.Abs(summon.Movement.PhysicalRadius -
                         expectedRadius) < 0.001f &&
               MathF.Abs(summon.Movement.MaximumSpeed -
                         (source.Summary.Movement.Speed ?? 0f) *
                         policy.MovementSpeedScale) < 0.001f &&
               MathF.Abs(summon.Combat.MaximumHealth -
                         (source.Summary.HitPoints.Effective ?? 0f)) < 0.001f &&
               MathF.Abs(summon.Combat.AttackDamage -
                         (attack.Damage.Average ?? 0f)) < 0.001f &&
               MathF.Abs(summon.Perception.VisionRange -
                         (source.Summary.Sight.Day ?? 0f) *
                         policy.WorldDistanceScale) < 0.001f;
    }
}
