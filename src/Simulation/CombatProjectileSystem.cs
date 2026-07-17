using System.Numerics;

namespace RtsDemo.Simulation;

public readonly record struct CombatProjectileSnapshot(
    int Id,
    int AttackerUnit,
    CombatTargetKind TargetKind,
    int TargetId,
    Vector2 Position,
    float Speed,
    CombatWeaponDamageSnapshot Weapon);

public sealed record CombatProjectileRuntimeSnapshot(
    int NextId,
    CombatProjectileSnapshot[] Active);

public sealed class CombatProjectileSystem
{
    public const int MaximumProjectiles = 4096;
    private readonly CombatProjectileSnapshot[] _slots =
        new CombatProjectileSnapshot[MaximumProjectiles];
    private readonly bool[] _active = new bool[MaximumProjectiles];
    private int _nextId = 1;
    private int _oldestId = 1;

    public int ActiveCount { get; private set; }
    public int NextId => _nextId;

    public bool Launch(
        int attacker,
        CombatTargetKind targetKind,
        int targetId,
        Vector2 position,
        float speed,
        CombatWeaponDamageSnapshot weapon,
        out int projectileId)
    {
        projectileId = 0;
        if (ActiveCount >= MaximumProjectiles || _nextId == int.MaxValue ||
            attacker < 0 || targetId < 0 ||
            targetKind is not (CombatTargetKind.Unit or
                CombatTargetKind.Building or CombatTargetKind.Object) ||
            !float.IsFinite(speed) || speed <= 0f ||
            !float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            !ValidWeapon(weapon))
            return false;
        var id = _nextId++;
        var slot = id % MaximumProjectiles;
        if (_active[slot]) return false;
        _slots[slot] = new CombatProjectileSnapshot(
            id, attacker, targetKind, targetId, position, speed, weapon);
        _active[slot] = true;
        ActiveCount++;
        projectileId = id;
        return true;
    }

    public void Update(
        float delta,
        Func<CombatTargetKind, int, (bool Valid, Vector2 Position)> resolveTarget,
        Action<CombatProjectileSnapshot> impact,
        Action<CombatProjectileSnapshot> expire)
    {
        var end = _nextId;
        for (var id = _oldestId; id < end; id++)
        {
            var slot = id % MaximumProjectiles;
            if (!_active[slot] || _slots[slot].Id != id) continue;
            var projectile = _slots[slot];
            var target = resolveTarget(projectile.TargetKind, projectile.TargetId);
            if (!target.Valid)
            {
                expire(projectile);
                Release(slot);
                continue;
            }
            var offset = target.Position - projectile.Position;
            var distance = offset.Length();
            var step = projectile.Speed * delta;
            if (distance <= step || distance <= 0.001f)
            {
                projectile = projectile with { Position = target.Position };
                impact(projectile);
                Release(slot);
                continue;
            }
            _slots[slot] = projectile with
            {
                Position = projectile.Position + offset / distance * step
            };
        }
        while (_oldestId < _nextId)
        {
            var slot = _oldestId % MaximumProjectiles;
            if (_active[slot] && _slots[slot].Id == _oldestId) break;
            _oldestId++;
        }
    }

    public CombatProjectileSnapshot[] ObserveActive()
    {
        var result = new CombatProjectileSnapshot[ActiveCount];
        var index = 0;
        for (var id = _oldestId; id < _nextId; id++)
        {
            var slot = id % MaximumProjectiles;
            if (_active[slot] && _slots[slot].Id == id)
                result[index++] = _slots[slot];
        }
        return result;
    }

    public CombatProjectileRuntimeSnapshot CaptureRuntimeState() =>
        new(_nextId, ObserveActive());

