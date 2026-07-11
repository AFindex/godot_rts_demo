using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public readonly record struct SelfTestResult(bool Passed, string Summary);

public static class SimulationSelfTest
{
    public static SelfTestResult Run(
        NavigationMapSnapshot? navigationMap = null,
        GameplayProfileCatalogSnapshot? gameplayProfiles = null,
        ClearanceBakeSnapshot? clearanceBake = null)
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
            var passed = dataResult.Passed && profileResult.Passed &&
                         previewResult.Passed && connectivityResult.Passed &&
                         bakeResult.Passed && incrementalResult.Passed &&
                         reloadResult.Passed;
            var summaries = new List<string>(VisualTestCatalog.CaseIds.Length + 7)
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
                $"({reloadResult.Summary})"
            };
            foreach (var caseId in VisualTestCatalog.CaseIds)
            {
                var session = VisualTestCatalog.Create(
                    caseId, navigationMap, gameplayProfiles, clearanceBake);
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
