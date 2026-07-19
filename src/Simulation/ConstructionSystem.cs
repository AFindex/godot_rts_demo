using System.Collections.Immutable;
using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct GameplayBuildingId(int Value);

public enum BuildingFunctionKind : byte
{
    Supply,
    Production,
    TownHall,
    Refinery,
    Research
}

public enum ConstructionMethodKind : byte
{
    ContinuousWorker,
    StartAndRelease
}

/// <summary>
/// Static combat contract for an immobile building.  The values are authored
/// content (for Warcraft, compiled from the unit JSON) while target selection,
/// attack phases and damage application live in the generic simulation.
/// </summary>
public readonly record struct BuildingCombatProfileSnapshot(
    float AcquisitionRange)
{
    public ImmutableArray<CombatWeaponProfileSnapshot> Weapons { get; init; } = [];
    public ImmutableArray<BuildingWeaponOnHitEffectSnapshot> OnHitEffects
        { get; init; } = [];

    public bool Enabled => AcquisitionRange > 0f && !Weapons.IsDefaultOrEmpty;

    public void Validate()
    {
        if (AcquisitionRange == 0f && Weapons.IsDefaultOrEmpty &&
            OnHitEffects.IsDefaultOrEmpty)
            return;
        if (!float.IsFinite(AcquisitionRange) || AcquisitionRange <= 0f ||
            Weapons.IsDefaultOrEmpty || Weapons.Length > 8 ||
            OnHitEffects.IsDefault || OnHitEffects.Length > 16)
            throw new ArgumentOutOfRangeException(
                nameof(BuildingCombatProfileSnapshot));
        var slots = new HashSet<int>();
        foreach (var weapon in Weapons)
        {
            weapon.Validate();
            if (!slots.Add(weapon.Slot) || weapon.AttackRange > AcquisitionRange)
                throw new ArgumentOutOfRangeException(
                    nameof(BuildingCombatProfileSnapshot));
        }
        foreach (var effect in OnHitEffects)
        {
            effect.Validate();
            if (!slots.Contains(effect.WeaponSlot))
                throw new ArgumentOutOfRangeException(
                    nameof(BuildingCombatProfileSnapshot));
        }
    }
}

public enum BuildingWeaponOnHitEffectKind : byte
{
    ManaFeedback
}

/// <summary>
/// Generic, JSON-compiled attack-hit modifier.  Warcraft Feedback is the first
/// consumer; the simulation contract itself contains no ability rawcodes.
/// </summary>
public readonly record struct BuildingWeaponOnHitEffectSnapshot(
    int WeaponSlot,
    BuildingWeaponOnHitEffectKind Kind,
    float UnitMaximumValue,
    float UnitDamagePerValue,
    float HeroMaximumValue,
    float HeroDamagePerValue,
    float SummonedDamage)
{
    public void Validate()
    {
        if (WeaponSlot is < 0 or > 7 || !Enum.IsDefined(Kind) ||
            !Valid(UnitMaximumValue) || !Valid(UnitDamagePerValue) ||
            !Valid(HeroMaximumValue) || !Valid(HeroDamagePerValue) ||
            !Valid(SummonedDamage))
            throw new ArgumentOutOfRangeException(
                nameof(BuildingWeaponOnHitEffectSnapshot));
    }

    private static bool Valid(float value) =>
        float.IsFinite(value) && value >= 0f;
}

/// <summary>JSON-authored sight and detection contract for a building.</summary>
public readonly record struct BuildingPerceptionProfileSnapshot(
    float VisionRange,
    float DetectionRange = 0f,
    float ObservationHeight = PlayerVisibilitySystem.DefaultGroundObservationHeight,
    TerrainVisionMode TerrainVisionMode = TerrainVisionMode.Ground,
    int DetectionTechnologyId = -1)
{
    public bool Enabled => VisionRange > 0f;

    public void Validate()
    {
        if (!float.IsFinite(VisionRange) || VisionRange < 0f ||
            !float.IsFinite(DetectionRange) || DetectionRange < 0f ||
            !float.IsFinite(ObservationHeight) || ObservationHeight < 0f ||
            !Enum.IsDefined(TerrainVisionMode) ||
            DetectionTechnologyId < -1 ||
            VisionRange == 0f && DetectionRange != 0f ||
            DetectionRange == 0f && DetectionTechnologyId is not (-1 or 0))
            throw new ArgumentOutOfRangeException(
                nameof(BuildingPerceptionProfileSnapshot));
    }
}

public enum QueuedConstructionPaymentTiming : byte
{
    ReserveOnIssue
}

public enum QueuedConstructionStaticFailureAction : byte
{
    RefundAndContinue
}

public enum QueuedConstructionBuilderDeathAction : byte
{
    RefundPendingReservations
}

public readonly record struct QueuedConstructionPolicy(
    QueuedConstructionPaymentTiming PaymentTiming,
    QueuedConstructionStaticFailureAction StaticFailureAction,
    QueuedConstructionBuilderDeathAction BuilderDeathAction)
{
    public static QueuedConstructionPolicy ProjectDefault { get; } = new(
        QueuedConstructionPaymentTiming.ReserveOnIssue,
        QueuedConstructionStaticFailureAction.RefundAndContinue,
        QueuedConstructionBuilderDeathAction.RefundPendingReservations);
}

public readonly record struct BuildingTypeProfile(
    int Id,
    string Name,
    BuildingFunctionKind Function,
    Vector2 Size,
    MovementClass MinimumPassageClass,
    EconomyCost Cost,
    float BuildSeconds,
    float MaximumHealth,
    int SupplyProvided,
    float CancelRefundFraction,
    ConstructionMethodKind ConstructionMethod,
    bool RequiresVespeneNode = false,
    float Armor = 0f,
    CombatAttribute Attributes = CombatAttribute.Structure | CombatAttribute.Mechanical,
    float ArmorUpgradePerLevel = 0f,
    CombatArmorType ArmorType = CombatArmorType.Legacy,
    BuildingCombatProfileSnapshot Combat = default,
    BuildingPerceptionProfileSnapshot Perception = default)
{
    public BuildingFootprintProfileSnapshot PlacementProfile => new(
        Id,
        Name,
        Size.X <= 48f && Size.Y <= 48f
            ? BuildingFootprintClass.Small
            : Size.X <= 72f && Size.Y <= 64f
                ? BuildingFootprintClass.Medium
                : Size.X <= 128f && Size.Y <= 96f
                    ? BuildingFootprintClass.Large
                    : BuildingFootprintClass.Huge,
        Size,
        MinimumPassageClass,
        2f);
}

