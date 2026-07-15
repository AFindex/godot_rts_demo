using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Scenarios;

[Flags]
public enum TerrainTestPresetTags : ushort
{
    None = 0,
    Baseline = 1 << 0,
    Ramps = 1 << 1,
    MultiLevel = 1 << 2,
    Water = 1 << 3,
    Vision = 1 << 4,
    Building = 1 << 5,
    AlternateRoutes = 1 << 6,
    Unreachable = 1 << 7,
    Performance = 1 << 8
}

public sealed record TerrainTestPreset(
    string Id,
    string DisplayName,
    string Purpose,
    TerrainTestPresetTags Tags,
    TerrainMapSnapshot Terrain,
    Vector2 Start,
    Vector2 Goal,
    bool StartGoalConnected,
    int ExpectedRampCount,
    int ExpectedMediumRouteChokes,
    SimRect BuildingProbe,
    bool BuildingProbeBuildable,
    SimRect? DynamicBlocker = null)
{
    public string ResourcePath =>
        $"res://test_resources/terrain_presets/{Id}.tres";
}

/// <summary>
/// Stable, engine-independent terrain fixtures. These maps intentionally vary
/// geometry and gameplay rules; they are not visual skins of one layout.
/// </summary>
public static class TerrainTestPresetCatalog
{
    private const float CellSize = 32f;
    private const float CliffHeight = 48f;

    private static readonly TerrainSurfaceDefinition[] Surfaces =
    [
        new(0, "badlands", "Open Ground"),
        new(1, "rock", "Cliff Rock"),
        new(2, "metal", "Ramp Trim"),
        new(3, "shallow-water", "Shallow Water"),
        new(4, "deep-water", "Deep Water"),
        new(5, "mud", "Rough Ground"),
        new(6, "vision-smoke", "Vision Blocker")
    ];

    private static readonly TerrainTestPreset[] Values = CreateAll();
    private static readonly Dictionary<string, TerrainTestPreset> ById =
        Values.ToDictionary(value => value.Id, StringComparer.Ordinal);

    public static ReadOnlySpan<TerrainTestPreset> Presets => Values;

    public static TerrainTestPreset Get(string id) =>
        ById.TryGetValue(id, out var preset)
            ? preset
            : throw new KeyNotFoundException($"Unknown terrain preset '{id}'.");

    private static TerrainTestPreset[] CreateAll() =>
    [
        OpenField(),
        SingleWideRamp(),
        ParallelRampBypass(),
        NarrowWideChoice(),
        ThreeLevelSwitchback(),
        RingPlateau(),
        SunkenBasin(),
        ShallowWaterFords(),
        IslandCauseway(),
        VisionRidge(),
        AlternatingGates(),
        LargeFourRoutes()
    ];

    private static TerrainTestPreset OpenField()
    {
        var map = new PresetCanvas(24, 16);
        map.Fill(8, 2, 11, 5, Mud());
        map.Fill(17, 11, 20, 13, ShallowWater());
        return map.Preset(
            "open-field",
            "Open Field",
            "Flat baseline with harmless surface variation and an off-route pond.",
            TerrainTestPresetTags.Baseline | TerrainTestPresetTags.Building,
            start: map.Center(2, 8),
            goal: map.Center(21, 8),
            connected: true,
            ramps: 0,
            routeChokes: 0,
            buildingProbe: map.Rect(11, 7, 13, 9),
            buildingProbeBuildable: true);
    }

    private static TerrainTestPreset SingleWideRamp()
    {
        var map = new PresetCanvas(24, 16);
        map.Fill(12, 0, 23, 15, Ground(1, 1));
        map.RampX(11, 6, 9, TerrainRampDirection.PositiveX, 0);
        return map.Preset(
            "single-wide-ramp",
            "Single Wide Ramp",
            "Canonical one-level climb through a 128px ramp.",
            TerrainTestPresetTags.Ramps | TerrainTestPresetTags.MultiLevel,
            map.Center(2, 8), map.Center(21, 8), true, 1, 1,
            map.Rect(4, 5, 7, 8), true);
    }

