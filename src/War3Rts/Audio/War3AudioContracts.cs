using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace War3Rts.Audio;

public enum War3AudioSpatialMode : byte
{
    NonPositional,
    World3D
}

public enum War3AudioSemantic : byte
{
    Interface,
    Notification,
    Selection,
    Command,
    AttackCommand,
    UnitReady,
    Attack,
    Projectile,
    Impact,
    Death,
    Animation,
    Ability,
    Ambient,
    Music,
    Dialogue
}

/// <summary>
/// Engine-independent request emitted by the Warcraft presentation adapter.
/// Simulation systems only publish gameplay events; they never construct this
/// request or reference audio files, buses, or Godot nodes.
/// </summary>
public readonly record struct War3AudioCueRequest(
    string CueId,
    War3AudioSemantic Semantic,
    ulong EventSequence = 0,
    Vector2? WorldPosition = null,
    int EmitterId = -1,
    bool? LoopOverride = null);

/// <summary>A logical cue as exported from the Warcraft SoundInfo tables.</summary>
public sealed record War3AudioCueDefinition(
    string Id,
    string Category,
    string[] ResourcePaths,
    War3AudioSpatialMode SpatialMode,
    bool Loop,
    float VolumeLinear,
    float Pitch,
    float PitchVariance,
    float MinimumDistance,
    float MaximumDistance,
    float CutoffDistance,
    int Priority,
    string Bus,
    string[] Flags);

/// <summary>
/// Immutable playback decision. Candidate selection is completed before the
/// request reaches Godot, which makes replay and smoke-test behavior stable.
/// </summary>
public readonly record struct War3ResolvedAudioCue(
    string CueId,
    string ResourcePath,
    War3AudioSpatialMode SpatialMode,
    bool Loop,
    float VolumeLinear,
    float PitchScale,
    float MinimumDistance,
    float MaximumDistance,
    float CutoffDistance,
    int Priority,
    string Bus);

public interface IWar3AudioCatalog
{
    bool IsAvailable { get; }
    string Error { get; }
    int CueCount { get; }
    int UnitBindingCount { get; }
    int AbilityBindingCount { get; }
    int AnimationEventBindingCount { get; }

    bool ContainsCue(string cueId);
    bool TryGetUnitBinding(
        string objectId,
        [NotNullWhen(true)] out War3UnitAudioBinding? binding);
    bool TryGetAbilityBinding(
        string abilityId,
        [NotNullWhen(true)] out War3AbilityAudioBinding? binding);
    bool TryGetAnimationEventCue(
        string eventCode,
        [NotNullWhen(true)] out string? cueId);
    bool TryResolve(
        in War3AudioCueRequest request,
        out War3ResolvedAudioCue cue);
}

public sealed record War3UnitAudioBinding(
    string ObjectId,
    string VoiceSet,
    string ArmorMaterial,
    War3WeaponAudioBinding[] Weapons,
    string MovementLoop,
    string BuildingLoop,
    float LoopFadeInSeconds,
    float LoopFadeOutSeconds);

public sealed record War3WeaponAudioBinding(
    int Slot,
    string ImpactPrefix);

public sealed record War3AbilityAudioBinding(
    string AbilityId,
    string EffectCue,
    string LoopedEffectCue);

public readonly record struct War3AudioLoopHandle(
    int EmitterId,
    string CueId)
{
    public bool IsValid => EmitterId >= 0 && !string.IsNullOrWhiteSpace(CueId);
}

public readonly record struct War3AbilityAudioSession(
    int EmitterId,
    string AbilityId,
    War3AudioLoopHandle LoopHandle)
{
    public bool HasLoop => LoopHandle.IsValid;
}

public readonly record struct War3AudioRuntimeSnapshot(
    int CachedStreams,
    int ActiveNonPositionalVoices,
    int ActiveWorldVoices,
    int ActiveLoops,
    long Played,
    long Suppressed,
    long Dropped,
    long Culled,
    long Missing);

/// <summary>Playback boundary implemented by the Godot presentation layer.</summary>
public interface IWar3AudioPlayback
{
    bool Play(
        in War3ResolvedAudioCue cue,
        in War3AudioCueRequest request);
    bool StartLoop(
        in War3ResolvedAudioCue cue,
        in War3AudioCueRequest request,
        out War3AudioLoopHandle handle);
    void StopLoop(War3AudioLoopHandle handle, float fadeSeconds = 0f);
    void StopEmitter(int emitterId, float fadeSeconds = 0f);
    void StopAll();
    War3AudioRuntimeSnapshot Snapshot();
}
