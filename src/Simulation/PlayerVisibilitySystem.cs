using System.Numerics;

namespace RtsDemo.Simulation;

public enum MapVisibility : byte
{
    Hidden,
    Explored,
    Visible
}

public enum PlayerEntityRelation : byte
{
    Own,
    Ally,
    Enemy,
    Neutral
}

public enum PlayerConcealmentState : byte
{
    NotConcealed,
    ConcealedOwn,
    ConcealedAlly,
    ConcealedDetected
}

public enum PlayerOrderCommandCode : byte
{
    Success,
    InvalidPlayer,
    EmptySelection,
    InvalidUnit,
    WrongOwner,
    InvalidTarget,
    FriendlyTarget,
    TargetNotVisible,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant,
    ContextActionUnavailable
}

public readonly record struct PlayerOrderCommandResult(
    PlayerOrderCommandCode Code)
{
    public bool Succeeded => Code == PlayerOrderCommandCode.Success;
}

public readonly record struct PlayerUnitViewSnapshot(
    int UnitId,
    int OwnerPlayerId,
    PlayerEntityRelation Relation,
    Vector2 Position,
    float Radius,
    float Health,
    float MaximumHealth,
    UnitMoveMode MoveMode,
    CombatPhase CombatPhase,
    PlayerConcealmentState ConcealmentState =
        PlayerConcealmentState.NotConcealed,
    UnitConcealmentPhase ConcealmentPhase = UnitConcealmentPhase.Visible,
    float ConcealmentTransitionProgress = 1f,
    bool CanToggleConcealment = false);

public readonly record struct PlayerBuildingViewSnapshot(
    GameplayBuildingId BuildingId,
    int OwnerPlayerId,
    PlayerEntityRelation Relation,
    BuildingTypeProfile Type,
    SimRect Bounds,
    BuildingLifecycleState State,
    float Progress,
    float Health,
    float MaximumHealth,
    PublicConstructionStatus ConstructionStatus = PublicConstructionStatus.None);

public readonly record struct PlayerResourceViewSnapshot(
    EconomyResourceNodeId NodeId,
    EconomyResourceKind Kind,
    Vector2 Position,
    MapVisibility Visibility,
    int KnownRemaining,
    bool KnownOperational);

public sealed record PlayerViewSnapshot(
    int PlayerId,
    SimRect WorldBounds,
    float VisibilityCellSize,
    int VisibilityColumns,
    int VisibilityRows,
    byte[] VisibilityCells,
    PlayerUnitViewSnapshot[] Units,
    PlayerBuildingViewSnapshot[] Buildings,
    PlayerResourceViewSnapshot[] Resources);

public readonly record struct PlayerVisibilityRuntimeEntry(
    int PlayerId,
    byte[] ExploredCells);

public sealed record PlayerVisibilityRuntimeSnapshot(
    float CellSize,
    int Columns,
    int Rows,
    PlayerVisibilityRuntimeEntry[] Players);

public sealed class PlayerVisibilitySystem
{
    public const int MaximumPlayers = 16;
    public const float DefaultCellSize = 32f;
    public const float UnitVisionRadius = 224f;
    public const float BuildingVisionRadius = 256f;
    public const float TownHallVisionRadius = 320f;
    public const float DefaultGroundObservationHeight = 12f;
    private readonly SimRect _bounds;
    private readonly PlayerDiplomacySystem _diplomacy;
    private readonly ITerrainMapQuery? _terrain;
    private readonly Dictionary<int, VisibilityGrid> _players = [];
    private readonly Dictionary<StaticVisionSourceKey, int[]>
        _staticVisionCells = [];
    private readonly List<int> _visionCellScratch = new(512);
    private readonly Action<int, Vector2, BuildingFunctionKind> _revealBuilding;
    private VisionFootprintCache[] _unitVisionCaches = [];

