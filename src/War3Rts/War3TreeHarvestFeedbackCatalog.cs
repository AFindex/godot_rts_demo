using War3Rts.Data;

namespace War3Rts;

/// <summary>
/// Presentation timing and weapon binding for a worker's tree-only attack.
/// Values come from the exported Unit Editor attack array; the sound itself is
/// still resolved by the independent audio catalog at playback time.
/// </summary>
public readonly record struct War3TreeHarvestFeedbackProfile(
    int WeaponSlot,
    string SoundFamily,
    float CooldownSeconds,
    float DamagePointSeconds,
    bool DataDriven)
{
    public static War3TreeHarvestFeedbackProfile Fallback { get; } = new(
        WeaponSlot: 0,
        SoundFamily: string.Empty,
        CooldownSeconds: 1f,
        DamagePointSeconds: 0.43f,
        DataDriven: false);
}

public static class War3TreeHarvestFeedbackCatalog
{
    public static War3TreeHarvestFeedbackProfile Resolve(
        IWar3UnitDataCatalog catalog,
        string workerObjectId)
    {
        if (!catalog.TryGet(workerObjectId, out var data))
            return War3TreeHarvestFeedbackProfile.Fallback;

        var attacks = data.Summary.Combat.Attacks;
        for (var slot = 0; slot < attacks.Length; slot++)
        {
            var attack = attacks[slot];
            if (!attack.Enabled || !attack.Targets.Any(value =>
                    value.Equals("tree", StringComparison.OrdinalIgnoreCase)))
                continue;

            var cooldown = attack.Cooldown is > 0f and var configuredCooldown
                ? configuredCooldown
                : War3TreeHarvestFeedbackProfile.Fallback.CooldownSeconds;
            var damagePoint = attack.Timing.DamagePoint is >= 0f and var configuredPoint
                ? Math.Clamp(configuredPoint, 0f, cooldown)
                : MathF.Min(
                    War3TreeHarvestFeedbackProfile.Fallback.DamagePointSeconds,
                    cooldown);
            return new War3TreeHarvestFeedbackProfile(
                slot,
                attack.SoundType ?? string.Empty,
                cooldown,
                damagePoint,
                DataDriven: true);
        }

        return War3TreeHarvestFeedbackProfile.Fallback;
    }

    /// <summary>
    /// Returns -1 before the first authored damage point, then the zero-based
    /// strike cycle. This keeps tree motion and sound aligned with the attack
    /// animation while economy cargo can progress at its own finer cadence.
    /// </summary>
    public static int StrikeIndex(
        in War3TreeHarvestFeedbackProfile profile,
        float harvestSeconds,
        float workRemaining)
    {
        var duration = MathF.Max(0f, harvestSeconds);
        var elapsed = Math.Clamp(duration - workRemaining, 0f, duration);
        if (elapsed + 0.0001f < profile.DamagePointSeconds) return -1;
        return Math.Max(0, (int)MathF.Floor(
            (elapsed - profile.DamagePointSeconds) /
            MathF.Max(0.05f, profile.CooldownSeconds) + 0.0001f));
    }
}
