using System.Collections.Immutable;
using System.Text;

namespace RtsDemo.Simulation;

public enum TechnologyRequirementKind : byte
{
    CompletedBuilding,
    TechnologyLevel
}

public readonly record struct TechnologyRequirementProfile(
    TechnologyRequirementKind Kind,
    int TargetId,
    int Value);

public readonly record struct TechnologyProfile(
    int Id,
    string Name,
    int ResearcherBuildingTypeId,
    EconomyCost Cost,
    float ResearchSeconds,
    int MaximumLevel,
    float CancelRefundFraction,
    int ExclusiveGroupId)
{
    public ImmutableArray<TechnologyRequirementProfile> Requirements { get; init; } = [];
}

public sealed class TechnologyCatalogSnapshot
{
    public const int CurrentFormatVersion = 1;
    private readonly TechnologyProfile[] _technologies;
    private readonly byte[] _canonicalBytes;

    private TechnologyCatalogSnapshot(int formatVersion, TechnologyProfile[] technologies)
    {
        FormatVersion = formatVersion;
        _technologies = technologies;
        _canonicalBytes = Serialize();
        StableHash = StableHash64.Compute(_canonicalBytes);
    }

    public int FormatVersion { get; }
    public ReadOnlySpan<TechnologyProfile> Technologies => _technologies;
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");
    public TechnologyProfile Technology(int id) => _technologies[id];

    public static bool TryCreate(
        int formatVersion,
        ReadOnlySpan<TechnologyProfile> technologies,
        out TechnologyCatalogSnapshot? snapshot,
        out string error)
    {
        var copy = technologies.ToArray();
        if (formatVersion != CurrentFormatVersion || copy.Length == 0)
        {
            snapshot = null;
            error = "Technology catalog format or count is invalid.";
            return false;
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < copy.Length; index++)
        {
            var value = copy[index];
            if (value.Id != index || !ValidProfile(value) || !names.Add(value.Name))
            {
                snapshot = null;
                error = $"Technology {index} is invalid or duplicated.";
                return false;
            }
        }
        snapshot = new TechnologyCatalogSnapshot(formatVersion, copy);
        error = string.Empty;
        return true;
    }

    internal static bool ValidProfile(TechnologyProfile value)
    {
        if (value.Id < 0 || string.IsNullOrWhiteSpace(value.Name) ||
            value.ResearcherBuildingTypeId < 0 || !value.Cost.IsValid ||
            value.Cost.Supply != 0 || !float.IsFinite(value.ResearchSeconds) ||
            value.ResearchSeconds <= 0f || value.MaximumLevel <= 0 ||
            !float.IsFinite(value.CancelRefundFraction) ||
            value.CancelRefundFraction is < 0f or > 1f ||
            value.ExclusiveGroupId < -1 || value.Requirements.IsDefault ||
            value.Requirements.Length > 32)
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

    private byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(FormatVersion);
        writer.Write(_technologies.Length);
        foreach (var value in _technologies)
            TechnologySerialization.WriteProfile(writer, value);
        writer.Flush();
        return stream.ToArray();
    }
}

public static class DemoTechnologies
{
    private static readonly TechnologyCatalogSnapshot Catalog = Build();
    public static TechnologyCatalogSnapshot CreateCatalog() => Catalog;
    public static TechnologyProfile InfantryWeapons => Catalog.Technology(0);
    public static TechnologyProfile AssaultDoctrine => Catalog.Technology(1);
    public static TechnologyProfile FortificationDoctrine => Catalog.Technology(2);

    private static TechnologyCatalogSnapshot Build()
    {
        TechnologyProfile[] values =
        [
            new(0, "Infantry Weapons", DemoBuildingTypes.Academy.Id,
                new EconomyCost(100, 100), 4f, 3, 0.75f, -1)
            {
                Requirements =
                [new(TechnologyRequirementKind.CompletedBuilding,
                    DemoBuildingTypes.Academy.Id, 1)]
            },
            new(1, "Assault Doctrine", DemoBuildingTypes.Academy.Id,
                new EconomyCost(150, 100), 5f, 1, 0.75f, 1)
            {
                Requirements =
                [new(TechnologyRequirementKind.TechnologyLevel, 0, 1)]
            },
            new(2, "Fortification Doctrine", DemoBuildingTypes.Academy.Id,
                new EconomyCost(150, 100), 5f, 1, 0.75f, 1)
            {
                Requirements =
                [new(TechnologyRequirementKind.TechnologyLevel, 0, 1)]
            }
        ];
        if (!TechnologyCatalogSnapshot.TryCreate(
                TechnologyCatalogSnapshot.CurrentFormatVersion,
                values, out var catalog, out var error) || catalog is null)
            throw new InvalidOperationException(error);
        return catalog;
    }
}