public static class DemoBuildingTypes
{
    private static readonly BuildingTypeProfile[] Definitions =
    [
        new(
        0, "Supply Depot", BuildingFunctionKind.Supply,
        new Vector2(48f, 48f), MovementClass.Medium,
        new EconomyCost(100, 0), 4f, 400f, 8, 0.75f,
        ConstructionMethodKind.ContinuousWorker,
        Armor: 0f, ArmorUpgradePerLevel: 1f),
        new(
        1, "Barracks", BuildingFunctionKind.Production,
        new Vector2(112f, 80f), MovementClass.Large,
        new EconomyCost(150, 0), 7f, 1000f, 0, 0.75f,
        ConstructionMethodKind.ContinuousWorker,
        Armor: 1f, ArmorUpgradePerLevel: 1f),
        new(
        2, "Command Center", BuildingFunctionKind.TownHall,
        new Vector2(160f, 120f), MovementClass.Large,
        new EconomyCost(400, 0), 10f, 1500f, 15, 0.75f,
        ConstructionMethodKind.ContinuousWorker,
        Armor: 2f, ArmorUpgradePerLevel: 1f),
        new(
        3, "Refinery", BuildingFunctionKind.Refinery,
        new Vector2(72f, 72f), MovementClass.Medium,
        new EconomyCost(75, 0), 5f, 500f, 0, 0.75f,
        ConstructionMethodKind.ContinuousWorker,
        RequiresVespeneNode: true,
        Armor: 1f, ArmorUpgradePerLevel: 1f),
        new(
        4, "Academy", BuildingFunctionKind.Research,
        new Vector2(96f, 72f), MovementClass.Large,
        new EconomyCost(150, 100), 6f, 850f, 0, 0.75f,
        ConstructionMethodKind.ContinuousWorker,
        Armor: 1f, ArmorUpgradePerLevel: 1f)
    ];

    private static readonly BuildingTypeCatalogSnapshot Catalog = BuildCatalog();

    public static BuildingTypeProfile SupplyDepot => Catalog.Type(0);
    public static BuildingTypeProfile Barracks => Catalog.Type(1);
    public static BuildingTypeProfile CommandCenter => Catalog.Type(2);
    public static BuildingTypeProfile Refinery => Catalog.Type(3);
    public static BuildingTypeProfile Academy => Catalog.Type(4);

    public static BuildingTypeProfile[] All => Catalog.Types.ToArray();
    public static BuildingTypeCatalogSnapshot CreateCatalog() => Catalog;

    private static BuildingTypeCatalogSnapshot BuildCatalog()
    {
        if (!BuildingTypeCatalogSnapshot.TryCreate(
                BuildingTypeCatalogSnapshot.CurrentFormatVersion,
                Definitions,
                out var catalog,
                out var validation) || catalog is null)
        {
            throw new InvalidOperationException(
                $"Built-in building types are invalid: {validation.FirstError}.");
        }
        return catalog;
    }
}

public enum BuildingLifecycleState : byte
{
    ReservedApproach,
    BlockedAtStart,
    Approaching,
    Constructing,
    WaitingForBuilder,
    Completed,
    Canceled,
    Destroyed
}

public enum ConstructionCommandCode : byte
{
    Success,
    InvalidPlayer,
    InvalidBuilder,
    WrongOwner,
    BuilderBusy,
    InvalidProfile,
    InsufficientMinerals,
    InsufficientVespeneGas,
    SupplyBlocked,
    PlacementRejected,
    RefineryNodeRequired,
    InvalidRefineryNode,
    RefineryAlreadyBound,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant,
    QueueFull
    ,ApproachUnavailable
}

public readonly record struct ConstructionCommandResult(
    ConstructionCommandCode Code,
    GameplayBuildingId BuildingId,
    BuildingPlacementCode PlacementCode = BuildingPlacementCode.Success)
{
    public bool Succeeded => Code == ConstructionCommandCode.Success;
}

public readonly record struct GameplayBuildingSnapshot(
    GameplayBuildingId Id,
    int PlayerId,
    BuildingTypeProfile Type,
    SimRect Bounds,
    ConstructionReservationId ReservationId,
    DynamicFootprintId FootprintId,
    BuildingLifecycleState State,
    int BuilderUnit,
    Vector2 AccessPoint,
    float Progress,
    float Health,
    float MaximumHealth,
    float EffectiveArmor,
    EconomyResourceNodeId RefineryNode)
{
    public bool IsTerminal => State is
        BuildingLifecycleState.Canceled or BuildingLifecycleState.Destroyed;
}

public enum PublicConstructionStatus : byte
{
    None,
    ClearingFriendlyUnits,
    KnownOccupant,
    WaitingForClearance
}

public readonly record struct ConstructionRuntimeEntry(
    GameplayBuildingId Id,
    int PlayerId,
    BuildingTypeProfile Type,
    SimRect Bounds,
    ConstructionReservationId ReservationId,
    DynamicFootprintId FootprintId,
    BuildingLifecycleState State,
    int BuilderUnit,
    Vector2 AccessPoint,
    float Progress,
    float Health,
    EconomyResourceNodeId RefineryNode);

public sealed record ConstructionRuntimeSnapshot(
    ConstructionRuntimeEntry[] Buildings,
    ConstructionReservationRuntimeSnapshot Reservations);

public sealed class ConstructionSystem
{
    private readonly List<BuildingEntity> _buildings = [];
    private readonly HashSet<int> _boundRefineryNodes = [];
    private readonly HashSet<int> _assignedBuilders = [];
    private readonly HashSet<int> _collisionSuppressedBuilders = [];
    private readonly HashSet<int> _busyBuilders = [];
    private readonly Dictionary<int, int> _assignedBuildingByBuilder = [];
    private bool _builderIndexesDirty = true;

