using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct CombatObjectId(int Value);

public enum CombatObjectKind : byte
{
    Tree,
    Debris,
    Item,
    Wall
}

public readonly record struct CombatObjectProfile(
    CombatObjectKind Kind,
    SimRect Bounds,
    float MaximumHealth,
    float Armor = 0f,
    CombatArmorType ArmorType = CombatArmorType.None,
    CombatAttribute Attributes = CombatAttribute.None,
    int OwnerTeam = -1,
    int LinkedResourceNodeId = -1,
    int LinkedDynamicFootprintId = 0)
{
    public Vector2 Position => (Bounds.Min + Bounds.Max) * 0.5f;
    public CombatTargetLayer TargetLayer => Kind switch
    {
        CombatObjectKind.Tree => CombatTargetLayer.Tree,
        CombatObjectKind.Debris => CombatTargetLayer.Debris,
        CombatObjectKind.Item => CombatTargetLayer.Item,
        CombatObjectKind.Wall => CombatTargetLayer.Wall,
        _ => CombatTargetLayer.None
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Kind) ||
            !float.IsFinite(Bounds.Min.X) ||
            !float.IsFinite(Bounds.Min.Y) ||
            !float.IsFinite(Bounds.Max.X) ||
            !float.IsFinite(Bounds.Max.Y) ||
            Bounds.Width <= 0f || Bounds.Height <= 0f ||
            !float.IsFinite(MaximumHealth) || MaximumHealth <= 0f ||
            !float.IsFinite(Armor) || !Enum.IsDefined(ArmorType) ||
            (Attributes & ~CombatAttribute.All) != 0 ||
            OwnerTeam < -1 || LinkedResourceNodeId < -1 ||
            LinkedDynamicFootprintId < 0)
            throw new ArgumentOutOfRangeException(nameof(CombatObjectProfile));
    }
}

public readonly record struct CombatObjectSnapshot(
    CombatObjectId Id,
    CombatObjectProfile Profile,
    float Health,
    bool Alive)
{
    public Vector2 Position => Profile.Position;
    public SimRect Bounds => Profile.Bounds;
    public CombatTargetLayer TargetLayer => Profile.TargetLayer;
}

public readonly record struct CombatObjectDamageResult(
    bool Applied,
    float AppliedDamage,
    float RemainingHealth,
    float DamagePerAttack,
    int AttacksApplied,
    bool BonusApplied,
    bool Destroyed);

public readonly record struct CombatObjectRuntimeEntry(
    CombatObjectId Id,
    CombatObjectProfile Profile,
    float Health,
    bool Alive);

public sealed record CombatObjectRuntimeSnapshot(
    CombatObjectRuntimeEntry[] Objects);

/// <summary>
/// Dense, content-neutral combat boundary for destructibles and items. Units
/// (including wards) and gameplay buildings remain in their specialized stores.
/// </summary>
public sealed class CombatObjectStore
{
    private readonly List<CombatObjectRuntimeEntry> _objects = [];

    public int Count => _objects.Count;

    public CombatObjectId Add(in CombatObjectProfile profile)
    {
        profile.Validate();
        var id = new CombatObjectId(_objects.Count);
        _objects.Add(new CombatObjectRuntimeEntry(
            id, profile, profile.MaximumHealth, true));
        return id;
    }

    public bool IsAlive(CombatObjectId id) =>
        (uint)id.Value < (uint)_objects.Count && _objects[id.Value].Alive;

    public CombatObjectSnapshot Observe(CombatObjectId id)
    {
        if ((uint)id.Value >= (uint)_objects.Count)
            throw new ArgumentOutOfRangeException(nameof(id));
        var value = _objects[id.Value];
        return new CombatObjectSnapshot(
            value.Id, value.Profile, value.Health, value.Alive);
    }

    public CombatObjectSnapshot[] CreateOverview()
    {
        var result = new CombatObjectSnapshot[_objects.Count];
        for (var index = 0; index < result.Length; index++)
            result[index] = Observe(new CombatObjectId(index));
        return result;
    }

    internal void SetHealth(CombatObjectId id, float health)
    {
        if ((uint)id.Value >= (uint)_objects.Count ||
            !float.IsFinite(health) || health < 0f)
            throw new ArgumentOutOfRangeException(nameof(id));
        var value = _objects[id.Value];
        var resolved = Math.Clamp(health, 0f, value.Profile.MaximumHealth);
        _objects[id.Value] = value with
        {
            Health = resolved,
            Alive = resolved > 0f
        };
    }

    internal CombatObjectDamageResult ApplyDamage(
        CombatObjectId id,
        in CombatWeaponDamageSnapshot weapon)
    {
        if (!IsAlive(id)) return default;
        var value = _objects[id.Value];
        var resolved = CombatDamageResolver.Resolve(
            weapon,
            new CombatDefenseSnapshot(
                value.Profile.Armor,
                value.Profile.Attributes,
                value.Profile.ArmorType),
            value.Health);
        SetHealth(id, resolved.RemainingHealth);
        return new CombatObjectDamageResult(
            true,
            resolved.TotalDamage,
            resolved.RemainingHealth,
            resolved.DamagePerAttack,
            resolved.AttacksApplied,
            resolved.BonusApplied,
            resolved.Killed);
    }

    internal CombatObjectRuntimeSnapshot CaptureRuntimeState() => new(
        _objects.ToArray());

    internal void RestoreRuntimeState(CombatObjectRuntimeSnapshot snapshot)
    {
        _objects.Clear();
        for (var index = 0; index < snapshot.Objects.Length; index++)
        {
            var value = snapshot.Objects[index];
            value.Profile.Validate();
            if (value.Id.Value != index ||
                !float.IsFinite(value.Health) || value.Health < 0f ||
                value.Health > value.Profile.MaximumHealth ||
                value.Alive != (value.Health > 0f))
                throw new InvalidOperationException(
                    "Combat object runtime entries must be dense and valid.");
            _objects.Add(value);
        }
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add(_objects.Count);
        foreach (var value in _objects)
        {
            hash.Add(value.Id.Value);
            hash.Add((byte)value.Profile.Kind);
            hash.Add(value.Profile.Bounds.Min);
            hash.Add(value.Profile.Bounds.Max);
            hash.Add(value.Profile.MaximumHealth);
            hash.Add(value.Profile.Armor);
            hash.Add((byte)value.Profile.ArmorType);
            hash.Add((ushort)value.Profile.Attributes);
            hash.Add(value.Profile.OwnerTeam);
            hash.Add(value.Profile.LinkedResourceNodeId);
            hash.Add(value.Profile.LinkedDynamicFootprintId);
            hash.Add(value.Health);
            hash.Add(value.Alive);
        }
    }
}