public static class TechnologyCatalogDependencyValidator
{
    public static bool TryValidate(
        TechnologyCatalogSnapshot technologies,
        BuildingTypeCatalogSnapshot buildings,
        out string error)
    {
        foreach (var technology in technologies.Technologies)
        {
            if ((uint)technology.ResearcherBuildingTypeId >=
                    (uint)buildings.Types.Length)
            {
                error = $"Technology {technology.Id} has an invalid researcher.";
                return false;
            }
            foreach (var requirement in technology.Requirements)
            {
                if (requirement.Kind == TechnologyRequirementKind.CompletedBuilding &&
                        (uint)requirement.TargetId >= (uint)buildings.Types.Length ||
                    requirement.Kind == TechnologyRequirementKind.TechnologyLevel &&
                        requirement.TargetId >= technology.Id)
                {
                    error = $"Technology {technology.Id} has an invalid or cyclic prerequisite.";
                    return false;
                }
            }
        }
        error = string.Empty;
        return true;
    }
}

public readonly record struct ResearchOrderId(int Value);

public enum ResearchCommandCode : byte
{
    Success,
    InvalidPlayer,
    InvalidResearcher,
    WrongOwner,
    ResearcherNotCompleted,
    WrongResearcherType,
    QueueFull,
    InvalidTechnology,
    MissingPrerequisite,
    MaximumLevel,
    AlreadyQueued,
    MutuallyExclusive,
    InsufficientMinerals,
    InsufficientVespeneGas,
    InvalidOrder,
    PlayerDefeated,
    MatchCompleted,
    NotParticipant
}

public readonly record struct ResearchCommandResult(
    ResearchCommandCode Code,
    ResearchOrderId OrderId)
{
    public bool Succeeded => Code == ResearchCommandCode.Success;
}

public readonly record struct TechnologyRequirementStatus(
    TechnologyRequirementProfile Requirement,
    int CurrentValue)
{
    public bool Satisfied => CurrentValue >= Requirement.Value;
}

public readonly record struct ResearchAvailabilitySnapshot(
    ResearchCommandCode Code,
    int CurrentLevel,
    TechnologyRequirementStatus[] Requirements)
{
    public bool Available => Code == ResearchCommandCode.Success;
}

public readonly record struct ResearchOrderSnapshot(
    ResearchOrderId Id,
    GameplayBuildingId Researcher,
    int PlayerId,
    TechnologyProfile Technology,
    float Progress);

public readonly record struct ResearchQueueSnapshot(
    GameplayBuildingId Researcher,
    ResearchOrderSnapshot[] Orders);

public readonly record struct TechnologyLevelRuntimeEntry(
    int PlayerId, TechnologyProfile Technology, int Level);
public readonly record struct ResearchOrderRuntimeEntry(
    ResearchOrderId Id, int PlayerId, TechnologyProfile Technology, float Progress);
public readonly record struct ResearchQueueRuntimeEntry(
    GameplayBuildingId Researcher, ResearchOrderRuntimeEntry[] Orders);
public sealed record TechnologyRuntimeSnapshot(
    int NextOrderId,
    TechnologyLevelRuntimeEntry[] Levels,
    ResearchQueueRuntimeEntry[] Queues);

public sealed class TechnologySystem
{
    public const int MaximumQueueLength = 5;
    private readonly Dictionary<int, Dictionary<int, CompletedTechnology>> _levels = [];
    private readonly Dictionary<int, ResearchQueue> _queues = [];
    private int _nextOrderId = 1;

    public int ActiveOrderCount => _queues.Values.Sum(value => value.Orders.Count);

    public int Level(int playerId, int technologyId) =>
        _levels.TryGetValue(playerId, out var levels)
            ? levels.TryGetValue(technologyId, out var value)
                ? value.Level
                : 0
            : 0;

