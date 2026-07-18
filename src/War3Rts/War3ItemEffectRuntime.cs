using System.Collections.Immutable;
using System.Numerics;
using RtsDemo.Simulation;

namespace War3Rts;

/// <summary>
/// Executes Warcraft item effects against the authoritative simulation. Shop
/// ownership, slots, stock and cooldowns remain isolated in
/// <see cref="War3ItemShopRuntime"/>; scene input only composes the two.
/// </summary>
public sealed class War3ItemEffectRuntime
{
    public const float WorldDistanceScale = 4f / 15f;

    private readonly List<PeriodicRecovery> _recoveries = [];
    private readonly List<PendingTownPortal> _townPortals = [];
    private readonly HashSet<int> _orbBearers = [];

    public int ActiveRecoveryCount => _recoveries.Count;
    public int ActiveTownPortalCount => _townPortals.Count;

    public void Update(float deltaSeconds, RtsSimulation simulation)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f) return;
        for (var index = _recoveries.Count - 1; index >= 0; index--)
        {
            var recovery = _recoveries[index];
            if ((uint)recovery.Unit >= (uint)simulation.Units.Count ||
                !simulation.Units.Alive[recovery.Unit])
            {
                _recoveries.RemoveAt(index);
                continue;
            }
            if (recovery.DelayRemaining > 0f)
            {
                recovery = recovery with
                {
                    DelayRemaining = MathF.Max(
                        0f, recovery.DelayRemaining - deltaSeconds)
                };
                _recoveries[index] = recovery;
                continue;
            }
            var elapsed = recovery.UntilFull
                ? deltaSeconds
                : MathF.Min(deltaSeconds, recovery.RemainingSeconds);
            if (recovery.HealthPerSecond > 0f)
                simulation.RestoreUnitHealth(
                    recovery.Unit, recovery.HealthPerSecond * elapsed);
            if (recovery.ManaPerSecond > 0f)
                simulation.Abilities.RestoreMana(
                    recovery.Unit, recovery.ManaPerSecond * elapsed);
            recovery = recovery with
            {
                RemainingSeconds = recovery.RemainingSeconds - elapsed
            };
            var full = recovery.UntilFull &&
                       simulation.Combat.Health[recovery.Unit] >=
                       simulation.Combat.MaximumHealth[recovery.Unit] - 0.001f;
            if (full || (!recovery.UntilFull && recovery.RemainingSeconds <= 0f))
                _recoveries.RemoveAt(index);
            else
                _recoveries[index] = recovery;
        }
        for (var index = _townPortals.Count - 1; index >= 0; index--)
        {
            var portal = _townPortals[index];
            if (!ValidUnit(simulation, portal.Caster) ||
                Vector2.DistanceSquared(
                    simulation.Units.Positions[portal.Caster], portal.Origin) > 1f ||
                simulation.Combat.Health[portal.Caster] + 0.001f <
                    portal.StartingHealth)
            {
                _townPortals.RemoveAt(index);
                continue;
            }
            portal = portal with
            {
                RemainingSeconds = portal.RemainingSeconds - deltaSeconds
            };
            if (portal.RemainingSeconds > 0f)
            {
                _townPortals[index] = portal;
                continue;
            }
            CompleteTownPortal(
                simulation, portal.Caster, portal.Destination, portal.Radius);
            _townPortals.RemoveAt(index);
        }
    }

    public War3ItemUseCode UseRegenerationScroll(
        RtsSimulation simulation,
        int caster,
        War3ShopItemDefinition item)
    {
        if (!ValidUnit(simulation, caster))
            return War3ItemUseCode.InvalidUnit;
        var owner = simulation.Combat.Teams[caster];
        var center = simulation.Units.Positions[caster];
        var affected = 0;
        for (var unit = 0; unit < simulation.Units.Count; unit++)
        {
            if (!ValidUnit(simulation, unit) ||
                !simulation.Diplomacy.IsFriendly(
                    owner, simulation.Combat.Teams[unit]) ||
                (simulation.Combat.Attributes[unit] &
                    CombatAttribute.Mechanical) != 0 ||
                Vector2.DistanceSquared(center, simulation.Units.Positions[unit]) >
                    Radius(item.Area) * Radius(item.Area))
                continue;
            var duration = MathF.Max(0.01f, item.Duration);
            AddRecovery(
                unit, item.AbilityRawId, duration,
                Value(item, "A") / duration,
                Value(item, "B") / duration);
            affected++;
        }
        return affected > 0
            ? War3ItemUseCode.Success
            : War3ItemUseCode.NoEffect;
    }

    public War3ItemUseCode UseClarityPotion(
        RtsSimulation simulation,
        int caster,
        War3ShopItemDefinition item)
    {
        if (!ValidUnit(simulation, caster))
            return War3ItemUseCode.InvalidUnit;
        var mana = simulation.Abilities.Observe(caster);
        if (mana.MaximumMana <= 0f || mana.Mana >= mana.MaximumMana - 0.001f)
            return War3ItemUseCode.NoEffect;
        var duration = MathF.Max(0.01f, item.Duration);
        AddRecovery(caster, item.AbilityRawId, duration,
            Value(item, "A") / duration,
            Value(item, "B") / duration);
        return War3ItemUseCode.Success;
    }

    public War3ItemUseCode UseHealingPotion(
        RtsSimulation simulation,
        int caster,
        War3ShopItemDefinition item) =>
        simulation.RestoreUnitHealth(caster, Value(item, "A")) > 0f
        ? War3ItemUseCode.Success
        : War3ItemUseCode.NoEffect;

    public War3ItemUseCode UseManaPotion(
        RtsSimulation simulation,
        int caster,
        War3ShopItemDefinition item) =>
        simulation.Abilities.RestoreMana(caster, Value(item, "A")) > 0f
        ? War3ItemUseCode.Success
        : War3ItemUseCode.NoEffect;

    public War3ItemUseCode UseMechanicalCritter(
        RtsSimulation simulation,
        int caster,
        War3ShopItemDefinition item,
        UnitTypeProfile baseline,
        out int summonedUnit)
    {
        summonedUnit = -1;
        if (!ValidUnit(simulation, caster))
            return War3ItemUseCode.InvalidUnit;
        var direction = new Vector2(1f, 0.35f);
        var position = simulation.World.Bounds.Inset(8f).Clamp(
            simulation.Units.Positions[caster] + direction * 18f);
        var movement = baseline.Movement with
        {
            Name = "Mechanical Critter",
            PhysicalRadius = 16f * WorldDistanceScale,
            NavigationRadius = 16f * WorldDistanceScale,
            MaximumSpeed = 100f * WorldDistanceScale,
            Acceleration = MathF.Max(100f, baseline.Movement.Acceleration)
        };
        var combat = new CombatProfileSnapshot(
            MaximumHealth: 15f,
            AttackDamage: 0f,
            AttackRange: 0f,
            AcquisitionRange: 0f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 0f,
            Positioning: CombatPositioningKind.Melee,
            Armor: 0f,
            Attributes: CombatAttribute.Light | CombatAttribute.Mechanical,
            ArmorType: CombatArmorType.None);
        var perception = new UnitPerceptionProfileSnapshot(
            UnitConcealmentKind.None,
            0f,
            350f * WorldDistanceScale);
        summonedUnit = simulation.AddUnit(
            position,
            movement,
            simulation.Combat.Teams[caster],
            combat,
            perception);
        simulation.Abilities.RegisterExternalSummon(
            summonedUnit, caster,
            item.UnitIds.FirstOrDefault() ?? "necr");
        return War3ItemUseCode.Success;
    }

    public War3ItemUseCode BeginTownPortal(
        RtsSimulation simulation,
        int caster,
        Vector2 destination,
        War3ShopItemDefinition item)
    {
        if (!ValidUnit(simulation, caster))
            return War3ItemUseCode.InvalidUnit;
        _townPortals.RemoveAll(value => value.Caster == caster);
        _townPortals.Add(new PendingTownPortal(
            caster,
            simulation.Units.Positions[caster],
            destination,
            simulation.Combat.Health[caster],
            MathF.Max(0f, item.CastTime),
            Radius(item.Area)));
        return War3ItemUseCode.Success;
    }

    private static void CompleteTownPortal(
        RtsSimulation simulation,
        int caster,
        Vector2 destination,
        float radius)
    {
        var owner = simulation.Combat.Teams[caster];
        var center = simulation.Units.Positions[caster];
        var units = Enumerable.Range(0, simulation.Units.Count)
            .Where(unit => ValidUnit(simulation, unit) &&
                simulation.Diplomacy.IsFriendly(
                    owner, simulation.Combat.Teams[unit]) &&
                Vector2.DistanceSquared(
                    center, simulation.Units.Positions[unit]) <=
                    radius * radius)
            .Order()
            .ToArray();
        if (units.Length > 0) simulation.TeleportUnits(units, destination);
    }

    public War3ItemUseCode UseSanctuaryStaff(
        RtsSimulation simulation,
        int caster,
        int target,
        Vector2 destination,
        War3ShopItemDefinition item)
    {
        if (!ValidUnit(simulation, caster))
            return War3ItemUseCode.InvalidUnit;
        if (!ValidUnit(simulation, target) ||
            !simulation.Diplomacy.IsFriendly(
                simulation.Combat.Teams[caster],
                simulation.Combat.Teams[target]))
            return War3ItemUseCode.InvalidTarget;
        if (Vector2.DistanceSquared(
                simulation.Units.Positions[caster],
                simulation.Units.Positions[target]) >
            Radius(item.Range) * Radius(item.Range))
            return War3ItemUseCode.OutOfRange;
        simulation.TeleportUnit(target, destination);
        var hero = simulation.Abilities.Observe(target).Hero;
        AddRecovery(
            target, item.AbilityRawId, 0f,
            Value(item, "E"), 0f,
            hero ? Value(item, "B") : Value(item, "C"),
            untilFull: true);
        return War3ItemUseCode.Success;
    }

    public War3ItemUseCode ApplyOrbOfFire(
        RtsSimulation simulation,
        int unit,
        War3ShopItemDefinition item)
    {
        if (!ValidUnit(simulation, unit))
            return War3ItemUseCode.InvalidUnit;
        if (!_orbBearers.Add(unit)) return War3ItemUseCode.Success;
        var weapons = simulation.Combat.WeaponProfiles[unit]
            .Select(weapon => weapon with
            {
                AttackDamage = weapon.AttackDamage + Value(item, "A"),
                TargetLayers = weapon.TargetLayers | CombatTargetLayer.AirUnit,
                Area = weapon.Area.Enabled
                    ? weapon.Area
                    : new CombatWeaponAreaSnapshot(
                        0f,
                        0f,
                        Radius(item.Area),
                        CombatTargetLayer.GroundUnit |
                        CombatTargetLayer.AirUnit |
                        CombatTargetLayer.Ward)
            })
            .ToImmutableArray();
        simulation.Combat.ReplaceWeaponProfiles(unit, weapons);
        return War3ItemUseCode.Success;
    }

    private void AddRecovery(
        int unit,
        string source,
        float duration,
        float healthPerSecond,
        float manaPerSecond,
        float delay = 0f,
        bool untilFull = false)
    {
        _recoveries.RemoveAll(value =>
            value.Unit == unit && value.Source == source);
        _recoveries.Add(new PeriodicRecovery(
            unit, source, duration, healthPerSecond, manaPerSecond,
            delay, untilFull));
    }

    private static float Value(
        War3ShopItemDefinition item,
        string key) => item.EffectData.GetValueOrDefault(key);

    private static float Radius(float editorDistance) =>
        MathF.Max(0f, editorDistance) * WorldDistanceScale;

    private static bool ValidUnit(RtsSimulation simulation, int unit) =>
        (uint)unit < (uint)simulation.Units.Count &&
        simulation.Units.Alive[unit];

    private readonly record struct PeriodicRecovery(
        int Unit,
        string Source,
        float RemainingSeconds,
        float HealthPerSecond,
        float ManaPerSecond,
        float DelayRemaining,
        bool UntilFull);

    private readonly record struct PendingTownPortal(
        int Caster,
        Vector2 Origin,
        Vector2 Destination,
        float StartingHealth,
        float RemainingSeconds,
        float Radius);
}