    public ConstructionReservationStore Reservations { get; } = new();

    public int Count => _buildings.Count(value => !value.IsTerminal);
    public int SlotCount => _buildings.Count;
    public bool HasRuntimeState => _buildings.Count > 0;

    public bool TryFindReservationOverlap(
        SimRect bounds,
        out ConstructionReservationEntry conflict) =>
        Reservations.TryFindOverlap(bounds, out conflict);

    public bool IsAssignedBuilder(int unit)
    {
        EnsureBuilderIndexes();
        return _assignedBuilders.Contains(unit);
    }

    public bool SuppressesBuilderCombat(int unit)
    {
        EnsureBuilderIndexes();
        return _assignedBuilders.Contains(unit);
    }

    public bool SuppressesBuilderUnitCollision(int unit)
    {
        EnsureBuilderIndexes();
        return _collisionSuppressedBuilders.Contains(unit);
    }

    public ConstructionCommandResult ValidateRequest(
        EconomySystem economy,
        UnitStore units,
        int playerId,
        int builderUnit,
        BuildingTypeProfile profile,
        EconomyResourceNodeId refineryNode,
        bool allowBusyBuilder = false)
    {
        if (!economy.Players.IsRegistered(playerId))
        {
            return Failure(ConstructionCommandCode.InvalidPlayer);
        }
        if ((uint)builderUnit >= (uint)units.Count || !units.Alive[builderUnit] ||
            !economy.IsWorker(builderUnit))
        {
            return Failure(ConstructionCommandCode.InvalidBuilder);
        }
        if (!economy.IsWorkerOwnedBy(builderUnit, playerId))
        {
            return Failure(ConstructionCommandCode.WrongOwner);
        }
        EnsureBuilderIndexes();
        if (!allowBusyBuilder && _busyBuilders.Contains(builderUnit))
        {
            return Failure(ConstructionCommandCode.BuilderBusy);
        }
        if (!ValidProfile(profile))
        {
            return Failure(ConstructionCommandCode.InvalidProfile);
        }
        if (!profile.RequiresVespeneNode)
        {
            return refineryNode.Value < 0
                ? new ConstructionCommandResult(ConstructionCommandCode.Success, default)
                : Failure(ConstructionCommandCode.InvalidRefineryNode);
        }
        if (refineryNode.Value < 0)
        {
            return Failure(ConstructionCommandCode.RefineryNodeRequired);
        }
        if (!economy.IsVespeneNode(refineryNode))
        {
            return Failure(ConstructionCommandCode.InvalidRefineryNode);
        }
        return _boundRefineryNodes.Contains(refineryNode.Value)
            ? Failure(ConstructionCommandCode.RefineryAlreadyBound)
            : new ConstructionCommandResult(ConstructionCommandCode.Success, default);
    }

    public GameplayBuildingId Add(
        int playerId,
        int builderUnit,
        BuildingTypeProfile profile,
        SimRect bounds,
        EconomyResourceNodeId refineryNode,
        Vector2 accessPoint,
        long acceptedTick)
    {
        var id = new GameplayBuildingId(_buildings.Count);
        var reservationId = Reservations.Add(
            id, playerId, bounds, acceptedTick);
        _buildings.Add(new BuildingEntity(
            id, playerId, profile, bounds, reservationId, default, builderUnit,
            refineryNode, accessPoint));
        InvalidateBuilderIndexes();
        if (refineryNode.Value >= 0)
        {
            _boundRefineryNodes.Add(refineryNode.Value);
        }
        return id;
    }

    public GameplayBuildingId AddQueued(
        int playerId,
        BuildingTypeProfile profile,
        SimRect bounds,
        EconomyResourceNodeId refineryNode,
        long acceptedTick)
    {
        var id = new GameplayBuildingId(_buildings.Count);
        var reservationId = Reservations.Add(
            id, playerId, bounds, acceptedTick);
        _buildings.Add(new BuildingEntity(
            id, playerId, profile, bounds, reservationId, default, -1,
            refineryNode, (bounds.Min + bounds.Max) * 0.5f)
        {
            State = BuildingLifecycleState.WaitingForBuilder
        });
        InvalidateBuilderIndexes();
        if (refineryNode.Value >= 0)
        {
            _boundRefineryNodes.Add(refineryNode.Value);
        }
        return id;
    }

    /// <summary>
    /// Registers an already completed structure after the caller has committed
    /// its authoritative hard footprint. Intended for data-driven item or map
    /// effects that create a building without assigning a worker.
    /// </summary>
    public GameplayBuildingId AddCompleted(
        int playerId,
        BuildingTypeProfile profile,
        SimRect bounds,
        DynamicFootprintId footprintId)
    {
        if (!ValidProfile(profile) || footprintId.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(profile));
        var id = new GameplayBuildingId(_buildings.Count);
        _buildings.Add(new BuildingEntity(
            id, playerId, profile, bounds, default, footprintId, -1,
            new EconomyResourceNodeId(-1),
            (bounds.Min + bounds.Max) * 0.5f)
        {
            State = BuildingLifecycleState.Completed,
            Progress = 1f,
            Health = profile.MaximumHealth
        });
        InvalidateBuilderIndexes();
        return id;
    }

    /// <summary>
    /// Registers a committed start-and-release structure without a worker.
    /// The normal update loop advances progress and publishes completion.
    /// </summary>
    public GameplayBuildingId AddReleased(
        int playerId,
        BuildingTypeProfile profile,
        SimRect bounds,
        DynamicFootprintId footprintId)
    {
        if (!ValidProfile(profile) || footprintId.Value <= 0 ||
            profile.ConstructionMethod != ConstructionMethodKind.StartAndRelease)
            throw new ArgumentOutOfRangeException(nameof(profile));
        var id = new GameplayBuildingId(_buildings.Count);
        _buildings.Add(new BuildingEntity(
            id, playerId, profile, bounds, default, footprintId, -1,
            new EconomyResourceNodeId(-1),
            (bounds.Min + bounds.Max) * 0.5f)
        {
            State = BuildingLifecycleState.Constructing,
            Progress = 0f,
            Health = profile.MaximumHealth * 0.1f
        });
        InvalidateBuilderIndexes();
        return id;
    }

