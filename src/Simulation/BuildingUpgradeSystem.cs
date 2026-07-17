using System.Collections.Immutable;
using System.Text;

namespace RtsDemo.Simulation;

public readonly record struct BuildingUpgradeProfile(
    int Id,
    string Name,
    int SourceBuildingTypeId,
    BuildingTypeProfile TargetType,
    EconomyCost Cost,
    float UpgradeSeconds,
    float CancelRefundFraction)
{
    public ImmutableArray<TechnologyRequirementProfile> Requirements { get; init; } = [];
}

/// <summary>
/// Immutable content boundary for in-place building transformations. The
/// simulation only sees dense building IDs and resolved profiles; Warcraft
/// rawcodes remain in the faction adapter.
/// </summary>
public sealed class BuildingUpgradeCatalogSnapshot
{
    public const int CurrentFormatVersion = 2;
    private readonly BuildingUpgradeProfile[] _profiles;
    private readonly Dictionary<int, BuildingUpgradeProfile[]> _bySource;
    private readonly Dictionary<int, BuildingUpgradeProfile> _byId;
    private readonly Dictionary<int, int> _parentByTarget;

    private BuildingUpgradeCatalogSnapshot(
        int formatVersion,
        BuildingUpgradeProfile[] profiles)
    {
        FormatVersion = formatVersion;
        _profiles = profiles;
        _bySource = profiles
            .GroupBy(value => value.SourceBuildingTypeId)
            .ToDictionary(
                value => value.Key,
                value => value.OrderBy(profile => profile.Id).ToArray());
        _byId = profiles.ToDictionary(value => value.Id);
        _parentByTarget = profiles.ToDictionary(
            value => value.TargetType.Id,
            value => value.SourceBuildingTypeId);
        CanonicalBytes = Serialize();
        StableHash = StableHash64.Compute(CanonicalBytes);
    }

    public int FormatVersion { get; }
    public ReadOnlySpan<BuildingUpgradeProfile> Profiles => _profiles;
    public byte[] CanonicalBytes { get; }
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");

    public bool TryForSource(
        int sourceBuildingTypeId,
        out BuildingUpgradeProfile profile)
    {
        profile = default;
        if (!_bySource.TryGetValue(sourceBuildingTypeId, out var profiles) ||
            profiles.Length == 0)
            return false;
        profile = profiles[0];
        return true;
    }

    public ReadOnlySpan<BuildingUpgradeProfile> ForSource(
        int sourceBuildingTypeId) =>
        _bySource.TryGetValue(sourceBuildingTypeId, out var profiles)
            ? profiles
            : [];

    public bool TryGet(int profileId, out BuildingUpgradeProfile profile) =>
        _byId.TryGetValue(profileId, out profile);

    /// <summary>
    /// Upgraded buildings retain the production/research capabilities of
    /// their ancestors. This keeps one canonical peasant recipe and one
    /// canonical technology level instead of cloning them per tier.
    /// </summary>
    public bool SatisfiesBuildingType(int actualTypeId, int requiredTypeId)
    {
        if (actualTypeId == requiredTypeId) return true;
        var visited = 0;
        while (_parentByTarget.TryGetValue(actualTypeId, out var parent))
        {
            if (parent == requiredTypeId) return true;
            actualTypeId = parent;
            if (++visited > _profiles.Length) return false;
        }
        return false;
    }

    public static BuildingUpgradeCatalogSnapshot Empty { get; } =
        new(CurrentFormatVersion, []);

    public static bool TryCreate(
        int formatVersion,
        ReadOnlySpan<BuildingUpgradeProfile> profiles,
        out BuildingUpgradeCatalogSnapshot? snapshot,
        out string error)
    {
        snapshot = null;
        if (formatVersion != CurrentFormatVersion)
        {
            error = $"Expected building upgrade catalog {CurrentFormatVersion}.";
            return false;
        }
        var copy = profiles.ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<int>();
        for (var index = 0; index < copy.Length; index++)
        {
            var value = copy[index];
            if (value.Id != index || !ValidProfile(value) ||
                !names.Add(value.Name) ||
                !targets.Add(value.TargetType.Id))
            {
                error = $"Building upgrade {index} is invalid or duplicated.";
                return false;
            }
        }
        var sourceToTargets = copy
            .GroupBy(value => value.SourceBuildingTypeId)
            .ToDictionary(
                value => value.Key,
                value => value.Select(profile => profile.TargetType.Id).ToArray());
        foreach (var source in sourceToTargets.Keys)
        {
            var visiting = new HashSet<int>();
            if (HasCycle(source))
            {
                error = "Building upgrade catalog contains a cycle.";
                return false;
            }

            bool HasCycle(int cursor)
            {
                if (!visiting.Add(cursor)) return true;
                if (sourceToTargets.TryGetValue(cursor, out var next))
                    foreach (var target in next)
                        if (HasCycle(target)) return true;
                visiting.Remove(cursor);
                return false;
            }
        }
        snapshot = new BuildingUpgradeCatalogSnapshot(formatVersion, copy);
        error = string.Empty;
        return true;
    }