    private static TerrainTestPreset ParallelRampBypass()
    {
        var map = new PresetCanvas(28, 20);
        map.Fill(14, 0, 27, 19, Ground(1, 1));
        map.RampX(13, 3, 5, TerrainRampDirection.PositiveX, 0);
        map.RampX(13, 14, 17, TerrainRampDirection.PositiveX, 0);
        var blocker = map.Rect(9, 2, 13, 6);
        return map.Preset(
            "parallel-ramp-bypass",
            "Parallel Ramp Bypass",
            "A near ramp and a wider far ramp for dynamic building reroutes.",
            TerrainTestPresetTags.Ramps |
            TerrainTestPresetTags.AlternateRoutes |
            TerrainTestPresetTags.Building,
            map.Center(2, 4), map.Center(24, 8), true, 2, 1,
            map.Rect(8, 8, 11, 10), true, blocker);
    }

    private static TerrainTestPreset NarrowWideChoice()
    {
        var map = new PresetCanvas(30, 22, cellSize: 20f);
        map.Fill(16, 0, 29, 21, Ground(1, 1));
        map.RampX(15, 4, 4, TerrainRampDirection.PositiveX, 0);
        map.RampX(15, 14, 19, TerrainRampDirection.PositiveX, 0);
        return map.Preset(
            "narrow-wide-choice",
            "Narrow / Wide Choice",
            "Medium may use the 20px ramp; Large must select the 120px ramp.",
            TerrainTestPresetTags.Ramps |
            TerrainTestPresetTags.AlternateRoutes |
            TerrainTestPresetTags.Building,
            map.Center(2, 5), map.Center(27, 8), true, 2, 1,
            map.Rect(8, 9, 12, 12), true);
    }

    private static TerrainTestPreset ThreeLevelSwitchback()
    {
        var map = new PresetCanvas(30, 18);
        map.Fill(10, 0, 29, 17, Ground(1, 1));
        map.Fill(20, 0, 29, 17, Ground(2, 1));
        map.RampX(9, 3, 5, TerrainRampDirection.PositiveX, 0);
        map.RampX(19, 12, 14, TerrainRampDirection.PositiveX, 1);
        return map.Preset(
            "three-level-switchback",
            "Three-Level Switchback",
            "Two separated climbs force a long route across three height levels.",
            TerrainTestPresetTags.Ramps |
            TerrainTestPresetTags.MultiLevel,
            map.Center(2, 4), map.Center(27, 13), true, 2, 2,
            map.Rect(12, 7, 15, 9), true);
    }

    private static TerrainTestPreset RingPlateau()
    {
        var map = new PresetCanvas(24, 20);
        map.Fill(8, 5, 15, 14, Ground(1, 1));
        map.RampX(7, 8, 10, TerrainRampDirection.PositiveX, 0);
        map.RampX(16, 10, 12, TerrainRampDirection.NegativeX, 0);
        map.RampY(4, 10, 12, TerrainRampDirection.PositiveY, 0);
        map.RampY(15, 11, 13, TerrainRampDirection.NegativeY, 0);
        return map.Preset(
            "ring-plateau",
            "Four-Sided Plateau",
            "A central plateau with entrances on every side for route choice tests.",
            TerrainTestPresetTags.Ramps |
            TerrainTestPresetTags.MultiLevel |
            TerrainTestPresetTags.AlternateRoutes,
            map.Center(2, 10), map.Center(12, 10), true, 4, 1,
            map.Rect(10, 7, 13, 9), true);
    }

    private static TerrainTestPreset SunkenBasin()
    {
        var map = new PresetCanvas(24, 20, Ground(1, 1));
        map.Fill(8, 5, 15, 14, Ground(0));
        map.RampX(7, 8, 10, TerrainRampDirection.NegativeX, 0);
        map.RampX(16, 10, 12, TerrainRampDirection.PositiveX, 0);
        map.RampY(4, 10, 12, TerrainRampDirection.NegativeY, 0);
        map.RampY(15, 11, 13, TerrainRampDirection.PositiveY, 0);
        return map.Preset(
            "sunken-basin",
            "Sunken Basin",
            "Inverse plateau geometry verifies descending approaches and four exits.",
            TerrainTestPresetTags.Ramps |
            TerrainTestPresetTags.MultiLevel |
            TerrainTestPresetTags.AlternateRoutes,
            map.Center(2, 10), map.Center(12, 10), true, 4, 1,
            map.Rect(10, 7, 13, 9), true);
    }

