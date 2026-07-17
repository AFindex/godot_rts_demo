using System.Numerics;

namespace War3Rts.Audio;

/// <summary>
/// Engine-independent semantic audio facade. The RTS composition root sends
/// object ids and presentation events; this class owns Warcraft cue naming and
/// leaves stream loading, buses and voice pools behind IWar3AudioPlayback.
/// </summary>
public sealed class War3WorldAudioController
{
    private readonly IWar3AudioCatalog _catalog;
    private readonly IWar3AudioPlayback _playback;
    private readonly IWar3AudioAudiencePolicy _audience;
    private readonly int _localPlayerId;
    private ulong _presentationSequence = 1;

    public War3WorldAudioController(
        IWar3AudioCatalog catalog,
        IWar3AudioPlayback playback,
        int localPlayerId,
        IWar3AudioAudiencePolicy? audience = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(playback);
        _catalog = catalog;
        _playback = playback;
        _localPlayerId = localPlayerId;
        _audience = audience ?? War3AudioAudiencePolicy.Default;
    }

    public bool IsAvailable => _catalog.IsAvailable;
    public string Error => _catalog.Error;
    public long Suppressed { get; private set; }

    public bool PlayInterfaceClick() => PlayFirst(
        ["InterfaceClick", "MouseClick1"],
        War3AudioSemantic.Interface);

    public bool PlayInterfaceError() => PlayFirst(
        ["InterfaceError", "ErrorMessage"],
        War3AudioSemantic.Interface);

    public bool PlayRallyPoint(int sourcePlayerId, ulong eventSequence) =>
        Play(
            "RallyPointPlace",
            War3AudioSemantic.Notification,
            sourcePlayerId,
            null,
            -1,
            eventSequence);

    public bool PlayWaypoint(int sourcePlayerId) => Play(
        "WayPoint",
        War3AudioSemantic.Notification,
        sourcePlayerId,
        null,
        -1,
        NextSequence());

    public bool PlayBuildingPlaced(int sourcePlayerId) => Play(
        "PlaceBuildingDefault",
        War3AudioSemantic.Notification,
        sourcePlayerId,
        null,
        -1,
        NextSequence());

    public bool PlayConstructionStarted(
        int sourcePlayerId,
        Vector2 worldPosition,
        int emitterId,
        ulong eventSequence) => Play(
        "ConstructingBuildingDefault",
        War3AudioSemantic.Ambient,
        sourcePlayerId,
        worldPosition,
        emitterId,
        eventSequence);

    public bool PlayConstructionCompleted(
        int sourcePlayerId,
        ulong eventSequence) => Play(
        "JobDoneSoundHuman",
        War3AudioSemantic.Notification,
        sourcePlayerId,
        null,
        -1,
        eventSequence);

    public bool PlayResearchComplete(
        int sourcePlayerId,
        ulong eventSequence,
        bool upgrade = false) => Play(
        upgrade ? "UpgradeCompleteHuman" : "ResearchCompleteHuman",
        War3AudioSemantic.Notification,
        sourcePlayerId,
        null,
        -1,
        eventSequence);

    public bool PlayUnitSelection(
        string objectId,
        int sourcePlayerId,
        Vector2 worldPosition,
        int emitterId) =>
        TryVoice(objectId, "What", War3AudioSemantic.Selection,
            sourcePlayerId, worldPosition, emitterId, NextSequence());

    public bool PlayUnitCommand(
        string objectId,
        int sourcePlayerId,
        bool attack,
        Vector2 worldPosition,
        int emitterId) =>
        TryVoice(objectId, attack ? "YesAttack" : "Yes",
            attack ? War3AudioSemantic.AttackCommand : War3AudioSemantic.Command,
            sourcePlayerId, worldPosition, emitterId, NextSequence());

    public bool PlayUnitReady(
        string objectId,
        int sourcePlayerId,
        Vector2 worldPosition,
        int emitterId,
        ulong eventSequence) =>
        TryVoice(objectId, "Ready", War3AudioSemantic.UnitReady,
            sourcePlayerId, worldPosition, emitterId, eventSequence);

    public bool PlayUnitDeath(
        string objectId,
        int sourcePlayerId,
        Vector2 worldPosition,
        int emitterId,
        ulong eventSequence) =>
        TryVoice(objectId, "Death", War3AudioSemantic.Death,
            sourcePlayerId, worldPosition, emitterId, eventSequence);

