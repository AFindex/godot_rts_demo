using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ClearanceIncrementalSelfTest
{
    public static readonly SimRect ChangedArea = new(
        new Vector2(320f, 280f),
        new Vector2(432f, 360f));

    public static SelfTestResult Run(
        NavigationMapSnapshot? navigation = null,
        ClearanceBakeSnapshot? clearanceBake = null)
    {
        navigation ??= DemoMapDefinition.CreateSnapshot();
        clearanceBake ??= ClearanceBakeSnapshot.Build(navigation);
        var world = navigation.CreateWorld();
        var movementClass = MovementClass.Large;
        var radius = MovementClearance.ForClass(movementClass).NavigationRadius;
        var updater = new IncrementalNavigationConnectivityUpdater(
            world, clearanceBake);
        var baseline = clearanceBake.CreateConnectivitySnapshot(movementClass);
        var analyzer = new NavigationConnectivityAnalyzer(
            world, clearanceBake.CellSize);
        var fullBaseline = analyzer.Analyze(radius);
        var baselineExact = TopologyEqual(baseline, fullBaseline);
        var freshBake = ClearanceBakeSnapshot.Build(
            navigation, clearanceBake.CellSize, clearanceBake.ChunkSizeCells);
        var bakeMatchesCurrent =
            freshBake.StableHash == clearanceBake.StableHash;

        var footprintId = world.DynamicOccupancy.Place(ChangedArea);
        var added = updater.Update(baseline, ChangedArea);
        var fullAdded = analyzer.Analyze(radius);

        var removed = world.DynamicOccupancy.Remove(
            footprintId, out var removedArea);
        var restored = updater.Update(added.Snapshot, removedArea);
        var fullRestored = analyzer.Analyze(radius);

        var exactAdd = TopologyEqual(added.Snapshot, fullAdded);
        var exactRemove = TopologyEqual(restored.Snapshot, fullRestored);

        var pathWorld = navigation.CreateWorld();
        var pathProvider = new GridPathProvider(
            pathWorld, clearanceBake.CellSize, clearanceBake);
        var pathStart = new Vector2(100f, 353f);
        var pathEnd = new Vector2(1150f, 353f);
        var initialPath = pathProvider.FindPath(pathStart, pathEnd, radius);
        pathWorld.DynamicOccupancy.Place(ChangedArea);
        var updatedPath = pathProvider.FindPath(pathStart, pathEnd, radius);
        var runtimeUsesIncremental = initialPath.Length >= 2 &&
                                     updatedPath.Length >= 2 &&
                                     pathProvider.IncrementalConnectivityUpdates == 1 &&
                                     pathProvider.IncrementalConnectivityResampledCells ==
                                         added.ResampledCells;
        var rejectsMultipleRevisions = RejectsMultipleRevisions(
            navigation, clearanceBake, baseline);
        var bounded = added.DirtyChunkIds.Length > 0 &&
                      added.ResampledCells < added.Snapshot.NodeCount &&
                      added.ResampledRatio < 0.5f;
        var passed = removed && exactAdd && exactRemove && bounded &&
                     runtimeUsesIncremental && rejectsMultipleRevisions &&
                     added.ChangedCells > 0 && restored.ChangedCells > 0 &&
                     added.DirtyChunkIds.SequenceEqual(restored.DirtyChunkIds) &&
                     added.Snapshot.Source ==
                         NavigationConnectivitySource.IncrementalRuntimeAnalysis;
        return new SelfTestResult(
            passed,
            $"chunks={string.Join(',', added.DirtyChunkIds)}, " +
            $"resampled={added.ResampledCells}/{added.Snapshot.NodeCount}, " +
            $"ratio={added.ResampledRatio:P1}, changed={added.ChangedCells}, " +
            $"runtime={runtimeUsesIncremental}, multiReject={rejectsMultipleRevisions}, " +
            $"baselineExact={baselineExact}, bakeCurrent={bakeMatchesCurrent}, " +
            $"loaded={clearanceBake.StableHashText}, fresh={freshBake.StableHashText}, " +
            $"addExact={exactAdd}, removeExact={exactRemove}");
    }

    private static bool RejectsMultipleRevisions(
        NavigationMapSnapshot navigation,
        ClearanceBakeSnapshot clearanceBake,
        NavigationConnectivitySnapshot baseline)
    {
        var world = navigation.CreateWorld();
        world.DynamicOccupancy.Place(ChangedArea);
        world.DynamicOccupancy.Place(new SimRect(
            new Vector2(450f, 500f), new Vector2(482f, 532f)));
        try
        {
            new IncrementalNavigationConnectivityUpdater(world, clearanceBake)
                .Update(baseline, ChangedArea);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool TopologyEqual(
        NavigationConnectivitySnapshot incremental,
        NavigationConnectivitySnapshot full)
    {
        if (incremental.NodeCount != full.NodeCount ||
            incremental.ComponentCount != full.ComponentCount ||
            incremental.WorldRevision != full.WorldRevision)
        {
            return false;
        }
        for (var node = 0; node < incremental.NodeCount; node++)
        {
            if (incremental.IsWalkable(node) != full.IsWalkable(node) ||
                incremental.ComponentAt(node) != full.ComponentAt(node))
            {
                return false;
            }
        }
        return true;
    }
}