    private static TerrainTestPreset ShallowWaterFords()
    {
        var map = new PresetCanvas(28, 18);
        map.Fill(0, 7, 27, 10, DeepWater());
        map.Fill(4, 7, 7, 10, ShallowWater());
        map.Fill(19, 7, 23, 10, ShallowWater());
        return map.Preset(
            "shallow-water-fords",
            "Shallow-Water Fords",
            "Two ground-passable but non-buildable fords cross deep water.",
            TerrainTestPresetTags.Water |
            TerrainTestPresetTags.AlternateRoutes |
            TerrainTestPresetTags.Building,
            map.Center(6, 3), map.Center(6, 14), true, 0, 0,
            map.Rect(5, 8, 7, 10), false);
    }

    private static TerrainTestPreset IslandCauseway()
    {
        var map = new PresetCanvas(28, 18, DeepWater());
        map.Fill(1, 3, 8, 14, Ground());
        map.Fill(19, 3, 26, 14, Ground());
        map.Fill(8, 8, 19, 9, Ground(0, 5));
        var blocker = map.Rect(13, 8, 14, 10);
        return map.Preset(
            "island-causeway",
            "Island Causeway",
            "A two-cell land bridge supports disconnect and restoration tests.",
            TerrainTestPresetTags.Water |
            TerrainTestPresetTags.Building,
            map.Center(4, 9), map.Center(23, 9), true, 0, 0,
            map.Rect(3, 6, 6, 8), true, blocker);
    }

    private static TerrainTestPreset VisionRidge()
    {
        var map = new PresetCanvas(26, 18);
        map.Fill(10, 0, 13, 17, Ground(1, 1));
        map.Fill(11, 0, 12, 17, VisionRock(1));
        map.RampX(9, 7, 9, TerrainRampDirection.PositiveX, 0);
        map.RampX(14, 7, 9, TerrainRampDirection.NegativeX, 0);
        return map.Preset(
            "vision-ridge",
            "Vision-Blocking Ridge",
            "A raised sight blocker requires climbing and descending through paired ramps.",
            TerrainTestPresetTags.Ramps |
            TerrainTestPresetTags.MultiLevel |
            TerrainTestPresetTags.Vision,
            map.Center(3, 8), map.Center(22, 8), true, 2, 2,
            map.Rect(3, 11, 6, 13), true);
    }

    private static TerrainTestPreset AlternatingGates()
    {
        var map = new PresetCanvas(36, 22);
        map.Fill(9, 0, 11, 16, Blocked());
        map.Fill(9, 19, 11, 21, Blocked());
        map.Fill(18, 5, 20, 21, Blocked());
        map.Fill(27, 0, 29, 16, Blocked());
        return map.Preset(
            "alternating-gates",
            "Alternating Canyon Gates",
            "Offset wall openings force repeated long turns and corner clearance checks.",
            TerrainTestPresetTags.Building |
            TerrainTestPresetTags.Vision |
            TerrainTestPresetTags.Performance,
            map.Center(2, 10), map.Center(33, 10), true, 0, 0,
            map.Rect(18, 9, 21, 12), false);
    }

    private static TerrainTestPreset LargeFourRoutes()
    {
        var map = new PresetCanvas(64, 40);
        map.Fill(32, 0, 63, 39, Ground(1, 1));
        map.RampX(31, 3, 6, TerrainRampDirection.PositiveX, 0);
        map.RampX(31, 12, 15, TerrainRampDirection.PositiveX, 0);
        map.RampX(31, 24, 27, TerrainRampDirection.PositiveX, 0);
        map.RampX(31, 33, 36, TerrainRampDirection.PositiveX, 0);
        return map.Preset(
            "large-four-routes",
            "Large Four-Route Map",
            "A 64x40 performance fixture with four equivalent cross-level routes.",
            TerrainTestPresetTags.Ramps |
            TerrainTestPresetTags.AlternateRoutes |
            TerrainTestPresetTags.Performance,
            map.Center(3, 20), map.Center(60, 20), true, 4, 1,
            map.Rect(18, 17, 22, 20), true);
    }

