using RtsDemo.Demos.ThreeD;
using RtsDemo.Demos.War3;

namespace RtsDemo.Tests;

public static class War3TerrainStressPresetCatalogSelfTest
{
    public static SelfTestResult Run()
    {
        var presets = War3TerrainStressPresetCatalog.Presets.ToArray();
        var count = presets.Length == 5;
        var unique = presets.Select(preset => preset.Id).Distinct().Count() ==
                     presets.Length &&
                     presets.Select(preset => preset.Terrain.StableHash)
                         .Distinct().Count() == presets.Length &&
                     presets.Select(preset => preset.VisualLayers.StableHash)
                         .Distinct().Count() == presets.Length;
        var bindings = presets.All(preset =>
            preset.VisualLayers.SourceTerrainHash ==
                preset.Terrain.StableHashText &&
            preset.VisualLayers.CellColumns == preset.Terrain.Columns &&
            preset.VisualLayers.CellRows == preset.Terrain.Rows);
        var allMaterials = presets.All(preset =>
        {
            var layers = new HashSet<byte>();
            for (var row = 0; row < preset.VisualLayers.PointRows; row++)
            for (var column = 0;
                 column < preset.VisualLayers.PointColumns;
                 column++)
            {
                layers.Add(preset.VisualLayers.PointLayer(column, row));
            }
            return layers.SetEquals([0, 1, 2, 3]);
        });
        var layouts = presets.ToDictionary(
            preset => preset.Id,
            preset => TerrainCliffMeshLayout.Build(
                preset.Terrain, _ => 1));
        var dense = layouts.Values.All(layout =>
            layout.Diagnostics.CandidateTiles >= 100 &&
            layout.Diagnostics.SelectedTiles ==
                layout.Diagnostics.CandidateTiles &&
            layout.Diagnostics.UnsupportedHeightTiles == 0 &&
            layout.Diagnostics.RampFallbackTiles == 0);
        var signatureLayout = layouts["signature-matrix"];
        var signatures = signatureLayout.Tiles.ToArray()
            .Select(tile => tile.Signature)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var signatureCoverage = signatures.Count == 64;
        var candidates = string.Join(",",
            presets.Select(preset =>
                $"{preset.Id}:{layouts[preset.Id].Diagnostics.CandidateTiles}"));
        var passed = count && unique && bindings && allMaterials && dense &&
                     signatureCoverage;
        return new SelfTestResult(
            passed,
            $"count={count}, unique={unique}, bindings={bindings}, " +
            $"materials={allMaterials}, dense={dense}, " +
            $"signatures={signatureCoverage}({signatures.Count}), " +
            $"candidates=[{candidates}]");
    }
}
