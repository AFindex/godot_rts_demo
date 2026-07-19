using System.Numerics;

namespace RtsDemo.Simulation;

public enum BuildingFootprintClass : byte
{
    Small,
    Medium,
    Large,
    Huge
}

public readonly record struct BuildingFootprintProfile(
    BuildingFootprintClass Class,
    Vector2 Size)
{
    public static BuildingFootprintProfile For(BuildingFootprintClass value) =>
        value switch
        {
            BuildingFootprintClass.Small => new(value, new Vector2(32f, 32f)),
            BuildingFootprintClass.Medium => new(value, new Vector2(64f, 48f)),
            BuildingFootprintClass.Large => new(value, new Vector2(112f, 80f)),
            BuildingFootprintClass.Huge => new(value, new Vector2(160f, 120f)),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
}

public readonly record struct BuildingPlacementRules(
    MovementClass MinimumPassageClass,
    float UnitPadding = 2f,
    // Full-world connectivity preservation is a specialized anti-block rule.
    // Standard RTS construction intentionally allows walls and sealed routes.
    bool PreserveConnectivity = false);

public enum BuildingPlacementCode : byte
{
    Success,
    InvalidFootprint,
    OutsideWorld,
    StaticObstacleOverlap,
    DynamicFootprintOverlap,
    UnitOverlap,
    InsufficientClearance,
    DisconnectsNavigation,
    TerrainUnbuildable
}

public readonly record struct BuildingPlacementResult(
    BuildingPlacementCode Code,
    DynamicFootprintId FootprintId,
    int ConflictId)
{
    public bool Succeeded => Code == BuildingPlacementCode.Success;
}

public enum StaticPlacementCode : byte
{
    Success,
    InvalidFootprint,
    OutsideWorld,
    StaticObstacleOverlap,
    DynamicFootprintOverlap,
    InsufficientClearance,
    DisconnectsNavigation,
    TerrainUnbuildable
}

public readonly record struct StaticPlacementResult(
    StaticPlacementCode Code,
    int ConflictId)
{
    public bool Succeeded => Code == StaticPlacementCode.Success;
}

public enum DynamicStartValidationCode : byte
{
    NotEvaluated,
    Success,
    InvalidFootprint,
    UnitOverlap
}

public readonly record struct DynamicStartValidationResult(
    DynamicStartValidationCode Code,
    int ConflictUnit)
{
    public bool Succeeded => Code == DynamicStartValidationCode.Success;
}

public readonly record struct BuildingPlacementAssessment(
    StaticPlacementResult Static,
    DynamicStartValidationResult Dynamic)
{
    public BuildingPlacementCode Code
    {
        get
        {
            if (Static.Code is StaticPlacementCode.InvalidFootprint or
                StaticPlacementCode.OutsideWorld or
                StaticPlacementCode.StaticObstacleOverlap or
                StaticPlacementCode.DynamicFootprintOverlap or
                StaticPlacementCode.TerrainUnbuildable)
                return ToPlacementCode(Static.Code);
            if (Dynamic.Code == DynamicStartValidationCode.UnitOverlap)
                return BuildingPlacementCode.UnitOverlap;
            if (Dynamic.Code is DynamicStartValidationCode.InvalidFootprint or
                DynamicStartValidationCode.NotEvaluated)
                return BuildingPlacementCode.InvalidFootprint;
            return ToPlacementCode(Static.Code);
        }
    }

    public int ConflictId => Code == BuildingPlacementCode.UnitOverlap
        ? Dynamic.ConflictUnit
        : Static.ConflictId;
    public bool Succeeded => Code == BuildingPlacementCode.Success;

    public BuildingPlacementResult ToPlacementResult(
        DynamicFootprintId footprintId = default) =>
        new(Code, footprintId, ConflictId);

    private static BuildingPlacementCode ToPlacementCode(
        StaticPlacementCode code) => code switch
        {
            StaticPlacementCode.Success => BuildingPlacementCode.Success,
            StaticPlacementCode.InvalidFootprint =>
                BuildingPlacementCode.InvalidFootprint,
            StaticPlacementCode.OutsideWorld =>
                BuildingPlacementCode.OutsideWorld,
            StaticPlacementCode.StaticObstacleOverlap =>
                BuildingPlacementCode.StaticObstacleOverlap,
            StaticPlacementCode.DynamicFootprintOverlap =>
                BuildingPlacementCode.DynamicFootprintOverlap,
            StaticPlacementCode.InsufficientClearance =>
                BuildingPlacementCode.InsufficientClearance,
            StaticPlacementCode.DisconnectsNavigation =>
                BuildingPlacementCode.DisconnectsNavigation,
            StaticPlacementCode.TerrainUnbuildable =>
                BuildingPlacementCode.TerrainUnbuildable,
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };
}

public enum HardFootprintCommitCode : byte
{
    Success,
    StaticPlacementRejected,
    DynamicOccupantRejected
}

public readonly record struct HardFootprintCommitResult(
    HardFootprintCommitCode Code,
    DynamicFootprintId FootprintId,
    BuildingPlacementAssessment Assessment)
{
    public bool Succeeded => Code == HardFootprintCommitCode.Success;
}

public static class BuildingPlacementValidator
{
    public static BuildingPlacementAssessment Evaluate(
        StaticWorld world,
        UnitStore units,
        SimRect footprint,
        BuildingPlacementRules rules,
        BuildingConnectivityGuard? connectivityGuard = null,
        Predicate<DynamicFootprint>? includeDynamicFootprint = null,
        Predicate<int>? includeUnit = null)
    {
        var deferConnectivity = rules.PreserveConnectivity &&
                                connectivityGuard is not null;
        var initialRules = deferConnectivity
            ? rules with { PreserveConnectivity = false }
            : rules;
        var staticResult = ValidateStatic(
            world, footprint, initialRules, connectivityGuard,
            includeDynamicFootprint);
        var dynamicResult = BlocksDynamicEvaluation(staticResult.Code)
            ? new DynamicStartValidationResult(
                DynamicStartValidationCode.NotEvaluated, -1)
            : ValidateDynamicStart(units, footprint, rules, includeUnit);
        if (deferConnectivity && staticResult.Succeeded &&
            dynamicResult.Succeeded &&
            HasCompleteConnectivityKnowledge(world, includeDynamicFootprint))
        {
            var connectivity = connectivityGuard!.Evaluate(
                footprint, rules.MinimumPassageClass);
            if (!connectivity.Preserved)
            {
                staticResult = StaticFailure(
                    StaticPlacementCode.DisconnectsNavigation,
                    connectivity.FirstSplitComponentId);
            }
        }
        return new BuildingPlacementAssessment(staticResult, dynamicResult);
    }

    public static StaticPlacementResult ValidateStatic(
        StaticWorld world,
        SimRect footprint,
        BuildingPlacementRules rules,
        BuildingConnectivityGuard? connectivityGuard = null,
        Predicate<DynamicFootprint>? includeDynamicFootprint = null)
    {
        if (!ValidRequest(footprint, rules))
        {
            return StaticFailure(StaticPlacementCode.InvalidFootprint);
        }

        if (!world.Bounds.Contains(footprint.Min) ||
            !world.Bounds.Contains(footprint.Max))
        {
            return StaticFailure(StaticPlacementCode.OutsideWorld);
        }

        if (world.Terrain is not null &&
            !world.Terrain.IsAreaBuildable(footprint))
        {
            return StaticFailure(StaticPlacementCode.TerrainUnbuildable);
        }

        var obstacles = world.Obstacles;
        for (var index = 0; index < obstacles.Length; index++)
        {
            if (OverlapsArea(footprint, obstacles[index]))
            {
                return StaticFailure(
                    StaticPlacementCode.StaticObstacleOverlap, index);
            }
        }

        var dynamicFootprints = world.DynamicOccupancy.Snapshot();
        var completeConnectivityKnowledge =
            !rules.PreserveConnectivity || connectivityGuard is null ||
            includeDynamicFootprint is null ||
            dynamicFootprints.All(value => includeDynamicFootprint(value));
        for (var index = 0; index < dynamicFootprints.Length; index++)
        {
            if (!OverlapsArea(footprint, dynamicFootprints[index].Bounds))
                continue;
            if (includeDynamicFootprint is not null &&
                !includeDynamicFootprint(dynamicFootprints[index]))
                continue;
            return StaticFailure(
                StaticPlacementCode.DynamicFootprintOverlap,
                dynamicFootprints[index].Id.Value);
        }

        var requiredWidth = MovementClearance.ForClass(
            rules.MinimumPassageClass).RequiredWidth;
        if (CreatesNarrowBoundaryGap(world.Bounds, footprint, requiredWidth))
        {
            return StaticFailure(
                StaticPlacementCode.InsufficientClearance, -1);
        }

        for (var index = 0; index < obstacles.Length; index++)
        {
            if (CreatesNarrowGap(footprint, obstacles[index], requiredWidth))
            {
                return StaticFailure(
                    StaticPlacementCode.InsufficientClearance, index);
            }
        }

        for (var index = 0; index < dynamicFootprints.Length; index++)
        {
            if (!CreatesNarrowGap(
                    footprint, dynamicFootprints[index].Bounds, requiredWidth))
                continue;
            if (includeDynamicFootprint is not null &&
                !includeDynamicFootprint(dynamicFootprints[index]))
                continue;
            return StaticFailure(
                StaticPlacementCode.InsufficientClearance,
                dynamicFootprints[index].Id.Value);
        }

        // The authoritative connectivity guard contains every hard footprint.
        // Running it with a filtered player-known world would leak an excluded
        // building through the preview result, so defer this check to hard
        // commit whenever knowledge is incomplete.
        if (rules.PreserveConnectivity && connectivityGuard is not null &&
            completeConnectivityKnowledge)
        {
            var connectivity = connectivityGuard.Evaluate(
                footprint, rules.MinimumPassageClass);
            if (!connectivity.Preserved)
            {
                return StaticFailure(
                    StaticPlacementCode.DisconnectsNavigation,
                    connectivity.FirstSplitComponentId);
            }
        }

        return new StaticPlacementResult(StaticPlacementCode.Success, -1);
    }

    public static DynamicStartValidationResult ValidateDynamicStart(
        UnitStore units,
        SimRect footprint,
        BuildingPlacementRules rules,
        Predicate<int>? includeUnit = null)
    {
        if (!ValidRequest(footprint, rules))
        {
            return new DynamicStartValidationResult(
                DynamicStartValidationCode.InvalidFootprint, -1);
        }

        foreach (var unit in units.AliveUnits)
        {
            if (includeUnit is not null && !includeUnit(unit))
                continue;
            if (footprint.Expanded(units.Radii[unit] + rules.UnitPadding)
                .Contains(units.Positions[unit]))
            {
                return new DynamicStartValidationResult(
                    DynamicStartValidationCode.UnitOverlap, unit);
            }
        }

        return new DynamicStartValidationResult(
            DynamicStartValidationCode.Success, -1);
    }

    private static bool CreatesNarrowBoundaryGap(
        SimRect bounds,
        SimRect footprint,
        float requiredWidth) =>
        IsNarrow(footprint.Min.X - bounds.Min.X, requiredWidth) ||
        IsNarrow(bounds.Max.X - footprint.Max.X, requiredWidth) ||
        IsNarrow(footprint.Min.Y - bounds.Min.Y, requiredWidth) ||
        IsNarrow(bounds.Max.Y - footprint.Max.Y, requiredWidth);

    private static bool CreatesNarrowGap(
        SimRect left,
        SimRect right,
        float requiredWidth)
    {
        var overlapsX = left.Min.X < right.Max.X && left.Max.X > right.Min.X;
        if (overlapsX)
        {
            var verticalGap = MathF.Max(
                right.Min.Y - left.Max.Y,
                left.Min.Y - right.Max.Y);
            if (IsNarrow(verticalGap, requiredWidth))
            {
                return true;
            }
        }

        var overlapsY = left.Min.Y < right.Max.Y && left.Max.Y > right.Min.Y;
        if (!overlapsY)
        {
            return false;
        }

        var horizontalGap = MathF.Max(
            right.Min.X - left.Max.X,
            left.Min.X - right.Max.X);
        return IsNarrow(horizontalGap, requiredWidth);
    }

    private static bool IsNarrow(float gap, float requiredWidth) =>
        gap > 0.0001f && gap < requiredWidth;

    private static bool OverlapsArea(SimRect left, SimRect right) =>
        left.Min.X < right.Max.X && left.Max.X > right.Min.X &&
        left.Min.Y < right.Max.Y && left.Max.Y > right.Min.Y;

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool ValidRequest(
        SimRect footprint,
        BuildingPlacementRules rules) =>
        IsFinite(footprint.Min) && IsFinite(footprint.Max) &&
        footprint.Width > 0f && footprint.Height > 0f &&
        float.IsFinite(rules.UnitPadding) && rules.UnitPadding >= 0f &&
        Enum.IsDefined(rules.MinimumPassageClass);

    private static bool BlocksDynamicEvaluation(StaticPlacementCode code) =>
        code is StaticPlacementCode.InvalidFootprint or
            StaticPlacementCode.OutsideWorld or
            StaticPlacementCode.StaticObstacleOverlap or
            StaticPlacementCode.DynamicFootprintOverlap or
            StaticPlacementCode.TerrainUnbuildable;

    private static bool HasCompleteConnectivityKnowledge(
        StaticWorld world,
        Predicate<DynamicFootprint>? includeDynamicFootprint)
    {
        if (includeDynamicFootprint is null)
            return true;
        var footprints = world.DynamicOccupancy.Snapshot();
        for (var index = 0; index < footprints.Length; index++)
        {
            if (!includeDynamicFootprint(footprints[index]))
                return false;
        }
        return true;
    }

    private static StaticPlacementResult StaticFailure(
        StaticPlacementCode code,
        int conflictId = -1) =>
        new(code, conflictId);
}
