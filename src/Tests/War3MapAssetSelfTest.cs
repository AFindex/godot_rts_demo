using System.Numerics;
using RtsDemo.Simulation;
using War3Rts;
using War3Rts.Maps;

namespace RtsDemo.Tests;

public static class War3MapAssetSelfTest
{
    public static SelfTestResult Run()
    {
        try
        {
            var catalog = War3MapCatalog.Enumerate();
            var defaultEntry = catalog.SingleOrDefault(value =>
                value.Manifest.Id == War3MapCodec.DefaultMapId);
            var catalogError = defaultEntry is null
                ? "default entry is missing"
                : string.Empty;
            if (defaultEntry is null ||
                !War3MapCatalog.TryLoadRuntime(
                    defaultEntry, out var defaultMap, out catalogError) ||
                defaultMap is null)
            {
                return new SelfTestResult(false,
                    $"default catalog map failed: {catalogError}");
            }

            var legacyTerrain = War3HumanBattlefield.Create(
                War3HumanScenario.WorldBounds,
                War3HumanScenario.TerrainCellSize,
                War3HumanScenario.TerrainCliffHeight);
            var resources = defaultMap.Resources.ToArray();
            var navigation = defaultMap.CreateNavigation();
            var defaultCompatible =
                defaultMap.Terrain.StableHash == legacyTerrain.StableHash &&
                defaultMap.PlayerSpawn == War3HumanScenario.PlayerHome &&
                defaultMap.EnemySpawn == War3HumanScenario.EnemyHome &&
                resources.Length == War3HumanScenario.ExpectedResourceNodeCount &&
                navigation.Obstacles.Length == resources.Length;

            var asset = War3MapCodec.CreateNew(
                "roundtrip_self_test", "Round-trip Self Test", 48, 32);
            if (!War3MapEditDocument.TryCreate(
                    asset, out var document, out var createValidation) ||
                document is null)
            {
                return new SelfTestResult(false, createValidation.Summary);
            }
            var before = document.CaptureJson();
            var surfaceChanged = document.Apply(
                new Vector2(640f, 480f),
                new War3MapBrush(
                    War3MapTool.Surface, 2, 1f, War3BrushShape.Circle,
                    SurfaceId: 2), 1) > 0;
            var heightChanged = document.Apply(
                new Vector2(640f, 480f),
                new War3MapBrush(
                    War3MapTool.RaiseHeight, 3, 6f, War3BrushShape.Diamond), 2) > 0;
            var cliffChanged = document.Apply(
                new Vector2(352f, 336f),
                new War3MapBrush(
                    War3MapTool.CliffLevel, 0, 1f, War3BrushShape.Circle,
                    CliffLevel: 1), 3) > 0;
            var rampChanged = document.Apply(
                new Vector2(320f, 336f),
                new War3MapBrush(
                    War3MapTool.PlaceRamp, 0, 1f, War3BrushShape.Circle,
                    CliffLevel: 0,
                    RampDirection: TerrainRampDirection.PositiveX), 4) > 0;
            var pathingChanged = document.Apply(
                new Vector2(800f, 600f),
                new War3MapBrush(
                    War3MapTool.GroundPathing, 1, 1f, War3BrushShape.Square,
                    Enabled: false), 5) > 0;
            var buildableChanged = document.Apply(
                new Vector2(900f, 600f),
                new War3MapBrush(
                    War3MapTool.Buildable, 1, 1f, War3BrushShape.Circle,
                    Enabled: false), 6) > 0;
            var after = document.CaptureJson();
            var oneStrokePayload = before != after &&
                War3MapCodec.TryDeserialize(before, out var beforeAsset, out _) &&
                beforeAsset is not null &&
                War3MapEditDocument.TryCreate(
                    beforeAsset, out var restoredDocument, out _) &&
                restoredDocument is not null &&
                restoredDocument.CaptureJson() == before;

            var editedAsset = document.CaptureAsset();
            var expanded = War3MapCodec.TryExpand(
                editedAsset, out var editedMap, out var editedValidation) &&
                editedMap is not null;
            var editedTerrain = editedMap?.Terrain;
            var editingExact = expanded &&
                surfaceChanged && heightChanged && cliffChanged && rampChanged &&
                pathingChanged && buildableChanged &&
                editedTerrain!.Cell(20, 15).SurfaceId == 2 &&
                editedTerrain.HasFineHeight &&
                editedTerrain.Cell(10, 10).IsRamp &&
                editedTerrain.Cell(11, 10).CliffLevel == 1;

            var savePath = "user://war3_map_self_test/roundtrip/map.w3map.json";
            var saved = War3MapCodec.TrySavePackage(
                editedAsset, savePath, false,
                out var saveValidation, out var saveError);
            var reloaded = saved &&
                War3MapCodec.TryLoad(savePath, out var loadedAsset, out saveError) &&
                loadedAsset is not null &&
                War3MapCodec.TryExpand(
                    loadedAsset, out var loadedMap, out saveValidation) &&
                loadedMap is not null &&
                loadedMap.StableHashText == editedAsset.RuntimeHash &&
                loadedMap.Terrain.StableHash == editedTerrain!.StableHash &&
                loadedMap.Objects.Length == editedMap!.Objects.Length;

            var invalidAsset = War3MapCodec.CreateNew(
                "invalid_export", "Invalid Export", 32, 32);
            invalidAsset.Objects.RemoveAll(value =>
                value.Kind == War3MapObjectKind.SpawnPoint && value.OwnerSlot == 2);
            var invalidBlocked = !War3MapCodec.TrySavePackage(
                invalidAsset,
                "user://war3_map_self_test/invalid/map.w3map.json",
                false,
                out var invalidValidation,
                out _) &&
                !invalidValidation.IsValid &&
                invalidValidation.Issues.Any(value => value.Code == "missing_spawn");
            var badRampAsset = War3MapCodec.CreateNew(
                "invalid_ramp", "Invalid Ramp", 32, 32);
            War3MapEditDocument.TryCreate(
                badRampAsset, out var badRampDocument, out _);
            badRampDocument!.Apply(
                new Vector2(512f, 512f),
                new War3MapBrush(
                    War3MapTool.PlaceRamp, 0, 1f, War3BrushShape.Circle,
                    CliffLevel: 0,
                    RampDirection: TerrainRampDirection.PositiveX), 7);
            var rampBlocked = !War3MapCodec.TrySavePackage(
                badRampDocument.CaptureAsset(),
                "user://war3_map_self_test/invalid_ramp/map.w3map.json",
                false,
                out var rampValidation,
                out _) && rampValidation.Issues.Any(value =>
                value.Code == "invalid_ramp_high");

            var passed = defaultCompatible && oneStrokePayload && editingExact &&
                         editedValidation.IsValid && reloaded &&
                         saveValidation.IsValid && invalidBlocked && rampBlocked;
            return new SelfTestResult(
                passed,
                $"catalog={catalog.Count} defaultHash={defaultMap.StableHashText} " +
                $"objects={defaultMap.Objects.Length} roundtrip={reloaded} " +
                $"undoPayload={oneStrokePayload} editing={editingExact} " +
                $"invalidBlocked={invalidBlocked}/{rampBlocked} saveError={saveError}");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }
}
