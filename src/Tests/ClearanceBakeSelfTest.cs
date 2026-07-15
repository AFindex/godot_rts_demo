using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class ClearanceBakeSelfTest
{
    public static SelfTestResult Run(
        NavigationMapSnapshot? navigation = null,
        ClearanceBakeSnapshot? loadedBake = null)
    {
        navigation ??= DemoMapDefinition.CreateSnapshot();
        var first = ClearanceBakeSnapshot.Build(navigation);
        var second = ClearanceBakeSnapshot.Build(navigation);
        var terrain = CreateFlatTerrain(navigation.WorldBounds);
        var terrainBake = ClearanceBakeSnapshot.Build(navigation, terrain);
        var deserialized = ClearanceBakeSnapshot.TryDeserialize(
            first.CanonicalBytes.Span,
            out var roundTrip,
            out var validation);
        var truncated = first.CanonicalBytes.Span[..^1].ToArray();
        var rejectedTruncated = !ClearanceBakeSnapshot.TryDeserialize(
            truncated,
            out _,
            out var truncatedValidation) &&
            truncatedValidation.FirstError ==
                ClearanceBakeErrorCode.InvalidLayerPayload;
        loadedBake ??= first;
        var world = navigation.CreateWorld();
        var provider = new GridPathProvider(
            world, cellSize: first.CellSize, staticBake: loadedBake);
        var staticPath = provider.FindPath(
            new Vector2(100f, 353f),
            new Vector2(1150f, 353f),
            MovementClearance.LargeNavigationRadius);
        world.DynamicOccupancy.Place(
            new SimRect(new Vector2(280f, 90f), new Vector2(312f, 122f)));
        var dynamicPath = provider.FindPath(
            new Vector2(100f, 353f),
            new Vector2(1150f, 353f),
            MovementClearance.LargeNavigationRadius);
        var affectedChunks = first.FindIntersectingChunks(
            new SimRect(new Vector2(540f, 285f), new Vector2(720f, 420f)));

        var layersValid = first.Layers.Length == 3;
        for (var classIndex = 0; classIndex < first.Layers.Length; classIndex++)
        {
            var layer = first.Layer((MovementClass)classIndex);
            layersValid &= layer.NodeCount == first.NodeCount &&
                           layer.ComponentCount > 0;
            layersValid &= first.CreateConnectivitySnapshot(
                (MovementClass)classIndex).Source ==
                NavigationConnectivitySource.StaticBake;
        }

        var passed = first.StableHash == second.StableHash &&
                     first.CanonicalBytes.Span.SequenceEqual(
                         second.CanonicalBytes.Span) &&
                     deserialized && validation.IsValid &&
                     roundTrip is not null &&
                     roundTrip.StableHash == first.StableHash &&
                     loadedBake.SourceNavigationHash == navigation.StableHash &&
                     loadedBake.SourceTerrainHash == 0UL &&
                     loadedBake.StableHash == first.StableHash &&
                     terrainBake.SourceNavigationHash == navigation.StableHash &&
                     terrainBake.SourceTerrainHash == terrain.StableHash &&
                     terrainBake.StableHash != first.StableHash &&
                     terrainBake.IsCompatible(
                         navigation.CreateWorld(terrain),
                         terrainBake.CellSize,
                         MovementClearance.SmallNavigationRadius) &&
                     !terrainBake.IsCompatible(
                         navigation.CreateWorld(),
                         terrainBake.CellSize,
                         MovementClearance.SmallNavigationRadius) &&
                     layersValid && rejectedTruncated &&
                     staticPath.Length >= 2 && dynamicPath.Length >= 2 &&
                     affectedChunks.Length > 0 &&
                     affectedChunks.All(id =>
                         (uint)id < (uint)first.ChunkCount);
        return new SelfTestResult(
            passed,
            $"format={first.FormatVersion}, hash={first.StableHashText}, " +
            $"source={first.SourceNavigationHashText}, " +
            $"terrain={terrainBake.SourceTerrainHashText}, " +
            $"bytes={first.CanonicalBytes.Length}, " +
            $"grid={first.Columns}x{first.Rows}, " +
            $"chunks={first.ChunkColumns}x{first.ChunkRows}, " +
            $"layers={first.Layers.Length}, " +
            $"paths={staticPath.Length}/{dynamicPath.Length}, " +
            $"affectedChunks={affectedChunks.Length}, " +
            $"invalid={truncatedValidation.FirstError}");
    }

    private static TerrainMapSnapshot CreateFlatTerrain(SimRect bounds)
    {
        TerrainSurfaceDefinition[] surfaces = [new(0, "test", "Test")];
        var cell = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        return new TerrainMapBuilder(bounds, 32f, 48f, surfaces, cell).Build();
    }
}
