namespace RtsDemo.Simulation;

public enum MovementClass : byte
{
    Small,
    Medium,
    Large
}

public readonly record struct MovementClearanceProfile(
    MovementClass Class,
    float PhysicalRadius,
    float NavigationRadius)
{
    public float RequiredWidth => NavigationRadius * 2f + 2f;

    public bool FitsWidth(float width) => width >= RequiredWidth;
}

/// <summary>
/// Converts arbitrary physical radii to the three navigation-clearance tiers
/// used by Grid and Portal planning. Navigation radii round upward so a path
/// accepted for a tier is safe for every physical unit in that tier.
/// </summary>
public static class MovementClearance
{
    public const float SmallNavigationRadius = 6f;
    public const float MediumNavigationRadius = 8f;
    public const float LargeNavigationRadius = 12f;

    public static MovementClearanceProfile FromPhysicalRadius(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius), "Unit radius must be finite and positive.");
        }

        if (radius <= SmallNavigationRadius)
        {
            return new MovementClearanceProfile(
                MovementClass.Small, radius, SmallNavigationRadius);
        }

        if (radius <= MediumNavigationRadius)
        {
            return new MovementClearanceProfile(
                MovementClass.Medium, radius, MediumNavigationRadius);
        }

        return new MovementClearanceProfile(
            MovementClass.Large,
            radius,
            MathF.Max(LargeNavigationRadius, radius));
    }

    public static MovementClearanceProfile ForClass(MovementClass value) =>
        value switch
        {
            MovementClass.Small => new(
                value, SmallNavigationRadius, SmallNavigationRadius),
            MovementClass.Medium => new(
                value, MediumNavigationRadius, MediumNavigationRadius),
            MovementClass.Large => new(
                value, LargeNavigationRadius, LargeNavigationRadius),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
}
