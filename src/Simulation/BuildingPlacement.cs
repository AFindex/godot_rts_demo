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
    bool PreserveConnectivity = true);

public enum BuildingPlacementCode : byte
{
    Success,
    InvalidFootprint,
    OutsideWorld,
    StaticObstacleOverlap,
    DynamicFootprintOverlap,
    UnitOverlap,
    InsufficientClearance,
    DisconnectsNavigation
}

public readonly record struct BuildingPlacementResult(
    BuildingPlacementCode Code,
    DynamicFootprintId FootprintId,
    int ConflictId)
{
    public bool Succeeded => Code == BuildingPlacementCode.Success;
}

public static class BuildingPlacementValidator
{
    public static BuildingPlacementResult Validate(
        StaticWorld world,
        UnitStore units,
        SimRect footprint,
        BuildingPlacementRules rules,
        BuildingConnectivityGuard? connectivityGuard = null)
    {
        if (!IsFinite(footprint.Min) || !IsFinite(footprint.Max) ||
            footprint.Width <= 0f || footprint.Height <= 0f ||
            !float.IsFinite(rules.UnitPadding) || rules.UnitPadding < 0f ||
            !Enum.IsDefined(rules.MinimumPassageClass))
        {
            return Failure(BuildingPlacementCode.InvalidFootprint);
        }

        if (!world.Bounds.Contains(footprint.Min) ||
            !world.Bounds.Contains(footprint.Max))
        {
            return Failure(BuildingPlacementCode.OutsideWorld);
        }

        var obstacles = world.Obstacles;
        for (var index = 0; index < obstacles.Length; index++)
        {
            if (OverlapsArea(footprint, obstacles[index]))
            {
                return Failure(BuildingPlacementCode.StaticObstacleOverlap, index);
            }
        }

        var dynamicFootprints = world.DynamicOccupancy.Snapshot();
        for (var index = 0; index < dynamicFootprints.Length; index++)
        {
            if (OverlapsArea(footprint, dynamicFootprints[index].Bounds))
            {
                return Failure(
                    BuildingPlacementCode.DynamicFootprintOverlap,
                    dynamicFootprints[index].Id.Value);
            }
        }

        for (var unit = 0; unit < units.Count; unit++)
        {
            if (footprint.Expanded(units.Radii[unit] + rules.UnitPadding)
                .Contains(units.Positions[unit]))
            {
                return Failure(BuildingPlacementCode.UnitOverlap, unit);
            }
        }

        var requiredWidth = MovementClearance.ForClass(
            rules.MinimumPassageClass).RequiredWidth;
        if (CreatesNarrowBoundaryGap(world.Bounds, footprint, requiredWidth))
        {
            return Failure(BuildingPlacementCode.InsufficientClearance, -1);
        }

        for (var index = 0; index < obstacles.Length; index++)
        {
            if (CreatesNarrowGap(footprint, obstacles[index], requiredWidth))
            {
                return Failure(BuildingPlacementCode.InsufficientClearance, index);
            }
        }

        for (var index = 0; index < dynamicFootprints.Length; index++)
        {
            if (CreatesNarrowGap(
                    footprint, dynamicFootprints[index].Bounds, requiredWidth))
            {
                return Failure(
                    BuildingPlacementCode.InsufficientClearance,
                    dynamicFootprints[index].Id.Value);
            }
        }

        if (rules.PreserveConnectivity && connectivityGuard is not null)
        {
            var connectivity = connectivityGuard.Evaluate(
                footprint, rules.MinimumPassageClass);
            if (!connectivity.Preserved)
            {
                return Failure(
                    BuildingPlacementCode.DisconnectsNavigation,
                    connectivity.FirstSplitComponentId);
            }
        }

        return new BuildingPlacementResult(
            BuildingPlacementCode.Success, default, -1);
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

    private static BuildingPlacementResult Failure(
        BuildingPlacementCode code,
        int conflictId = -1) =>
        new(code, default, conflictId);
}
