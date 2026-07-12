using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class CombatProjectileSelfTest
{
    public static SelfTestResult Run()
    {
        var system = new CombatProjectileSystem();
        var weapon = new CombatWeaponDamageSnapshot(
            10f, 1, CombatAttribute.Armored, 4f, 2, 1f, 0.5f);
        var targetPosition = new Vector2(10f, 0f);
        var impacts = new List<CombatProjectileSnapshot>();
        var expired = new List<CombatProjectileSnapshot>();

        var launched = system.Launch(
            3, CombatTargetKind.Unit, 7, Vector2.Zero, 4f, weapon,
            out var firstId);
        system.Update(0.5f, Resolve, impacts.Add, expired.Add);
        var firstStep = system.ObserveActive().Single();
        targetPosition = new Vector2(10f, 4f);
        system.Update(0.5f, Resolve, impacts.Add, expired.Add);
        var homingStep = system.ObserveActive().Single();
        var captured = system.CaptureRuntimeState();

        var restored = new CombatProjectileSystem();
        restored.RestoreRuntimeState(captured);
        var restoredExact = captured.Active.SequenceEqual(
            restored.CaptureRuntimeState().Active) &&
            captured.NextId == restored.NextId;

        var secondLaunched = restored.Launch(
            3, CombatTargetKind.Unit, 9, Vector2.Zero, 4f, weapon,
            out var secondId);
        restored.Update(0.25f,
            (kind, id) => id == 9
                ? (false, Vector2.Zero)
                : (true, targetPosition),
            impacts.Add, expired.Add);

        var passed = launched && firstId == 1 &&
                     firstStep.Position == new Vector2(2f, 0f) &&
                     homingStep.Position.Y > 0f &&
                     homingStep.Position.X > firstStep.Position.X &&
                     restoredExact && secondLaunched && secondId == 2 &&
                     expired.Count == 1 && expired[0].Id == secondId &&
                     impacts.Count == 0 && restored.ActiveCount == 1;
        return new SelfTestResult(passed,
            $"ids={firstId}/{secondId}, first={firstStep.Position}, " +
            $"homing={homingStep.Position}, restored={restoredExact}, " +
            $"expired={expired.Count}, active={restored.ActiveCount}");

        (bool Valid, Vector2 Position) Resolve(
            CombatTargetKind kind, int id) =>
            kind == CombatTargetKind.Unit && id == 7
                ? (true, targetPosition)
                : (false, Vector2.Zero);
    }
}