    public PlayerVisibilitySystem(
        SimRect bounds,
        PlayerDiplomacySystem diplomacy,
        float cellSize = DefaultCellSize,
        ITerrainMapQuery? terrain = null)
    {
        if (bounds.Width <= 0f || bounds.Height <= 0f ||
            !float.IsFinite(cellSize) || cellSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }
        _bounds = bounds;
        _diplomacy = diplomacy;
        if (terrain is not null && terrain.Bounds != bounds)
            throw new ArgumentException(
                "Visibility terrain bounds must match world bounds.",
                nameof(terrain));
        _terrain = terrain;
        CellSize = cellSize;
        Columns = (int)MathF.Ceiling(bounds.Width / cellSize);
        Rows = (int)MathF.Ceiling(bounds.Height / cellSize);
        _revealBuilding = RevealBuilding;
    }

    public float CellSize { get; }
    public int Columns { get; }
    public int Rows { get; }
    public bool HasExploredState =>
        _players.Values.Any(value => value.Explored.Any(cell => cell));

    public void Update(
        UnitStore units,
        CombatStore combat,
        ConstructionSystem construction)
    {
        if (_unitVisionCaches.Length < units.Count)
            Array.Resize(ref _unitVisionCaches, units.Count);
        foreach (var grid in _players.Values)
        {
            Array.Clear(grid.Visible);
            Array.Clear(grid.Detected);
        }

        for (var unit = 0; unit < units.Count; unit++)
        {
            var playerId = combat.Teams[unit];
            if (!units.Alive[unit] || playerId <= 0 ||
                playerId >= MaximumPlayers)
                continue;
            RevealUnitVisionSource(
                unit,
                playerId,
                units.Positions[unit],
                combat.VisionRanges[unit],
                combat.ObservationHeights[unit],
                combat.TerrainVisionModes[unit]);
            if (combat.DetectionRanges[unit] > 0f)
            {
                RevealDetectionSource(
                    playerId,
                    units.Positions[unit],
                    combat.DetectionRanges[unit]);
            }
        }

        construction.VisitVisionSources(_revealBuilding);
    }

    public void UpdateDetection(UnitStore units, CombatStore combat)
    {
        foreach (var grid in _players.Values)
            Array.Clear(grid.Detected);
        for (var unit = 0; unit < units.Count; unit++)
        {
            var playerId = combat.Teams[unit];
            if (!units.Alive[unit] || playerId <= 0 ||
                playerId >= MaximumPlayers ||
                combat.DetectionRanges[unit] <= 0f)
            {
                continue;
            }
            RevealDetectionSource(
                playerId,
                units.Positions[unit],
                combat.DetectionRanges[unit]);
        }
    }

    public MapVisibility At(int playerId, Vector2 position)
    {
        if (!_players.TryGetValue(playerId, out var grid) ||
            !TryCell(position, out var cell))
        {
            return MapVisibility.Hidden;
        }
        return grid.Visible[cell]
            ? MapVisibility.Visible
            : grid.Explored[cell]
                ? MapVisibility.Explored
                : MapVisibility.Hidden;
    }

    public bool IsVisible(int playerId, Vector2 position) =>
        At(playerId, position) == MapVisibility.Visible;

    public bool IsUnitVisible(
        int playerId,
        int unit,
        UnitStore units,
        CombatStore combat)
    {
        if ((uint)unit >= (uint)units.Count || !units.Alive[unit])
            return false;
        var relation = _diplomacy.Relation(playerId, combat.Teams[unit]);
        if (relation == PlayerEntityRelation.Own ||
            relation == PlayerEntityRelation.Ally &&
            _diplomacy.SharesVision(playerId, combat.Teams[unit]))
            return true;
        var position = units.Positions[unit];
        if (!IsVisible(playerId, position))
            return false;
        return relation == PlayerEntityRelation.Ally ||
               combat.ConcealmentKinds[unit] == UnitConcealmentKind.None ||
               IsDetected(playerId, position);
    }

