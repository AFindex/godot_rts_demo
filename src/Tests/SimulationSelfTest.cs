using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public readonly record struct SelfTestResult(bool Passed, string Summary);

public static class SimulationSelfTest
{
    public static SelfTestResult Run(NavigationMapSnapshot? navigationMap = null)
    {
        try
        {
            var dataResult = NavigationMapSelfTest.Run();
            var passed = dataResult.Passed;
            var summaries = new List<string>(VisualTestCatalog.CaseIds.Length + 1)
            {
                $"navigation-data={(dataResult.Passed ? "PASS" : "FAIL")}" +
                $"({dataResult.Summary})"
            };
            foreach (var caseId in VisualTestCatalog.CaseIds)
            {
                var session = VisualTestCatalog.Create(caseId, navigationMap);
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
