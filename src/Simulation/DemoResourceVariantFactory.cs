using System.Numerics;

namespace RtsDemo.Simulation;

/// <summary>
/// Deterministic fixture variants shared by the Resource generator and tests.
/// They are deliberately separate from the production demo assets.
/// </summary>
public static class DemoResourceVariantFactory
{
    public static NavigationMapSnapshot CreateNavigationVariant(
        NavigationMapSnapshot source)
    {
        var obstacles = source.Obstacles.ToArray();
        Array.Resize(ref obstacles, obstacles.Length + 1);
        obstacles[^1] = new SimRect(
            new Vector2(360f, 500f), new Vector2(408f, 548f));
        if (!NavigationMapSnapshot.TryCreate(
                source.FormatVersion,
                source.WorldBounds,
                obstacles,
                source.PortalNodes,
                source.PortalEdges,
                source.Chokes,
                out var variant,
                out var validation) || variant is null)
        {
            throw new InvalidOperationException(
                $"Navigation reload fixture is invalid: {validation.FirstError}.");
        }
        return variant;
    }

    public static GameplayProfileCatalogSnapshot CreateGameplayVariant(
        GameplayProfileCatalogSnapshot source)
    {
        var units = source.UnitProfiles.ToArray();
        units[0] = units[0] with
        {
            MaximumSpeed = units[0].MaximumSpeed + 16f
        };
        if (!GameplayProfileCatalogSnapshot.TryCreate(
                source.FormatVersion,
                units,
                source.BuildingProfiles,
                out var variant,
                out var validation) || variant is null)
        {
            throw new InvalidOperationException(
                $"Gameplay reload fixture is invalid: {validation.FirstError}.");
        }
        return variant;
    }
}
