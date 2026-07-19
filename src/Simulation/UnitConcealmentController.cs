namespace RtsDemo.Simulation;

public enum UnitConcealmentTransitionCode : byte
{
    Success,
    InvalidUnit,
    UnitDead,
    CapabilityUnavailable,
    AlreadyInRequestedState,
    TransitionActive
}

public readonly record struct UnitConcealmentTransitionResult(
    UnitConcealmentTransitionCode Code)
{
    public bool Succeeded => Code == UnitConcealmentTransitionCode.Success;
}

public readonly record struct UnitConcealmentRuntimeSnapshot(
    UnitConcealmentKind CapabilityKind,
    UnitConcealmentPhase Phase,
    UnitConcealmentKind ActiveKind,
    float TransitionRemaining,
    float TransitionProgress,
    bool CanMove,
    bool CanAttack);

/// <summary>
/// Owns concealment transitions without owning visibility, collision or combat.
/// Those systems consume the current CombatStore state and capability gates.
/// </summary>
public sealed class UnitConcealmentController
{
    private readonly UnitStore _units;
    private readonly CombatStore _combat;

    public UnitConcealmentController(UnitStore units, CombatStore combat)
    {
        _units = units;
        _combat = combat;
    }

    public UnitConcealmentTransitionResult Begin(int unit, bool activate)
    {
        if ((uint)unit >= (uint)_units.Count)
        {
            return new UnitConcealmentTransitionResult(
                UnitConcealmentTransitionCode.InvalidUnit);
        }
        if (!_units.Alive[unit])
        {
            return new UnitConcealmentTransitionResult(
                UnitConcealmentTransitionCode.UnitDead);
        }

        var capability = _combat.ConcealmentCapabilities[unit];
        if (capability.Kind == UnitConcealmentKind.None)
        {
            return new UnitConcealmentTransitionResult(
                UnitConcealmentTransitionCode.CapabilityUnavailable);
        }

        var phase = _combat.ConcealmentPhases[unit];
        if (phase is UnitConcealmentPhase.Activating or
            UnitConcealmentPhase.Deactivating)
        {
            return new UnitConcealmentTransitionResult(
                UnitConcealmentTransitionCode.TransitionActive);
        }
        if (activate && phase == UnitConcealmentPhase.Concealed ||
            !activate && phase == UnitConcealmentPhase.Visible)
        {
            return new UnitConcealmentTransitionResult(
                UnitConcealmentTransitionCode.AlreadyInRequestedState);
        }

        _combat.ConcealmentPhases[unit] = activate
            ? UnitConcealmentPhase.Activating
            : UnitConcealmentPhase.Deactivating;
        _combat.ConcealmentTransitionRemaining[unit] = activate
            ? capability.ActivationSeconds
            : capability.DeactivationSeconds;
        return new UnitConcealmentTransitionResult(
            UnitConcealmentTransitionCode.Success);
    }

    public void Update(
        float delta,
        Action<int, bool>? transitionCompleted = null)
    {
        foreach (var unit in _units.AliveUnits)
        {
            var phase = _combat.ConcealmentPhases[unit];
            if (phase is not (UnitConcealmentPhase.Activating or
                UnitConcealmentPhase.Deactivating))
            {
                continue;
            }

            var remaining = MathF.Max(
                0f, _combat.ConcealmentTransitionRemaining[unit] - delta);
            _combat.ConcealmentTransitionRemaining[unit] = remaining;
            if (remaining > 0f)
                continue;

            var capability = _combat.ConcealmentCapabilities[unit];
            if (phase == UnitConcealmentPhase.Activating)
            {
                _combat.ConcealmentPhases[unit] =
                    UnitConcealmentPhase.Concealed;
                _combat.ConcealmentKinds[unit] = capability.Kind;
                _combat.VisionRanges[unit] = capability.ConcealedVisionRange;
                transitionCompleted?.Invoke(unit, true);
            }
            else
            {
                _combat.ConcealmentPhases[unit] =
                    UnitConcealmentPhase.Visible;
                _combat.ConcealmentKinds[unit] = UnitConcealmentKind.None;
                _combat.VisionRanges[unit] = _combat.BaseVisionRanges[unit];
                transitionCompleted?.Invoke(unit, false);
            }
        }
    }

    public bool CanActivate(int unit) =>
        (uint)unit < (uint)_units.Count && _units.Alive[unit] &&
        _combat.ConcealmentCapabilities[unit].Kind !=
            UnitConcealmentKind.None;

    public bool CanMove(int unit) => AllowsAction(
        unit,
        static capability => capability.CanMoveWhileConcealed);

    public bool CanAttack(int unit) => AllowsAction(
        unit,
        static capability => capability.CanAttackWhileConcealed);

    public bool RequestedStateReached(int unit, bool active)
    {
        if ((uint)unit >= (uint)_units.Count || !_units.Alive[unit])
            return true;
        return active
            ? _combat.ConcealmentPhases[unit] ==
                UnitConcealmentPhase.Concealed
            : _combat.ConcealmentPhases[unit] ==
                UnitConcealmentPhase.Visible;
    }

    public UnitConcealmentRuntimeSnapshot Observe(int unit)
    {
        if ((uint)unit >= (uint)_units.Count)
            throw new ArgumentOutOfRangeException(nameof(unit));
        var capability = _combat.ConcealmentCapabilities[unit];
        var phase = _combat.ConcealmentPhases[unit];
        var duration = phase switch
        {
            UnitConcealmentPhase.Activating => capability.ActivationSeconds,
            UnitConcealmentPhase.Deactivating => capability.DeactivationSeconds,
            _ => 0f
        };
        var remaining = _combat.ConcealmentTransitionRemaining[unit];
        var progress = duration > 0f
            ? Math.Clamp(1f - remaining / duration, 0f, 1f)
            : 1f;
        return new UnitConcealmentRuntimeSnapshot(
            capability.Kind,
            phase,
            _combat.ConcealmentKinds[unit],
            remaining,
            progress,
            CanMove(unit),
            CanAttack(unit));
    }

    private bool AllowsAction(
        int unit,
        Func<UnitConcealmentCapabilitySnapshot, bool> activePermission)
    {
        if ((uint)unit >= (uint)_units.Count || !_units.Alive[unit])
            return false;
        var capability = _combat.ConcealmentCapabilities[unit];
        if (capability.Kind == UnitConcealmentKind.None ||
            _combat.ConcealmentPhases[unit] == UnitConcealmentPhase.Visible)
        {
            return true;
        }
        return activePermission(capability);
    }
}
