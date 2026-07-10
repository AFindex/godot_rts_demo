using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public readonly record struct SelfTestResult(bool Passed, string Summary);

public static class SimulationSelfTest
{
    public static SelfTestResult Run(
        NavigationMapSnapshot? navigationMap = null,
        GameplayProfileCatalogSnapshot? gameplayProfiles = null)
    {
        try
        {
            var dataResult = NavigationMapSelfTest.Run();
            var profileResult = GameplayProfileSelfTest.Run(gameplayProfiles);
            var passed = dataResult.Passed && profileResult.Passed;
            var summaries = new List<string>(VisualTestCatalog.CaseIds.Length + 2)
            {
                $"navigation-data={(dataResult.Passed ? "PASS" : "FAIL")}" +
                $"({dataResult.Summary})",
                $"gameplay-profiles={(profileResult.Passed ? "PASS" : "FAIL")}" +
                $"({profileResult.Summary})"
            };
            foreach (var caseId in VisualTestCatalog.CaseIds)
            {
                var session = VisualTestCatalog.Create(
                    caseId, navigationMap, gameplayProfiles);
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