    public bool RejectQueuedReservation(
        GameplayBuildingId id,
        int playerId,
        EconomySystem economy)
    {
        if (!TryGet(id, out var building) || building.PlayerId != playerId ||
            building.State != BuildingLifecycleState.WaitingForBuilder ||
            building.BuilderUnit != -1 || !building.ReservationId.IsValid ||
            building.FootprintId.Value > 0)
        {
            return false;
        }
        if (!Reservations.Remove(building.ReservationId))
        {
            throw new InvalidOperationException(
                "Queued construction reservation disappeared before rejection.");
        }
        building.ReservationId = default;
        economy.Players.Refund(playerId, building.Type.Cost, 1f);
        ReleaseRefinery(building, economy);
        building.State = BuildingLifecycleState.Canceled;
        InvalidateBuilderIndexes();
        return true;
    }

    public void Update(
        float delta,
        long tick,
        GameplayEventStream events,
        UnitStore units,
        EconomySystem economy,
        Action<int, Vector2> moveBuilder,
        Action<int> stopBuilder,
        Func<ConstructionReservationId, SimRect, BuildingPlacementRules, int,
            HardFootprintCommitResult>
            commitHardFootprint,
        Action<int, int, GameplayBuildingId, SimRect, BuildingPlacementRules, int>
            evacuateStartOccupant)
    {
        InvalidateBuilderIndexes();
        for (var index = 0; index < _buildings.Count; index++)
        {
            var building = _buildings[index];
            if (building.State is BuildingLifecycleState.Completed or
                BuildingLifecycleState.Canceled or BuildingLifecycleState.Destroyed)
            {
                continue;
            }
            var needsBuilder = building.State is
                                   BuildingLifecycleState.ReservedApproach or
                                   BuildingLifecycleState.BlockedAtStart or
                                   BuildingLifecycleState.Approaching ||
                               building.Type.ConstructionMethod ==
                                   ConstructionMethodKind.ContinuousWorker;
            if (needsBuilder &&
                ((uint)building.BuilderUnit >= (uint)units.Count ||
                 !units.Alive[building.BuilderUnit]))
            {
                building.State = BuildingLifecycleState.WaitingForBuilder;
                continue;
            }
            if (building.State is BuildingLifecycleState.ReservedApproach or
                BuildingLifecycleState.BlockedAtStart)
            {
                if (!BuilderHasArrived(building, units))
                {
                    building.State = BuildingLifecycleState.ReservedApproach;
                    var builder = building.BuilderUnit;
                    if (units.Modes[builder] is
                            UnitMoveMode.Idle or UnitMoveMode.Hold or
                            UnitMoveMode.Arrived ||
                        Vector2.DistanceSquared(
                            units.MoveGoals[builder], building.AccessPoint) >
                        0.25f)
                    {
                        moveBuilder(builder, building.AccessPoint);
                    }
                    continue;
                }
                var placement = building.Type.PlacementProfile;
                var commit = commitHardFootprint(
                    building.ReservationId,
                    building.Bounds,
                    new BuildingPlacementRules(
                        placement.MinimumPassageClass,
                        placement.UnitPadding),
                    building.BuilderUnit);
                if (!commit.Succeeded)
                {
                    building.State = BuildingLifecycleState.BlockedAtStart;
                    stopBuilder(building.BuilderUnit);
                    if (commit.Code ==
                            HardFootprintCommitCode.DynamicOccupantRejected &&
                        commit.Assessment.Dynamic.ConflictUnit >= 0)
                    {
                        evacuateStartOccupant(
                            building.PlayerId,
                            building.BuilderUnit,
                            building.Id,
                            building.Bounds,
                            new BuildingPlacementRules(
                                placement.MinimumPassageClass,
                                placement.UnitPadding),
                            commit.Assessment.Dynamic.ConflictUnit);
                    }
                    continue;
                }
                building.FootprintId = commit.FootprintId;
                if (!Reservations.Remove(building.ReservationId))
                    throw new InvalidOperationException(
                        "Construction reservation disappeared before hard commit.");
                building.ReservationId = default;
                building.State = BuildingLifecycleState.Constructing;
                events.Publish(
                    tick,
                    GameplayEventKind.ConstructionStarted,
                    building.BuilderUnit,
                    building.Id.Value,
                    worldPosition: units.Positions[building.BuilderUnit]);
                stopBuilder(building.BuilderUnit);
                if (building.Type.ConstructionMethod ==
                    ConstructionMethodKind.StartAndRelease)
                {
                    building.BuilderUnit = -1;
                }
            }
            else if (building.State == BuildingLifecycleState.Approaching)
            {
                if (!BuilderHasArrived(building, units))
                {
                    continue;
                }
                building.State = BuildingLifecycleState.Constructing;
                events.Publish(
                    tick,
                    GameplayEventKind.ConstructionResumed,
                    building.BuilderUnit,
                    building.Id.Value,
                    worldPosition: units.Positions[building.BuilderUnit]);
                stopBuilder(building.BuilderUnit);
                if (building.Type.ConstructionMethod ==
                    ConstructionMethodKind.StartAndRelease)
                {
                    building.BuilderUnit = -1;
                }
            }
            if (building.State == BuildingLifecycleState.Constructing &&
                building.Type.ConstructionMethod ==
                    ConstructionMethodKind.ContinuousWorker &&
                !BuilderHasArrived(building, units))
            {
                building.State = BuildingLifecycleState.Approaching;
                moveBuilder(building.BuilderUnit, building.AccessPoint);
                continue;
            }
            if (building.State == BuildingLifecycleState.WaitingForBuilder)
            {
                continue;
            }
            var progressBefore = building.Progress;
            building.Progress = Math.Clamp(
                building.Progress + delta / building.Type.BuildSeconds, 0f, 1f);
            events.Publish(
                tick,
                GameplayEventKind.ConstructionProgressed,
                building.BuilderUnit,
                building.Id.Value,
                value: building.Progress - progressBefore,
                worldPosition: building.BuilderUnit >= 0
                    ? units.Positions[building.BuilderUnit]
                    : (building.Bounds.Min + building.Bounds.Max) * 0.5f);
            building.Health = MathF.Max(
                building.Health,
                building.Type.MaximumHealth * (0.1f + 0.9f * building.Progress));
            if (building.Progress < 1f)
            {
                continue;
            }
            building.State = BuildingLifecycleState.Completed;
            building.Health = building.Type.MaximumHealth;
            events.Publish(
                tick,
                GameplayEventKind.ConstructionCompleted,
                building.BuilderUnit,
                building.Id.Value,
                worldPosition: (building.Bounds.Min + building.Bounds.Max) * 0.5f);
            if (building.Type.SupplyProvided > 0)
            {
                economy.Players.AddSupplyCapacity(
                    building.PlayerId, building.Type.SupplyProvided);
            }
            if (building.Type.Function == BuildingFunctionKind.TownHall)
            {
                economy.RegisterTownHall(
                    building.PlayerId,
                    building.Id,
                    building.Bounds);
            }
            if (building.RefineryNode.Value >= 0)
            {
                economy.SetRefineryOperational(
                    building.RefineryNode, true, building.Bounds);
            }
        }
        InvalidateBuilderIndexes();
    }

