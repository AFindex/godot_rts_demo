using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class TerrainMapSelfTest
{
    public static SelfTestResult Run()
    {
        var first = CreateFixture();
        var second = CreateFixture();
        var cliffBlocked = !first.IsSegmentTraversable(
            new Vector2(64f, 32f), new Vector2(256f, 32f), 6f);
        var rampOpen = first.IsSegmentTraversable(
            new Vector2(64f, 112f), new Vector2(256f, 112f), 6f);
        var plateauBuildable = first.IsAreaBuildable(
            new SimRect(new Vector2(32f, 128f), new Vector2(64f, 160f)));
        var rampUnbuildable = !first.IsAreaBuildable(
            new SimRect(new Vector2(132f, 72f), new Vector2(156f, 96f)));
        var waterUnbuildable = !first.IsAreaBuildable(
            new SimRect(new Vector2(32f, 164f), new Vector2(64f, 188f)));
        var groundCrossesShallow = first.IsSegmentTraversable(
            new Vector2(16f, 176f), new Vector2(96f, 176f), 4f);
        var floatRejectsGround = !first.IsSegmentTraversable(
            new Vector2(16f, 144f), new Vector2(96f, 144f), 4f,
            TerrainMovementMode.Float);
        var heights =
            MathF.Abs(first.HeightAt(new Vector2(64f, 112f))) < 0.001f &&
            MathF.Abs(first.HeightAt(new Vector2(128f, 112f)) - 12f) < 0.001f &&
            MathF.Abs(first.HeightAt(new Vector2(144f, 112f)) - 24f) < 0.001f &&
            MathF.Abs(first.HeightAt(new Vector2(160f, 112f)) - 36f) < 0.001f &&
            MathF.Abs(first.HeightAt(new Vector2(224f, 112f)) - 48f) < 0.001f;
        var fineHeights = Enumerable.Range(
                0, (first.Columns + 1) * (first.Rows + 1))
            .Select(index =>
                (float)(index / (first.Columns + 1) * 2 +
                        index % (first.Columns + 1)))
            .ToArray();
        var fineCreated = TerrainMapSnapshot.TryCreate(
                              first.Bounds,
                              first.CellSize,
                              first.CliffLevelHeight,
                              first.Surfaces,
                              first.Cells,
                              fineHeights,
                              out var fineTerrain,
                              out var fineValidation) &&
                          fineTerrain is not null &&
                          fineValidation.IsValid;
        var fineHeight = fineCreated &&
                         fineTerrain!.FormatVersion ==
                         TerrainMapSnapshot.CurrentFormatVersion &&
                         fineTerrain.HasFineHeight &&
                         MathF.Abs(
                             fineTerrain.FineHeightAt(new Vector2(16f, 16f)) -
                             1.5f) < 0.001f &&
                         TerrainMapSnapshot.TryDeserialize(
                             fineTerrain.CanonicalBytes.Span,
                             out var fineDecoded,
                             out var fineDecodeValidation) &&
                         fineDecoded is not null &&
                         fineDecodeValidation.IsValid &&
                         fineDecoded.StableHash == fineTerrain.StableHash &&
                         fineDecoded.FineHeightPoints.SequenceEqual(
                             fineTerrain.FineHeightPoints);
        var stable = first.StableHash != 0UL &&
                     first.StableHash == second.StableHash &&
                     first.CanonicalBytes.Span.SequenceEqual(
                         second.CanonicalBytes.Span);
        var roundTrip = TerrainMapSnapshot.TryDeserialize(
                            first.CanonicalBytes.Span,
                            out var decoded,
                            out var decodeValidation) &&
                        decoded is not null && decodeValidation.IsValid &&
                        decoded.StableHash == first.StableHash &&
                        decoded.CanonicalBytes.Span.SequenceEqual(
                            first.CanonicalBytes.Span);

        var world = new StaticWorld(first.Bounds, first);
        var worldBlocksCliff = !world.IsSegmentFree(
            new Vector2(64f, 32f), new Vector2(256f, 32f), 6f);
        var worldUsesRamp = world.IsSegmentFree(
            new Vector2(64f, 112f), new Vector2(256f, 112f), 6f);
        var placement = BuildingPlacementValidator.Evaluate(
            world,
            new UnitStore(4),
            new SimRect(new Vector2(32f, 164f), new Vector2(64f, 188f)),
            new BuildingPlacementRules(
                MovementClass.Small, PreserveConnectivity: false));
        var placementReportsTerrain =
            placement.Code == BuildingPlacementCode.TerrainUnbuildable;

        var passed = cliffBlocked && rampOpen && plateauBuildable &&
                     rampUnbuildable && waterUnbuildable &&
                     groundCrossesShallow && floatRejectsGround && heights &&
                     fineHeight && stable && roundTrip && worldBlocksCliff && worldUsesRamp &&
                     placementReportsTerrain;
        return new SelfTestResult(
            passed,
            $"terrain cliff={cliffBlocked}, ramp={rampOpen}, " +
            $"build={plateauBuildable}/{rampUnbuildable}/{waterUnbuildable}, " +
            $"water={groundCrossesShallow}/{floatRejectsGround}, " +
            $"heights={heights}/{fineHeight}, " +
            $"world={worldBlocksCliff}/{worldUsesRamp}, " +
            $"placement={placement.Code}, roundTrip={roundTrip}, " +
            $"hash={first.StableHashText}");
    }

    private static TerrainMapSnapshot CreateFixture()
    {
        var bounds = new SimRect(Vector2.Zero, new Vector2(320f, 192f));
        TerrainSurfaceDefinition[] surfaces =
        [
            new(0, "soil", "Soil"),
            new(1, "water", "Water")
        ];
        var low = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var high = low with { CliffLevel = 1 };
        var builder = new TerrainMapBuilder(bounds, 32f, 48f, surfaces, low);
        for (var row = 0; row < builder.Rows; row++)
        {
            for (var column = 4; column < builder.Columns; column++)
                builder.SetCell(column, row, high);
        }
        for (var row = 2; row <= 3; row++)
        {
            builder.SetCell(
                4,
                row,
                low with
                {
                    Flags = TerrainCellFlags.Ramp,
                    RampDirection = TerrainRampDirection.PositiveX
                });
        }
        var shallow = new TerrainCell(
            0, 1, TerrainPathing.ShallowWater, TerrainCellFlags.None);
        for (var column = 0; column <= 3; column++)
            builder.SetCell(column, 5, shallow);
        return builder.Build();
    }
}
