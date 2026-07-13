using System.Numerics;

namespace RtsDemo.Simulation;

public enum CombatProjectileVisualKind : byte
{
    Bolt,
    Orb,
    Volley
}

public enum CombatPresentationCueKind : byte
{
    Impact,
    Expired,
    MuzzleFlash
}

public readonly record struct CombatProjectilePresentationSnapshot(
    int Id,
    CombatProjectileVisualKind VisualKind,
    Vector2 Position,
    Vector2 Heading,
    Vector2[] Trail);

public readonly record struct CombatPresentationCueSnapshot(
    ulong Sequence,
    CombatPresentationCueKind Kind,
    int ProjectileId,
    Vector2 Position,
    float NormalizedAge,
    float Damage,
    bool BonusApplied);

public sealed record CombatPresentationFrame(
    CombatProjectilePresentationSnapshot[] Projectiles,
    CombatPresentationCueSnapshot[] Cues,
    ulong LatestEventSequence,
    int LostEvents)
{
    public static CombatPresentationFrame Empty { get; } =
        new([], [], 0, 0);
}

/// <summary>
/// Non-authoritative presentation adapter. It consumes public projectile snapshots
/// and derived combat events, retaining only short visual history. None of its state
/// participates in simulation hashes, replay packages or hot snapshots.
/// </summary>
public sealed class CombatPresentationComposer
{
    public const int MaximumTrailPoints = 10;
    public const float CueLifetimeSeconds = 0.4f;
    public const float MuzzleFlashLifetimeSeconds = 0.14f;
    private const float MinimumTrailDistanceSquared = 0.25f;

    private readonly Dictionary<int, ProjectileTrack> _tracks = [];
    private readonly List<MutableCue> _cues = [];
    private readonly Func<CombatProjectileSnapshot, CombatProjectileVisualKind>
        _resolveVisualKind;

    public CombatPresentationComposer(
        Func<CombatProjectileSnapshot, CombatProjectileVisualKind>?
            resolveVisualKind = null)
    {
        _resolveVisualKind = resolveVisualKind ?? DefaultVisualKind;
    }

    public ulong LatestEventSequence { get; private set; }

    public CombatPresentationFrame Update(
        IReadOnlyList<CombatProjectileSnapshot> activeProjectiles,
        CombatEventBatch eventBatch,
        float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (eventBatch.LatestSequence < LatestEventSequence)
            Reset();

        AgeCues(deltaSeconds);
        foreach (var combatEvent in eventBatch.Events)
        {
            if (combatEvent.Kind is CombatEventKind.AttackStarted or
                CombatEventKind.Impact or CombatEventKind.ProjectileExpired)
            {
                _cues.Add(new MutableCue(
                    combatEvent.Sequence,
                    combatEvent.Kind switch
                    {
                        CombatEventKind.AttackStarted =>
                            CombatPresentationCueKind.MuzzleFlash,
                        CombatEventKind.Impact => CombatPresentationCueKind.Impact,
                        _ => CombatPresentationCueKind.Expired
                    },
                    combatEvent.ProjectileId,
                    combatEvent.WorldPosition,
                    0f,
                    combatEvent.Damage,
                    combatEvent.BonusApplied));
            }
        }
        LatestEventSequence = eventBatch.LatestSequence;

        var activeIds = new HashSet<int>();
        var projectiles = new CombatProjectilePresentationSnapshot[
            activeProjectiles.Count];
        for (var index = 0; index < activeProjectiles.Count; index++)
        {
            var projectile = activeProjectiles[index];
            activeIds.Add(projectile.Id);
            if (!_tracks.TryGetValue(projectile.Id, out var track))
            {
                track = new ProjectileTrack(_resolveVisualKind(projectile));
                _tracks.Add(projectile.Id, track);
            }
            track.Add(projectile.Position);
            projectiles[index] = new CombatProjectilePresentationSnapshot(
                projectile.Id,
                track.VisualKind,
                projectile.Position,
                track.Heading,
                track.Points.ToArray());
        }
        foreach (var id in _tracks.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            _tracks.Remove(id);

        var cues = _cues.Select(value => new CombatPresentationCueSnapshot(
            value.Sequence,
            value.Kind,
            value.ProjectileId,
            value.Position,
            Math.Clamp(
                value.AgeSeconds / LifetimeFor(value.Kind), 0f, 1f),
            value.Damage,
            value.BonusApplied)).ToArray();
        return new CombatPresentationFrame(
            projectiles, cues, LatestEventSequence, eventBatch.LostEvents);
    }

    public void Reset()
    {
        _tracks.Clear();
        _cues.Clear();
        LatestEventSequence = 0;
    }

    private void AgeCues(float deltaSeconds)
    {
        for (var index = _cues.Count - 1; index >= 0; index--)
        {
            var cue = _cues[index];
            cue.AgeSeconds += deltaSeconds;
            if (cue.AgeSeconds >= LifetimeFor(cue.Kind))
                _cues.RemoveAt(index);
            else
                _cues[index] = cue;
        }
    }

    private static float LifetimeFor(CombatPresentationCueKind kind) =>
        kind == CombatPresentationCueKind.MuzzleFlash
            ? MuzzleFlashLifetimeSeconds
            : CueLifetimeSeconds;

    private static CombatProjectileVisualKind DefaultVisualKind(
        CombatProjectileSnapshot projectile) =>
        projectile.Weapon.AttacksPerVolley > 1
            ? CombatProjectileVisualKind.Volley
            : projectile.Weapon.BonusVs != CombatAttribute.None
                ? CombatProjectileVisualKind.Orb
                : CombatProjectileVisualKind.Bolt;

    private sealed class ProjectileTrack(
        CombatProjectileVisualKind visualKind)
    {
        public CombatProjectileVisualKind VisualKind { get; } = visualKind;
        public List<Vector2> Points { get; } = new(MaximumTrailPoints);
        public Vector2 Heading { get; private set; }

        public void Add(Vector2 position)
        {
            if (Points.Count > 0)
            {
                var offset = position - Points[^1];
                if (offset.LengthSquared() < MinimumTrailDistanceSquared)
                    return;
                Heading = Vector2.Normalize(offset);
            }
            Points.Add(position);
            if (Points.Count > MaximumTrailPoints)
                Points.RemoveAt(0);
        }
    }

    private record struct MutableCue(
        ulong Sequence,
        CombatPresentationCueKind Kind,
        int ProjectileId,
        Vector2 Position,
        float AgeSeconds,
        float Damage,
        bool BonusApplied);
}
