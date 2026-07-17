using War3Rts;
using War3Rts.Data;
using RtsDemo.Demos.War3;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Simulation;

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
            var runtimeEffectSemantics =
                runtimeImport.Catalog.TryFind("AHtb", out var stormBolt) &&
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
                siegeEngine.Weapons[0].TargetLayers ==
                    CombatTargetLayer.Building &&
                !siegeEngine.Weapons[1].EnabledByDefault &&
                siegeEngine.Weapons[1].RequiredTechnologyId == 6 &&
                siegeEngine.Weapons[1].TargetLayers ==
                    CombatTargetLayer.AirUnit &&
                mortarTeam.Weapons.Length == 2 &&
                mortarTeam.Weapons[0].TargetLayers ==
                    CombatTargetLayer.GroundUnit &&
                mortarTeam.Weapons[1].TargetLayers ==
                    CombatTargetLayer.Building &&
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
                War3AbilityBehaviorRegistry.All.Count == 43 &&
                runtimeImport.RequestedCount == 44 &&
                runtimeImport.BehaviorFamilyCount == 43 &&
                runtimeImport.PrototypeCount == 36 &&
                runtimeImport.UnclassifiedBaseCodes.Length == 0 &&
                runtimeImport.UnresolvedRequirementIds.Length == 0 &&
                runtimeCoverage.RegistryBaseFamilyCount == 43 &&
                runtimeCoverage.All.AbilityCount == 801 &&
                runtimeCoverage.All.BaseFamilyCount == 415 &&
                runtimeCoverage.All.ClassifiedBaseFamilyCount == 43 &&
                runtimeCoverage.UnitReferenced.AbilityCount == 461 &&
                runtimeCoverage.UnitReferenced.BaseFamilyCount == 285 &&
                runtimeCoverage.Items.AbilityCount == 234 &&
                runtimeCoverage.Items.BaseFamilyCount == 129 &&
                runtimeCoverage.CurrentRuntime.AbilityCount == 44 &&
                runtimeCoverage.CurrentRuntime.BaseFamilyCount == 43 &&
                runtimeCoverage.CurrentRuntime.PrototypeAbilityCount == 36 &&
                runtimeCoverage.CurrentRuntime.StatusCounts["unclassified"] == 0 &&
                runtimeCoverage.OrphanRegisteredBaseCodes.Length == 0 &&
                runtimeCoverage.Targeting.ExportedTokens.Length == 27 &&
                runtimeCoverage.Targeting.UnrecognizedTokens.Length == 0 &&
                runtimeCoverage.Targeting.RuntimeUnsupportedTokens.Length == 5 &&
                runtimeCoverage.Targeting.AbilitiesWithUnsupportedTokens == 57 &&
                runtimeCoverage.TechnologyRequirements
                    .AbilitiesWithRequirements == 93 &&
                runtimeCoverage.TechnologyRequirements
                    .CurrentRuntimeAbilitiesWithRequirements == 15 &&
                runtimeCoverage.TechnologyRequirements
                    .CurrentRuntimeRequirementIds.Length == 12 &&
                runtimeCoverage.TechnologyRequirements
                    .CurrentRuntimeResolvedRequirementIds.Length == 12 &&
                runtimeCoverage.TechnologyRequirements
                    .CurrentRuntimeUnresolvedRequirementIds.Length == 0 &&
                runtimeCompilerMatrix.Length == 36 &&
                runtimeCompilerMatrix.Select(value => value.Compiler)
                    .Distinct().Count() == 36 &&
                runtimeCompilerMatrix.All(value => value.HasEffects) &&
                runtimeRequirementsPreserved && heroLearningBindings &&
                runtimeEffectSemantics && runtimeUnitTraits &&
                runtimeHeroExperienceData && weaponProfilesValid &&
                weaponSelectionValid && weaponTechnologyRequirementsValid &&
                weaponBehaviorStatusValid && weaponCommandRoundTrip &&
                weaponResourceRoundTrip;
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
                $"unit_traits={runtimeUnitTraits}, " +
                $"hero_xp_data={runtimeHeroExperienceData}, " +
                $"weapons={weaponProfilesValid}/{weaponSelectionValid}/" +
                $"{weaponTechnologyRequirementsValid}/" +
                $"{weaponBehaviorStatusValid}/{weaponCommandRoundTrip}/" +
                $"{weaponResourceRoundTrip}, " +
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
}
