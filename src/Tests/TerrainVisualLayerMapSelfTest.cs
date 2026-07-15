using System.Numerics;
using RtsDemo.Demos.ThreeD;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class TerrainVisualLayerMapSelfTest
{
    public static SelfTestResult Run()
    {
        TerrainSurfaceDefinition[] surfaces = [new(0, "soil", "Soil")];
        var source = new TerrainMapBuilder(
            new SimRect(Vector2.Zero, Vector2.One),
            1f,
            1f,
            surfaces,
            new TerrainCell(
                0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable))
            .Build();

        // Row-major tilepoints: BL=0, BR=1, TL=3, TR=2.
        var fourCorner = TerrainVisualLayerMap.FromPoints(
            source,
            new byte[] { 0, 1, 3, 2 },
            new byte[] { 16, 1, 2, 3 });
        var cell = fourCorner.Cell(0, 0);
        var masks = cell.LayerMask(0) == 15 &&
                    cell.LayerMask(1) == 1 &&
                    cell.LayerMask(2) == 4 &&
                    cell.LayerMask(3) == 8 &&
                    cell.PackedLayerMasks == 0x841F &&
                    cell.BaseVariation == 16;

        var uniform = TerrainVisualLayerMap.FromPoints(
            source,
            new byte[] { 2, 2, 2, 2 },
            new byte[] { 0, 0, 0, 0 });
        var uniformCell = uniform.Cell(0, 0);
        var uniformMask = uniformCell.LayerMask(0) == 0 &&
                          uniformCell.LayerMask(1) == 0 &&
                          uniformCell.LayerMask(2) == 15 &&
                          uniformCell.LayerMask(3) == 0;
        var hashes = fourCorner.SourceTerrainHash == source.StableHashText &&
                     uniform.SourceTerrainHash == source.StableHashText &&
                     fourCorner.StableHashText != uniform.StableHashText;
        var rejectsInvalidLayer = false;
        try
        {
            TerrainVisualLayerMap.FromPoints(
                source,
                new byte[] { 0, 1, 2, 4 },
                new byte[] { 0, 0, 0, 0 });
        }
        catch (ArgumentOutOfRangeException)
        {
            rejectsInvalidLayer = true;
        }

        var roundTrip = TerrainVisualLayerMap.TryDeserialize(
                            fourCorner.CanonicalBytes.Span,
                            source,
                            out var restored,
                            out var roundTripValidation) &&
                        roundTripValidation.IsValid &&
                        restored is not null &&
                        restored.StableHash == fourCorner.StableHash &&
                        restored.CanonicalBytes.Span.SequenceEqual(
                            fourCorner.CanonicalBytes.Span);
        var resource = TerrainVisualLayerResourceConverter.FromMap(fourCorner);
        var resourceRoundTrip = TerrainVisualLayerResourceConverter.TryConvert(
                                    resource,
                                    source,
                                    out var resourceMap,
                                    out var resourceValidation) &&
                                resourceValidation.IsValid &&
                                resourceMap?.StableHash == fourCorner.StableHash;
        resource.VisualHash = "0000000000000000";
        var rejectsDeclaredHash =
            !TerrainVisualLayerResourceConverter.TryConvert(
                resource,
                source,
                out _,
                out var declaredHashValidation) &&
            declaredHashValidation.FirstError ==
                TerrainVisualLayerErrorCode.DeclaredHashMismatch;

        var otherSource = new TerrainMapBuilder(
            new SimRect(Vector2.Zero, Vector2.One),
            1f,
            1f,
            new TerrainSurfaceDefinition[] { new(0, "other", "Other") },
            new TerrainCell(
                0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable))
            .Build();
        var rejectsOtherTerrain = !TerrainVisualLayerMap.TryDeserialize(
                                      fourCorner.CanonicalBytes.Span,
                                      otherSource,
                                      out _,
                                      out var sourceValidation) &&
                                  sourceValidation.FirstError ==
                                      TerrainVisualLayerErrorCode.SourceTerrainMismatch;

        var distributionSource = new TerrainMapBuilder(
            new SimRect(Vector2.Zero, new Vector2(64f, 64f)),
            1f,
            1f,
            surfaces,
            new TerrainCell(
                0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable))
            .Build();
        var generated = TerrainVisualLayerMap.FromTerrain(
            distributionSource, new TestDualGridProvider());
        var variationCounts = new int[TerrainVisualLayerMap.MaximumVariation + 1];
        for (var row = 0; row < generated.PointRows; row++)
        {
            for (var column = 0; column < generated.PointColumns; column++)
                variationCounts[generated.PointVariation(column, row)]++;
        }
        var weightedVariations = variationCounts[0] > variationCounts[16] &&
                                 variationCounts[16] > variationCounts[15] &&
                                 variationCounts.Sum() == generated.PointCount;

        var passed = masks && uniformMask && hashes && rejectsInvalidLayer &&
                     roundTrip && resourceRoundTrip && rejectsDeclaredHash &&
                     rejectsOtherTerrain && weightedVariations;
        return new SelfTestResult(
            passed,
            $"masks={masks}, uniform={uniformMask}, hashes={hashes}, " +
            $"validation={rejectsInvalidLayer}, roundTrip={roundTrip}, " +
            $"resource={resourceRoundTrip}/{rejectsDeclaredHash}, " +
            $"sourceMismatch={rejectsOtherTerrain}, weighted={weightedVariations}" +
            $"(v0={variationCounts[0]},v16={variationCounts[16]}," +
            $"v15={variationCounts[15]}), packed=0x{cell.PackedLayerMasks:X4}");
    }

    private sealed class TestDualGridProvider :
        IRts3DTerrainDualGridMaterialProvider
    {
        private readonly Godot.Material _material = new Godot.StandardMaterial3D();

        public bool DualGridEnabled => true;
        public Godot.Material SurfaceMaterial(TerrainSurfaceDefinition surface) =>
            _material;
        public Godot.Material CliffMaterial(
            TerrainSurfaceDefinition upperSurface) =>
            _material;
        public Godot.Material DualGridSurfaceMaterial() => _material;
        public bool TryGetDualGridLayer(
            TerrainSurfaceDefinition surface,
            out int layer)
        {
            layer = 0;
            return true;
        }
    }
}