    public static bool TryValidateDependencies(
        BuildingUpgradeCatalogSnapshot upgrades,
        BuildingTypeCatalogSnapshot buildings,
        out string error)
    {
        foreach (var value in upgrades.Profiles)
        {
            if ((uint)value.SourceBuildingTypeId >= (uint)buildings.Types.Length ||
                (uint)value.TargetType.Id >= (uint)buildings.Types.Length ||
                value.TargetType != buildings.Type(value.TargetType.Id))
            {
                error = $"Building upgrade {value.Id} references an unknown type.";
                return false;
            }
            var source = buildings.Type(value.SourceBuildingTypeId);
            if (source.Function != value.TargetType.Function ||
                source.Size != value.TargetType.Size ||
                source.MinimumPassageClass != value.TargetType.MinimumPassageClass ||
                source.RequiresVespeneNode != value.TargetType.RequiresVespeneNode ||
                source.ConstructionMethod != value.TargetType.ConstructionMethod)
            {
                error = $"Building upgrade {value.Id} changes footprint or function.";
                return false;
            }
            foreach (var requirement in value.Requirements)
            {
                if (requirement.Kind == TechnologyRequirementKind.CompletedBuilding &&
                    (uint)requirement.TargetId >= (uint)buildings.Types.Length)
                {
                    error = $"Building upgrade {value.Id} has an unknown prerequisite.";
                    return false;
                }
            }
        }
        error = string.Empty;
        return true;
    }

    internal static bool ValidProfile(in BuildingUpgradeProfile value)
    {
        if (value.Id < 0 || string.IsNullOrWhiteSpace(value.Name) ||
            value.SourceBuildingTypeId < 0 ||
            value.SourceBuildingTypeId == value.TargetType.Id ||
            !ConstructionSystem.ValidProfile(value.TargetType) ||
            !value.Cost.IsValid || value.Cost.Supply != 0 ||
            !float.IsFinite(value.UpgradeSeconds) || value.UpgradeSeconds <= 0f ||
            !float.IsFinite(value.CancelRefundFraction) ||
            value.CancelRefundFraction is < 0f or > 1f ||
            value.Requirements.IsDefault || value.Requirements.Length > 32)
            return false;
        var keys = new HashSet<(TechnologyRequirementKind, int)>();
        foreach (var requirement in value.Requirements)
        {
            if (!Enum.IsDefined(requirement.Kind) || requirement.TargetId < 0 ||
                requirement.Value <= 0 ||
                !keys.Add((requirement.Kind, requirement.TargetId)))
                return false;
        }
        return true;
    }

    internal static bool ProfileEquals(
        in BuildingUpgradeProfile left,
        in BuildingUpgradeProfile right) =>
        left with { Requirements = default } ==
            right with { Requirements = default } &&
        left.Requirements.AsSpan().SequenceEqual(right.Requirements.AsSpan());

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(FormatVersion);
        writer.Write(_profiles.Length);
        foreach (var value in _profiles)
            BuildingUpgradeSerialization.WriteProfile(writer, value);
        writer.Flush();
        return stream.ToArray();
    }
}

public readonly record struct BuildingUpgradeOrderId(int Value);

public enum BuildingUpgradeCommandCode : byte
{
    Success,
    InvalidPlayer,
    InvalidBuilding,
    WrongOwner,
    BuildingNotCompleted,
    InvalidProfile,
    WrongSourceType,
    AlreadyUpgrading,
    BuildingBusy,
    MissingPrerequisite,
    InsufficientMinerals,
    InsufficientVespeneGas,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant
}

public readonly record struct BuildingUpgradeCommandResult(
    BuildingUpgradeCommandCode Code,
    BuildingUpgradeOrderId OrderId)
{
    public bool Succeeded => Code == BuildingUpgradeCommandCode.Success;
}

public readonly record struct BuildingUpgradeRequirementStatus(
    TechnologyRequirementProfile Requirement,
    int CurrentValue)
{
    public bool Satisfied => CurrentValue >= Requirement.Value;
}

