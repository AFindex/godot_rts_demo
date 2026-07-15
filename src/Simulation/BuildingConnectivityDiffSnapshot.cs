namespace RtsDemo.Simulation;

public readonly record struct BuildingConnectivityClassDiff(
    MovementClass MovementClass,
    float NavigationRadius,
    NavigationConnectivitySource BaselineSource,
    int BaselineComponents,
    int CandidateComponents,
    int BlockedCells,
    int SplitComponents,
    int DisconnectedCells,
    bool Preserved);

/// <summary>
/// Engine-independent placement preview. It compares one proposed footprint
/// against all Movement Classes without mutating the source world.
/// </summary>
public sealed class BuildingConnectivityDiffSnapshot
{
    private BuildingConnectivityDiffSnapshot(
        SimRect worldBounds,
        SimRect proposedFootprint,
        BuildingConnectivityClassDiff[] classes,
        ClearanceBakeChunk[] dirtyChunks)
    {
        WorldBounds = worldBounds;
        ProposedFootprint = proposedFootprint;
        Classes = classes;
        DirtyChunks = dirtyChunks;
    }

    public SimRect WorldBounds { get; }
    public SimRect ProposedFootprint { get; }
    public BuildingConnectivityClassDiff[] Classes { get; }
    public ClearanceBakeChunk[] DirtyChunks { get; }
    public bool PreservedForAll => Classes.All(value => value.Preserved);

    public static BuildingConnectivityDiffSnapshot Create(
        NavigationMapSnapshot navigation,
        SimRect proposedFootprint,
        ClearanceBakeSnapshot? clearanceBake = null)
    {
        if (proposedFootprint.Width <= 0f || proposedFootprint.Height <= 0f ||
            !float.IsFinite(proposedFootprint.Min.X) ||
            !float.IsFinite(proposedFootprint.Min.Y) ||
            !float.IsFinite(proposedFootprint.Max.X) ||
            !float.IsFinite(proposedFootprint.Max.Y) ||
            !navigation.WorldBounds.Contains(proposedFootprint.Min) ||
            !navigation.WorldBounds.Contains(proposedFootprint.Max))
        {
            throw new ArgumentException(
                "Proposed footprint must be finite, non-empty and inside the world.",
                nameof(proposedFootprint));
        }

        var world = navigation.CreateWorld();
        var cellSize = clearanceBake?.CellSize ?? 16f;
        var analyzer = new NavigationConnectivityAnalyzer(world, cellSize);
        var classes = new BuildingConnectivityClassDiff[3];
        for (var classIndex = 0; classIndex < classes.Length; classIndex++)
        {
            var movementClass = (MovementClass)classIndex;
            var radius = MovementClearance.ForClass(movementClass).NavigationRadius;
            var baseline = clearanceBake is not null &&
                           clearanceBake.SourceNavigationHash == navigation.StableHash &&
                           clearanceBake.SourceTerrainHash == 0UL &&
                           clearanceBake.IsCompatible(world, cellSize, radius)
                ? clearanceBake.CreateConnectivitySnapshot(movementClass)
                : analyzer.Analyze(radius);
            var candidate = analyzer.Analyze(radius, proposedFootprint);
            var report = NavigationConnectivityComparer.Compare(
                baseline, candidate);
            var blockedCells = 0;
            for (var node = 0; node < baseline.NodeCount; node++)
            {
                blockedCells += baseline.IsWalkable(node) &&
                                !candidate.IsWalkable(node)
                    ? 1
                    : 0;
            }
            classes[classIndex] = new BuildingConnectivityClassDiff(
                movementClass,
                radius,
                baseline.Source,
                baseline.ComponentCount,
                candidate.ComponentCount,
                blockedCells,
                report.SplitComponentCount,
                report.DisconnectedCellCount,
                report.Preserved);
        }

        var dirtyChunks = Array.Empty<ClearanceBakeChunk>();
        if (clearanceBake is not null &&
            clearanceBake.SourceNavigationHash == navigation.StableHash &&
            clearanceBake.SourceTerrainHash == 0UL)
        {
            var dirtyIds = clearanceBake.FindIntersectingChunks(
                proposedFootprint.Expanded(
                    MovementClearance.LargeNavigationRadius));
            dirtyChunks = new ClearanceBakeChunk[dirtyIds.Length];
            for (var index = 0; index < dirtyIds.Length; index++)
            {
                dirtyChunks[index] = clearanceBake.Chunk(dirtyIds[index]);
            }
        }

        return new BuildingConnectivityDiffSnapshot(
            navigation.WorldBounds,
            proposedFootprint,
            classes,
            dirtyChunks);
    }
}
