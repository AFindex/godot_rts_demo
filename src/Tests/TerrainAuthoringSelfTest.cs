using Godot;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Tests;

public static class TerrainAuthoringSelfTest
{
    public static SelfTestResult Run()
    {
        GD.Print("RTS_TERRAIN_AUTHORING_CASE valid begin");
        var document = CreateValidDocument();
        var original = document.Cell(2, 2);
        var surfaceChanged = document.ApplyBrush(
            2, 2, new TerrainBrush(
                TerrainBrushKind.Surface,
                RadiusCells: 1,
                SurfaceId: 1));
        var painted = document.Cell(2, 2);
        var orthogonal = surfaceChanged == 5 &&
                         painted.SurfaceId == 1 &&
                         painted.CliffLevel == original.CliffLevel &&
                         painted.Pathing == original.Pathing &&
                         painted.Flags == original.Flags &&
                         painted.RampDirection == original.RampDirection;
        document.ApplyBrush(
            2, 2, new TerrainBrush(
                TerrainBrushKind.Surface,
                RadiusCells: 1,
                SurfaceId: original.SurfaceId));

        var valid = document.TryExport(
            TerrainAuthoringValidationSettings.Standard,
            out var snapshot,
            out var validation) &&
            snapshot is not null && validation.IsValid;
        GD.Print($"RTS_TERRAIN_AUTHORING_CASE valid export={valid}");

        var authoringResource = TerrainAuthoringResourceConverter.FromDocument(
            document,
            "res://data/test_authored_runtime.tres");
        var resourceRoundTrip = TerrainAuthoringResourceConverter.TryConvert(
                                    authoringResource,
                                    out var decodedDocument,
                                    out var resourceValidation) &&
                                decodedDocument is not null &&
                                resourceValidation.IsValid &&
                                decodedDocument.TryExport(
                                    TerrainAuthoringValidationSettings.Standard,
                                    out var decodedSnapshot,
                                    out var decodedValidation) &&
                                decodedSnapshot is not null &&
                                decodedValidation.IsValid &&
                                decodedSnapshot.StableHash == snapshot!.StableHash;
        var beforePayload = authoringResource.CaptureCellPayloadBase64();
        authoringResource.CliffLevels[0] = 7;
        authoringResource.RestoreCellPayloadBase64(beforePayload);
        var undoExact = authoringResource.CliffLevels[0] ==
                        document.Cell(0, 0).CliffLevel;
        var runtimeResource = TerrainMapResourceConverter.FromSnapshot(snapshot!);
        var runtimeRoundTrip = TerrainMapResourceConverter.TryConvert(
                                   runtimeResource,
                                   out var runtimeSnapshot,
                                   out var runtimeValidation) &&
                               runtimeSnapshot is not null &&
                               runtimeValidation.IsValid &&
                               runtimeSnapshot.StableHash == snapshot!.StableHash;
        GD.Print(
            $"RTS_TERRAIN_AUTHORING_CASE resource={resourceRoundTrip}/" +
            $"{runtimeRoundTrip} undo={undoExact}");

        var invalidLength = TerrainAuthoringResourceConverter.FromDocument(document);
        invalidLength.CellFlags = invalidLength.CellFlags[..^1];
        var lengthRejected = !TerrainAuthoringResourceConverter.TryConvert(
                                 invalidLength, out _, out var lengthValidation) &&
                             lengthValidation.FirstError?.Code ==
                                 TerrainAuthoringErrorCode.InvalidDocument;

        var resized = TerrainAuthoringResourceConverter.FromDocument(document);
        resized.WorldBounds = new Godot.Rect2(
            resized.WorldBounds.Position,
            resized.WorldBounds.Size + new Godot.Vector2(32f, 32f));
        var resizePreconditionRejected =
            !TerrainAuthoringResourceConverter.TryConvert(
                resized, out _, out _);
        var resizeResult = TerrainAuthoringResourceConverter.ResizeCellArrays(
            resized,
            new TerrainCell(
                0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable));
        var resizeAccepted = resizePreconditionRejected &&
                             resizeResult.NewColumns == document.Columns + 1 &&
                             resizeResult.NewRows == document.Rows + 1 &&
                             resizeResult.CopiedCells ==
                                 document.Columns * document.Rows &&
                             TerrainAuthoringResourceConverter.TryConvert(
                                 resized, out _, out var resizeValidation) &&
                             resizeValidation.IsValid;

        var brokenRamp = CreateValidDocument();
        brokenRamp.SetCell(8, 4, brokenRamp.Cell(8, 4) with { CliffLevel = 0 });
        var rampRejected = !brokenRamp.TryExport(
                               TerrainAuthoringValidationSettings.Standard,
                               out _, out var rampValidation) &&
                           rampValidation.Issues.Any(value =>
                               value.Code ==
                                   TerrainAuthoringErrorCode.InvalidRampUpperNeighbor &&
                               value.Column == 7 && value.Row == 4 &&
                               value.Message.Contains("[7,4]",
                                   StringComparison.Ordinal));
        GD.Print(
            $"RTS_TERRAIN_AUTHORING_CASE invalid length={lengthRejected} " +
            $"resize={resizeAccepted} ramp={rampRejected}");

        var isolated = CreateIsolatedDocument();
        var islandRejected = !isolated.TryExport(
                                 new TerrainAuthoringValidationSettings(4, 4f),
                                 out _, out var islandValidation) &&
                             islandValidation.Issues.Any(value =>
                                 value.Code ==
                                 TerrainAuthoringErrorCode.IsolatedGroundIsland &&
                                 value.Column == 4 && value.Row == 3);
        GD.Print(
            $"RTS_TERRAIN_AUTHORING_CASE island={islandRejected}");

        var narrow = CreateNarrowPassageDocument();
        var narrowRejected = !narrow.TryExport(
                                 new TerrainAuthoringValidationSettings(0, 18f),
                                 out _, out var narrowValidation) &&
                             narrowValidation.Issues.Any(value =>
                                 value.Code == TerrainAuthoringErrorCode.NarrowPassage &&
                                 value.Message.Contains("radius 18",
                                     StringComparison.Ordinal));
        GD.Print(
            $"RTS_TERRAIN_AUTHORING_CASE narrow={narrowRejected}");

        var disconnected = CreateNarrowPassageDocument(closeGap: true);
        var disconnectedRejected = !disconnected.TryExport(
                                       new TerrainAuthoringValidationSettings(
                                           0, 8f),
                                       out _, out var disconnectedValidation) &&
                                   disconnectedValidation.Issues.Any(value =>
                                       value.Code ==
                                           TerrainAuthoringErrorCode.AnchorUnreachable);
        GD.Print(
            $"RTS_TERRAIN_AUTHORING_CASE disconnected={disconnectedRejected}");

        var passed = orthogonal && valid && resourceRoundTrip && undoExact &&
                     runtimeRoundTrip && lengthRejected && resizeAccepted &&
                     rampRejected &&
                     islandRejected && narrowRejected && disconnectedRejected;
        return new SelfTestResult(
            passed,
            $"brush={orthogonal}/{surfaceChanged}, export={valid}, " +
            $"resource={resourceRoundTrip}, undo={undoExact}, " +
            $"runtime={runtimeRoundTrip}, length={lengthRejected}, " +
            $"resize={resizeAccepted}, ramp={rampRejected}, " +
            $"island={islandRejected}, " +
            $"narrow={narrowRejected}, disconnected={disconnectedRejected}, " +
            $"hash={snapshot?.StableHashText}");
    }

