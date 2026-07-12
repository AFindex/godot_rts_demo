using RtsDemo.Presentation;

namespace RtsDemo.Tests;

public static class TestShowcaseCatalogSelfTest
{
    public static SelfTestResult Run()
    {
        var entries = TestShowcaseCatalog.Build(VisualTestCatalog.CaseIds);
        var ids = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        var complete = entries.Length == VisualTestCatalog.CaseIds.Length &&
                       ids.Count == entries.Length &&
                       entries.All(entry =>
                           !string.IsNullOrWhiteSpace(entry.Title) &&
                           !string.IsNullOrWhiteSpace(entry.Category) &&
                           !string.IsNullOrWhiteSpace(entry.Summary));
        var categories = TestShowcaseCatalog.Categories(entries);
        var searchable = TestShowcaseCatalog.Filter(
            entries, "AI", TestShowcaseCatalog.AllCategories).Length >= 3 &&
            TestShowcaseCatalog.Filter(
                entries, "热恢复", TestShowcaseCatalog.AllCategories).Length >= 1;
        var rejectsUnknown = false;
        try
        {
            TestShowcaseCatalog.Build([.. VisualTestCatalog.CaseIds, "unknown-case"]);
        }
        catch (InvalidOperationException)
        {
            rejectsUnknown = true;
        }

        var passed = complete && categories.Length >= 6 && searchable &&
                     rejectsUnknown;
        return new SelfTestResult(
            passed,
            $"entries={entries.Length}, categories={categories.Length}, " +
            $"searchable={searchable}, rejects-unknown={rejectsUnknown}");
    }
}
