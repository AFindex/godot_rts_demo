using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class CombatPresentationSelfTest
{
    public static SelfTestResult Run()
    {
        var composer = new CombatPresentationComposer();
        var bolt = Projectile(1, new CombatWeaponDamageSnapshot(
            10f, 1, CombatAttribute.None, 0f, 0, 0f, 0f));
        var orb = Projectile(2, new CombatWeaponDamageSnapshot(
            10f, 1, CombatAttribute.Armored, 5f, 0, 0f, 0f));
        var volley = Projectile(3, new CombatWeaponDamageSnapshot(
            8f, 2, CombatAttribute.None, 0f, 0, 0f, 0f));
        composer.Update([bolt, orb, volley], new CombatEventBatch([], 0, 0), 0f);

        var moved = new[]
        {
            bolt with { Position = new Vector2(4f, 0f) },
            orb with { Position = new Vector2(4f, 10f) },
            volley with { Position = new Vector2(4f, 20f) }
        };
        var events = new CombatEventBatch(
        [
            new CombatEvent(
                10, 1, CombatEventKind.Impact, 0,
                CombatTargetKind.Unit, 4, 15f, 85f, 15f, 1, true, 2,
                new Vector2(20f, 10f)),
            new CombatEvent(
                10, 2, CombatEventKind.ProjectileExpired, 0,
                CombatTargetKind.Unit, 5, 0f, 0f, 0f, 0, false, 3,
                new Vector2(18f, 20f))
        ], 2, 0);
        var frame = composer.Update(moved, events, 0.1f);
        var aged = composer.Update([], new CombatEventBatch([], 2, 0), 0.2f);
        var expired = composer.Update([], new CombatEventBatch([], 2, 0), 0.21f);

        var passed = frame.Projectiles.Select(value => value.VisualKind)
                         .SequenceEqual([
                             CombatProjectileVisualKind.Bolt,
                             CombatProjectileVisualKind.Orb,
                             CombatProjectileVisualKind.Volley]) &&
                     frame.Projectiles.All(value =>
                         value.Trail.Length == 2 &&
                         value.Heading == Vector2.UnitX) &&
                     frame.Cues.Length == 2 &&
                     frame.Cues[0].Kind == CombatPresentationCueKind.Impact &&
                     frame.Cues[0].BonusApplied &&
                     frame.Cues[1].Kind == CombatPresentationCueKind.Expired &&
                     aged.Projectiles.Length == 0 && aged.Cues.Length == 2 &&
                     aged.Cues.All(value => value.NormalizedAge == 0.5f) &&
                     expired.Cues.Length == 0 &&
                     composer.LatestEventSequence == 2;
        return new SelfTestResult(passed,
            $"styles={string.Join(',', frame.Projectiles.Select(value => value.VisualKind))}, " +
            $"trails={string.Join(',', frame.Projectiles.Select(value => value.Trail.Length))}, " +
            $"cues={frame.Cues.Length}->{aged.Cues.Length}->{expired.Cues.Length}");
    }

    private static CombatProjectileSnapshot Projectile(
        int id,
        CombatWeaponDamageSnapshot weapon) => new(
            id, 0, CombatTargetKind.Unit, id + 3,
            new Vector2(0f, (id - 1) * 10f), 100f, weapon);
}