    public bool PlayImpact(
        string attackerObjectId,
        string targetObjectId,
        int sourcePlayerId,
        Vector2 worldPosition,
        int emitterId,
        ulong eventSequence,
        int weaponSlot = 0)
    {
        if (!_catalog.TryGetUnitBinding(attackerObjectId, out var attacker) ||
            !_catalog.TryGetUnitBinding(targetObjectId, out var target))
            return false;
        var weapon = attacker.Weapons.FirstOrDefault(value =>
            value.Slot == weaponSlot);
        if (weapon is null || string.IsNullOrWhiteSpace(weapon.ImpactPrefix) ||
            string.IsNullOrWhiteSpace(target.ArmorMaterial))
            return false;
        return Play(
            weapon.ImpactPrefix + target.ArmorMaterial,
            War3AudioSemantic.Impact,
            sourcePlayerId,
            worldPosition,
            emitterId,
            eventSequence);
    }

    public bool PlayAnimationEvent(
        string eventCode,
        int sourcePlayerId,
        Vector2 worldPosition,
        int emitterId,
        ulong eventSequence)
    {
        return _catalog.TryGetAnimationEventCue(eventCode, out var cueId) &&
               Play(cueId, War3AudioSemantic.Animation, sourcePlayerId,
                   worldPosition, emitterId, eventSequence);
    }

    public bool StartAbility(
        string abilityId,
        int sourcePlayerId,
        Vector2 worldPosition,
        int emitterId,
        ulong eventSequence,
        out War3AbilityAudioSession session)
    {
        session = new War3AbilityAudioSession(
            emitterId, abilityId, default);
        if (!_audience.CanHear(
                War3AudioSemantic.Ability, _localPlayerId, sourcePlayerId))
        {
            Suppressed++;
            return false;
        }
        if (!_catalog.TryGetAbilityBinding(abilityId, out var binding))
            return false;

        var played = binding.EffectCue.Length > 0 && Play(
            binding.EffectCue, War3AudioSemantic.Ability, sourcePlayerId,
            worldPosition, emitterId, eventSequence);
        if (binding.LoopedEffectCue.Length == 0) return played;

        var request = new War3AudioCueRequest(
            binding.LoopedEffectCue,
            War3AudioSemantic.Ability,
            eventSequence,
            worldPosition,
            emitterId,
            LoopOverride: true);
        if (!_catalog.TryResolve(request, out var cue) ||
            !_playback.StartLoop(cue, request, out var loopHandle))
            return played;
        session = new War3AbilityAudioSession(
            emitterId, abilityId, loopHandle);
        return true;
    }

    public void StopAbility(
        in War3AbilityAudioSession session,
        float fadeSeconds = 0f)
    {
        if (session.HasLoop)
            _playback.StopLoop(session.LoopHandle, fadeSeconds);
    }

    public void StopEmitter(int emitterId, float fadeSeconds = 0f) =>
        _playback.StopEmitter(emitterId, fadeSeconds);

    public War3AudioRuntimeSnapshot Snapshot() => _playback.Snapshot();

    public void StopAll() => _playback.StopAll();

    private bool TryVoice(
        string objectId,
        string suffix,
        War3AudioSemantic semantic,
        int sourcePlayerId,
        Vector2 worldPosition,
        int emitterId,
        ulong eventSequence)
    {
        if (!_catalog.TryGetUnitBinding(objectId, out var binding) ||
            string.IsNullOrWhiteSpace(binding.VoiceSet))
            return false;
        return Play(binding.VoiceSet + suffix, semantic, sourcePlayerId, worldPosition,
            emitterId, eventSequence);
    }

    private bool PlayFirst(
        IReadOnlyList<string> cueIds,
        War3AudioSemantic semantic)
    {
        foreach (var cueId in cueIds)
        {
            if (!_catalog.ContainsCue(cueId)) continue;
            return Play(cueId, semantic, -1, null, -1, NextSequence());
        }
        return false;
    }

    private bool Play(
        string cueId,
        War3AudioSemantic semantic,
        int sourcePlayerId,
        Vector2? worldPosition,
        int emitterId,
        ulong eventSequence)
    {
        if (!_audience.CanHear(semantic, _localPlayerId, sourcePlayerId))
        {
            Suppressed++;
            return false;
        }
        var request = new War3AudioCueRequest(
            cueId, semantic, eventSequence, worldPosition, emitterId);
        return _catalog.TryResolve(request, out var resolved) &&
               _playback.Play(resolved, request);
    }

    private ulong NextSequence() => _presentationSequence++;
}