public readonly record struct BuildingUpgradeAvailabilitySnapshot(
    BuildingUpgradeCommandCode Code,
    BuildingUpgradeRequirementStatus[] Requirements)
{
    public bool Available => Code == BuildingUpgradeCommandCode.Success;
}

public readonly record struct BuildingUpgradeOrderSnapshot(
    BuildingUpgradeOrderId Id,
    GameplayBuildingId Building,
    int PlayerId,
    BuildingUpgradeProfile Profile,
    float Progress);

public readonly record struct BuildingUpgradeOrderRuntimeEntry(
    BuildingUpgradeOrderId Id,
    GameplayBuildingId Building,
    int PlayerId,
    BuildingUpgradeProfile Profile,
    float Progress);

public sealed record BuildingUpgradeRuntimeSnapshot(
    int NextOrderId,
    BuildingUpgradeProfile[] CatalogProfiles,
    BuildingUpgradeOrderRuntimeEntry[] Orders);

public sealed class BuildingUpgradeSystem
{
    private readonly Dictionary<int, BuildingUpgradeOrder> _orders = [];
    private BuildingUpgradeCatalogSnapshot _catalog =
        BuildingUpgradeCatalogSnapshot.Empty;
    private int _nextOrderId = 1;

    public BuildingUpgradeCatalogSnapshot Catalog => _catalog;
    public int ActiveOrderCount => _orders.Count;

    public void ConfigureCatalog(BuildingUpgradeCatalogSnapshot catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (_orders.Count > 0)
            throw new InvalidOperationException(
                "Building upgrade catalog cannot change while orders are active.");
        _catalog = catalog;
    }

    public bool IsUpgrading(GameplayBuildingId building) =>
        _orders.ContainsKey(building.Value);

    public bool SatisfiesBuildingType(int actualTypeId, int requiredTypeId) =>
        _catalog.SatisfiesBuildingType(actualTypeId, requiredTypeId);

    public BuildingUpgradeCommandResult Enqueue(
        int playerId,
        GameplayBuildingId building,
        BuildingUpgradeProfile profile,
        ConstructionSystem construction,
        PlayerEconomyStore economy,
        Func<GameplayBuildingId, bool> isBuildingBusy,
        Func<int, int> technologyLevel)
    {
        var validation = ValidateEnqueue(
            playerId, building, profile, construction, economy,
            isBuildingBusy, technologyLevel);
        if (!validation.Succeeded) return validation;
        if (!economy.TrySpend(playerId, profile.Cost).Succeeded)
            throw new InvalidOperationException(
                "Building upgrade cost changed after validation.");
        var id = new BuildingUpgradeOrderId(_nextOrderId++);
        _orders.Add(building.Value,
            new BuildingUpgradeOrder(id, building, playerId, profile));
        return new BuildingUpgradeCommandResult(
            BuildingUpgradeCommandCode.Success, id);
    }

    public BuildingUpgradeCommandResult ValidateEnqueue(
        int playerId,
        GameplayBuildingId building,
        BuildingUpgradeProfile profile,
        ConstructionSystem construction,
        PlayerEconomyStore economy,
        Func<GameplayBuildingId, bool> isBuildingBusy,
        Func<int, int> technologyLevel)
    {
        if (!economy.IsRegistered(playerId))
            return Failure(BuildingUpgradeCommandCode.InvalidPlayer);
        if (!BuildingUpgradeCatalogSnapshot.ValidProfile(profile) ||
            !_catalog.TryGet(profile.Id, out var canonical) ||
            !BuildingUpgradeCatalogSnapshot.ProfileEquals(profile, canonical))
            return Failure(BuildingUpgradeCommandCode.InvalidProfile);
        if (!construction.IsAlive(building))
            return Failure(BuildingUpgradeCommandCode.InvalidBuilding);
        var source = construction.Observe(building);
        if (source.PlayerId != playerId)
            return Failure(BuildingUpgradeCommandCode.WrongOwner);
        if (source.State != BuildingLifecycleState.Completed)
            return Failure(BuildingUpgradeCommandCode.BuildingNotCompleted);
        if (source.Type.Id != profile.SourceBuildingTypeId)
            return Failure(BuildingUpgradeCommandCode.WrongSourceType);
        if (_orders.ContainsKey(building.Value))
            return Failure(BuildingUpgradeCommandCode.AlreadyUpgrading);
        if (isBuildingBusy(building))
            return Failure(BuildingUpgradeCommandCode.BuildingBusy);
        if (EvaluateRequirements(
                playerId, profile.Requirements, construction, technologyLevel)
            .Any(value => !value.Satisfied))
            return Failure(BuildingUpgradeCommandCode.MissingPrerequisite);
        return economy.ValidateSpend(playerId, profile.Cost).Code switch
        {
            EconomyTransactionCode.Success =>
                new BuildingUpgradeCommandResult(
                    BuildingUpgradeCommandCode.Success, default),
            EconomyTransactionCode.InsufficientMinerals =>
                Failure(BuildingUpgradeCommandCode.InsufficientMinerals),
            EconomyTransactionCode.InsufficientVespeneGas =>
                Failure(BuildingUpgradeCommandCode.InsufficientVespeneGas),
            _ => Failure(BuildingUpgradeCommandCode.InvalidPlayer)
        };
    }