    public ResearchCommandResult Enqueue(
        int playerId,
        GameplayBuildingId researcher,
        TechnologyProfile technology,
        ConstructionSystem construction,
        PlayerEconomyStore economy)
    {
        var validation = ValidateEnqueue(
            playerId, researcher, technology, construction, economy);
        if (!validation.Succeeded) return validation;
        if (!economy.TrySpend(playerId, technology.Cost).Succeeded)
            throw new InvalidOperationException("Research spend changed after validation.");
        if (!_queues.TryGetValue(researcher.Value, out var queue))
        {
            queue = new ResearchQueue(researcher);
            _queues.Add(researcher.Value, queue);
        }
        var id = new ResearchOrderId(_nextOrderId++);
        queue.Orders.Add(new ResearchOrder(id, playerId, technology));
        return new ResearchCommandResult(ResearchCommandCode.Success, id);
    }

    public ResearchCommandResult ValidateEnqueue(
        int playerId,
        GameplayBuildingId researcher,
        TechnologyProfile technology,
        ConstructionSystem construction,
        PlayerEconomyStore economy)
    {
        if (!economy.IsRegistered(playerId)) return Failure(ResearchCommandCode.InvalidPlayer);
        if (!TechnologyCatalogSnapshot.ValidProfile(technology))
            return Failure(ResearchCommandCode.InvalidTechnology);
        if (!construction.IsAlive(researcher))
            return Failure(ResearchCommandCode.InvalidResearcher);
        var building = construction.Observe(researcher);
        if (building.PlayerId != playerId) return Failure(ResearchCommandCode.WrongOwner);
        if (building.State != BuildingLifecycleState.Completed)
            return Failure(ResearchCommandCode.ResearcherNotCompleted);
        if (building.Type.Id != technology.ResearcherBuildingTypeId)
            return Failure(ResearchCommandCode.WrongResearcherType);
        if (_queues.TryGetValue(researcher.Value, out var queue) &&
            queue.Orders.Count >= MaximumQueueLength)
            return Failure(ResearchCommandCode.QueueFull);
        var level = Level(playerId, technology.Id);
        if (_levels.TryGetValue(playerId, out var completed) &&
            completed.TryGetValue(technology.Id, out var existing) &&
            !ProfileEquals(existing.Technology, technology))
            return Failure(ResearchCommandCode.InvalidTechnology);
        if (level >= technology.MaximumLevel)
            return Failure(ResearchCommandCode.MaximumLevel);
        if (QueuedCount(playerId, technology.Id) > 0)
            return Failure(ResearchCommandCode.AlreadyQueued);
        if (Conflicts(playerId, technology))
            return Failure(ResearchCommandCode.MutuallyExclusive);
        if (EvaluateRequirements(playerId, technology.Requirements, construction)
            .Any(value => !value.Satisfied))
            return Failure(ResearchCommandCode.MissingPrerequisite);
        var spend = economy.ValidateSpend(playerId, technology.Cost);
        return spend.Code switch
        {
            EconomyTransactionCode.Success => new(ResearchCommandCode.Success, default),
            EconomyTransactionCode.InsufficientMinerals =>
                Failure(ResearchCommandCode.InsufficientMinerals),
            EconomyTransactionCode.InsufficientVespeneGas =>
                Failure(ResearchCommandCode.InsufficientVespeneGas),
            _ => Failure(ResearchCommandCode.InvalidPlayer)
        };
    }

    public ResearchAvailabilitySnapshot ObserveAvailability(
        int playerId,
        GameplayBuildingId researcher,
        TechnologyProfile technology,
        ConstructionSystem construction,
        PlayerEconomyStore economy) => new(
        ValidateEnqueue(playerId, researcher, technology, construction, economy).Code,
        Level(playerId, technology.Id),
        TechnologyCatalogSnapshot.ValidProfile(technology)
            ? EvaluateRequirements(playerId, technology.Requirements, construction)
            : []);

    public bool Cancel(
        int playerId,
        ResearchOrderId orderId,
        PlayerEconomyStore economy,
        out GameplayBuildingId researcher)
    {
        researcher = default;
        foreach (var queue in _queues.Values)
        {
            var index = queue.Orders.FindIndex(value =>
                value.Id == orderId && value.PlayerId == playerId);
            if (index < 0) continue;
            var order = queue.Orders[index];
            economy.Refund(playerId, order.Technology.Cost,
                order.Technology.CancelRefundFraction);
            queue.Orders.RemoveAt(index);
            researcher = queue.Researcher;
            return true;
        }
        return false;
    }

