using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct ClearanceClassPreview(
    MovementClass Class,
    float NavigationRadius,
    float RequiredWidth,
    int TraversablePortalEdges,
    int ConnectedComponents,
    int WalkableCells,
    int LargestComponentCells);

public readonly record struct PortalClearancePreview(
    int EdgeIndex,
    Vector2 From,
    Vector2 To,
    float Width,
    bool SmallTraversable,
    bool MediumTraversable,
    bool LargeTraversable)
{
    public string ClassLabel =>
        $"{(SmallTraversable ? 'S' : '-')}" +
        $"{(MediumTraversable ? 'M' : '-')}" +
        $"{(LargeTraversable ? 'L' : '-')}";
}

public readonly record struct BuildingClearancePreview(
    int ProfileId,
    string Name,
    BuildingFootprintClass Class,
    Vector2 Size,
    float RequiredPassageWidth);

public sealed class ClearancePreviewSnapshot
{
    private ClearancePreviewSnapshot(
        SimRect worldBounds,
        SimRect[] obstacles,
        ClearanceClassPreview[] classes,
        NavigationConnectivitySnapshot[] connectivity,
        PortalClearancePreview[] portals,
        BuildingClearancePreview[] buildings)
    {
        WorldBounds = worldBounds;
        Obstacles = obstacles;
        Classes = classes;
        Connectivity = connectivity;
        Portals = portals;
        Buildings = buildings;
    }

    public SimRect WorldBounds { get; }
    public SimRect[] Obstacles { get; }
    public ClearanceClassPreview[] Classes { get; }
    public NavigationConnectivitySnapshot[] Connectivity { get; }
    public PortalClearancePreview[] Portals { get; }
    public BuildingClearancePreview[] Buildings { get; }

    public static ClearancePreviewSnapshot Create(
        NavigationMapSnapshot navigation,
        GameplayProfileCatalogSnapshot profiles)
    {
        var classes = new ClearanceClassPreview[3];
        var connectivity = new NavigationConnectivitySnapshot[3];
        var analyzer = new NavigationConnectivityAnalyzer(
            navigation.CreateWorld());
        for (var classIndex = 0; classIndex < classes.Length; classIndex++)
        {
            var movementClass = (MovementClass)classIndex;
            var clearance = MovementClearance.ForClass(movementClass);
            var traversable = 0;
            var edges = navigation.PortalEdges;
            for (var edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
            {
                if (clearance.FitsWidth(edges[edgeIndex].Width))
                {
                    traversable++;
                }
            }

            var topology = analyzer.Analyze(clearance.NavigationRadius);
            connectivity[classIndex] = topology;
            var walkableCells = 0;
            var largestComponentCells = 0;
            var components = topology.Components;
            for (var componentIndex = 0;
                 componentIndex < components.Length;
                 componentIndex++)
            {
                walkableCells += components[componentIndex].CellCount;
                largestComponentCells = Math.Max(
                    largestComponentCells,
                    components[componentIndex].CellCount);
            }

            classes[classIndex] = new ClearanceClassPreview(
                movementClass,
                clearance.NavigationRadius,
                clearance.RequiredWidth,
                traversable,
                topology.ComponentCount,
                walkableCells,
                largestComponentCells);
        }

        var portalEdges = navigation.PortalEdges;
        var portalNodes = navigation.PortalNodes;
        var portals = new PortalClearancePreview[portalEdges.Length];
        for (var index = 0; index < portals.Length; index++)
        {
            var edge = portalEdges[index];
            portals[index] = new PortalClearancePreview(
                index,
                portalNodes[edge.FromNode].Position,
                portalNodes[edge.ToNode].Position,
                edge.Width,
                MovementClearance.ForClass(MovementClass.Small).FitsWidth(edge.Width),
                MovementClearance.ForClass(MovementClass.Medium).FitsWidth(edge.Width),
                MovementClearance.ForClass(MovementClass.Large).FitsWidth(edge.Width));
        }

        var sourceBuildings = profiles.BuildingProfiles;
        var buildings = new BuildingClearancePreview[sourceBuildings.Length];
        for (var index = 0; index < buildings.Length; index++)
        {
            var profile = sourceBuildings[index];
            buildings[index] = new BuildingClearancePreview(
                profile.Id,
                profile.Name,
                profile.FootprintClass,
                profile.Size,
                MovementClearance.ForClass(
                    profile.MinimumPassageClass).RequiredWidth);
        }

        return new ClearancePreviewSnapshot(
            navigation.WorldBounds,
            navigation.Obstacles.ToArray(),
            classes,
            connectivity,
            portals,
            buildings);
    }
}