    public BuildingUpgradeAvailabilitySnapshot ObserveAvailability(
        int playerId,
        GameplayBuildingId building,
        BuildingUpgradeProfile profile,
        ConstructionSystem construction,
        PlayerEconomyStore economy,
        Func<GameplayBuildingId, bool> isBuildingBusy,
        Func<int, int> technologyLevel) => new(
        ValidateEnqueue(
            playerId, building, profile, construction, economy,
            isBuildingBusy, technologyLevel).Code,
        BuildingUpgradeCatalogSnapshot.ValidProfile(profile)
            ? EvaluateRequirements(
                playerId, profile.Requirements, construction, technologyLevel)
            : []);

    public bool Cancel(
        int playerId,
        BuildingUpgradeOrderId orderId,
        PlayerEconomyStore economy,
        out GameplayBuildingId building)
    {
        building = default;
        var pair = _orders.FirstOrDefault(value =>
            value.Value.Id == orderId && value.Value.PlayerId == playerId);
        if (pair.Value is null) return false;
        var order = pair.Value;
        economy.Refund(
            playerId, order.Profile.Cost,
            order.Profile.CancelRefundFraction);
        building = order.Building;
        _orders.Remove(pair.Key);
        return true;
    }

    public void Update(
        float delta,
        long tick,
        GameplayEventStream events,
        ConstructionSystem construction,
        EconomySystem economy)
    {
        List<int>? retired = null;
        foreach (var pair in _orders.OrderBy(value => value.Key))
        {
            var order = pair.Value;
            if (!construction.IsAlive(order.Building))
            {
                economy.Players.Refund(order.PlayerId, order.Profile.Cost);
                retired ??= [];
                retired.Add(pair.Key);
                continue;
            }
            order.Progress = Math.Clamp(
                order.Progress + delta / order.Profile.UpgradeSeconds, 0f, 1f);
            if (order.Progress < 1f) continue;
            var before = construction.Observe(order.Building);
            var alreadyApplied =
                before.State == BuildingLifecycleState.Completed &&
                before.Health > 0f &&
                before.Type.Id == order.Profile.TargetType.Id;
            if (!alreadyApplied && !construction.TryTransformCompleted(
                    order.Building,
                    order.Profile.SourceBuildingTypeId,
                    order.Profile.TargetType,
                    economy))
            {
                // Runtime state can be replaced by destruction, restoration,
                // or a content hot reload between enqueue and completion. A
                // paid order must never crash every subsequent simulation
                // tick. Retire the stale order and fully refund it.
                economy.Players.Refund(
                    order.PlayerId, order.Profile.Cost);
                events.Publish(
                    tick,
                    GameplayEventKind.BuildingUpgradeCanceled,
                    building: order.Building.Value,
                    value: order.Profile.TargetType.Id,
                    worldPosition:
                        (before.Bounds.Min + before.Bounds.Max) * 0.5f,
                    player: order.PlayerId);
                retired ??= [];
                retired.Add(pair.Key);
                continue;
            }
            events.Publish(
                tick,
                GameplayEventKind.BuildingUpgradeCompleted,
                building: order.Building.Value,
                value: order.Profile.TargetType.Id,
                worldPosition: (before.Bounds.Min + before.Bounds.Max) * 0.5f,
                player: order.PlayerId);
            retired ??= [];
            retired.Add(pair.Key);
        }
        if (retired is not null)
            foreach (var key in retired) _orders.Remove(key);
    }

    public bool TryObserve(
        GameplayBuildingId building,
        out BuildingUpgradeOrderSnapshot snapshot)
    {
        if (_orders.TryGetValue(building.Value, out var order))
        {
            snapshot = order.Snapshot();
            return true;
        }
        snapshot = default;
        return false;
    }

