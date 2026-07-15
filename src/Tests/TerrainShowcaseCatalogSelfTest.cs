using RtsDemo.Demos;
using RtsDemo.Presentation;

namespace RtsDemo.Tests;

public static class TerrainShowcaseCatalogSelfTest
{
    public static SelfTestResult Run()
    {
        var entries = TerrainShowcaseCatalog.Entries.ToArray();
        var targets = Enum.GetValues<TerrainShowcaseTarget>();
        var paths = entries.Select(entry =>
                DemoSceneCatalog.TerrainShowcaseScene(entry.Target))
            .ToArray();
        var passed = entries.Length == 6 &&
                     entries.Length == targets.Length &&
                     entries.Select(entry => entry.Target).SequenceEqual(targets) &&
                     entries.Select(entry => entry.Target).Distinct().Count() ==
                     entries.Length &&
                     entries.All(entry =>
                         !string.IsNullOrWhiteSpace(entry.Title) &&
                         !string.IsNullOrWhiteSpace(entry.Scope) &&
                         !string.IsNullOrWhiteSpace(entry.Summary) &&
                         !string.IsNullOrWhiteSpace(entry.Evidence)) &&
                     paths.Distinct(StringComparer.Ordinal).Count() == paths.Length &&
                     paths.All(path => path.StartsWith(
                         "res://demo/", StringComparison.Ordinal) &&
                         path.EndsWith(".tscn", StringComparison.Ordinal));
        return new SelfTestResult(
            passed,
            $"entries={entries.Length}, targets={targets.Length}, " +
            $"scenes={paths.Distinct(StringComparer.Ordinal).Count()}");
    }
}