    public void InterruptBuilders(ReadOnlySpan<int> units)
    {
        var changed = false;
        for (var index = 0; index < _buildings.Count; index++)
        {
            var building = _buildings[index];
            if (building.State is not
                    (BuildingLifecycleState.ReservedApproach or
                     BuildingLifecycleState.BlockedAtStart or
                     BuildingLifecycleState.Approaching or
                     BuildingLifecycleState.Constructing))
            {
                continue;
            }
            for (var unitIndex = 0; unitIndex < units.Length; unitIndex++)
            {
                if (building.BuilderUnit == units[unitIndex])
                {
                    building.State = BuildingLifecycleState.WaitingForBuilder;
                    changed = true;
                    break;
                }
            }
        }
        if (changed) InvalidateBuilderIndexes();
    }

    public bool Resume(
        GameplayBuildingId buildingId,
        int playerId,
        int builderUnit,
        EconomySystem economy,
        UnitStore units,
        Vector2 accessPoint,
        Action<int, Vector2> moveBuilder)
    {
        if (!TryGet(buildingId, out var building) ||
            building.PlayerId != playerId ||
            building.State != BuildingLifecycleState.WaitingForBuilder ||
            (uint)builderUnit >= (uint)units.Count || !units.Alive[builderUnit] ||
            !economy.IsWorkerOwnedBy(builderUnit, playerId))
        {
            return false;
        }
        building.BuilderUnit = builderUnit;
        building.AccessPoint = accessPoint;
        building.State = building.FootprintId.Value > 0
            ? BuildingLifecycleState.Approaching
            : BuildingLifecycleState.ReservedApproach;
        InvalidateBuilderIndexes();
        moveBuilder(builderUnit, building.AccessPoint);
        return true;
    }

    public bool CanResume(
        GameplayBuildingId buildingId,
        int playerId,
        int builderUnit,
        EconomySystem economy,
        UnitStore units) =>
        TryGet(buildingId, out var building) &&
        building.PlayerId == playerId &&
        building.State == BuildingLifecycleState.WaitingForBuilder &&
        (uint)builderUnit < (uint)units.Count && units.Alive[builderUnit] &&
        economy.IsWorkerOwnedBy(builderUnit, playerId) &&
        !IsAssignedBuilder(builderUnit);

    private static bool BuilderHasArrived(
        BuildingEntity building,
        UnitStore units)
    {
        var unit = building.BuilderUnit;
        return InteractionGeometry.DiscTouchesRectangle(
            units.Positions[unit],
            units.Radii[unit],
            building.Bounds);
    }

    private static bool HasActiveBuilderAssignment(BuildingEntity building) =>
        building.State is
            BuildingLifecycleState.ReservedApproach or
            BuildingLifecycleState.BlockedAtStart or
            BuildingLifecycleState.Approaching or
            BuildingLifecycleState.Constructing;

    public bool Cancel(
        GameplayBuildingId id,
        int playerId,
        EconomySystem economy,
        Func<DynamicFootprintId, bool> removeFootprint)
    {
        if (!TryGet(id, out var building) || building.PlayerId != playerId ||
            building.State is BuildingLifecycleState.Completed or
                BuildingLifecycleState.Canceled or BuildingLifecycleState.Destroyed)
        {
            return false;
        }
        if (building.FootprintId.Value > 0 &&
            !removeFootprint(building.FootprintId))
        {
            return false;
        }
        building.FootprintId = default;
        if (building.ReservationId.IsValid &&
            !Reservations.Remove(building.ReservationId))
            throw new InvalidOperationException(
                "Construction reservation disappeared before cancel.");
        building.ReservationId = default;
        economy.Players.Refund(
            playerId, building.Type.Cost, building.Type.CancelRefundFraction);
        ReleaseRefinery(building, economy);
        building.State = BuildingLifecycleState.Canceled;
        InvalidateBuilderIndexes();
        return true;
    }

    public bool ApplyDamage(
        GameplayBuildingId id,
        float damage,
        EconomySystem economy,
        Func<DynamicFootprintId, bool> removeFootprint)
    {
        if (!float.IsFinite(damage) || damage <= 0f ||
            !TryGet(id, out var building) || building.IsTerminal ||
            building.FootprintId.Value <= 0)
        {
            return false;
        }
        building.Health = MathF.Max(0f, building.Health - damage);
        if (building.Health > 0f)
        {
            return true;
        }
        if (!removeFootprint(building.FootprintId))
        {
            return false;
        }
        building.FootprintId = default;
        if (building.State == BuildingLifecycleState.Completed &&
            building.Type.SupplyProvided > 0)
        {
            economy.Players.RemoveSupplyCapacity(
                building.PlayerId, building.Type.SupplyProvided);
        }
        if (building.State == BuildingLifecycleState.Completed &&
            building.Type.Function == BuildingFunctionKind.TownHall)
        {
            economy.SetTownHallOperational(building.Id, false);
        }
        ReleaseRefinery(building, economy);
        building.State = BuildingLifecycleState.Destroyed;
        InvalidateBuilderIndexes();
        return true;
    }

