using System.Numerics;
using System.Runtime.InteropServices;

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

internal readonly record struct PlayerVisibilityUpdateProfile(
    double ClearMilliseconds,
    double UnitVisionMilliseconds,
    double BuildingVisionMilliseconds,
    int UnitCacheHits,
    int UnitCacheRebuilds,
    int CandidateCells);

public sealed class PlayerVisibilitySystem
{
    private const float VisionUpdateCellScale = 0.25f;
    public const int MaximumPlayers = 16;
    public const float DefaultCellSize = 32f;
    public const float UnitVisionRadius = 224f;
    public const float BuildingVisionRadius = 256f;
    public const float TownHallVisionRadius = 320f;
    public const float DefaultGroundObservationHeight = 12f;
    private readonly SimRect _bounds;
    private readonly PlayerDiplomacySystem _diplomacy;
    private readonly ITerrainMapQuery? _terrain;
    private readonly float _visionUpdateCellSize;
    private readonly int _visionUpdateColumns;
    private readonly int _visionUpdateRows;
    private readonly float[] _cellCentersX;
    private readonly float[] _cellCentersY;
    private readonly ushort[] _sharedVisionMasks =
        new ushort[MaximumPlayers];
    private int _sharedVisionRevision = -1;
    private readonly Dictionary<int, VisibilityGrid> _players = [];
    private readonly Dictionary<StaticVisionSourceKey, int[]>
        _staticVisionCells = [];
    private readonly List<int> _visionCellScratch = new(512);
    private List<int> _unitVisionCellScratch = new(512);
    private readonly Action<int, Vector2, BuildingTypeProfile>
        _revealBuildingProfile;
    private readonly Action<int, Vector2, BuildingTypeProfile>
        _revealBuildingDetection;
    private VisionFootprintCache[] _unitVisionCaches = [];
    private readonly HashSet<int> _activeUnitVisionSources = [];
    private readonly List<int> _retiredUnitVisionSources = [];
    private int _profileUnitCacheHits;
    private int _profileUnitCacheRebuilds;
    private int _profileCandidateCells;

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
        _cellCentersX = new float[Columns];
        _cellCentersY = new float[Rows];
        for (var column = 0; column < Columns; column++)
            _cellCentersX[column] =
                bounds.Min.X + (column + 0.5f) * cellSize;
        for (var row = 0; row < Rows; row++)
            _cellCentersY[row] =
                bounds.Min.Y + (row + 0.5f) * cellSize;
        var authorityCellSize = terrain is null
            ? cellSize
            : MathF.Min(cellSize, terrain.CellSize);
        // Fog is authoritative at a grid resolution. Sampling LOS four times
        // per terrain/fog cell keeps cliff transitions precise while allowing
        // ordinary movement to reuse most previous-tick visibility work.
        _visionUpdateCellSize = authorityCellSize * VisionUpdateCellScale;
        _visionUpdateColumns =
            (int)MathF.Ceiling(bounds.Width / _visionUpdateCellSize);
        _visionUpdateRows =
            (int)MathF.Ceiling(bounds.Height / _visionUpdateCellSize);
        _revealBuildingProfile = RevealBuildingProfile;
        _revealBuildingDetection = RevealBuildingDetection;
    }

    public float CellSize { get; }
    public int Columns { get; }
    public int Rows { get; }
    public bool HasExploredState =>
        _players.Values.Any(value => value.Explored.Any(cell => cell));
    public Func<int, int, int>? TechnologyLevelResolver { get; set; }
    internal bool ProfilingEnabled { get; set; }
    internal PlayerVisibilityUpdateProfile LastUpdateProfile { get; private set; }

    public void Update(
        UnitStore units,
        CombatStore combat,
        ConstructionSystem construction)
    {
        RefreshSharedVisionMasks();
        var updateStart = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        _profileUnitCacheHits = 0;
        _profileUnitCacheRebuilds = 0;
        _profileCandidateCells = 0;
        if (_unitVisionCaches.Length < units.Count)
            Array.Resize(ref _unitVisionCaches, units.Count);
        foreach (var grid in _players.Values)
        {
            Array.Clear(grid.BuildingVisible);
            Array.Clear(grid.TemporaryVisible);
            Array.Clear(grid.Detected);
        }
        var clearEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;

        _retiredUnitVisionSources.Clear();
        foreach (var unit in _activeUnitVisionSources)
        {
            if ((uint)unit < (uint)units.Count && units.Alive[unit])
                continue;
            ref var cache = ref _unitVisionCaches[unit];
            SyncUnitVisionSource(
                unit,
                active: false,
                cache.SourcePlayerId,
                units.Positions[unit],
                combat.VisionRanges[unit],
                combat.ObservationHeights[unit],
                combat.TerrainVisionModes[unit]);
            _retiredUnitVisionSources.Add(unit);
        }
        foreach (var unit in _retiredUnitVisionSources)
            _activeUnitVisionSources.Remove(unit);

        foreach (var unit in units.AliveUnits)
        {
            var playerId = combat.Teams[unit];
            var active = playerId > 0 &&
                         playerId < MaximumPlayers;
            SyncUnitVisionSource(
                unit,
                active,
                playerId,
                units.Positions[unit],
                combat.VisionRanges[unit],
                combat.ObservationHeights[unit],
                combat.TerrainVisionModes[unit]);
            if (active) _activeUnitVisionSources.Add(unit);
            else _activeUnitVisionSources.Remove(unit);
            if (!active) continue;
            if (combat.DetectionRanges[unit] > 0f)
            {
                RevealDetectionSource(
                    playerId,
                    units.Positions[unit],
                    combat.DetectionRanges[unit]);
            }
        }

        var unitsEnd = ProfilingEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;

        construction.VisitPerceptionSources(_revealBuildingProfile);
        if (ProfilingEnabled)
        {
            var buildingsEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            LastUpdateProfile = new PlayerVisibilityUpdateProfile(
                ElapsedMilliseconds(updateStart, clearEnd),
                ElapsedMilliseconds(clearEnd, unitsEnd),
                ElapsedMilliseconds(unitsEnd, buildingsEnd),
                _profileUnitCacheHits,
                _profileUnitCacheRebuilds,
                _profileCandidateCells);
        }
    }

    public void UpdateDetection(
        UnitStore units,
        CombatStore combat,
        ConstructionSystem? construction = null)
    {
        RefreshSharedVisionMasks();
        foreach (var grid in _players.Values)
        {
            Array.Clear(grid.Detected);
            Array.Clear(grid.TemporaryVisible);
        }
        foreach (var unit in units.AliveUnits)
        {
            var playerId = combat.Teams[unit];
            if (playerId <= 0 ||
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
        construction?.VisitPerceptionSources(_revealBuildingDetection);
    }

    public MapVisibility At(int playerId, Vector2 position)
    {
        if (!_players.TryGetValue(playerId, out var grid) ||
            !TryCell(position, out var cell))
        {
            return MapVisibility.Hidden;
        }
        return grid.UnitVisibleCounts[cell] > 0 ||
               grid.BuildingVisible[cell] ||
               grid.TemporaryVisible[cell]
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

    /// <summary>
    /// Adds an elevated temporary vision source after the regular visibility
    /// clear/update pass. AbilitySystem calls this every tick while the reveal
    /// zone is alive, so no presentation state becomes authoritative.
    /// </summary>
    public void RevealAbilityArea(
        int sourcePlayerId,
        Vector2 center,
        float radius,
        bool detection)
    {
        if (sourcePlayerId <= 0 || sourcePlayerId >= MaximumPlayers ||
            !float.IsFinite(center.X) || !float.IsFinite(center.Y) ||
            !float.IsFinite(radius) || radius <= 0f)
            return;
        for (var viewer = 1; viewer < MaximumPlayers; viewer++)
        {
            if (!SharesVisionCached(viewer, sourcePlayerId)) continue;
            RevealCircle(
                viewer, center, radius, detection: false,
                DefaultGroundObservationHeight, TerrainVisionMode.Elevated);
            if (detection)
                RevealCircle(
                    viewer, center, radius, detection: true,
                    DefaultGroundObservationHeight, TerrainVisionMode.Elevated);
        }
    }

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
        CopyCells(playerId, result);
        return result;
    }

    /// <summary>
    /// Copies the current player-visible fog grid without allocating. This is
    /// intended for presentation textures; visibility authority remains here.
    /// </summary>
    public void CopyCells(int playerId, Span<byte> destination)
    {
        var cellCount = Columns * Rows;
        if (destination.Length < cellCount)
            throw new ArgumentException(
                "Visibility destination is smaller than the grid.",
                nameof(destination));
        destination[..cellCount].Clear();
        if (!_players.TryGetValue(playerId, out var grid))
            return;
        for (var index = 0; index < cellCount; index++)
        {
            destination[index] = grid.UnitVisibleCounts[index] > 0 ||
                                 grid.BuildingVisible[index] ||
                                 grid.TemporaryVisible[index]
                ? (byte)MapVisibility.Visible
                : grid.Explored[index]
                    ? (byte)MapVisibility.Explored
                    : (byte)MapVisibility.Hidden;
        }
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
        _unitVisionCaches = [];
        _activeUnitVisionSources.Clear();
        _retiredUnitVisionSources.Clear();
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

    private void SyncUnitVisionSource(
        int unit,
        bool active,
        int sourcePlayerId,
        Vector2 center,
        float radius,
        float observationHeight = DefaultGroundObservationHeight,
        TerrainVisionMode terrainVisionMode = TerrainVisionMode.Ground)
    {
        ref var cache = ref _unitVisionCaches[unit];
        var sourceCell = VisionUpdateCell(center);
        var matches = active && cache.Matches(
            sourcePlayerId, sourceCell, radius,
            observationHeight, terrainVisionMode);
        if (matches)
        {
            if (ProfilingEnabled) _profileUnitCacheHits++;
            return;
        }
        var previousCells = cache.Cells;
        var previousApplied = cache.Applied;
        if (previousApplied && previousCells is not null &&
            cache.SourcePlayerId != sourcePlayerId)
        {
            RemoveUnitVisionCells(
                cache.SourcePlayerId,
                CollectionsMarshal.AsSpan(previousCells));
        }
        cache.Applied = false;
        if (!active)
        {
            if (previousApplied && previousCells is not null &&
                cache.SourcePlayerId == sourcePlayerId)
            {
                RemoveUnitVisionCells(
                    cache.SourcePlayerId,
                    CollectionsMarshal.AsSpan(previousCells));
            }
            cache.Valid = false;
            return;
        }
        if (ProfilingEnabled) _profileUnitCacheRebuilds++;
        var nextCells = _unitVisionCellScratch;
        nextCells.Clear();
        CollectVisionCells(
            VisionUpdateCellCenter(sourceCell),
            radius,
            observationHeight,
            terrainVisionMode,
            nextCells);
        if (previousApplied && previousCells is not null &&
            cache.SourcePlayerId == sourcePlayerId)
        {
            ApplyUnitVisionCellDelta(
                sourcePlayerId,
                CollectionsMarshal.AsSpan(previousCells),
                CollectionsMarshal.AsSpan(nextCells));
        }
        else
        {
            AddUnitVisionCells(
                sourcePlayerId, CollectionsMarshal.AsSpan(nextCells));
        }
        _unitVisionCellScratch = previousCells ?? new List<int>(512);
        cache.Cells = nextCells;
        cache.Applied = true;
        cache.Valid = true;
        cache.SourcePlayerId = sourcePlayerId;
        cache.SourceCell = sourceCell;
        cache.Radius = radius;
        cache.ObservationHeight = observationHeight;
        cache.TerrainVisionMode = terrainVisionMode;
    }

    private void AddUnitVisionCells(
        int sourcePlayerId,
        ReadOnlySpan<int> cells)
    {
        var viewers = (uint)_sharedVisionMasks[sourcePlayerId];
        while (viewers != 0)
        {
            var viewer = System.Numerics.BitOperations.TrailingZeroCount(viewers);
            viewers &= viewers - 1;
            if (!_players.TryGetValue(viewer, out var grid))
            {
                grid = new VisibilityGrid(Columns * Rows);
                _players.Add(viewer, grid);
            }
            for (var index = 0; index < cells.Length; index++)
            {
                var cell = cells[index];
                if (grid.UnitVisibleCounts[cell] < ushort.MaxValue)
                    grid.UnitVisibleCounts[cell]++;
                grid.Explored[cell] = true;
            }
        }
    }

    private void RemoveUnitVisionCells(
        int sourcePlayerId,
        ReadOnlySpan<int> cells)
    {
        var viewers = (uint)_sharedVisionMasks[sourcePlayerId];
        while (viewers != 0)
        {
            var viewer = System.Numerics.BitOperations.TrailingZeroCount(viewers);
            viewers &= viewers - 1;
            if (!_players.TryGetValue(viewer, out var grid))
                continue;
            for (var index = 0; index < cells.Length; index++)
            {
                var cell = cells[index];
                if (grid.UnitVisibleCounts[cell] > 0)
                    grid.UnitVisibleCounts[cell]--;
            }
        }
    }

    private void ApplyUnitVisionCellDelta(
        int sourcePlayerId,
        ReadOnlySpan<int> previousCells,
        ReadOnlySpan<int> nextCells)
    {
        var viewers = (uint)_sharedVisionMasks[sourcePlayerId];
        while (viewers != 0)
        {
            var viewer = System.Numerics.BitOperations.TrailingZeroCount(viewers);
            viewers &= viewers - 1;
            if (!_players.TryGetValue(viewer, out var grid))
            {
                grid = new VisibilityGrid(Columns * Rows);
                _players.Add(viewer, grid);
                for (var index = 0; index < nextCells.Length; index++)
                {
                    var cell = nextCells[index];
                    grid.UnitVisibleCounts[cell]++;
                    grid.Explored[cell] = true;
                }
                continue;
            }

            var previousIndex = 0;
            var nextIndex = 0;
            while (previousIndex < previousCells.Length &&
                   nextIndex < nextCells.Length)
            {
                var previous = previousCells[previousIndex];
                var next = nextCells[nextIndex];
                if (previous < next)
                {
                    if (grid.UnitVisibleCounts[previous] > 0)
                        grid.UnitVisibleCounts[previous]--;
                    previousIndex++;
                }
                else if (next < previous)
                {
                    if (grid.UnitVisibleCounts[next] < ushort.MaxValue)
                        grid.UnitVisibleCounts[next]++;
                    grid.Explored[next] = true;
                    nextIndex++;
                }
                else
                {
                    previousIndex++;
                    nextIndex++;
                }
            }
            while (previousIndex < previousCells.Length)
            {
                var cell = previousCells[previousIndex++];
                if (grid.UnitVisibleCounts[cell] > 0)
                    grid.UnitVisibleCounts[cell]--;
            }
            while (nextIndex < nextCells.Length)
            {
                var cell = nextCells[nextIndex++];
                if (grid.UnitVisibleCounts[cell] < ushort.MaxValue)
                    grid.UnitVisibleCounts[cell]++;
                grid.Explored[cell] = true;
            }
        }
    }

    private void ApplyBuildingVisionCells(
        int sourcePlayerId,
        ReadOnlySpan<int> cells)
    {
        for (var viewer = 1; viewer < MaximumPlayers; viewer++)
        {
            if (!SharesVisionCached(viewer, sourcePlayerId)) continue;
            if (!_players.TryGetValue(viewer, out var grid))
            {
                grid = new VisibilityGrid(Columns * Rows);
                _players.Add(viewer, grid);
            }
            for (var index = 0; index < cells.Length; index++)
            {
                var cell = cells[index];
                grid.BuildingVisible[cell] = true;
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
        if (ProfilingEnabled)
        {
            _profileCandidateCells +=
                (maximumColumn - minimumColumn + 1) *
                (maximumRow - minimumRow + 1);
        }
        var paddedRadius = radius + CellSize * 0.70710678f;
        var radiusSquared = paddedRadius * paddedRadius;
        var terrainOcclusion = _terrain is not null &&
            (_terrain.HasHeightVariation || _terrain.HasVisionBlockers);
        for (var row = minimumRow; row <= maximumRow; row++)
        {
            var y = _cellCentersY[row];
            var deltaY = y - center.Y;
            var deltaYSquared = deltaY * deltaY;
            for (var column = minimumColumn; column <= maximumColumn; column++)
            {
                var x = _cellCentersX[column];
                var deltaX = x - center.X;
                if (deltaX * deltaX + deltaYSquared > radiusSquared)
                    continue;
                if (terrainOcclusion && !_terrain!.IsVisibleFrom(
                        center,
                        new Vector2(x, y),
                        observationHeight,
                        terrainVisionMode))
                {
                    continue;
                }
                output.Add(row * Columns + column);
            }
        }
    }

    private void RevealDetectionSource(
        int sourcePlayerId,
        Vector2 center,
        float radius)
    {
        for (var viewer = 1; viewer < MaximumPlayers; viewer++)
        {
            if (SharesVisionCached(viewer, sourcePlayerId))
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
        var terrainOcclusion = !detection && _terrain is not null &&
            (_terrain.HasHeightVariation || _terrain.HasVisionBlockers);
        for (var row = minimumRow; row <= maximumRow; row++)
        {
            for (var column = minimumColumn; column <= maximumColumn; column++)
            {
                var position = new Vector2(
                    _cellCentersX[column], _cellCentersY[row]);
                if (Vector2.DistanceSquared(position, center) > radiusSquared)
                    continue;
                if (terrainOcclusion && !_terrain!.IsVisibleFrom(
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
                    grid.TemporaryVisible[cell] = true;
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
        ApplyBuildingVisionCells(playerId, cells.AsSpan());
    }

    private void RevealBuildingProfile(
        int playerId,
        Vector2 center,
        BuildingTypeProfile type)
    {
        if (!type.Perception.Enabled)
        {
            RevealBuilding(playerId, center, type.Function);
            return;
        }
        RevealStaticVision(
            playerId, center, type.Perception.VisionRange,
            type.Perception.ObservationHeight,
            type.Perception.TerrainVisionMode);
        if (BuildingDetectionEnabled(playerId, type.Perception))
            RevealDetectionSource(
                playerId, center, type.Perception.DetectionRange);
    }

    private void RevealBuildingDetection(
        int playerId,
        Vector2 center,
        BuildingTypeProfile type)
    {
        if (BuildingDetectionEnabled(playerId, type.Perception))
            RevealDetectionSource(
                playerId, center, type.Perception.DetectionRange);
    }

    private bool BuildingDetectionEnabled(
        int playerId,
        in BuildingPerceptionProfileSnapshot perception) =>
        perception.DetectionRange > 0f &&
        (perception.DetectionTechnologyId < 0 ||
         TechnologyLevelResolver?.Invoke(
             playerId, perception.DetectionTechnologyId) > 0);

    private void RevealStaticVision(
        int playerId,
        Vector2 center,
        float radius,
        float observationHeight,
        TerrainVisionMode terrainVisionMode)
    {
        if (playerId <= 0 || playerId >= MaximumPlayers || radius <= 0f)
            return;
        var key = new StaticVisionSourceKey(
            center, radius, observationHeight, terrainVisionMode);
        if (!_staticVisionCells.TryGetValue(key, out var cells))
        {
            _visionCellScratch.Clear();
            CollectVisionCells(
                center, radius, observationHeight, terrainVisionMode,
                _visionCellScratch);
            cells = _visionCellScratch.ToArray();
            _staticVisionCells.Add(key, cells);
        }
        ApplyBuildingVisionCells(playerId, cells.AsSpan());
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

    private int VisionUpdateCell(Vector2 position)
    {
        var column = Math.Clamp(
            (int)MathF.Floor(
                (position.X - _bounds.Min.X) / _visionUpdateCellSize),
            0,
            _visionUpdateColumns - 1);
        var row = Math.Clamp(
            (int)MathF.Floor(
                (position.Y - _bounds.Min.Y) / _visionUpdateCellSize),
            0,
            _visionUpdateRows - 1);
        return row * _visionUpdateColumns + column;
    }

    private Vector2 VisionUpdateCellCenter(int cell)
    {
        var column = cell % _visionUpdateColumns;
        var row = cell / _visionUpdateColumns;
        var minimum = _bounds.Min + new Vector2(
            column * _visionUpdateCellSize,
            row * _visionUpdateCellSize);
        var maximum = Vector2.Min(
            minimum + new Vector2(_visionUpdateCellSize),
            _bounds.Max);
        return (minimum + maximum) * 0.5f;
    }

    private void RefreshSharedVisionMasks()
    {
        if (_sharedVisionRevision == _diplomacy.Revision) return;
        for (var source = 1; source < MaximumPlayers; source++)
        {
            ushort mask = 0;
            for (var viewer = 1; viewer < MaximumPlayers; viewer++)
            {
                if (_diplomacy.SharesVision(viewer, source))
                    mask |= (ushort)(1 << viewer);
            }
            _sharedVisionMasks[source] = mask;
        }
        _sharedVisionRevision = _diplomacy.Revision;
    }

    private bool SharesVisionCached(int viewer, int source)
    {
        RefreshSharedVisionMasks();
        return (uint)source < (uint)_sharedVisionMasks.Length &&
               (_sharedVisionMasks[source] & (1 << viewer)) != 0;
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
        public ushort[] UnitVisibleCounts { get; } = new ushort[cells];
        public bool[] BuildingVisible { get; } = new bool[cells];
        public bool[] TemporaryVisible { get; } = new bool[cells];
        public bool[] Detected { get; } = new bool[cells];
    }

    private static double ElapsedMilliseconds(long start, long end) =>
        (end - start) * 1_000d / System.Diagnostics.Stopwatch.Frequency;

    private readonly record struct StaticVisionSourceKey(
        Vector2 Center,
        float Radius,
        float ObservationHeight,
        TerrainVisionMode TerrainVisionMode);

    private struct VisionFootprintCache
    {
        public bool Valid;
        public bool Applied;
        public int SourcePlayerId;
        public int SourceCell;
        public float Radius;
        public float ObservationHeight;
        public TerrainVisionMode TerrainVisionMode;
        public List<int>? Cells;

        public bool Matches(
            int sourcePlayerId,
            int sourceCell,
            float radius,
            float observationHeight,
            TerrainVisionMode terrainVisionMode) =>
            Valid && Applied && SourcePlayerId == sourcePlayerId &&
            SourceCell == sourceCell && Radius == radius &&
            ObservationHeight == observationHeight &&
            TerrainVisionMode == terrainVisionMode;
    }
}
