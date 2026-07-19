using System.Numerics;
using War3Rts;
using War3Rts.Data;

namespace RtsDemo.Tests;

public static class War3SpatialSizingSelfTest
{
    public static SelfTestResult Run()
    {
        try
        {
            Vector2[] expectedBuildingSizes =
            [
                new(32f, 32f), new(96f, 96f), new(128f, 128f), new(64f, 64f),
                new(80f, 80f), new(80f, 80f), new(96f, 96f), new(96f, 96f),
                new(64f, 64f), new(96f, 96f), new(32f, 32f), new(128f, 128f),
                new(128f, 128f), new(32f, 32f), new(32f, 32f), new(32f, 32f)
            ];
            var buildings = War3HumanContent.CreateBuildingCatalog().Types.ToArray();
            var buildingFit = buildings.Length == expectedBuildingSizes.Length &&
                              buildings.Select(value => value.Size)
                                  .SequenceEqual(expectedBuildingSizes);
            var pathingCoverage = War3HumanContent.Buildings.All(value =>
                War3HumanContent.DataCatalog.TryGetEditorValue(
                    value.ObjectId, "UnitData", "pathTex", out var path) &&
                path is not "_" and not "-");
            var selectionScaleCoverage = War3HumanContent.Buildings.All(value =>
                value.SelectionCircleScale > 0f);
            var groundVisualCoverage = AuditBuildingGroundVisuals();

            var production = War3HumanContent.CreateProductionCatalog();
            var policy = War3GameplayImportPolicy.Default;
            var radiusFit = War3HumanContent.Units.All(binding =>
            {
                if (!War3HumanContent.DataCatalog.TryGet(
                        binding.ObjectId, out var data) ||
                    data.Summary.Movement.CollisionSize is not > 0f)
                    return false;
                var raw = data.Summary.Movement.CollisionSize.Value;
                var profile = production.UnitType(binding.TypeId).Movement;
                var expected = MathF.Max(
                    policy.MinimumUnitRadius,
                    raw * policy.UnitCollisionRadiusScale);
                var previous = MathF.Max(5.5f, raw * policy.WorldDistanceScale);
                return Nearly(profile.PhysicalRadius, expected) &&
                       profile.PhysicalRadius > previous &&
                       profile.NavigationRadius >= profile.PhysicalRadius;
            });

            var upgrades = War3HumanContent.CreateBuildingUpgradeCatalog();
            var upgradeFit = upgrades.Profiles.ToArray().All(value =>
                buildings[value.SourceBuildingTypeId].Size == value.TargetType.Size);
            var passed = buildingFit && pathingCoverage && selectionScaleCoverage &&
                         radiusFit && upgradeFit && groundVisualCoverage.Passed;
            return new SelfTestResult(
                passed,
                $"buildings={buildingFit}/{buildings.Length}, " +
                $"pathing={pathingCoverage}, circles={selectionScaleCoverage}, " +
                $"units={radiusFit}/" +
                $"{War3HumanContent.Units.Count}, upgrades={upgradeFit}, " +
                $"ground-art={groundVisualCoverage.Summary}, " +
                $"unit_scale={policy.UnitCollisionRadiusScale:0.###}, " +
                $"path_cell={policy.PathingCellSize:0.###}");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }

    private static bool Nearly(float left, float right) =>
        MathF.Abs(left - right) <= 0.001f;

    private static (bool Passed, string Summary) AuditBuildingGroundVisuals()
    {
        var catalog = War3HumanContent.DataCatalog;
        var buildings = catalog.Entries.Where(value => value.IsBuilding).ToArray();
        var foundationRecords = 0;
        var shadowRecords = 0;
        var unresolvedFoundations = new List<string>();
        var unresolvedShadows = new List<string>();
        var races = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in buildings)
        {
            if (!catalog.TryGet(entry.Id, out var data))
            {
                unresolvedFoundations.Add($"{entry.Id}:data");
                continue;
            }
            var visual = War3BuildingGroundVisualCatalog.Resolve(data);
            var hasSplat = Meaningful(Editor(catalog, entry.Id, "uberSplat"));
            var hasShadow = Meaningful(Editor(
                catalog, entry.Id, "buildingShadow"));
            if (hasSplat)
            {
                foundationRecords++;
                races.Add(entry.Race);
                var themed = War3BuildingGroundVisualCatalog
                    .ResolveLordaeronFoundationTexturePath(
                        visual.FoundationTexturePath);
                if (!visual.HasFoundation ||
                    !War3BuildingGroundVisualCatalog.TextureExists(themed))
                    unresolvedFoundations.Add(entry.Id);
            }
            else if (visual.HasFoundation)
            {
                unresolvedFoundations.Add($"{entry.Id}:unexpected");
            }
            if (hasShadow)
            {
                shadowRecords++;
                if (!visual.HasBuildingShadow ||
                    !War3BuildingGroundVisualCatalog.TextureExists(
                        visual.BuildingShadowTexturePath))
                    unresolvedShadows.Add(entry.Id);
            }
            else if (visual.HasBuildingShadow)
            {
                unresolvedShadows.Add($"{entry.Id}:unexpected");
            }
        }

        var expectedSamples = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["hhou"] = "HSMA",
            ["htow"] = "HTOW",
            ["ofrt"] = "OLAR",
            ["unpl"] = "ULAR",
            ["etol"] = "EMDA",
            ["ntav"] = "HMED",
            ["ngol"] = "NGOL"
        };
        var samplesFit = expectedSamples.All(pair =>
            War3BuildingGroundVisualCatalog.Resolve(catalog, pair.Key)
                .UberSplatId.Equals(pair.Value, StringComparison.Ordinal));
        var humanRuntimeFit = War3HumanContent.Buildings.All(value =>
            value.GroundVisual.HasFoundation &&
            value.GroundVisual.HasBuildingShadow);
        var definitionsFit =
            War3BuildingGroundVisualCatalog.Definitions.Count == 20 &&
            War3BuildingGroundVisualCatalog.Definitions.All(value =>
                War3BuildingGroundVisualCatalog.TextureExists(
                    War3BuildingGroundVisualCatalog
                        .ResolveLordaeronFoundationTexturePath(
                            value.TexturePath))) &&
            War3BuildingGroundVisualCatalog.TextureExists(
                War3BuildingGroundVisualCatalog
                    .CityTreeShadowTexturePath);
        var exactSplatScaleFit =
            SplatScaleFits("HSMA", 110f, 2.2f) &&
            SplatScaleFits("HMED", 190f, 3.8f) &&
            SplatScaleFits("HTOW", 230f, 4.6f) &&
            SplatScaleFits("HCAS", 230f, 4.6f) &&
            SplatScaleFits("NGOL", 180f, 3.6f);
        var passed = buildings.Length >= 200 && foundationRecords >= 170 &&
                     shadowRecords >= 200 && races.Count >= 5 &&
                     unresolvedFoundations.Count == 0 &&
                     unresolvedShadows.Count == 0 && samplesFit &&
                     humanRuntimeFit && definitionsFit && exactSplatScaleFit;
        return (
            passed,
            $"{foundationRecords}/{shadowRecords}/{buildings.Length}, " +
            $"races={races.Count}, samples={samplesFit}, " +
            $"human={humanRuntimeFit}, definitions={definitionsFit}, " +
            $"scale={exactSplatScaleFit}, " +
            $"missing={unresolvedFoundations.Count}/" +
            $"{unresolvedShadows.Count}");
    }

    private static bool SplatScaleFits(
        string id,
        float expectedHalfExtent,
        float expectedWorldDiameter) =>
        War3BuildingGroundVisualCatalog.TryResolveUberSplat(
            id, out var definition) &&
        Nearly(definition.SourceHalfExtent, expectedHalfExtent) &&
        Nearly(
            War3GroundOverlayBatch.FoundationWorldDiameter(
                definition.SourceHalfExtent),
            expectedWorldDiameter);

    private static string Editor(
        IWar3UnitDataCatalog catalog,
        string objectId,
        string field) =>
        catalog.TryGetEditorValue(objectId, "UnitUI", field, out var value)
            ? value
            : string.Empty;

    private static bool Meaningful(string value) =>
        !string.IsNullOrWhiteSpace(value) && value is not "_" and not "-";
}