    /// <summary>
    /// Restores an existing building without changing its lifecycle. Resource
    /// charging is owned by the caller so payment and mutation remain one
    /// deterministic transaction.
    /// </summary>
    public float RestoreHealth(GameplayBuildingId id, float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0f ||
            !TryGet(id, out var building) || building.IsTerminal)
            return 0f;
        var before = building.Health;
        building.Health = MathF.Min(
            building.Type.MaximumHealth, building.Health + amount);
        return building.Health - before;
    }

    /// <summary>
    /// Applies extra progress from a repairing worker. Completion remains in
    /// Update(), preserving normal events, supply, town-hall and refinery work.
    /// </summary>
    public float ApplyPowerBuild(
        GameplayBuildingId id,
        float progressDelta)
    {
        if (!float.IsFinite(progressDelta) || progressDelta <= 0f ||
            !TryGet(id, out var building) ||
            building.State != BuildingLifecycleState.Constructing)
            return 0f;
        var before = building.Progress;
        building.Progress = Math.Clamp(
            building.Progress + progressDelta, 0f, 1f);
        building.Health = MathF.Max(
            building.Health,
            building.Type.MaximumHealth *
            (0.1f + 0.9f * building.Progress));
        return building.Progress - before;
    }

    public GameplayBuildingSnapshot Observe(GameplayBuildingId id)
    {
        if (!TryGet(id, out var building))
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }
        return building.Snapshot();
    }

    public bool TryObserveFootprint(
        DynamicFootprintId footprintId,
        out GameplayBuildingSnapshot snapshot)
    {
        for (var index = 0; index < _buildings.Count; index++)
        {
            var building = _buildings[index];
            if (building.FootprintId != footprintId || building.IsTerminal)
                continue;
            snapshot = building.Snapshot();
            return true;
        }
        snapshot = default;
        return false;
    }

    public Vector2 BuilderAccessPoint(GameplayBuildingId id)
    {
        if (!TryGet(id, out var building))
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }
        return building.AccessPoint;
    }

    public GameplayBuildingSnapshot[] CreateOverview() =>
        _buildings.Select(value => value.Snapshot()).ToArray();

    internal int CopyOverview(Span<GameplayBuildingSnapshot> destination)
    {
        if (destination.Length < _buildings.Count)
            throw new ArgumentException(
                "Destination is smaller than the building store.",
                nameof(destination));
        for (var index = 0; index < _buildings.Count; index++)
            destination[index] = _buildings[index].Snapshot();
        return _buildings.Count;
    }

    public bool TryObserveAssignedBuilding(
        int builderUnit,
        out GameplayBuildingSnapshot snapshot)
    {
        EnsureBuilderIndexes();
        if (_assignedBuildingByBuilder.TryGetValue(
                builderUnit, out var buildingId))
        {
            var building = _buildings[buildingId];
            snapshot = building.Snapshot();
            return true;
        }
        snapshot = default;
        return false;
    }

    public bool TryUpdateBuilderAccessPoint(int builderUnit, Vector2 accessPoint)
    {
        EnsureBuilderIndexes();
        if (_assignedBuildingByBuilder.TryGetValue(
                builderUnit, out var buildingId))
        {
            var building = _buildings[buildingId];
            building.AccessPoint = accessPoint;
            return true;
        }
        return false;
    }

    public bool MarkBuilderApproachUnavailable(int builderUnit)
    {
        EnsureBuilderIndexes();
        if (_assignedBuildingByBuilder.TryGetValue(
                builderUnit, out var buildingId))
        {
            var building = _buildings[buildingId];
            building.BuilderUnit = -1;
            building.State = BuildingLifecycleState.WaitingForBuilder;
            InvalidateBuilderIndexes();
            return true;
        }
        return false;
    }

    internal void VisitVisionSources(
        Action<int, Vector2, BuildingFunctionKind> visitor)
    {
        for (var index = 0; index < _buildings.Count; index++)
        {
            var building = _buildings[index];
            if (building.IsTerminal || building.PlayerId <= 0 ||
                building.FootprintId.Value <= 0)
                continue;
            visitor(
                building.PlayerId,
                (building.Bounds.Min + building.Bounds.Max) * 0.5f,
                building.Type.Function);
        }
    }

    internal void VisitPerceptionSources(
        Action<int, Vector2, BuildingTypeProfile> visitor)
    {
        for (var index = 0; index < _buildings.Count; index++)
        {
            var building = _buildings[index];
            if (building.IsTerminal || building.PlayerId <= 0 ||
                building.FootprintId.Value <= 0)
                continue;
            visitor(
                building.PlayerId,
                (building.Bounds.Min + building.Bounds.Max) * 0.5f,
                building.Type);
        }
    }

    internal PlayerBuildingCapabilities CountPlayerCapabilities(int playerId)
    {
        var active = 0;
        var completed = 0;
        var townHalls = 0;
        var production = 0;
        var research = 0;
        for (var index = 0; index < _buildings.Count; index++)
        {
            var building = _buildings[index];
            if (building.PlayerId != playerId || building.IsTerminal)
                continue;
            active++;
            if (building.State != BuildingLifecycleState.Completed)
                continue;
            completed++;
            townHalls += building.Type.Function == BuildingFunctionKind.TownHall ? 1 : 0;
            production += building.Type.Function == BuildingFunctionKind.Production ? 1 : 0;
            research += building.Type.Function == BuildingFunctionKind.Research ? 1 : 0;
        }
        return new PlayerBuildingCapabilities(
            active, completed, townHalls, production, research);
    }

    public int CountCompleted(int playerId, int buildingTypeId) =>
        _buildings.Count(value =>
            value.PlayerId == playerId && value.Type.Id == buildingTypeId &&
            value.State == BuildingLifecycleState.Completed &&
            value.Health > 0f);

    /// <summary>
    /// Atomically replaces the gameplay profile of an existing completed
    /// building without changing its stable entity or footprint ID. The
    /// upgrade catalog guarantees footprint/function compatibility before the
    /// order can be issued.
    /// </summary>
    internal bool TryTransformCompleted(
        GameplayBuildingId id,
        int expectedSourceTypeId,
        BuildingTypeProfile target,
        EconomySystem economy)
    {
        if (!TryGet(id, out var building) ||
            building.State != BuildingLifecycleState.Completed ||
            building.Health <= 0f || !ValidProfile(target) ||
            building.Type.Id != expectedSourceTypeId ||
            building.Type.Id == target.Id)
            return false;

        // Source/target footprint compatibility is a content-catalog
        // invariant, validated before the match starts. Do not re-compare the
        // live source profile here: a restored or hot-reloaded building can
        // legitimately carry an older authored profile with the same stable
        // type ID. Rejecting that drift at completion leaves a paid upgrade
        // permanently wedged and used to throw out of the main game loop.
        var previous = building.Type;
        var healthRatio = Math.Clamp(
            building.Health / previous.MaximumHealth, 0f, 1f);
        var supplyDelta = target.SupplyProvided - previous.SupplyProvided;
        if (supplyDelta > 0)
            economy.Players.AddSupplyCapacity(building.PlayerId, supplyDelta);
        else if (supplyDelta < 0)
            economy.Players.RemoveSupplyCapacity(building.PlayerId, -supplyDelta);
        building.Type = target;
        building.Health = Math.Clamp(
            target.MaximumHealth * healthRatio, 1f, target.MaximumHealth);
        return true;
    }

    public ConstructionRuntimeSnapshot CaptureRuntimeState() => new(
        _buildings.Select(value => value.RuntimeSnapshot()).ToArray(),
        Reservations.CaptureRuntimeState());

    public void RestoreRuntimeState(ConstructionRuntimeSnapshot snapshot)
    {
        _buildings.Clear();
        _boundRefineryNodes.Clear();
        InvalidateBuilderIndexes();
        Reservations.RestoreRuntimeState(snapshot.Reservations);
        for (var index = 0; index < snapshot.Buildings.Length; index++)
        {
            var value = snapshot.Buildings[index];
            if (value.Id.Value != index || !ValidProfile(value.Type) ||
                !Enum.IsDefined(value.State) || value.PlayerId < 0 ||
                value.FootprintId.Value < 0 ||
                value.ReservationId.Value < 0 ||
                (value.State is BuildingLifecycleState.Canceled or
                    BuildingLifecycleState.Destroyed
                    ? value.FootprintId.Value > 0 || value.ReservationId.IsValid
                    : (value.FootprintId.Value > 0) ==
                        value.ReservationId.IsValid) ||
                (value.State is BuildingLifecycleState.ReservedApproach or
                    BuildingLifecycleState.BlockedAtStart) &&
                    !value.ReservationId.IsValid ||
                (value.State is BuildingLifecycleState.Approaching or
                    BuildingLifecycleState.Constructing or
                    BuildingLifecycleState.Completed) &&
                    value.FootprintId.Value <= 0 ||
                !float.IsFinite(value.Progress) || value.Progress is < 0f or > 1f ||
                !float.IsFinite(value.Health) || value.Health < 0f ||
                !float.IsFinite(value.AccessPoint.X) ||
                !float.IsFinite(value.AccessPoint.Y))
            {
                throw new InvalidOperationException(
                    $"Construction runtime entry is invalid: id={value.Id.Value}, " +
                    $"state={value.State}, reservation={value.ReservationId.Value}, " +
                    $"footprint={value.FootprintId.Value}, builder={value.BuilderUnit}, " +
                    $"progress={value.Progress}, health={value.Health}, " +
                    $"access={value.AccessPoint}.");
            }
            var building = new BuildingEntity(
                value.Id, value.PlayerId, value.Type, value.Bounds,
                value.ReservationId, value.FootprintId,
                value.BuilderUnit, value.RefineryNode,
                value.AccessPoint)
            {
                State = value.State,
                Progress = value.Progress,
                Health = value.Health
            };
            _buildings.Add(building);
            if (value.ReservationId.IsValid)
            {
                var reservation = snapshot.Reservations.Entries.SingleOrDefault(
                    entry => entry.Id == value.ReservationId);
                if (reservation.BuildingId != value.Id ||
                    reservation.PlayerId != value.PlayerId ||
                    reservation.Bounds != value.Bounds)
                    throw new InvalidOperationException(
                        "Construction reservation does not match its building.");
            }
            if (!building.IsTerminal && value.RefineryNode.Value >= 0 &&
                !_boundRefineryNodes.Add(value.RefineryNode.Value))
            {
                throw new InvalidOperationException(
                    "A Vespene node is bound by multiple buildings.");
            }
        }
        if (Reservations.Count != _buildings.Count(value =>
                !value.IsTerminal && value.ReservationId.IsValid))
            throw new InvalidOperationException(
                "Construction reservation count does not match buildings.");
        InvalidateBuilderIndexes();
    }

    public bool OwnsActiveFootprint(DynamicFootprintId footprintId) =>
        footprintId.Value > 0 &&
        _buildings.Any(value =>
            !value.IsTerminal && value.FootprintId == footprintId);

    public bool IsEnemyTarget(GameplayBuildingId id, int playerId) =>
        TryGet(id, out var building) && !building.IsTerminal &&
        building.FootprintId.Value > 0 && building.PlayerId != playerId;

    public bool IsAlive(GameplayBuildingId id) =>
        TryGet(id, out var building) && !building.IsTerminal &&
        building.Health > 0f;

    internal void AppendStateHash(ref StableHash64 hash)
    {
        Reservations.AppendStateHash(ref hash);
        hash.Add(_buildings.Count);
        foreach (var building in _buildings)
        {
            var value = building.Snapshot();
            hash.Add(value.Id.Value);
            hash.Add(value.PlayerId);
            hash.Add(value.Type.Id);
            hash.Add((byte)value.Type.Function);
            hash.Add(value.Type.Size);
            hash.Add((byte)value.Type.MinimumPassageClass);
            hash.Add(value.Type.Cost.Minerals);
            hash.Add(value.Type.Cost.VespeneGas);
            hash.Add(value.Type.Cost.Supply);
            hash.Add(value.Type.BuildSeconds);
            hash.Add(value.Type.MaximumHealth);
            hash.Add(value.Type.SupplyProvided);
            hash.Add(value.Type.CancelRefundFraction);
            hash.Add((byte)value.Type.ConstructionMethod);
            hash.Add(value.Type.RequiresVespeneNode);
            hash.Add(value.Type.Armor);
            hash.Add((ushort)value.Type.Attributes);
            hash.Add(value.Type.ArmorUpgradePerLevel);
            hash.Add((byte)value.Type.ArmorType);
            hash.Add(value.Bounds.Min);
            hash.Add(value.Bounds.Max);
            hash.Add(value.ReservationId.Value);
            hash.Add(value.FootprintId.Value);
            hash.Add((byte)value.State);
            hash.Add(value.BuilderUnit);
            hash.Add(building.AccessPoint);
            hash.Add(value.Progress);
            hash.Add(value.Health);
            hash.Add(value.RefineryNode.Value);
        }
    }

    private bool TryGet(GameplayBuildingId id, out BuildingEntity building)
    {
        if ((uint)id.Value < (uint)_buildings.Count)
        {
            building = _buildings[id.Value];
            return true;
        }
        building = null!;
        return false;
    }

    private void InvalidateBuilderIndexes() => _builderIndexesDirty = true;

    private void EnsureBuilderIndexes()
    {
        if (!_builderIndexesDirty) return;
        _assignedBuilders.Clear();
        _collisionSuppressedBuilders.Clear();
        _busyBuilders.Clear();
        _assignedBuildingByBuilder.Clear();
        for (var index = 0; index < _buildings.Count; index++)
        {
            var building = _buildings[index];
            var builder = building.BuilderUnit;
            if (builder < 0) continue;
            if (!building.IsTerminal &&
                building.State != BuildingLifecycleState.Completed)
                _busyBuilders.Add(builder);
            if (!HasActiveBuilderAssignment(building)) continue;
            _assignedBuilders.Add(builder);
            _assignedBuildingByBuilder.TryAdd(builder, index);
            if (building.State is BuildingLifecycleState.ReservedApproach or
                BuildingLifecycleState.BlockedAtStart)
                _collisionSuppressedBuilders.Add(builder);
        }
        _builderIndexesDirty = false;
    }

    private void ReleaseRefinery(BuildingEntity building, EconomySystem economy)
    {
        if (building.RefineryNode.Value < 0)
        {
            return;
        }
        _boundRefineryNodes.Remove(building.RefineryNode.Value);
        economy.SetRefineryOperational(building.RefineryNode, false);
    }

    internal static bool ValidProfile(BuildingTypeProfile profile) =>
        profile.Id >= 0 && !string.IsNullOrWhiteSpace(profile.Name) &&
        Enum.IsDefined(profile.Function) && Enum.IsDefined(profile.MinimumPassageClass) &&
        Enum.IsDefined(profile.ConstructionMethod) && profile.Cost.IsValid &&
        float.IsFinite(profile.Size.X) && profile.Size.X > 0f &&
        float.IsFinite(profile.Size.Y) && profile.Size.Y > 0f &&
        float.IsFinite(profile.BuildSeconds) && profile.BuildSeconds > 0f &&
        float.IsFinite(profile.MaximumHealth) && profile.MaximumHealth > 0f &&
        float.IsFinite(profile.Armor) &&
        (profile.Attributes & ~CombatAttribute.All) == 0 &&
        (profile.Attributes & CombatAttribute.Structure) != 0 &&
        float.IsFinite(profile.ArmorUpgradePerLevel) &&
        profile.ArmorUpgradePerLevel >= 0f &&
        Enum.IsDefined(profile.ArmorType) &&
        ValidBuildingCombat(profile.Combat) &&
        ValidBuildingPerception(profile.Perception) &&
        profile.SupplyProvided >= 0 &&
        float.IsFinite(profile.CancelRefundFraction) &&
        profile.CancelRefundFraction is >= 0f and <= 1f;

    private static bool ValidBuildingCombat(
        in BuildingCombatProfileSnapshot value)
    {
        try
        {
            value.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool ValidBuildingPerception(
        in BuildingPerceptionProfileSnapshot value)
    {
        try
        {
            value.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static ConstructionCommandResult Failure(ConstructionCommandCode code) =>
        new(code, default);

    private sealed class BuildingEntity
    {
        public BuildingEntity(
            GameplayBuildingId id,
            int playerId,
            BuildingTypeProfile type,
            SimRect bounds,
            ConstructionReservationId reservationId,
            DynamicFootprintId footprintId,
            int builderUnit,
            EconomyResourceNodeId refineryNode,
            Vector2 accessPoint)
        {
            Id = id;
            PlayerId = playerId;
            Type = type;
            Bounds = bounds;
            ReservationId = reservationId;
            FootprintId = footprintId;
            BuilderUnit = builderUnit;
            RefineryNode = refineryNode;
            AccessPoint = accessPoint;
            State = reservationId.IsValid
                ? BuildingLifecycleState.ReservedApproach
                : BuildingLifecycleState.Approaching;
            Health = type.MaximumHealth * 0.1f;
        }

        public GameplayBuildingId Id { get; }
        public int PlayerId { get; }
        public BuildingTypeProfile Type { get; set; }
        public SimRect Bounds { get; }
        public ConstructionReservationId ReservationId { get; set; }
        public DynamicFootprintId FootprintId { get; set; }
        public int BuilderUnit { get; set; }
        public EconomyResourceNodeId RefineryNode { get; }
        public Vector2 AccessPoint { get; set; }
        public BuildingLifecycleState State { get; set; }
        public float Progress { get; set; }
        public float Health { get; set; }
        public bool IsTerminal => State is
            BuildingLifecycleState.Canceled or BuildingLifecycleState.Destroyed;

        public GameplayBuildingSnapshot Snapshot() => new(
            Id, PlayerId, Type, Bounds, ReservationId, FootprintId,
            State, BuilderUnit, AccessPoint,
            Progress, Health, Type.MaximumHealth,
            State == BuildingLifecycleState.Completed ? Type.Armor : 0f,
            RefineryNode);

        public ConstructionRuntimeEntry RuntimeSnapshot() => new(
            Id, PlayerId, Type, Bounds, ReservationId, FootprintId,
            State, BuilderUnit,
            AccessPoint, Progress, Health, RefineryNode);
    }
}