    public static TerrainAuthoringDocument CreateValidDocument()
    {
        const int columns = 14;
        const int rows = 10;
        const float cellSize = 32f;
        TerrainSurfaceDefinition[] surfaces =
        [
            new(0, "soil", "Soil"),
            new(1, "rock", "Rock"),
            new(2, "metal", "Ramp")
        ];
        var low = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var cells = Enumerable.Repeat(low, columns * rows).ToArray();
        for (var row = 0; row < rows; row++)
        {
            for (var column = 8; column < columns; column++)
                cells[row * columns + column] = low with
                {
                    CliffLevel = 1,
                    SurfaceId = 1
                };
        }
        for (var row = 3; row <= 5; row++)
        {
            cells[row * columns + 7] = low with
            {
                SurfaceId = 2,
                Flags = TerrainCellFlags.Ramp,
                RampDirection = TerrainRampDirection.PositiveX
            };
        }
        TerrainAuthoringAnchor[] anchors =
        [
            new(0, TerrainAuthoringAnchorKind.Spawn,
                CellCenter(2, 4, cellSize), 6f),
            new(1, TerrainAuthoringAnchorKind.Resource,
                CellCenter(11, 4, cellSize), 6f)
        ];
        return new TerrainAuthoringDocument(
            new SimRect(NVector2.Zero,
                new NVector2(columns * cellSize, rows * cellSize)),
            cellSize,
            48f,
            surfaces,
            cells,
            anchors);
    }

    private static TerrainAuthoringDocument CreateIsolatedDocument()
    {
        const int columns = 8;
        const int rows = 6;
        var blocked = new TerrainCell(0, 0, TerrainPathing.None,
            TerrainCellFlags.None);
        var cells = Enumerable.Repeat(blocked, columns * rows).ToArray();
        cells[3 * columns + 4] = blocked with { Pathing = TerrainPathing.Ground };
        return new TerrainAuthoringDocument(
            new SimRect(NVector2.Zero,
                new NVector2(columns * 32f, rows * 32f)),
            32f,
            48f,
            [new TerrainSurfaceDefinition(0, "soil", "Soil")],
            cells,
            []);
    }

    private static TerrainAuthoringDocument CreateNarrowPassageDocument(
        bool closeGap = false)
    {
        const int columns = 12;
        const int rows = 9;
        const float cellSize = 32f;
        var ground = new TerrainCell(0, 0, TerrainPathing.Ground,
            TerrainCellFlags.Buildable);
        var cells = Enumerable.Repeat(ground, columns * rows).ToArray();
        for (var row = 0; row < rows; row++)
        {
            if (!closeGap && row == 4) continue;
            cells[row * columns + 6] = ground with { Pathing = TerrainPathing.None };
        }
        return new TerrainAuthoringDocument(
            new SimRect(NVector2.Zero,
                new NVector2(columns * cellSize, rows * cellSize)),
            cellSize,
            48f,
            [new TerrainSurfaceDefinition(0, "soil", "Soil")],
            cells,
            [
                new TerrainAuthoringAnchor(
                    0, TerrainAuthoringAnchorKind.Spawn,
                    CellCenter(2, 4, cellSize), 8f),
                new TerrainAuthoringAnchor(
                    1, TerrainAuthoringAnchorKind.Resource,
                    CellCenter(9, 4, cellSize), 8f)
            ]);
    }

    private static NVector2 CellCenter(int column, int row, float cellSize) =>
        new((column + 0.5f) * cellSize, (row + 0.5f) * cellSize);
}
