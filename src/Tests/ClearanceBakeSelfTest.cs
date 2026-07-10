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
                     loadedBake.StableHash == first.StableHash &&
                     layersValid && rejectedTruncated &&
                     staticPath.Length >= 2 && dynamicPath.Length >= 2 &&
                     affectedChunks.Length > 0 &&
                     affectedChunks.All(id =>
                         (uint)id < (uint)first.ChunkCount);
        return new SelfTestResult(
            passed,
            $"format={first.FormatVersion}, hash={first.StableHashText}, " +
            $"source={first.SourceNavigationHashText}, " +
            $"bytes={first.CanonicalBytes.Length}, " +
            $"grid={first.Columns}x{first.Rows}, " +
            $"chunks={first.ChunkColumns}x{first.ChunkRows}, " +
            $"layers={first.Layers.Length}, " +
            $"paths={staticPath.Length}/{dynamicPath.Length}, " +
            $"affectedChunks={affectedChunks.Length}, " +
            $"invalid={truncatedValidation.FirstError}");
    }
}
