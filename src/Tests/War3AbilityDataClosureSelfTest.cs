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
            var inventoryProfilesValid =
                War3HumanContent.TryInventoryAbility(
                    "AInv", out var heroInventory) &&
                heroInventory.Capacity == 6 &&
                !heroInventory.DropItemsOnDeath &&
                heroInventory.CanUseItems && heroInventory.CanGetItems &&
                heroInventory.CanDropItems &&
                War3HumanContent.TryInventoryAbility(
                    "Aihn", out var unitInventory) &&
                unitInventory.Capacity == 2 &&
                unitInventory.DropItemsOnDeath &&
                !unitInventory.CanUseItems && unitInventory.CanGetItems &&
                unitInventory.CanDropItems;
            var inventoryLifecycle = VerifyInventoryLifecycle();
            var runtimeCoverage = War3AbilityRuntimeCoverageAudit.Analyze(
                abilities,
                units,
                runtimeImport.Definitions.Select(value => value.ObjectId),
                War3HumanContent.Technologies.Select(value => value.ObjectId));
            var extendedFamilyCompilers = VerifyExtendedFamilyCompilers(
                abilities, buffEffects, units, out var extendedFamilySummary);
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
            var runtimeAutoCastBindingsFromJson =
                runtimeImport.Catalog.TryFind("Ahea", out var healAuto) &&
                runtimeImport.Catalog.TryFind("Ainf", out var innerFireAuto) &&
                runtimeImport.Catalog.TryFind("Aslo", out var slowAuto) &&
                runtimeImport.Catalog.TryFind("Ahrp", out var repairAuto) &&
                !healAuto.AutoCastDefault && !innerFireAuto.AutoCastDefault &&
                !slowAuto.AutoCastDefault && !repairAuto.AutoCastDefault &&
                runtimeImport.Catalog.TryBinding(
                    War3HumanContent.Priest, out var priestAutoBinding) &&
                priestAutoBinding.Abilities.Single(value =>
                    value.AbilityId == healAuto.Id).AutoCastEnabled &&
                !priestAutoBinding.Abilities.Single(value =>
                    value.AbilityId == innerFireAuto.Id).AutoCastEnabled &&
                runtimeImport.Catalog.TryBinding(
                    War3HumanContent.Sorceress, out var sorceressAutoBinding) &&
                sorceressAutoBinding.Abilities.Single(value =>
                    value.AbilityId == slowAuto.Id).AutoCastEnabled &&
                runtimeImport.Catalog.TryBinding(
                    War3HumanContent.Peasant, out var peasantAutoBinding) &&
                !peasantAutoBinding.Abilities.Single(value =>
                    value.AbilityId == repairAuto.Id).AutoCastEnabled;
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
            var runtimeRepairTargetsFromJson =
                runtimeImport.Catalog.Bindings.All(binding =>
                {
                    var definition = War3HumanContent.Units[
                        binding.UnitTypeId];
                    if (!units.TryGet(definition.ObjectId, out var source))
                        return false;
                    var cost = source.Summary.Cost;
                    if (cost.RepairGold is not >= 0 ||
                        cost.RepairLumber is not >= 0 ||
                        cost.RepairTime is not > 0f)
                        return !binding.Repair.Enabled;
                    return binding.Repair.Enabled &&
                           binding.Repair.BaseCost == new EconomyCost(
                               cost.RepairGold.Value,
                               cost.RepairLumber.Value) &&
                           Nearly(binding.Repair.BaseRepairSeconds,
                               cost.RepairTime.Value);
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
                    ]) &&
                War3HumanContent.TryAbility(
                    "Ahrp", out var repairPresentation) &&
                repairPresentation is { SupportsAutoCast: true };
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
                            1f / MathF.Max(0.1f, 1f - attackReduction))) &&
                abilities.TryGet("Adef", out var sourceDefend) &&
                runtimeImport.Catalog.TryFind("Adef", out var defendScalar) &&
                defendScalar.Levels.Zip(sourceDefend.Summary.Levels)
                    .All(pair =>
                        TryData(pair.Second, "A", out var received) &&
                        TryData(pair.Second, "B", out var dealt) &&
                        TryData(pair.Second, "C", out var movement) &&
                        TryData(pair.Second, "E", out var magicReceived) &&
                        TryData(pair.Second, "F", out var deflect) &&
                        TryData(pair.Second, "G", out var piercing) &&
                        TryData(pair.Second, "H", out var magic) &&
                        TryData(pair.Second, "I", out var miss) &&
                        pair.First.Effects.Length == 1 &&
                        Nearly(pair.First.Effects[0].Modifier
                            .MovementSpeedMultiplier, movement) &&
                        Nearly(pair.First.Effects[0].CombatModifier
                            .DamageTakenMultiplier, received) &&
                        Nearly(pair.First.Effects[0].CombatModifier
                            .DamageDealtMultiplier, dealt) &&
                        Nearly(pair.First.Effects[0].CombatModifier
                            .MagicDamageTakenMultiplier, magicReceived) &&
                        Nearly(pair.First.Effects[0].CombatModifier
                            .DeflectChancePercent, deflect) &&
                        Nearly(pair.First.Effects[0].CombatModifier
                            .DeflectedPiercingDamageMultiplier, piercing) &&
                        Nearly(pair.First.Effects[0].CombatModifier
                            .DeflectedMagicDamageMultiplier, magic) &&
                        Nearly(pair.First.Effects[0].CombatModifier
                            .AttackMissChancePercent, miss)) &&
                abilities.TryGet("Aclf", out var sourceCloud) &&
                runtimeImport.Catalog.TryFind("Aclf", out var cloudScalar) &&
                cloudScalar.Levels.Zip(sourceCloud.Summary.Levels)
                    .All(pair =>
                        TryData(pair.Second, "A", out var preventionType) &&
                        TryData(pair.Second, "B", out var missChance) &&
                        TryData(pair.Second, "C", out var movement) &&
                        TryData(pair.Second, "D", out var attackRate) &&
                        pair.First.Effects.Length == 1 &&
                        pair.First.Effects[0].AffectsBuildings &&
                        pair.First.Effects[0].Status ==
                            AbilityStatusFlags.AttackDisabled &&
                        Nearly(pair.First.Effects[0].SecondaryValue,
                            preventionType) &&
                        Nearly(pair.First.Effects[0].CombatModifier
                            .AttackMissChancePercent, missChance) &&
                        Nearly(pair.First.Effects[0].Modifier
                            .MovementSpeedMultiplier,
                            movement > 0f ? movement : 1f) &&
                        Nearly(pair.First.Effects[0].Modifier
                            .AttackCooldownMultiplier,
                            attackRate > 0f ? 1f / attackRate : 1f));
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
                abilities.TryGet("AHdr", out var sourceDrainMana) &&
                drainMana.Levels.Zip(sourceDrainMana.Summary.Levels)
                    .All(pair =>
                        TryData(pair.Second, "H", out var bonusFactor) &&
                        TryData(pair.Second, "I", out var bonusDecay) &&
                        pair.First.Effects.Length == 1 &&
                        pair.First.Effects[0].Kind ==
                            AbilityEffectKind.TransferMana &&
                        pair.First.Effects[0].Value is 30f or 60f or 90f &&
                        pair.First.Effects[0].Interval == 1f &&
                        Nearly(pair.First.Effects[0].SecondaryValue,
                            bonusFactor) &&
                        Nearly(pair.First.Effects[0].HeroValue,
                            bonusDecay)) &&
                runtimeImport.Catalog.TryFind("Acmg", out var controlMagic) &&
                abilities.TryGet("Acmg", out var sourceControlMagic) &&
                controlMagic.Levels.Zip(sourceControlMagic.Summary.Levels)
                    .All(pair =>
                        TryData(pair.Second, "A", out var maximumLevel) &&
                        TryData(pair.Second, "B", out var manaPerLife) &&
                        TryData(pair.Second, "C", out var chargeCurrentLife) &&
                        pair.First.Effects.Length == 1 &&
                        pair.First.Effects[0].Kind ==
                            AbilityEffectKind.TransferControl &&
                        pair.First.Effects[0].MaximumTargetUnitLevel ==
                            (int)maximumLevel &&
                        Nearly(pair.First.Effects[0].SecondaryValue,
                            manaPerLife) &&
                        Nearly(pair.First.Effects[0].HeroValue,
                            chargeCurrentLife)) &&
                runtimeImport.Catalog.TryFind("Aply", out var polymorph) &&
                abilities.TryGet("Aply", out var sourcePolymorph) &&
                units.TryGet("nshe", out var sheep) &&
                sheep.Summary.Movement.Speed is > 0f and var sheepSpeed &&
                polymorph.Levels.Zip(sourcePolymorph.Summary.Levels)
                    .All(pair =>
                        TryData(pair.Second, "A", out var maximumLevel) &&
                        pair.Second.Data.TryGetValue("B", out var ground) &&
                        pair.Second.Data.TryGetValue("C", out var air) &&
                        pair.Second.Data.TryGetValue("D", out var amphibious) &&
                        pair.Second.Data.TryGetValue("E", out var water) &&
                        pair.First.Effects.Length == 1 &&
                        pair.First.Effects[0].MaximumTargetUnitLevel ==
                            (int)maximumLevel &&
                        pair.First.Effects[0].Replacement ==
                            new AbilityReplacementProfile(
                                ground, air, amphibious, water) &&
                        Nearly(pair.First.Effects[0].SecondaryValue,
                            sheepSpeed * 4f / 15f) &&
                        (pair.First.Effects[0].Status &
                         (AbilityStatusFlags.Polymorphed |
                          AbilityStatusFlags.AttackDisabled)) ==
                         (AbilityStatusFlags.Polymorphed |
                          AbilityStatusFlags.AttackDisabled)) &&
                runtimeImport.Catalog.TryFind("Ahrp", out var repair) &&
                abilities.TryGet("Ahrp", out var sourceRepair) &&
                repair.Activation == AbilityActivationKind.TargetUnit &&
                (repair.Targets &
                 (AbilityTargetFlags.Building |
                  AbilityTargetFlags.Friendly |
                  AbilityTargetFlags.Mechanical)) ==
                (AbilityTargetFlags.Building |
                 AbilityTargetFlags.Friendly |
                 AbilityTargetFlags.Mechanical) &&
                (repair.Targets & AbilityTargetFlags.Dead) == 0 &&
                repair.Levels.Zip(sourceRepair.Summary.Levels)
                    .All(pair =>
                        TryData(pair.Second, "A", out var costRatio) &&
                        TryData(pair.Second, "B", out var timeRatio) &&
                        TryData(pair.Second, "C", out var powerCost) &&
                        TryData(pair.Second, "D", out var powerRate) &&
                        TryData(pair.Second, "E", out var navalRange) &&
                        pair.Second.Range is > 0f and var sourceRange &&
                        pair.First.Effects.Length == 1 &&
                        pair.First.Effects[0].Kind ==
                            AbilityEffectKind.Repair &&
                        pair.First.Effects[0].Timing ==
                            AbilityEffectTiming.PersistentPulse &&
                        pair.First.Effects[0].AffectsBuildings &&
                        Nearly(pair.First.Effects[0].Value, costRatio) &&
                        Nearly(pair.First.Effects[0].SecondaryValue,
                            timeRatio) &&
                        Nearly(pair.First.Effects[0].HeroValue,
                            powerCost) &&
                        Nearly(pair.First.Effects[0].HeroSecondaryValue,
                            powerRate) &&
                        Nearly(pair.First.Effects[0].Radius,
                            navalRange * pair.First.Range / sourceRange)) &&
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
                War3AbilityBehaviorRegistry.All.Count == 62 &&
                War3AbilityBehaviorRegistry.Resolve("Ahea").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                !War3AbilityBehaviorRegistry.Resolve("Ahea").AutoCastDefault &&
                War3AbilityBehaviorRegistry.Resolve("Ainf").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                !War3AbilityBehaviorRegistry.Resolve("Ainf").AutoCastDefault &&
                War3AbilityBehaviorRegistry.Resolve("Afbk", "Afbt").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                War3AbilityBehaviorRegistry.Resolve("Asth").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                War3AbilityBehaviorRegistry.Resolve("Asth").Compiler ==
                    War3AbilityCompilerKind.None &&
                War3AbilityBehaviorRegistry.Resolve("Adef").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                War3AbilityBehaviorRegistry.Resolve("Aclf").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                War3AbilityBehaviorRegistry.Resolve("Acmg").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                War3AbilityBehaviorRegistry.Resolve("Aply").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                War3AbilityBehaviorRegistry.Resolve("AInv").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                War3AbilityBehaviorRegistry.Resolve("Arep", "Ahrp").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                War3AbilityBehaviorRegistry.Resolve("Arep").Compiler ==
                    War3AbilityCompilerKind.Repair &&
                War3AbilityBehaviorRegistry.Resolve("Aens").Compiler ==
                    War3AbilityCompilerKind.Ensnare &&
                War3AbilityBehaviorRegistry.Resolve("Acri").Compiler ==
                    War3AbilityCompilerKind.Cripple &&
                War3AbilityBehaviorRegistry.Resolve("Ablo").Compiler ==
                    War3AbilityCompilerKind.Bloodlust &&
                War3AbilityBehaviorRegistry.Resolve("Arej").Compiler ==
                    War3AbilityCompilerKind.Rejuvenation &&
                War3AbilityBehaviorRegistry.Resolve("Afae").Compiler ==
                    War3AbilityCompilerKind.FaerieFire &&
                War3AbilityBehaviorRegistry.Resolve("Avul").Compiler ==
                    War3AbilityCompilerKind.Invulnerable &&
                War3AbilityBehaviorRegistry.Resolve("Arsk").Status ==
                    War3AbilityRuntimeSupportStatus.ImplementedGameplay &&
                War3AbilityBehaviorRegistry.Resolve("Arsk").Compiler ==
                    War3AbilityCompilerKind.ResistantSkin &&
                War3AbilityBehaviorRegistry.Resolve("AEev").Compiler ==
                    War3AbilityCompilerKind.Evasion &&
                War3AbilityBehaviorRegistry.Resolve("AOcr").Compiler ==
                    War3AbilityCompilerKind.CriticalStrike &&
                abilities.TryGet("Asth", out var stormHammersAbility) &&
                stormHammersAbility.Summary.Levels.All(level =>
                    level.Requirements.Contains("Rhhb",
                        StringComparer.Ordinal)) &&
                runtimeImport.RequestedCount == 47 &&
                runtimeImport.BehaviorFamilyCount == 45 &&
                runtimeImport.PrototypeCount == 40 &&
                runtimeImport.UnclassifiedBaseCodes.Length == 0 &&
                runtimeImport.UnresolvedRequirementIds.Length == 0 &&
                runtimeCoverage.RegistryBaseFamilyCount == 62 &&
                runtimeCoverage.All.AbilityCount == 801 &&
                runtimeCoverage.All.BaseFamilyCount == 415 &&
                runtimeCoverage.All.ClassifiedAbilityCount == 163 &&
                runtimeCoverage.All.ClassifiedBaseFamilyCount == 62 &&
                runtimeCoverage.UnitReferenced.AbilityCount == 461 &&
                runtimeCoverage.UnitReferenced.BaseFamilyCount == 285 &&
                runtimeCoverage.Items.AbilityCount == 234 &&
                runtimeCoverage.Items.BaseFamilyCount == 129 &&
                runtimeCoverage.CurrentRuntime.AbilityCount == 47 &&
                runtimeCoverage.CurrentRuntime.BaseFamilyCount == 45 &&
                runtimeCoverage.CurrentRuntime.PrototypeAbilityCount == 40 &&
                runtimeCoverage.CurrentRuntime.StatusCounts[
                    "implemented_gameplay"] == 43 &&
                runtimeCoverage.CurrentRuntime.StatusCounts["delegated"] == 3 &&
                runtimeCoverage.CurrentRuntime.StatusCounts[
                    "presentation_only"] == 1 &&
                runtimeCoverage.CurrentRuntime.StatusCounts["blocked"] == 0 &&
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
                runtimeCompilerMatrix.Length == 40 &&
                runtimeCompilerMatrix.Select(value => value.Compiler)
                    .Distinct().Count() == 37 &&
                runtimeCompilerMatrix.All(value => value.HasEffects) &&
                runtimeRequirementsPreserved && heroLearningBindings &&
                runtimeAutoCastBindingsFromJson &&
                runtimeEffectSemantics && runtimeConfiguredScalars &&
                extendedFamilyCompilers &&
                runtimeUnitTraits &&
                runtimeHeroExperienceData && weaponProfilesValid &&
                weaponSelectionValid && weaponTechnologyRequirementsValid &&
                weaponBehaviorStatusValid && weaponCommandRoundTrip &&
                weaponResourceRoundTrip && runtimeManaFromJson &&
                runtimeRepairTargetsFromJson &&
                runtimeSummonsFromJson && runtimePresentationAttachments;
            behaviorRegistryValid &=
                inventoryProfilesValid && inventoryLifecycle;
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
                $"autocast_json={runtimeAutoCastBindingsFromJson}, " +
                $"effect_semantics={runtimeEffectSemantics}, " +
                $"configured_scalars={runtimeConfiguredScalars}, " +
                $"extended_families={extendedFamilyCompilers}/" +
                $"{extendedFamilySummary}, " +
                $"mana_json={runtimeManaFromJson}, " +
                $"repair_targets_json={runtimeRepairTargetsFromJson}, " +
                $"summon_json={runtimeSummonsFromJson}, " +
                $"attachments_json={runtimePresentationAttachments}, " +
                $"inventory={inventoryProfilesValid}/{inventoryLifecycle}, " +
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

    private static bool VerifyExtendedFamilyCompilers(
        War3ObjectDataCatalog abilities,
        War3ObjectDataCatalog buffEffects,
        IWar3UnitDataCatalog units,
        out string summary)
    {
        var expected = new Dictionary<string, War3AbilityCompilerKind>(
            StringComparer.Ordinal)
        {
            ["Aens"] = War3AbilityCompilerKind.Ensnare,
            ["Acri"] = War3AbilityCompilerKind.Cripple,
            ["Ablo"] = War3AbilityCompilerKind.Bloodlust,
            ["Arej"] = War3AbilityCompilerKind.Rejuvenation,
            ["Afae"] = War3AbilityCompilerKind.FaerieFire,
            ["Avul"] = War3AbilityCompilerKind.Invulnerable,
            ["Arsk"] = War3AbilityCompilerKind.ResistantSkin,
            ["AEev"] = War3AbilityCompilerKind.Evasion,
            ["AOcr"] = War3AbilityCompilerKind.CriticalStrike
        };
        var policy = War3GameplayImportPolicy.Default;
        var requirementIds = abilities.Entries
            .Select(entry => abilities.TryGet(entry.Id, out var source)
                ? source
                : null)
            .Where(source => source is not null)
            .Where(source =>
            {
                var baseCode = string.IsNullOrWhiteSpace(
                    source!.Identity.BaseCode)
                    ? source.Id
                    : source.Identity.BaseCode;
                return expected.ContainsKey(baseCode);
            })
            .SelectMany(source => source!.Summary.Levels)
            .SelectMany(level => level.Requirements)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select((rawId, index) => (rawId, index))
            .ToDictionary(value => value.rawId, value => value.index,
                StringComparer.Ordinal);
        var adapter = new War3AbilityDataAdapter(
            abilities,
            buffEffects,
            units,
            policy,
            requirementIds,
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, UnitTypeProfile>(StringComparer.Ordinal));
        var familyCounts = expected.Keys.ToDictionary(
            value => value, _ => 0, StringComparer.Ordinal);
        var compiled = 0;
        foreach (var entry in abilities.Entries)
        {
            if (!abilities.TryGet(entry.Id, out var source))
                continue;
            var baseCode = string.IsNullOrWhiteSpace(source.Identity.BaseCode)
                ? source.Id
                : source.Identity.BaseCode;
            if (!expected.TryGetValue(baseCode, out var compiler))
                continue;
            familyCounts[baseCode]++;
            if (!adapter.TryCompileAbility(source.Id, compiled, out var profile) ||
                War3AbilityBehaviorRegistry.Resolve(baseCode, source.Id)
                    .Compiler != compiler ||
                profile.Levels.Length != source.Summary.Levels.Length ||
                !profile.Levels.Zip(source.Summary.Levels).All(pair =>
                    ExtendedFamilyLevelMatches(
                        compiler, pair.First, pair.Second,
                        policy.WorldDistanceScale)))
            {
                summary = $"failed={source.Id}/{baseCode}/{compiler}";
                return false;
            }
            compiled++;
        }
        var expectedCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Aens"] = 3,
            ["Acri"] = 3,
            ["Ablo"] = 3,
            ["Arej"] = 3,
            ["Afae"] = 3,
            ["Avul"] = 1,
            ["Arsk"] = 3,
            ["AEev"] = 4,
            ["AOcr"] = 3
        };
        var countsValid = familyCounts.All(value =>
            value.Value == expectedCounts[value.Key]);
        var resistantUnits = 0;
        foreach (var entry in units.Entries)
        {
            if (!units.TryGet(entry.Id, out var unit)) continue;
            if (unit.Summary.Abilities.Concat(unit.Summary.HeroAbilities)
                .Any(rawId =>
                {
                    if (!abilities.TryGet(rawId, out var source)) return false;
                    var baseCode = string.IsNullOrWhiteSpace(
                        source.Identity.BaseCode)
                        ? source.Id
                        : source.Identity.BaseCode;
                    return baseCode.Equals("Arsk", StringComparison.Ordinal);
                }))
                resistantUnits++;
        }
        var variantAutoCastValid =
            adapter.DefaultAutoCastEnabled("ACbb", "Ablo") &&
            adapter.DefaultAutoCastEnabled("Afa2", "Afae") &&
            !adapter.DefaultAutoCastEnabled("Aens", "Ablo");
        summary = $"compiled={compiled}, families=" +
                  string.Join('|', familyCounts.OrderBy(value => value.Key)
                      .Select(value => $"{value.Key}:{value.Value}")) +
                  $", resistant=3/{resistantUnits}, " +
                  $"requirements={requirementIds.Count}, " +
                  $"variant_auto={variantAutoCastValid}";
        return compiled == 26 && countsValid &&
               resistantUnits == 20 &&
               variantAutoCastValid;
    }

    private static bool ExtendedFamilyLevelMatches(
        War3AbilityCompilerKind compiler,
        AbilityLevelProfile runtime,
        War3ObjectLevel source,
        float distanceScale)
    {
        if (runtime.Effects.Length != 1 ||
            runtime.Requirements.Length != source.Requirements
                .Distinct(StringComparer.Ordinal).Count())
            return false;
        var effect = runtime.Effects[0];
        if (compiler == War3AbilityCompilerKind.Invulnerable)
            return effect.Timing == AbilityEffectTiming.Aura &&
                   (effect.Status & AbilityStatusFlags.Invulnerable) != 0;
        if (compiler == War3AbilityCompilerKind.ResistantSkin)
            return effect.Timing == AbilityEffectTiming.Aura &&
                   (effect.Status & AbilityStatusFlags.Resistant) != 0;
        if (!TryData(source, "A", out var a)) return false;
        return compiler switch
        {
            War3AbilityCompilerKind.Ensnare =>
                TryData(source, "B", out var ensnareHeight) &&
                TryData(source, "C", out var ensnareMeleeRange) &&
                effect.Kind == AbilityEffectKind.ApplyStatus &&
                effect.Duration == 0f &&
                (effect.Status & (AbilityStatusFlags.MovementDisabled |
                                  AbilityStatusFlags.Grounded)) ==
                (AbilityStatusFlags.MovementDisabled |
                 AbilityStatusFlags.Grounded) &&
                effect.BuffDispelKind == AbilityBuffDispelKind.Physical &&
                Nearly(effect.SecondaryValue, a) &&
                Nearly(effect.HeroValue, ensnareHeight * distanceScale) &&
                Nearly(effect.HeroSecondaryValue,
                    ensnareMeleeRange * distanceScale),
            War3AbilityCompilerKind.Cripple =>
                TryData(source, "B", out var attackReduction) &&
                TryData(source, "C", out var damageReduction) &&
                Nearly(effect.Modifier.MovementSpeedMultiplier, 1f - a) &&
                Nearly(effect.Modifier.AttackCooldownMultiplier,
                    1f / (1f - attackReduction)) &&
                Nearly(effect.Modifier.AttackDamageMultiplier,
                    1f - damageReduction),
            War3AbilityCompilerKind.Bloodlust =>
                TryData(source, "B", out var movementIncrease) &&
                TryData(source, "C", out var scaleIncrease) &&
                Nearly(effect.Modifier.AttackCooldownMultiplier,
                    1f / (1f + a)) &&
                Nearly(effect.Modifier.MovementSpeedMultiplier,
                    1f + movementIncrease) &&
                Nearly(effect.SecondaryValue, scaleIncrease),
            War3AbilityCompilerKind.Rejuvenation =>
                TryData(source, "B", out var manaGain) &&
                TryData(source, "C", out var fullFlags) &&
                TryData(source, "D", out var noTargetRequired) &&
                source.Duration is > 0f and var duration &&
                Nearly(effect.Modifier.HealthRegenerationAdd, a / duration) &&
                Nearly(effect.Modifier.ManaRegenerationAdd,
                    manaGain / duration) &&
                Nearly(effect.SecondaryValue, fullFlags) &&
                Nearly(effect.HeroValue, noTargetRequired),
            War3AbilityCompilerKind.FaerieFire =>
                (effect.Status & AbilityStatusFlags.Revealed) != 0 &&
                Nearly(effect.Modifier.ArmorAdd, -a) &&
                Nearly(effect.SecondaryValue,
                    TryData(source, "B", out var alwaysAuto)
                        ? alwaysAuto
                        : 0f),
            War3AbilityCompilerKind.Evasion =>
                effect.Timing == AbilityEffectTiming.Aura &&
                Nearly(effect.CombatModifier.EvasionChancePercent, a * 100f),
            War3AbilityCompilerKind.CriticalStrike =>
                TryData(source, "B", out var criticalMultiplier) &&
                TryData(source, "C", out var criticalBonus) &&
                TryData(source, "D", out var criticalEvasion) &&
                TryData(source, "E", out var criticalNeverMiss) &&
                effect.Timing == AbilityEffectTiming.Aura &&
                Nearly(effect.CombatModifier.CriticalStrikeChancePercent, a) &&
                Nearly(effect.CombatModifier.CriticalStrikeDamageMultiplier,
                    criticalMultiplier) &&
                Nearly(effect.CombatModifier.CriticalStrikeBonusDamage,
                    criticalBonus) &&
                Nearly(effect.CombatModifier.EvasionChancePercent,
                    criticalEvasion * 100f) &&
                effect.CombatModifier.CriticalStrikeNeverMiss ==
                (criticalNeverMiss > 0f),
            _ => false
        };
    }

    private static bool VerifyInventoryLifecycle()
    {
        var runtime = new War3ItemShopRuntime();
        var economy = new PlayerEconomyStore();
        economy.RegisterPlayer(1, 10_000, 10_000, 20);
        var purchase = runtime.Purchase(
            0, 0, 7, 2, true, 3, economy, 1);
        if (!purchase.Succeeded || runtime.InventoryCount(7) != 1)
            return false;
        if (!runtime.TryDropItem(
                7, 0, new System.Numerics.Vector2(10f, 20f), true,
                out var dropped) || runtime.InventoryCount(7) != 0)
            return false;
        if (!runtime.TryPickupItem(
                7, dropped.Id, new System.Numerics.Vector2(10f, 20f),
                2, true, 8f, out var slot) || slot != 0 ||
            runtime.InventoryCount(7) != 1)
            return false;
        var deathDrops = runtime.DropInventoryOnDeath(
            7, new System.Numerics.Vector2(30f, 40f), true);
        if (deathDrops.Length != 1 || runtime.InventoryCount(7) != 0)
            return false;
        var snapshot = runtime.CaptureRuntimeState();
        var restored = new War3ItemShopRuntime();
        restored.RestoreRuntimeState(snapshot);
        var ground = restored.GroundItems();
        return snapshot.NextGroundItemId > deathDrops[0].Id &&
               ground.Length == 1 && ground[0] == deathDrops[0] &&
               restored.InventoryCount(7) == 0;
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