    public BuildingUpgradeRuntimeSnapshot CaptureRuntimeState() => new(
        _nextOrderId,
        _catalog.Profiles.ToArray(),
        _orders.Values.OrderBy(value => value.Building.Value)
            .Select(value => value.RuntimeSnapshot()).ToArray());

    /// <summary>
    /// An upgrading building cannot simultaneously own a production or
    /// research order. Snapshot/package restoration calls this after all
    /// three systems have been restored so malformed cross-system state is
    /// rejected instead of being advanced ambiguously on the next tick.
    /// </summary>
    public void ValidateQueueExclusivity(
        ProductionSystem production,
        TechnologySystem technology)
    {
        foreach (var order in _orders.Values)
        {
            if (production.Observe(order.Building).Orders.Length > 0 ||
                technology.Observe(order.Building).Orders.Length > 0)
                throw new InvalidOperationException(
                    "An upgrading building also contains an active queue.");
        }
    }

    public void RestoreRuntimeState(
        BuildingUpgradeRuntimeSnapshot snapshot,
        ConstructionSystem construction,
        PlayerEconomyStore economy)
    {
        if (snapshot.NextOrderId <= 0 ||
            !BuildingUpgradeCatalogSnapshot.TryCreate(
                BuildingUpgradeCatalogSnapshot.CurrentFormatVersion,
                snapshot.CatalogProfiles,
                out var catalog,
                out _) || catalog is null)
            throw new InvalidOperationException("Invalid building upgrade runtime.");
        _orders.Clear();
        _catalog = catalog;
        var ids = new HashSet<int>();
        var maximumId = 0;
        foreach (var value in snapshot.Orders)
        {
            if (value.Id.Value <= 0 || !ids.Add(value.Id.Value) ||
                !economy.IsRegistered(value.PlayerId) ||
                !construction.IsAlive(value.Building) ||
                construction.Observe(value.Building).PlayerId != value.PlayerId ||
                construction.Observe(value.Building).State !=
                    BuildingLifecycleState.Completed ||
                construction.Observe(value.Building).Type.Id !=
                    value.Profile.SourceBuildingTypeId ||
                !_catalog.TryGet(value.Profile.Id, out var canonical) ||
                !BuildingUpgradeCatalogSnapshot.ProfileEquals(
                    value.Profile, canonical) ||
                !float.IsFinite(value.Progress) ||
                value.Progress is < 0f or >= 1f ||
                !_orders.TryAdd(value.Building.Value,
                    new BuildingUpgradeOrder(
                        value.Id, value.Building, value.PlayerId, value.Profile)
                    {
                        Progress = value.Progress
                    }))
                throw new InvalidOperationException(
                    "Invalid active building upgrade order.");
            maximumId = Math.Max(maximumId, value.Id.Value);
        }
        if (snapshot.NextOrderId <= maximumId)
            throw new InvalidOperationException(
                "Next building upgrade order ID is stale.");
        _nextOrderId = snapshot.NextOrderId;
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add(_nextOrderId);
        hash.Add(_catalog.FormatVersion);
        hash.Add(_catalog.Profiles.Length);
        foreach (var profile in _catalog.Profiles)
            AppendProfileHash(ref hash, profile);
        hash.Add(_orders.Count);
        foreach (var order in _orders.Values.OrderBy(value => value.Building.Value))
        {
            hash.Add(order.Id.Value);
            hash.Add(order.Building.Value);
            hash.Add(order.PlayerId);
            AppendProfileHash(ref hash, order.Profile);
            hash.Add(order.Progress);
        }
    }

    private BuildingUpgradeRequirementStatus[] EvaluateRequirements(
        int playerId,
        ImmutableArray<TechnologyRequirementProfile> requirements,
        ConstructionSystem construction,
        Func<int, int> technologyLevel)
    {
        var result = new BuildingUpgradeRequirementStatus[requirements.Length];
        var buildings = construction.CreateOverview();
        for (var index = 0; index < requirements.Length; index++)
        {
            var value = requirements[index];
            var current = value.Kind switch
            {
                TechnologyRequirementKind.CompletedBuilding =>
                    buildings.Count(building =>
                        building.PlayerId == playerId &&
                        building.State == BuildingLifecycleState.Completed &&
                        building.Health > 0f &&
                        _catalog.SatisfiesBuildingType(
                            building.Type.Id, value.TargetId)),
                TechnologyRequirementKind.TechnologyLevel =>
                    technologyLevel(value.TargetId),
                _ => 0
            };
            result[index] = new BuildingUpgradeRequirementStatus(value, current);
        }
        return result;
    }