    public void Update(
        float delta,
        long tick,
        GameplayEventStream events,
        ConstructionSystem construction,
        PlayerEconomyStore economy)
    {
        List<int>? retired = null;
        foreach (var pair in _queues)
        {
            var queue = pair.Value;
            if (!construction.IsAlive(queue.Researcher))
            {
                foreach (var order in queue.Orders)
                    economy.Refund(order.PlayerId, order.Technology.Cost);
                retired ??= [];
                retired.Add(pair.Key);
                continue;
            }
            if (queue.Orders.Count == 0) continue;
            var active = queue.Orders[0];
            active.Progress = Math.Clamp(
                active.Progress + delta / active.Technology.ResearchSeconds, 0f, 1f);
            if (active.Progress < 1f) continue;
            if (!_levels.TryGetValue(active.PlayerId, out var levels))
            {
                levels = [];
                _levels.Add(active.PlayerId, levels);
            }
            var current = levels.TryGetValue(
                active.Technology.Id, out var completed)
                ? completed.Level
                : 0;
            levels[active.Technology.Id] = new CompletedTechnology(
                active.Technology, current + 1);
            var researcher = construction.Observe(queue.Researcher);
            events.Publish(
                tick,
                GameplayEventKind.ResearchCompleted,
                building: queue.Researcher.Value,
                value: current + 1,
                worldPosition:
                    (researcher.Bounds.Min + researcher.Bounds.Max) * 0.5f,
                player: active.PlayerId,
                technology: active.Technology.Id);
            queue.Orders.RemoveAt(0);
        }
        if (retired is not null)
            foreach (var id in retired) _queues.Remove(id);
    }

    public ResearchQueueSnapshot Observe(GameplayBuildingId researcher)
    {
        if (!_queues.TryGetValue(researcher.Value, out var queue))
            return new ResearchQueueSnapshot(researcher, []);
        return new ResearchQueueSnapshot(
            researcher,
            queue.Orders.Select(value => new ResearchOrderSnapshot(
                value.Id, researcher, value.PlayerId,
                value.Technology, value.Progress)).ToArray());
    }

    public TechnologyRuntimeSnapshot CaptureRuntimeState() => new(
        _nextOrderId,
        _levels.OrderBy(value => value.Key)
            .SelectMany(player => player.Value.OrderBy(value => value.Key)
                .Select(value => new TechnologyLevelRuntimeEntry(
                    player.Key, value.Value.Technology,
                    value.Value.Level))).ToArray(),
        _queues.Values.OrderBy(value => value.Researcher.Value)
            .Select(value => new ResearchQueueRuntimeEntry(
                value.Researcher,
                value.Orders.Select(order => new ResearchOrderRuntimeEntry(
                    order.Id, order.PlayerId, order.Technology,
                    order.Progress)).ToArray())).ToArray());