    public void RestoreRuntimeState(CombatProjectileRuntimeSnapshot snapshot)
    {
        if (snapshot.NextId <= 0 || snapshot.Active.Length > MaximumProjectiles)
            throw new InvalidDataException();
        Array.Clear(_active);
        Array.Clear(_slots);
        ActiveCount = 0;
        _nextId = snapshot.NextId;
        _oldestId = snapshot.Active.Length == 0
            ? snapshot.NextId
            : snapshot.Active[0].Id;
        var previous = 0;
        foreach (var value in snapshot.Active)
        {
            if (value.Id <= previous || value.Id >= snapshot.NextId ||
                !LaunchRestored(value)) throw new InvalidDataException();
            previous = value.Id;
        }
    }

    internal void AppendStateHash(ref StableHash64 hash)
    {
        hash.Add(_nextId);
        var active = ObserveActive();
        hash.Add(active.Length);
        foreach (var value in active)
        {
            hash.Add(value.Id);
            hash.Add(value.AttackerUnit);
            hash.Add((byte)value.TargetKind);
            hash.Add(value.TargetId);
            hash.Add(value.Position);
            hash.Add(value.Speed);
            AddWeapon(ref hash, value.Weapon);
        }
    }

    private bool LaunchRestored(CombatProjectileSnapshot value)
    {
        var slot = value.Id % MaximumProjectiles;
        if (_active[slot] || value.AttackerUnit < 0 || value.TargetId < 0 ||
            value.TargetKind is not (CombatTargetKind.Unit or
                CombatTargetKind.Building or CombatTargetKind.Object) ||
            value.Speed <= 0f || !float.IsFinite(value.Speed) ||
            !float.IsFinite(value.Position.X) ||
            !float.IsFinite(value.Position.Y) || !ValidWeapon(value.Weapon))
            return false;
        _slots[slot] = value;
        _active[slot] = true;
        ActiveCount++;
        return true;
    }

    private void Release(int slot)
    {
        _active[slot] = false;
        ActiveCount--;
    }

    internal static void AddWeapon(
        ref StableHash64 hash,
        CombatWeaponDamageSnapshot value)
    {
        hash.Add(value.BaseDamage);
        hash.Add(value.AttacksPerVolley);
        hash.Add((ushort)value.BonusVs);
        hash.Add(value.BonusDamage);
        hash.Add(value.UpgradeLevel);
        hash.Add(value.BaseUpgradeDamage);
        hash.Add(value.BonusUpgradeDamage);
        hash.Add((byte)value.AttackType);
        hash.Add(value.Area.FullDamageRadius);
        hash.Add(value.Area.HalfDamageRadius);
        hash.Add(value.Area.QuarterDamageRadius);
        hash.Add((byte)value.Area.TargetLayers);
        hash.Add((byte)value.Propagation.Kind);
        hash.Add(value.Propagation.LineDistance);
        hash.Add(value.Propagation.Radius);
        hash.Add(value.Propagation.DamageLossFactor);
        hash.Add(value.Propagation.MaximumTargets);
        hash.Add((byte)value.Propagation.TargetLayers);
        hash.Add(value.Propagation.DistanceUpgradeTechnologyId);
        hash.Add(value.Propagation.DistanceUpgradePerLevel);
    }

    internal static bool ValidWeapon(CombatWeaponDamageSnapshot value) =>
        float.IsFinite(value.BaseDamage) && value.BaseDamage >= 0f &&
        value.AttacksPerVolley is >= 1 and <= 32 &&
        (value.BonusVs & ~CombatAttribute.All) == 0 &&
        float.IsFinite(value.BonusDamage) && value.BonusDamage >= 0f &&
        value.UpgradeLevel is >= 0 and <= 255 &&
        float.IsFinite(value.BaseUpgradeDamage) &&
        value.BaseUpgradeDamage >= 0f &&
        float.IsFinite(value.BonusUpgradeDamage) &&
        value.BonusUpgradeDamage >= 0f &&
        Enum.IsDefined(value.AttackType) &&
        ValidArea(value.Area) &&
        ValidPropagation(value.Propagation);

    private static bool ValidArea(CombatWeaponAreaSnapshot area)
    {
        try
        {
            area.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool ValidPropagation(
        CombatWeaponPropagationSnapshot propagation)
    {
        try
        {
            propagation.Validate();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
