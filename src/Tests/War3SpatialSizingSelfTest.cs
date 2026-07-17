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
                         radiusFit && upgradeFit;
            return new SelfTestResult(
                passed,
                $"buildings={buildingFit}/{buildings.Length}, " +
                $"pathing={pathingCoverage}, circles={selectionScaleCoverage}, " +
                $"units={radiusFit}/" +
                $"{War3HumanContent.Units.Count}, upgrades={upgradeFit}, " +
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
}
