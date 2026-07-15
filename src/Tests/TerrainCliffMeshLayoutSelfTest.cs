using System.Numerics;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Demos.War3;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class TerrainCliffMeshLayoutSelfTest
{
    public static SelfTestResult Run()
    {
        var surfaces = new TerrainSurfaceDefinition[]
        {
            new(0, "low", "Low"),
            new(1, "high", "High")
        };
        var builder = Builder(surfaces);
        builder.SetCell(0, 1, Cell(1, 1));
        var source = builder.Build();
        var first = TerrainCliffMeshLayout.Build(
            source, signature => signature == "BAAA" ? 2 : 0);
        var second = TerrainCliffMeshLayout.Build(
            source, signature => signature == "BAAA" ? 2 : 0);
        var signature = first.Tiles.Length == 1 &&
                        first.Tiles[0].Signature == "BAAA" &&
                        first.Tiles[0].BaseLevel == 0 &&
                        first.Tiles[0].MaximumLevel == 1 &&
                        first.Tiles[0].UpperSurfaceId == 1 &&
                        first.Tiles[0].Variation is >= 0 and < 2;
        var deterministic = first.StableHash == second.StableHash &&
                            first.Tiles.SequenceEqual(second.Tiles);

        var highBuilder = Builder(surfaces);
        highBuilder.SetCell(0, 1, Cell(3, 1));
        var highFallback = TerrainCliffMeshLayout.Build(
            highBuilder.Build(), _ => 1);
        var rejectsTall = highFallback.Tiles.Length == 0 &&
                          highFallback.Diagnostics.UnsupportedHeightTiles == 1;

        var rampBuilder = Builder(surfaces);
        rampBuilder.SetCell(0, 1, new TerrainCell(
            1, 1, TerrainPathing.Ground,
            TerrainCellFlags.Ramp,
            TerrainRampDirection.PositiveX));
        var rampFallback = TerrainCliffMeshLayout.Build(
            rampBuilder.Build(), _ => 1);
        var rejectsRamp = rampFallback.Tiles.Length == 0 &&
                          rampFallback.Diagnostics.RampFallbackTiles == 1;

        var missing = TerrainCliffMeshLayout.Build(source, _ => 0);
        var reportsMissing = missing.Tiles.Length == 0 &&
                             missing.Diagnostics.MissingAssetTiles == 1;

        var catalog = War3ClassicCliffMeshCatalog.LoadDefault();
        var exportedCatalog = catalog.SignatureCount == 64 &&
                              catalog.AssetCount == 94 &&
                              catalog.VariationCount("AAAB") == 2 &&
                              catalog.VariationCount("AABB") == 3;
        var passed = signature && deterministic && rejectsTall &&
                     rejectsRamp && reportsMissing && exportedCatalog;
        return new SelfTestResult(
            passed,
            $"signature={signature}, deterministic={deterministic}, " +
            $"tallFallback={rejectsTall}, rampFallback={rejectsRamp}, " +
            $"missing={reportsMissing}, catalog={exportedCatalog}" +
            $"({catalog.SignatureCount}/{catalog.AssetCount}), " +
            $"hash={first.StableHashText}");
    }

    private static TerrainMapBuilder Builder(
        TerrainSurfaceDefinition[] surfaces) =>
        new(
            new SimRect(Vector2.Zero, new Vector2(2f, 2f)),
            1f,
            1f,
            surfaces,
            Cell(0, 0));

    private static TerrainCell Cell(byte level, ushort surface) =>
        new(
            level,
            surface,
            TerrainPathing.Ground,
            TerrainCellFlags.Buildable);
}
