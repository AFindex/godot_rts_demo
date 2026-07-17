using War3Rts;
using War3Rts.Data;
using RtsDemo.Demos.War3;
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
                    var behavior = War3AbilityBehaviorRegistry.Resolve(baseCode);
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
            var behaviorRegistryValid =
                War3AbilityBehaviorRegistry.All.Count == 42 &&
                runtimeImport.RequestedCount == 43 &&
                runtimeImport.BehaviorFamilyCount == 42 &&
                runtimeImport.PrototypeCount == 34 &&
                runtimeImport.UnclassifiedBaseCodes.Length == 0 &&
                runtimeImport.UnresolvedRequirementIds.Length == 0 &&
                runtimeCoverage.RegistryBaseFamilyCount == 42 &&
                runtimeCoverage.All.AbilityCount == 801 &&
                runtimeCoverage.All.BaseFamilyCount == 415 &&
                runtimeCoverage.All.ClassifiedBaseFamilyCount == 42 &&
                runtimeCoverage.UnitReferenced.AbilityCount == 461 &&
                runtimeCoverage.UnitReferenced.BaseFamilyCount == 285 &&
                runtimeCoverage.Items.AbilityCount == 234 &&
                runtimeCoverage.Items.BaseFamilyCount == 129 &&
                runtimeCoverage.CurrentRuntime.AbilityCount == 43 &&
                runtimeCoverage.CurrentRuntime.BaseFamilyCount == 42 &&
                runtimeCoverage.CurrentRuntime.PrototypeAbilityCount == 34 &&
                runtimeCoverage.CurrentRuntime.StatusCounts["unclassified"] == 0 &&
                runtimeCoverage.OrphanRegisteredBaseCodes.Length == 0 &&
                runtimeCoverage.Targeting.ExportedTokens.Length == 27 &&
                runtimeCoverage.Targeting.UnrecognizedTokens.Length == 0 &&
                runtimeCoverage.Targeting.RuntimeUnsupportedTokens.Length == 10 &&
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
                runtimeCompilerMatrix.Length == 34 &&
                runtimeCompilerMatrix.Select(value => value.Compiler)
                    .Distinct().Count() == 34 &&
                runtimeCompilerMatrix.All(value => value.HasEffects) &&
                runtimeRequirementsPreserved && heroLearningBindings;
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
                         relatedPresentation && metadataLocalized &&
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