    public void RestoreRuntimeState(
        TechnologyRuntimeSnapshot snapshot,
        ConstructionSystem construction,
        PlayerEconomyStore economy)
    {
        if (snapshot.NextOrderId <= 0) throw new InvalidOperationException();
        _levels.Clear();
        _queues.Clear();
        foreach (var value in snapshot.Levels)
        {
            if (!economy.IsRegistered(value.PlayerId) ||
                !TechnologyCatalogSnapshot.ValidProfile(value.Technology) ||
                value.Level <= 0 || value.Level > value.Technology.MaximumLevel)
                throw new InvalidOperationException("Invalid technology level.");
            if (!_levels.TryGetValue(value.PlayerId, out var levels))
            {
                levels = [];
                _levels.Add(value.PlayerId, levels);
            }
            if (!levels.TryAdd(value.Technology.Id,
                    new CompletedTechnology(value.Technology, value.Level)))
                throw new InvalidOperationException("Duplicate technology level.");
        }
        var ids = new HashSet<int>();
        var queuedTechnologies = new HashSet<(int Player, int Technology)>();
        var exclusiveChoices = new Dictionary<(int Player, int Group), int>();
        foreach (var player in _levels)
        foreach (var completed in player.Value.Values)
        {
            if (completed.Technology.ExclusiveGroupId >= 0 &&
                exclusiveChoices.TryGetValue(
                    (player.Key, completed.Technology.ExclusiveGroupId),
                    out var chosen) && chosen != completed.Technology.Id)
                throw new InvalidOperationException(
                    "Mutually exclusive technologies are both completed.");
            if (completed.Technology.ExclusiveGroupId >= 0)
                exclusiveChoices[
                    (player.Key, completed.Technology.ExclusiveGroupId)] =
                    completed.Technology.Id;
        }
        var maximumId = 0;
        foreach (var value in snapshot.Queues)
        {
            if (!construction.IsAlive(value.Researcher) ||
                construction.Observe(value.Researcher).State !=
                    BuildingLifecycleState.Completed ||
                value.Orders.Length > MaximumQueueLength ||
                !_queues.TryAdd(value.Researcher.Value,
                    new ResearchQueue(value.Researcher)))
                throw new InvalidOperationException("Invalid research queue.");
            var queue = _queues[value.Researcher.Value];
            foreach (var order in value.Orders)
            {
                if (order.Id.Value <= 0 || !ids.Add(order.Id.Value) ||
                    !economy.IsRegistered(order.PlayerId) ||
                    !TechnologyCatalogSnapshot.ValidProfile(order.Technology) ||
                    construction.Observe(value.Researcher).PlayerId != order.PlayerId ||
                    construction.Observe(value.Researcher).Type.Id !=
                        order.Technology.ResearcherBuildingTypeId ||
                    Level(order.PlayerId, order.Technology.Id) >=
                        order.Technology.MaximumLevel ||
                    !queuedTechnologies.Add(
                        (order.PlayerId, order.Technology.Id)) ||
                    !float.IsFinite(order.Progress) ||
                    order.Progress is < 0f or >= 1f)
                    throw new InvalidOperationException("Invalid research order.");
                if (_levels.TryGetValue(order.PlayerId, out var completedLevels) &&
                    completedLevels.TryGetValue(
                        order.Technology.Id, out var completed) &&
                    !ProfileEquals(completed.Technology, order.Technology))
                    throw new InvalidOperationException(
                        "Queued technology differs from completed profile.");
                if (order.Technology.ExclusiveGroupId >= 0 &&
                    exclusiveChoices.TryGetValue(
                        (order.PlayerId, order.Technology.ExclusiveGroupId),
                        out var choice) && choice != order.Technology.Id)
                    throw new InvalidOperationException(
                        "Queued technology violates exclusivity.");
                if (order.Technology.ExclusiveGroupId >= 0)
                    exclusiveChoices[
                        (order.PlayerId, order.Technology.ExclusiveGroupId)] =
                        order.Technology.Id;
                queue.Orders.Add(new ResearchOrder(
                    order.Id, order.PlayerId, order.Technology)
                {
                    Progress = order.Progress
                });
                maximumId = Math.Max(maximumId, order.Id.Value);
            }
        }
        if (snapshot.NextOrderId <= maximumId) throw new InvalidOperationException();
        _nextOrderId = snapshot.NextOrderId;
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add(_nextOrderId);
        var levelCount = 0;
        foreach (var player in _levels.Values) levelCount += player.Count;
        hash.Add(levelCount);
        if (_levels.Count > 0)
        {
            foreach (var player in _levels.OrderBy(value => value.Key))
            foreach (var completed in player.Value.OrderBy(value => value.Key))
            {
                hash.Add(player.Key);
                AppendProfileHash(ref hash, completed.Value.Technology);
                hash.Add(completed.Value.Level);
            }
        }
        hash.Add(_queues.Count);
        if (_queues.Count == 0) return;
        var queues = _queues.Values.OrderBy(value => value.Researcher.Value).ToArray();
        foreach (var queue in queues)
        {
            hash.Add(queue.Researcher.Value);
            hash.Add(queue.Orders.Count);
            foreach (var order in queue.Orders)
            {
                hash.Add(order.Id.Value);
                hash.Add(order.PlayerId);
                AppendProfileHash(ref hash, order.Technology);
                hash.Add(order.Progress);
            }
        }
    }

    private int QueuedCount(int playerId, int technologyId) =>
        _queues.Values.Sum(queue => queue.Orders.Count(value =>
            value.PlayerId == playerId && value.Technology.Id == technologyId));

    private bool Conflicts(int playerId, TechnologyProfile technology)
    {
        if (technology.ExclusiveGroupId < 0) return false;
        foreach (var player in _levels.Where(value => value.Key == playerId))
            foreach (var level in player.Value.Where(value => value.Value.Level > 0))
            {
                var profile = level.Value.Technology;
                if (profile.Id != technology.Id &&
                    profile.ExclusiveGroupId == technology.ExclusiveGroupId)
                    return true;
            }
        return _queues.Values.SelectMany(value => value.Orders).Any(value =>
            value.PlayerId == playerId && value.Technology.Id != technology.Id &&
            value.Technology.ExclusiveGroupId == technology.ExclusiveGroupId);
    }

