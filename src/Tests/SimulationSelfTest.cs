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
            var profileResult = GameplayProfileSelfTest.Run(gameplayProfiles);
            var previewResult = ClearancePreviewSelfTest.Run();
            var connectivityResult = NavigationConnectivitySelfTest.Run();
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
            var aiArchitectureResult = AiArchitectureSelfTest.Run();
            var aiConfigurationResult = AiConfigurationSelfTest.Run(
                aiConfigurations);
            var modularAiResult = ModularAiPolicySelfTest.Run();
            var operationPresentationResult = OperationPresentationSelfTest.Run();
            var controlGroupResult = ControlGroupSelfTest.Run();
            var productionGroupResult = ProductionGroupPresentationSelfTest.Run();
            var combatEventResult = CombatEventStreamSelfTest.Run();
            var combatDamageResult = CombatDamageSelfTest.Run();
            var combatProjectileResult = CombatProjectileSelfTest.Run();
            var passed = dataResult.Passed && profileResult.Passed &&
                         previewResult.Passed && connectivityResult.Passed &&
                         bakeResult.Passed && incrementalResult.Passed &&
                         reloadResult.Passed && bakeCommitResult.Passed;
            passed &= placementDiffResult.Passed && watchWorkflowResult.Passed &&
                      economyResult.Passed && buildingTypeResult.Passed &&
                      productionCatalogResult.Passed &&
                      technologyCatalogResult.Passed &&
                       aiArchitectureResult.Passed &&
                       aiConfigurationResult.Passed && modularAiResult.Passed;
            passed &= operationPresentationResult.Passed && controlGroupResult.Passed &&
                      productionGroupResult.Passed && combatEventResult.Passed &&
                      combatDamageResult.Passed && combatProjectileResult.Passed;
            var summaries = new List<string>(VisualTestCatalog.CaseIds.Length + 23)
            {
                $"navigation-data={(dataResult.Passed ? "PASS" : "FAIL")}" +
                $"({dataResult.Summary})",
                $"gameplay-profiles={(profileResult.Passed ? "PASS" : "FAIL")}" +
                $"({profileResult.Summary})",
                $"clearance-preview={(previewResult.Passed ? "PASS" : "FAIL")}" +
                $"({previewResult.Summary})",
                $"navigation-connectivity=" +
                $"{(connectivityResult.Passed ? "PASS" : "FAIL")}" +
                $"({connectivityResult.Summary})",
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
                $"combat-projectiles=" +
                $"{(combatProjectileResult.Passed ? "PASS" : "FAIL")}" +
                $"({combatProjectileResult.Summary})"
            };
            foreach (var caseId in VisualTestCatalog.CaseIds)
            {
                var session = VisualTestCatalog.Create(
                    caseId, navigationMap, gameplayProfiles, clearanceBake,
                    buildingTypes: buildingTypes,
                    productionCatalog: productionCatalog,
                    technologyCatalog: technologyCatalog,
                    aiConfigurations: aiConfigurations);
                while (session.Rig.Tick < session.DurationTicks)
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