    private static void AppendProfileHash(
        ref StableHash64 hash,
        in BuildingUpgradeProfile value)
    {
        hash.Add(value.Id);
        hash.Add(value.SourceBuildingTypeId);
        hash.Add(value.TargetType.Id);
        hash.Add((byte)value.TargetType.Function);
        hash.Add(value.TargetType.Size);
        hash.Add((byte)value.TargetType.MinimumPassageClass);
        hash.Add(value.TargetType.Cost.Minerals);
        hash.Add(value.TargetType.Cost.VespeneGas);
        hash.Add(value.TargetType.Cost.Supply);
        hash.Add(value.TargetType.BuildSeconds);
        hash.Add(value.TargetType.MaximumHealth);
        hash.Add(value.TargetType.SupplyProvided);
        hash.Add(value.TargetType.CancelRefundFraction);
        hash.Add((byte)value.TargetType.ConstructionMethod);
        hash.Add(value.TargetType.RequiresVespeneNode);
        hash.Add(value.TargetType.Armor);
        hash.Add((ushort)value.TargetType.Attributes);
        hash.Add(value.TargetType.ArmorUpgradePerLevel);
        hash.Add((byte)value.TargetType.ArmorType);
        hash.Add(value.Cost.Minerals);
        hash.Add(value.Cost.VespeneGas);
        hash.Add(value.Cost.Supply);
        hash.Add(value.UpgradeSeconds);
        hash.Add(value.CancelRefundFraction);
        hash.Add(value.Requirements.Length);
        foreach (var requirement in value.Requirements)
        {
            hash.Add((byte)requirement.Kind);
            hash.Add(requirement.TargetId);
            hash.Add(requirement.Value);
        }
    }

    private static BuildingUpgradeCommandResult Failure(
        BuildingUpgradeCommandCode code) => new(code, default);

    private sealed class BuildingUpgradeOrder(
        BuildingUpgradeOrderId id,
        GameplayBuildingId building,
        int playerId,
        BuildingUpgradeProfile profile)
    {
        public BuildingUpgradeOrderId Id { get; } = id;
        public GameplayBuildingId Building { get; } = building;
        public int PlayerId { get; } = playerId;
        public BuildingUpgradeProfile Profile { get; } = profile;
        public float Progress { get; set; }

        public BuildingUpgradeOrderSnapshot Snapshot() => new(
            Id, Building, PlayerId, Profile, Progress);
        public BuildingUpgradeOrderRuntimeEntry RuntimeSnapshot() => new(
            Id, Building, PlayerId, Profile, Progress);
    }
}

internal static class BuildingUpgradeSerialization
{
    public static void WriteProfile(
        BinaryWriter writer,
        in BuildingUpgradeProfile value)
    {
        writer.Write(value.Id);
        TechnologySerialization.WriteString(writer, value.Name);
        writer.Write(value.SourceBuildingTypeId);
        ConstructionSerialization.WriteProfile(writer, value.TargetType);
        writer.Write(value.Cost.Minerals);
        writer.Write(value.Cost.VespeneGas);
        writer.Write(value.Cost.Supply);
        writer.Write(value.UpgradeSeconds);
        writer.Write(value.CancelRefundFraction);
        writer.Write(value.Requirements.Length);
        foreach (var requirement in value.Requirements)
        {
            writer.Write((byte)requirement.Kind);
            writer.Write(requirement.TargetId);
            writer.Write(requirement.Value);
        }
    }

    public static BuildingUpgradeProfile ReadProfile(BinaryReader reader)
    {
        var value = new BuildingUpgradeProfile(
            reader.ReadInt32(),
            TechnologySerialization.ReadString(reader),
            reader.ReadInt32(),
            ConstructionSerialization.ReadProfile(reader),
            new EconomyCost(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()),
            reader.ReadSingle(),
            reader.ReadSingle());
        var count = reader.ReadInt32();
        if (count is < 0 or > 32) throw new InvalidDataException();
        var requirements = new TechnologyRequirementProfile[count];
        for (var index = 0; index < count; index++)
            requirements[index] = new TechnologyRequirementProfile(
                (TechnologyRequirementKind)reader.ReadByte(),
                reader.ReadInt32(), reader.ReadInt32());
        return value with { Requirements = [.. requirements] };
    }
}
