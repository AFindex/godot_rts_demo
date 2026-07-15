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
        var seam = TerrainClassicCliffSeamMap.Build(source, first.Tiles.ToArray());
        var quarterOwnership =
            seam.CoveredClassicTiles == 1 &&
            seam.CoveredGroundQuadrants == 1 &&
            seam.CoveredFootprintQuadrants == 4 &&
            seam.GroundQuadrantMask(0, 0) == 0 &&
            seam.GroundQuadrantMask(1, 0) == 0 &&
            seam.GroundQuadrantMask(0, 1) ==
                TerrainClassicCliffSeamMap.BottomRight &&
            seam.GroundQuadrantMask(1, 1) == 0 &&
            seam.ClassicFootprintMask(0, 0) ==
                TerrainClassicCliffSeamMap.TopRight &&
            seam.ClassicFootprintMask(1, 0) ==
                TerrainClassicCliffSeamMap.TopLeft &&
            seam.ClassicFootprintMask(0, 1) ==
                TerrainClassicCliffSeamMap.BottomRight &&
            seam.ClassicFootprintMask(1, 1) ==
                TerrainClassicCliffSeamMap.BottomLeft;
        var sourceLayers = Enumerable.Repeat((byte)1, 9).ToArray();
        var sourceVariations = new byte[9];
        var sourceVisual = TerrainVisualLayerMap.FromPoints(
            source, sourceLayers, sourceVariations);
        var seamVisual = seam.BuildGroundTransitionMap(
            source, sourceVisual, _ => 0, out var changedPoints);
        var groundPriority = changedPoints == 1 &&
                             seamVisual.PointLayer(1, 1) == 0 &&
                             seamVisual.PointLayer(0, 0) == 1 &&
                             seamVisual.PointLayer(2, 2) == 1 &&
                             sourceVisual.PointLayer(1, 1) == 1 &&
                             seamVisual.StableHash != sourceVisual.StableHash;

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

        var cliffOriginsScaled = true;
        for (byte level = 0; level <= 15; level++)
        {
            var tile = new TerrainClassicCliffTile(
                0, 0, "BAAA", 0, level, (byte)(level + 1), 1);
            var origin = Rts3DTerrainPresenter.ClassicCliffOrigin(source, tile);
            var expectedY = SimPlane3DTransform.ToWorldLength(
                level * source.CliffLevelHeight);
            cliffOriginsScaled &= MathF.Abs(origin.Y - expectedY) < 0.00001f;
        }
        var blendCorners = Rts3DTerrainPresenter.ClassicCliffBlendCorners(
            source,
            first.Tiles[0],
            surface => surface.Id == 1 ? 1f : 0f);
        var blendCornerOrder =
            MathF.Abs(blendCorners.R - 1f) < 0.00001f &&
            MathF.Abs(blendCorners.G) < 0.00001f &&
            MathF.Abs(blendCorners.B) < 0.00001f &&
            MathF.Abs(blendCorners.A) < 0.00001f;
        var groundDepthBias = MathF.Abs(
            Rts3DTerrainPresenter.ClassicCliffGroundDepthBias(1.2f) -
            0.003f) < 0.00001f;

        var catalog = War3ClassicCliffMeshCatalog.LoadDefault();
        var exportedCatalog = catalog.SignatureCount == 64 &&
                              catalog.AssetCount == 94 &&
                              catalog.VariationCount("AAAB") == 2 &&
                              catalog.VariationCount("AABB") == 3;
        var passed = signature && deterministic && quarterOwnership &&
                     groundPriority && rejectsTall &&
                     rejectsRamp && reportsMissing && exportedCatalog &&
                     cliffOriginsScaled && blendCornerOrder &&
                     groundDepthBias;
        return new SelfTestResult(
            passed,
            $"signature={signature}, deterministic={deterministic}, " +
            $"quarterOwnership={quarterOwnership}, groundPriority={groundPriority}, " +
            $"tallFallback={rejectsTall}, rampFallback={rejectsRamp}, " +
            $"missing={reportsMissing}, catalog={exportedCatalog}" +
            $"({catalog.SignatureCount}/{catalog.AssetCount}), " +
            $"cliffOriginsScaled={cliffOriginsScaled}, " +
            $"blendCornerOrder={blendCornerOrder}, " +
            $"groundDepthBias={groundDepthBias}, " +
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
