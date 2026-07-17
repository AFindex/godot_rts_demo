using RtsDemo.Simulation;
using RtsDemo.AI;

namespace RtsDemo.Tests;

public readonly record struct SelfTestResult(bool Passed, string Summary);

public static class SimulationSelfTest
{
    public static SelfTestResult Run(
        NavigationMapSnapshot? navigationMap = null,
        GameplayProfileCatalogSnapshot? gameplayProfiles = null,
        ClearanceBakeSnapshot? clearanceBake = null,
        BuildingTypeCatalogSnapshot? buildingTypes = null,
        ProductionCatalogSnapshot? productionCatalog = null,
        TechnologyCatalogSnapshot? technologyCatalog = null,
        AiConfigurationCatalogSnapshot? aiConfigurations = null)
    {
        try
        {
            var dataResult = NavigationMapSelfTest.Run();
            var terrainResult = TerrainMapSelfTest.Run();
            var terrainAuthoringResult = TerrainAuthoringSelfTest.Run();
            var terrainTopologyResult = TerrainNavigationTopologySelfTest.Run();
            var terrainPresetResult = TerrainTestPresetCatalogSelfTest.Run();
            var terrainShowcaseResult = TerrainShowcaseCatalogSelfTest.Run();
            var terrainVisionResult = TerrainVisionSelfTest.Run();
            var war3PcgResult = War3BattlefieldPcgSelfTest.Run();
            var war3MapResult = War3MapAssetSelfTest.Run();
            var war3PointerTargetingResult = War3PointerTargetingSelfTest.Run();
            var war3CursorResult = War3CursorCatalogSelfTest.Run();
            var war3HumanUiResult = War3HumanUiDataSelfTest.Run();
            var war3SpatialSizingResult = War3SpatialSizingSelfTest.Run();
            var war3TreeHarvestFeedbackResult =
                War3TreeHarvestFeedbackSelfTest.Run();
            var fallbackPathResult = PathProviderFallbackSelfTest.Run();
            var profileResult = GameplayProfileSelfTest.Run(gameplayProfiles);
            var previewResult = ClearancePreviewSelfTest.Run();
            var connectivityResult = NavigationConnectivitySelfTest.Run();
            var destinationSlotClearanceResult =
                DestinationSlotClearanceSelfTest.Run();
            var chokeCommandReplacementResult =
                ChokeCommandReplacementSelfTest.Run();
            var bakeResult = ClearanceBakeSelfTest.Run(
                navigationMap, clearanceBake);
            var incrementalResult = ClearanceIncrementalSelfTest.Run(
                navigationMap, clearanceBake);
            var reloadResult = ResourceReloadSelfTest.Run(
                navigationMap, gameplayProfiles, clearanceBake);
            var bakeCommitResult = ClearanceBakeCommitSelfTest.Run(
                navigationMap, gameplayProfiles, clearanceBake);
            var placementDiffResult = BuildingConnectivityDiffSelfTest.Run();
            var watchWorkflowResult = ResourceReloadWorkflowSelfTest.Run();
            var economyResult = EconomySelfTest.Run();
            var buildingTypeResult = BuildingTypeCatalogSelfTest.Run(buildingTypes);
            var productionCatalogResult = ProductionCatalogSelfTest.Run(
                productionCatalog);
            var technologyCatalogResult = TechnologyCatalogSelfTest.Run(
                technologyCatalog);
            var buildingUpgradeResult = BuildingUpgradeSelfTest.Run();
            var aiArchitectureResult = AiArchitectureSelfTest.Run();
            var aiConfigurationResult = AiConfigurationSelfTest.Run(
                aiConfigurations);
            var modularAiResult = ModularAiPolicySelfTest.Run();
            var operationPresentationResult = OperationPresentationSelfTest.Run();
            var interface3DResult = Rts3DInterfaceSelfTest.Run();
            var controlGroupResult = ControlGroupSelfTest.Run();
            var productionGroupResult = ProductionGroupPresentationSelfTest.Run();
            var combatEventResult = CombatEventStreamSelfTest.Run();
            var combatDamageResult = CombatDamageSelfTest.Run();
            var war3CombatRulesResult = War3CombatRulesSelfTest.Run();
            var combatProjectileResult = CombatProjectileSelfTest.Run();
            var combatPresentationResult = CombatPresentationSelfTest.Run();
            var abilityResult = AbilitySystemSelfTest.Run();
            var testShowcaseResult = TestShowcaseCatalogSelfTest.Run();
            var playableSkirmishResult = PlayableSkirmishScenarioSelfTest.Run(
                buildingTypes, productionCatalog, technologyCatalog);
            var passed = dataResult.Passed && terrainResult.Passed &&
                         terrainAuthoringResult.Passed &&
                         terrainTopologyResult.Passed &&
                         terrainPresetResult.Passed &&
                         terrainShowcaseResult.Passed &&
                         terrainVisionResult.Passed &&
                         war3PcgResult.Passed &&
                         war3MapResult.Passed &&
                         war3PointerTargetingResult.Passed &&
                         war3CursorResult.Passed &&
                         war3HumanUiResult.Passed &&
                         war3SpatialSizingResult.Passed &&
                         war3TreeHarvestFeedbackResult.Passed &&
                         fallbackPathResult.Passed &&
                         profileResult.Passed &&
                         previewResult.Passed && connectivityResult.Passed &&
                         destinationSlotClearanceResult.Passed &&
                         chokeCommandReplacementResult.Passed &&
                         bakeResult.Passed && incrementalResult.Passed &&
                         reloadResult.Passed && bakeCommitResult.Passed;
            passed &= placementDiffResult.Passed && watchWorkflowResult.Passed &&
                      economyResult.Passed && buildingTypeResult.Passed &&
                      productionCatalogResult.Passed &&
                      technologyCatalogResult.Passed &&
                      buildingUpgradeResult.Passed &&
                       aiArchitectureResult.Passed &&
                       aiConfigurationResult.Passed && modularAiResult.Passed;
            passed &= operationPresentationResult.Passed && interface3DResult.Passed &&
                      controlGroupResult.Passed &&
                      productionGroupResult.Passed && combatEventResult.Passed &&
                      combatDamageResult.Passed &&
                      war3CombatRulesResult.Passed &&
                      combatProjectileResult.Passed &&
                      combatPresentationResult.Passed && abilityResult.Passed &&
                      testShowcaseResult.Passed &&
                      playableSkirmishResult.Passed;
            var summaries = new List<string>(VisualTestCatalog.CaseIds.Length + 26)
            {
                $"navigation-data={(dataResult.Passed ? "PASS" : "FAIL")}" +
                $"({dataResult.Summary})",
                $"terrain-map={(terrainResult.Passed ? "PASS" : "FAIL")}" +
                $"({terrainResult.Summary})",
                $"terrain-authoring=" +
                $"{(terrainAuthoringResult.Passed ? "PASS" : "FAIL")}" +
                $"({terrainAuthoringResult.Summary})",
                $"terrain-topology=" +
                $"{(terrainTopologyResult.Passed ? "PASS" : "FAIL")}" +
                $"({terrainTopologyResult.Summary})",
                $"terrain-presets=" +
                $"{(terrainPresetResult.Passed ? "PASS" : "FAIL")}" +
                $"({terrainPresetResult.Summary})",
                $"terrain-showcase=" +
                $"{(terrainShowcaseResult.Passed ? "PASS" : "FAIL")}" +
                $"({terrainShowcaseResult.Summary})",
                $"terrain-vision=" +
                $"{(terrainVisionResult.Passed ? "PASS" : "FAIL")}" +
                $"({terrainVisionResult.Summary})",
                $"war3-battlefield-pcg=" +
                $"{(war3PcgResult.Passed ? "PASS" : "FAIL")}" +
                $"({war3PcgResult.Summary})",
                $"war3-map-assets=" +
                $"{(war3MapResult.Passed ? "PASS" : "FAIL")}" +
                $"({war3MapResult.Summary})",
                $"war3-pointer-targeting=" +
                $"{(war3PointerTargetingResult.Passed ? "PASS" : "FAIL")}" +
                $"({war3PointerTargetingResult.Summary})",
                $"war3-cursors={(war3CursorResult.Passed ? "PASS" : "FAIL")}" +
                $"({war3CursorResult.Summary})",
                $"war3-human-ui=" +
                $"{(war3HumanUiResult.Passed ? "PASS" : "FAIL")}" +
                $"({war3HumanUiResult.Summary})",
                $"war3-spatial-sizing=" +
                $"{(war3SpatialSizingResult.Passed ? "PASS" : "FAIL")}" +
                $"({war3SpatialSizingResult.Summary})",
                $"war3-tree-harvest-feedback=" +
                $"{(war3TreeHarvestFeedbackResult.Passed ? "PASS" : "FAIL")}" +
                $"({war3TreeHarvestFeedbackResult.Summary})",
                $"path-provider-fallback=" +
                $"{(fallbackPathResult.Passed ? "PASS" : "FAIL")}" +
                $"({fallbackPathResult.Summary})",
                $"gameplay-profiles={(profileResult.Passed ? "PASS" : "FAIL")}" +
                $"({profileResult.Summary})",
                $"clearance-preview={(previewResult.Passed ? "PASS" : "FAIL")}" +
                $"({previewResult.Summary})",
                $"navigation-connectivity=" +
                $"{(connectivityResult.Passed ? "PASS" : "FAIL")}" +
                $"({connectivityResult.Summary})",
                $"destination-slot-clearance=" +
                $"{(destinationSlotClearanceResult.Passed ? "PASS" : "FAIL")}" +
                $"({destinationSlotClearanceResult.Summary})",
                $"choke-command-replacement=" +
                $"{(chokeCommandReplacementResult.Passed ? "PASS" : "FAIL")}" +
                $"({chokeCommandReplacementResult.Summary})",
                $"clearance-bake={(bakeResult.Passed ? "PASS" : "FAIL")}" +
                $"({bakeResult.Summary})",
                $"clearance-incremental=" +
                $"{(incrementalResult.Passed ? "PASS" : "FAIL")}" +
                $"({incrementalResult.Summary})",
                $"resource-reload={(reloadResult.Passed ? "PASS" : "FAIL")}" +
                $"({reloadResult.Summary})",
                $"clearance-bake-commit=" +
                $"{(bakeCommitResult.Passed ? "PASS" : "FAIL")}" +
                $"({bakeCommitResult.Summary})",
                $"building-connectivity-diff=" +
                $"{(placementDiffResult.Passed ? "PASS" : "FAIL")}" +
                $"({placementDiffResult.Summary})",
                $"resource-watch-workflow=" +
                $"{(watchWorkflowResult.Passed ? "PASS" : "FAIL")}" +
                $"({watchWorkflowResult.Summary})",
                $"economy-dual-resource=" +
                $"{(economyResult.Passed ? "PASS" : "FAIL")}" +
                $"({economyResult.Summary})",
                $"building-type-catalog=" +
                $"{(buildingTypeResult.Passed ? "PASS" : "FAIL")}" +
                $"({buildingTypeResult.Summary})",
                $"production-catalog=" +
                $"{(productionCatalogResult.Passed ? "PASS" : "FAIL")}" +
                $"({productionCatalogResult.Summary})",
                $"technology-catalog=" +
                $"{(technologyCatalogResult.Passed ? "PASS" : "FAIL")}" +
                $"({technologyCatalogResult.Summary})",
                $"building-upgrade=" +
                $"{(buildingUpgradeResult.Passed ? "PASS" : "FAIL")}" +
                $"({buildingUpgradeResult.Summary})",
                $"ai-architecture=" +
                $"{(aiArchitectureResult.Passed ? "PASS" : "FAIL")}" +
                $"({aiArchitectureResult.Summary})",
                $"ai-configuration=" +
                $"{(aiConfigurationResult.Passed ? "PASS" : "FAIL")}" +
                $"({aiConfigurationResult.Summary})",
                $"ai-modular-policy=" +
                $"{(modularAiResult.Passed ? "PASS" : "FAIL")}" +
                $"({modularAiResult.Summary})",
                $"operation-presentation=" +
                $"{(operationPresentationResult.Passed ? "PASS" : "FAIL")}" +
                $"({operationPresentationResult.Summary})",
                $"interface-3d=" +
                $"{(interface3DResult.Passed ? "PASS" : "FAIL")}" +
                $"({interface3DResult.Summary})",
                $"control-group=" +
                $"{(controlGroupResult.Passed ? "PASS" : "FAIL")}" +
                $"({controlGroupResult.Summary})",
                $"production-group=" +
                $"{(productionGroupResult.Passed ? "PASS" : "FAIL")}" +
                $"({productionGroupResult.Summary})",
                $"combat-events={(combatEventResult.Passed ? "PASS" : "FAIL")}" +
                $"({combatEventResult.Summary})",
                $"combat-damage={(combatDamageResult.Passed ? "PASS" : "FAIL")}" +
                $"({combatDamageResult.Summary})",
                $"war3-combat-rules=" +
                $"{(war3CombatRulesResult.Passed ? "PASS" : "FAIL")}" +
                $"({war3CombatRulesResult.Summary})",
                $"combat-projectiles=" +
                $"{(combatProjectileResult.Passed ? "PASS" : "FAIL")}" +
                $"({combatProjectileResult.Summary})",
                $"combat-presentation=" +
                $"{(combatPresentationResult.Passed ? "PASS" : "FAIL")}" +
                $"({combatPresentationResult.Summary})",
                $"ability-runtime=" +
                $"{(abilityResult.Passed ? "PASS" : "FAIL")}" +
                $"({abilityResult.Summary})",
                $"test-showcase=" +
                $"{(testShowcaseResult.Passed ? "PASS" : "FAIL")}" +
                $"({testShowcaseResult.Summary})",
                $"playable-skirmish=" +
                $"{(playableSkirmishResult.Passed ? "PASS" : "FAIL")}" +
                $"({playableSkirmishResult.Summary})"
            };
            foreach (var caseId in VisualTestCatalog.CaseIds)
            {
                var session = VisualTestCatalog.Create(
                    caseId, navigationMap, gameplayProfiles, clearanceBake,
                    buildingTypes: buildingTypes,
                    productionCatalog: productionCatalog,
                    technologyCatalog: technologyCatalog,
                    aiConfigurations: aiConfigurations);
                while (!session.HasReachedEnd)
                {
                    session.Step();
                }

                var result = session.Evaluate();
                passed &= result.Passed;
                summaries.Add($"{caseId}={(result.Passed ? "PASS" : "FAIL")}" +
                              $"({result.Summary})");
            }

            return new SelfTestResult(passed, string.Join("; ", summaries));
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }
}
