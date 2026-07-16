using RtsDemo.Demos.ThreeD;
using RtsDemo.Demos.War3;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class War3TerrainStressPresetCatalogSelfTest
{
    public static SelfTestResult Run()
    {
        var presets = War3TerrainStressPresetCatalog.Presets.ToArray();
        var count = presets.Length == 11;
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
        var rampCatalog = War3ClassicRampMeshCatalog.LoadDefault();
        var rampLayouts = presets.ToDictionary(
            preset => preset.Id,
            preset => TerrainClassicRampLayout.Build(
                preset.Terrain, rampCatalog.Contains));
        string[] cliffStressIds =
        [
            "interlocked-ridges",
            "nested-archipelago",
            "serpentine-canyons",
            "signature-matrix",
            "material-weave"
        ];
        var dense = cliffStressIds.All(id =>
        {
            var layout = layouts[id];
            return
            layout.Diagnostics.CandidateTiles >= 100 &&
            layout.Diagnostics.SelectedTiles ==
                layout.Diagnostics.CandidateTiles &&
            layout.Diagnostics.UnsupportedHeightTiles == 0 &&
            layout.Diagnostics.RampFallbackTiles == 0;
        });
        var rampPreset = presets.Single(preset =>
            preset.Id == "ramp-gallery");
        var rampDirections = rampPreset.Terrain.Cells.ToArray()
            .Where(static cell => cell.IsRamp)
            .Select(static cell => cell.RampDirection)
            .Distinct()
            .ToHashSet();
        var ramps = rampDirections.SetEquals(
                        Enum.GetValues<TerrainRampDirection>()
                            .Where(static direction =>
                                direction != TerrainRampDirection.None)) &&
                    rampLayouts["ramp-gallery"].Diagnostics.MappedRampCells ==
                    rampPreset.Terrain.Cells.ToArray().Count(
                        static cell => cell.IsRamp) &&
                    rampLayouts["ramp-gallery"].Diagnostics
                        .SelectedTransitions > 0;
        var rollingPresets = presets
            .Where(preset => preset.Terrain.HasFineHeight)
            .ToArray();
        var relief = rollingPresets.Length == 5 &&
                     rollingPresets.All(preset =>
                         preset.Terrain.HeightPointCount ==
                         (preset.Terrain.Columns + 1) *
                         (preset.Terrain.Rows + 1) &&
                         preset.Terrain.MaximumFineHeight -
                         preset.Terrain.MinimumFineHeight >= 14f) &&
                     rollingPresets.Single(preset =>
                             preset.Id == "rolling-ramp-pass")
                         .Terrain.Cells.ToArray()
                         .Any(static cell => cell.IsRamp) &&
                     rollingPresets.Single(preset =>
                             preset.Id == "rolling-foothills")
                         .Terrain.Cells.ToArray()
                         .All(static cell =>
                             cell.CliffLevel == 0 && !cell.IsRamp) &&
                     rampLayouts["rolling-cliff-ramp"].Diagnostics
                         .SelectedTransitions == 4 &&
                     rampLayouts["rolling-ramp-fortress"].Diagnostics
                         .SelectedTransitions >= 12 &&
                     rollingPresets.Single(preset =>
                             preset.Id == "relief-ridge-basin")
                         .Terrain.Cells.ToArray()
                         .All(static cell =>
                             cell.CliffLevel == 0 && !cell.IsRamp) &&
                     layouts["rolling-cliff-ramp"].Diagnostics
                         .SelectedTiles > 0;
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
                     ramps && relief && signatureCoverage;
        return new SelfTestResult(
            passed,
            $"count={count}, unique={unique}, bindings={bindings}, " +
            $"materials={allMaterials}, dense={dense}, " +
            $"ramps={ramps}({rampDirections.Count}/" +
            $"{rampLayouts["ramp-gallery"].Diagnostics}), " +
            $"relief={relief}, " +
            $"signatures={signatureCoverage}({signatures.Count}), " +
            $"candidates=[{candidates}]");
    }
}
