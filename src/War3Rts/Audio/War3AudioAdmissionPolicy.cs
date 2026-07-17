namespace War3Rts.Audio;

/// <summary>
/// Presentation-only overload protection. It rejects accidental duplicate
/// requests from the same emitter and caps noisy semantic groups before a
/// stream is loaded or a Godot voice is acquired.
/// </summary>
public sealed class War3AudioAdmissionPolicy
{
    private const ulong HistoryRetentionMilliseconds = 30_000;
    private readonly Dictionary<AdmissionKey, ulong> _lastAccepted = [];

    public bool TryAdmit(
        in War3AudioCueRequest request,
        int activeSemanticVoices,
        ulong nowMilliseconds,
        bool applyCooldown = true)
    {
        if (activeSemanticVoices >= ConcurrentLimit(request.Semantic))
            return false;
        if (!applyCooldown) return true;

        var key = new AdmissionKey(request.EmitterId, request.CueId);
        var cooldown = CooldownMilliseconds(request.Semantic);
        if (_lastAccepted.TryGetValue(key, out var previous) &&
            nowMilliseconds >= previous && nowMilliseconds - previous < cooldown)
        {
            return false;
        }
        _lastAccepted[key] = nowMilliseconds;
        if (_lastAccepted.Count > 2_048) Prune(nowMilliseconds);
        return true;
    }

    public void Reset() => _lastAccepted.Clear();

    public static int ConcurrentLimit(War3AudioSemantic semantic) => semantic switch
    {
        War3AudioSemantic.Interface or
            War3AudioSemantic.Notification => 6,
        War3AudioSemantic.Selection or
            War3AudioSemantic.Command or
            War3AudioSemantic.AttackCommand or
            War3AudioSemantic.UnitReady => 8,
        War3AudioSemantic.Impact => 24,
        War3AudioSemantic.Animation => 16,
        War3AudioSemantic.Ability => 12,
        War3AudioSemantic.Ambient => 4,
        _ => 40
    };

    public static ulong CooldownMilliseconds(War3AudioSemantic semantic) =>
        semantic switch
        {
            War3AudioSemantic.Interface or
                War3AudioSemantic.Notification => 40,
            War3AudioSemantic.Selection or
                War3AudioSemantic.Command or
                War3AudioSemantic.AttackCommand or
                War3AudioSemantic.UnitReady => 80,
            War3AudioSemantic.Impact => 25,
            War3AudioSemantic.Animation => 35,
            War3AudioSemantic.Ability => 60,
            War3AudioSemantic.Death => 250,
            _ => 20
        };

    private void Prune(ulong nowMilliseconds)
    {
        foreach (var key in _lastAccepted
                     .Where(value => nowMilliseconds >= value.Value &&
                                     nowMilliseconds - value.Value >
                                     HistoryRetentionMilliseconds)
                     .Select(value => value.Key)
                     .ToArray())
        {
            _lastAccepted.Remove(key);
        }
    }

    private readonly record struct AdmissionKey(int EmitterId, string CueId);
}
