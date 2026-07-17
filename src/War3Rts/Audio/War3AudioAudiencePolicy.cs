namespace War3Rts.Audio;

/// <summary>
/// Pure policy boundary for owner-private acknowledgements. World sound effects
/// remain audible to every listener and are subsequently limited by range.
/// </summary>
public interface IWar3AudioAudiencePolicy
{
    bool CanHear(
        War3AudioSemantic semantic,
        int localPlayerId,
        int sourcePlayerId);
}

public sealed class War3AudioAudiencePolicy : IWar3AudioAudiencePolicy
{
    public static War3AudioAudiencePolicy Default { get; } = new();

    public bool CanHear(
        War3AudioSemantic semantic,
        int localPlayerId,
        int sourcePlayerId)
    {
        if (sourcePlayerId < 0) return true;
        return semantic switch
        {
            War3AudioSemantic.Notification or
            War3AudioSemantic.Selection or
            War3AudioSemantic.Command or
            War3AudioSemantic.AttackCommand or
            War3AudioSemantic.UnitReady => sourcePlayerId == localPlayerId,
            _ => true
        };
    }
}

/// <summary>Engine-independent hard-cutoff test used before allocating a voice.</summary>
public static class War3AudioRangePolicy
{
    public static bool IsAudible(float distanceSquared, float cutoffDistance)
    {
        if (!float.IsFinite(distanceSquared) || distanceSquared < 0f)
            return false;
        if (!float.IsFinite(cutoffDistance) || cutoffDistance <= 0f)
            return true;
        return distanceSquared <= cutoffDistance * cutoffDistance;
    }
}