    public bool IsDetected(int playerId, Vector2 position) =>
        _players.TryGetValue(playerId, out var grid) &&
        TryCell(position, out var cell) && grid.Detected[cell];

    public PlayerConcealmentState ConcealmentStateFor(
        int playerId,
        int unit,
        UnitStore units,
        CombatStore combat)
    {
        var concealment = combat.ConcealmentKinds[unit];
        if (concealment == UnitConcealmentKind.None)
            return PlayerConcealmentState.NotConcealed;
        var relation = _diplomacy.Relation(playerId, combat.Teams[unit]);
        return relation == PlayerEntityRelation.Own
            ? PlayerConcealmentState.ConcealedOwn
            : relation == PlayerEntityRelation.Ally
                ? PlayerConcealmentState.ConcealedAlly
            : IsUnitVisible(playerId, unit, units, combat)
                ? PlayerConcealmentState.ConcealedDetected
                : throw new InvalidOperationException(
                    "An undetected enemy has no player-visible concealment state.");
    }

    public byte[] CreateCells(int playerId)
    {
        var result = new byte[Columns * Rows];
        if (!_players.TryGetValue(playerId, out var grid))
            return result;
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = grid.Visible[index]
                ? (byte)MapVisibility.Visible
                : grid.Explored[index]
                    ? (byte)MapVisibility.Explored
                    : (byte)MapVisibility.Hidden;
        }
        return result;
    }

    public PlayerVisibilityRuntimeSnapshot CaptureRuntimeState() => new(
        CellSize,
        Columns,
        Rows,
        _players.OrderBy(value => value.Key)
            .Select(value => new PlayerVisibilityRuntimeEntry(
                value.Key, PackBits(value.Value.Explored)))
            .ToArray());

    public void RestoreRuntimeState(PlayerVisibilityRuntimeSnapshot snapshot)
    {
        if (snapshot.CellSize != CellSize || snapshot.Columns != Columns ||
            snapshot.Rows != Rows)
        {
            throw new InvalidOperationException("Visibility grid mismatch.");
        }
        _players.Clear();
        var previousPlayer = 0;
        foreach (var entry in snapshot.Players)
        {
            if (entry.PlayerId <= previousPlayer ||
                entry.PlayerId >= MaximumPlayers)
                throw new InvalidOperationException("Visibility players are not ordered.");
            var grid = new VisibilityGrid(Columns * Rows);
            UnpackBits(entry.ExploredCells, grid.Explored);
            _players.Add(entry.PlayerId, grid);
            previousPlayer = entry.PlayerId;
        }
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add(CellSize);
        hash.Add(Columns);
        hash.Add(Rows);
        hash.Add(_players.Count);
        foreach (var entry in _players.OrderBy(value => value.Key))
        {
            hash.Add(entry.Key);
            var packed = PackBits(entry.Value.Explored);
            hash.Add(packed.Length);
            foreach (var value in packed)
                hash.Add(value);
        }
    }

    private void RevealDetectionCircle(int playerId, Vector2 center, float radius)
    {
        RevealCircle(
            playerId,
            center,
            radius,
            detection: true,
            DefaultGroundObservationHeight,
            TerrainVisionMode.Elevated);
    }

    private void RevealUnitVisionSource(
        int unit,
        int sourcePlayerId,
        Vector2 center,
        float radius,
        float observationHeight = DefaultGroundObservationHeight,
        TerrainVisionMode terrainVisionMode = TerrainVisionMode.Ground)
    {
        ref var cache = ref _unitVisionCaches[unit];
        var matches = cache.Matches(
            center, radius, observationHeight, terrainVisionMode);
        if (matches && cache.Cells is not null)
        {
            ApplyVisionCells(sourcePlayerId, cache.Cells);
            return;
        }
        _visionCellScratch.Clear();
        CollectVisionCells(
            center,
            radius,
            observationHeight,
            terrainVisionMode,
            _visionCellScratch);
        ApplyVisionCells(sourcePlayerId, _visionCellScratch);
        cache = new VisionFootprintCache(
            true,
            center,
            radius,
            observationHeight,
            terrainVisionMode,
            matches ? _visionCellScratch.ToArray() : null);
    }

    private void ApplyVisionCells(
        int sourcePlayerId,
        IReadOnlyList<int> cells)
    {
        for (var viewer = 1; viewer < MaximumPlayers; viewer++)
        {
            if (!_diplomacy.SharesVision(viewer, sourcePlayerId)) continue;
            if (!_players.TryGetValue(viewer, out var grid))
            {
                grid = new VisibilityGrid(Columns * Rows);
                _players.Add(viewer, grid);
            }
            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                grid.Visible[cell] = true;
                grid.Explored[cell] = true;
            }
        }
    }

    private void CollectVisionCells(
        Vector2 center,
        float radius,
        float observationHeight,
        TerrainVisionMode terrainVisionMode,
        List<int> output)
    {
        var minimumColumn = Math.Clamp(
            (int)MathF.Floor((center.X - radius - _bounds.Min.X) / CellSize),
            0, Columns - 1);
        var maximumColumn = Math.Clamp(
            (int)MathF.Floor((center.X + radius - _bounds.Min.X) / CellSize),
            0, Columns - 1);
        var minimumRow = Math.Clamp(
            (int)MathF.Floor((center.Y - radius - _bounds.Min.Y) / CellSize),
            0, Rows - 1);
        var maximumRow = Math.Clamp(
            (int)MathF.Floor((center.Y + radius - _bounds.Min.Y) / CellSize),
            0, Rows - 1);
        var paddedRadius = radius + CellSize * 0.70710678f;
        var radiusSquared = paddedRadius * paddedRadius;
        for (var row = minimumRow; row <= maximumRow; row++)
        for (var column = minimumColumn; column <= maximumColumn; column++)
        {
            var position = new Vector2(
                _bounds.Min.X + (column + 0.5f) * CellSize,
                _bounds.Min.Y + (row + 0.5f) * CellSize);
            if (Vector2.DistanceSquared(position, center) > radiusSquared ||
                _terrain is not null && !_terrain.IsVisibleFrom(
                    center,
                    position,
                    observationHeight,
                    terrainVisionMode))
            {
                continue;
            }
            output.Add(row * Columns + column);
        }
    }

    private void RevealDetectionSource(
        int sourcePlayerId,
        Vector2 center,
        float radius)
    {
        for (var viewer = 1; viewer < MaximumPlayers; viewer++)
        {
            if (_diplomacy.SharesVision(viewer, sourcePlayerId))
                RevealDetectionCircle(viewer, center, radius);
        }
    }

    private void RevealCircle(
        int playerId,
        Vector2 center,
        float radius,
        bool detection,
        float observationHeight,
        TerrainVisionMode terrainVisionMode)
    {
        if (!_players.TryGetValue(playerId, out var grid))
        {
            grid = new VisibilityGrid(Columns * Rows);
            _players.Add(playerId, grid);
        }
        var minimumColumn = Math.Clamp(
            (int)MathF.Floor((center.X - radius - _bounds.Min.X) / CellSize),
            0, Columns - 1);
        var maximumColumn = Math.Clamp(
            (int)MathF.Floor((center.X + radius - _bounds.Min.X) / CellSize),
            0, Columns - 1);
        var minimumRow = Math.Clamp(
            (int)MathF.Floor((center.Y - radius - _bounds.Min.Y) / CellSize),
            0, Rows - 1);
        var maximumRow = Math.Clamp(
            (int)MathF.Floor((center.Y + radius - _bounds.Min.Y) / CellSize),
            0, Rows - 1);
        var paddedRadius = radius + CellSize * 0.70710678f;
        var radiusSquared = paddedRadius * paddedRadius;
        for (var row = minimumRow; row <= maximumRow; row++)
        {
            for (var column = minimumColumn; column <= maximumColumn; column++)
            {
                var position = new Vector2(
                    _bounds.Min.X + (column + 0.5f) * CellSize,
                    _bounds.Min.Y + (row + 0.5f) * CellSize);
                if (Vector2.DistanceSquared(position, center) > radiusSquared)
                    continue;
                if (!detection && _terrain is not null &&
                    !_terrain.IsVisibleFrom(
                        center,
                        position,
                        observationHeight,
                        terrainVisionMode))
                {
                    continue;
                }
                var cell = row * Columns + column;
                if (detection)
                {
                    grid.Detected[cell] = true;
                }
                else
                {
                    grid.Visible[cell] = true;
                    grid.Explored[cell] = true;
                }
            }
        }
    }

    private void RevealBuilding(
        int playerId,
        Vector2 center,
        BuildingFunctionKind function)
    {
        if (playerId <= 0 || playerId >= MaximumPlayers)
            return;
        var radius = function == BuildingFunctionKind.TownHall
            ? TownHallVisionRadius
            : BuildingVisionRadius;
        var key = new StaticVisionSourceKey(
            center,
            radius,
            DefaultGroundObservationHeight,
            TerrainVisionMode.Ground);
        if (!_staticVisionCells.TryGetValue(key, out var cells))
        {
            _visionCellScratch.Clear();
            CollectVisionCells(
                center,
                radius,
                DefaultGroundObservationHeight,
                TerrainVisionMode.Ground,
                _visionCellScratch);
            cells = _visionCellScratch.ToArray();
            _staticVisionCells.Add(key, cells);
        }
        ApplyVisionCells(playerId, cells);
    }

    private bool TryCell(Vector2 position, out int cell)
    {
        var column = (int)MathF.Floor((position.X - _bounds.Min.X) / CellSize);
        var row = (int)MathF.Floor((position.Y - _bounds.Min.Y) / CellSize);
        if ((uint)column >= (uint)Columns || (uint)row >= (uint)Rows)
        {
            cell = -1;
            return false;
        }
        cell = row * Columns + column;
        return true;
    }

    private static byte[] PackBits(bool[] source)
    {
        var result = new byte[(source.Length + 7) / 8];
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index])
                result[index >> 3] |= (byte)(1 << (index & 7));
        }
        return result;
    }

    private static void UnpackBits(byte[] source, bool[] destination)
    {
        if (source.Length != (destination.Length + 7) / 8)
            throw new InvalidOperationException("Visibility payload length mismatch.");
        for (var index = 0; index < destination.Length; index++)
            destination[index] = (source[index >> 3] & (1 << (index & 7))) != 0;
        var unusedBits = source.Length * 8 - destination.Length;
        if (unusedBits > 0 &&
            (source[^1] & (0xFF << (8 - unusedBits))) != 0)
        {
            throw new InvalidOperationException("Visibility padding bits must be zero.");
        }
    }

    private sealed class VisibilityGrid(int cells)
    {
        public bool[] Explored { get; } = new bool[cells];
        public bool[] Visible { get; } = new bool[cells];
        public bool[] Detected { get; } = new bool[cells];
    }

    private readonly record struct StaticVisionSourceKey(
        Vector2 Center,
        float Radius,
        float ObservationHeight,
        TerrainVisionMode TerrainVisionMode);

    private readonly record struct VisionFootprintCache(
        bool Valid,
        Vector2 Center,
        float Radius,
        float ObservationHeight,
        TerrainVisionMode TerrainVisionMode,
        int[]? Cells)
    {
        public bool Matches(
            Vector2 center,
            float radius,
            float observationHeight,
            TerrainVisionMode terrainVisionMode) =>
            Valid && Center == center && Radius == radius &&
            ObservationHeight == observationHeight &&
            TerrainVisionMode == terrainVisionMode;
    }
}