    private static bool ProfileEquals(TechnologyProfile left, TechnologyProfile right) =>
        left with { Requirements = default } == right with { Requirements = default } &&
        left.Requirements.AsSpan().SequenceEqual(right.Requirements.AsSpan());

    private static void AppendProfileHash(
        ref StableHash64 hash,
        TechnologyProfile value)
    {
        hash.Add(value.Id);
        hash.Add(value.ResearcherBuildingTypeId);
        hash.Add(value.Cost.Minerals);
        hash.Add(value.Cost.VespeneGas);
        hash.Add(value.Cost.Supply);
        hash.Add(value.ResearchSeconds);
        hash.Add(value.MaximumLevel);
        hash.Add(value.CancelRefundFraction);
        hash.Add(value.ExclusiveGroupId);
        hash.Add(value.Requirements.Length);
        foreach (var requirement in value.Requirements)
        {
            hash.Add((byte)requirement.Kind);
            hash.Add(requirement.TargetId);
            hash.Add(requirement.Value);
        }
    }

    private TechnologyRequirementStatus[] EvaluateRequirements(
        int playerId,
        ImmutableArray<TechnologyRequirementProfile> requirements,
        ConstructionSystem construction)
    {
        var result = new TechnologyRequirementStatus[requirements.Length];
        for (var index = 0; index < requirements.Length; index++)
        {
            var value = requirements[index];
            var current = value.Kind switch
            {
                TechnologyRequirementKind.CompletedBuilding =>
                    construction.CountCompleted(playerId, value.TargetId),
                TechnologyRequirementKind.TechnologyLevel =>
                    Level(playerId, value.TargetId),
                _ => 0
            };
            result[index] = new TechnologyRequirementStatus(value, current);
        }
        return result;
    }

    private static ResearchCommandResult Failure(ResearchCommandCode code) =>
        new(code, default);

    private sealed class ResearchQueue(GameplayBuildingId researcher)
    {
        public GameplayBuildingId Researcher { get; } = researcher;
        public List<ResearchOrder> Orders { get; } = [];
    }

    private sealed class ResearchOrder(
        ResearchOrderId id, int playerId, TechnologyProfile technology)
    {
        public ResearchOrderId Id { get; } = id;
        public int PlayerId { get; } = playerId;
        public TechnologyProfile Technology { get; } = technology;
        public float Progress { get; set; }
    }

    private readonly record struct CompletedTechnology(
        TechnologyProfile Technology,
        int Level);
}

internal static class TechnologySerialization
{
    public static void WriteProfile(BinaryWriter writer, TechnologyProfile value)
    {
        writer.Write(value.Id);
        WriteString(writer, value.Name);
        writer.Write(value.ResearcherBuildingTypeId);
        writer.Write(value.Cost.Minerals);
        writer.Write(value.Cost.VespeneGas);
        writer.Write(value.Cost.Supply);
        writer.Write(value.ResearchSeconds);
        writer.Write(value.MaximumLevel);
        writer.Write(value.CancelRefundFraction);
        writer.Write(value.ExclusiveGroupId);
        writer.Write(value.Requirements.Length);
        foreach (var requirement in value.Requirements)
        {
            writer.Write((byte)requirement.Kind);
            writer.Write(requirement.TargetId);
            writer.Write(requirement.Value);
        }
    }

    public static TechnologyProfile ReadProfile(BinaryReader reader)
    {
        var value = new TechnologyProfile(
            reader.ReadInt32(), ReadString(reader), reader.ReadInt32(),
            new EconomyCost(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()),
            reader.ReadSingle(), reader.ReadInt32(), reader.ReadSingle(),
            reader.ReadInt32());
        var count = reader.ReadInt32();
        if (count is < 0 or > 32) throw new InvalidDataException();
        TechnologyRequirementProfile[] requirements = new TechnologyRequirementProfile[count];
        for (var index = 0; index < count; index++)
            requirements[index] = new TechnologyRequirementProfile(
                (TechnologyRequirementKind)reader.ReadByte(),
                reader.ReadInt32(), reader.ReadInt32());
        return value with { Requirements = [.. requirements] };
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 1 or > 1024) throw new InvalidDataException();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }
}