    private static TerrainCell Ground(byte level = 0, ushort surface = 0) =>
        new(level, surface, TerrainPathing.Ground, TerrainCellFlags.Buildable);

    private static TerrainCell Mud() => Ground(0, 5);

    private static TerrainCell ShallowWater() =>
        new(0, 3, TerrainPathing.ShallowWater, TerrainCellFlags.None);

    private static TerrainCell DeepWater() =>
        new(0, 4, TerrainPathing.DeepWater, TerrainCellFlags.None);

    private static TerrainCell Blocked() =>
        new(0, 6, TerrainPathing.None, TerrainCellFlags.BlocksVision);

    private static TerrainCell VisionRock(byte level) =>
        new(level, 6, TerrainPathing.Ground,
            TerrainCellFlags.Buildable | TerrainCellFlags.BlocksVision);

    private sealed class PresetCanvas
    {
        private readonly int _columns;
        private readonly int _rows;
        private readonly float _cellSize;
        private readonly TerrainCell[] _cells;

        public PresetCanvas(
            int columns,
            int rows,
            TerrainCell? fill = null,
            float cellSize = CellSize)
        {
            _columns = columns;
            _rows = rows;
            _cellSize = cellSize;
            _cells = Enumerable.Repeat(
                fill ?? Ground(), columns * rows).ToArray();
        }

        public void Fill(
            int firstColumn,
            int firstRow,
            int lastColumn,
            int lastRow,
            TerrainCell cell)
        {
            for (var row = firstRow; row <= lastRow; row++)
            {
                for (var column = firstColumn; column <= lastColumn; column++)
                    _cells[row * _columns + column] = cell;
            }
        }

        public void RampX(
            int column,
            int firstRow,
            int lastRow,
            TerrainRampDirection direction,
            byte lowerLevel)
        {
            if (direction is not
                (TerrainRampDirection.PositiveX or TerrainRampDirection.NegativeX))
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }
            Fill(
                column, firstRow, column, lastRow,
                new TerrainCell(
                    lowerLevel, 2, TerrainPathing.Ground,
                    TerrainCellFlags.Ramp, direction));
        }

        public void RampY(
            int row,
            int firstColumn,
            int lastColumn,
            TerrainRampDirection direction,
            byte lowerLevel)
        {
            if (direction is not
                (TerrainRampDirection.PositiveY or TerrainRampDirection.NegativeY))
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }
            Fill(
                firstColumn, row, lastColumn, row,
                new TerrainCell(
                    lowerLevel, 2, TerrainPathing.Ground,
                    TerrainCellFlags.Ramp, direction));
        }

        public Vector2 Center(int column, int row) =>
            new((column + 0.5f) * _cellSize, (row + 0.5f) * _cellSize);

        public SimRect Rect(
            int firstColumn,
            int firstRow,
            int lastColumnExclusive,
            int lastRowExclusive) =>
            new(
                new Vector2(firstColumn * _cellSize, firstRow * _cellSize),
                new Vector2(
                    lastColumnExclusive * _cellSize,
                    lastRowExclusive * _cellSize));

        public TerrainTestPreset Preset(
            string id,
            string displayName,
            string purpose,
            TerrainTestPresetTags tags,
            Vector2 start,
            Vector2 goal,
            bool connected,
            int ramps,
            int routeChokes,
            SimRect buildingProbe,
            bool buildingProbeBuildable,
            SimRect? dynamicBlocker = null)
        {
            var bounds = new SimRect(
                Vector2.Zero,
                new Vector2(_columns * _cellSize, _rows * _cellSize));
            if (!TerrainMapSnapshot.TryCreate(
                    bounds,
                    _cellSize,
                    CliffHeight,
                    Surfaces,
                    _cells,
                    out var terrain,
                    out var validation) || terrain is null)
            {
                throw new InvalidOperationException(
                    $"Terrain preset {id} failed: {validation.FirstError}.");
            }
            return new TerrainTestPreset(
                id,
                displayName,
                purpose,
                tags,
                terrain,
                start,
                goal,
                connected,
                ramps,
                routeChokes,
                buildingProbe,
                buildingProbeBuildable,
                dynamicBlocker);
        }
    }
}
