using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Demos.War3;
using RtsDemo.Simulation;
using System.Text.Json;
using War3Rts.Audio;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts;

public sealed partial class War3Rts
{
    private War3WorldAudioController? _worldAudio;
    private GodotWar3AudioPlayback? _audioPlayback;
    private GodotWar3MusicPlayer? _musicPlayer;
    private ulong _audioCombatCursor;
    private ulong _audioGameplayCursor;
    private ulong _audioAbilityCursor;
    private long _audioLostEvents;
    private long _audioAnimationEvents;
    private long _audioAnimationPlayed;
    private long _audioAbilityEvents;
    private long _audioTreeHarvestEvents;
    private long _audioTreeHarvestPlayed;
    private readonly Dictionary<AbilityAudioKey, War3AbilityAudioSession>
        _activeAbilityAudio = [];

    private void InitializeAudio()
    {
        if (_simulation is null || _worldAudio is not null) return;
        var catalog = War3AudioCatalog.Open(
            War3AssetPack.AbsolutePath("data/audio_catalog"));
        if (!catalog.IsAvailable)
        {
            GD.PushWarning($"WAR3_AUDIO unavailable={catalog.Error}");
            return;
        }

        _audioPlayback = new GodotWar3AudioPlayback { Name = "War3Audio" };
        AddChild(_audioPlayback);
        var settings = War3AudioMixSettingsCodec.LoadOrDefault(
            ProjectSettings.GlobalizePath("user://war3_audio_settings.json"));
        _audioPlayback.Initialize(
            ToAudioWorld,
            settings,
            () => _camera?.GlobalPosition ?? Vector3.Zero);
        _worldAudio = new War3WorldAudioController(
            catalog, _audioPlayback, War3HumanScenario.PlayerId);
        if (_presenter is not null)
        {
            _presenter.AnimationAudioEvent += OnAnimationAudioEvent;
            _presenter.TreeHarvestFeedback += OnTreeHarvestFeedback;
        }
        var musicCount = 0;
        try
        {
            var playlist = War3MusicPlaylist.Load(ProjectSettings.GlobalizePath(
                "res://assets/generated/warcraft3_audio/runtime_manifest.json"));
            musicCount = playlist.Count;
            if (musicCount > 0)
            {
                _musicPlayer = new GodotWar3MusicPlayer { Name = "War3Music" };
                AddChild(_musicPlayer);
                _musicPlayer.Initialize(playlist);
            }
        }
        catch (Exception exception) when (exception is IOException or
                                          JsonException or InvalidDataException)
        {
            GD.PushWarning($"WAR3_AUDIO music_unavailable={exception.Message}");
        }
        _audioCombatCursor = _simulation.CombatEvents.LatestSequence;
        _audioGameplayCursor = _simulation.GameplayEvents.LatestSequence;
        _audioAbilityCursor = _simulation.AbilityEvents.LatestSequence;
        GD.Print(
            $"WAR3_AUDIO ready cues={catalog.CueCount} " +
            $"units={catalog.UnitBindingCount} " +
            $"abilities={catalog.AbilityBindingCount} " +
            $"animation_events={catalog.AnimationEventBindingCount} " +
            $"music={musicCount}");
    }

    private void RunAudioSmoke()
    {
        var catalog = War3AudioCatalog.Open(
            War3AssetPack.AbsolutePath("data/audio_catalog"));
        var errors = new List<string>();
        if (!catalog.IsAvailable) errors.Add(catalog.Error);
        if (!catalog.TryGetUnitBinding("hfoo", out var footman))
        {
            errors.Add("hfoo binding is missing");
        }
        else
        {
            if (!footman.VoiceSet.Equals(
                    "Footman", StringComparison.OrdinalIgnoreCase))
                errors.Add($"unexpected hfoo voice set '{footman.VoiceSet}'");
            if (!footman.ArmorMaterial.Equals(
                    "Metal", StringComparison.OrdinalIgnoreCase))
                errors.Add($"unexpected hfoo armor '{footman.ArmorMaterial}'");
            if (!footman.Weapons.Any(value => value.Slot == 0 &&
                    value.ImpactPrefix.Equals(
                        "MetalMediumSlice", StringComparison.OrdinalIgnoreCase)))
                errors.Add("hfoo weapon sound family is missing");
        }
        if (!catalog.TryGetUnitBinding("hpea", out var peasant) ||
            !peasant.Weapons.Any(value => value.Slot == 1 &&
                value.ImpactPrefix.Equals(
                    "AxeMediumChop", StringComparison.OrdinalIgnoreCase)))
            errors.Add("hpea tree-harvest weapon sound family is missing");
        if (!catalog.TryGetAbilityBinding("Ainf", out var innerFire) ||
            !innerFire.EffectCue.Equals(
                "InnerFireCast", StringComparison.OrdinalIgnoreCase))
            errors.Add("Ainf ability audio binding is missing");
        if (!catalog.TryGetAbilityBinding("AHdr", out var siphonMana) ||
            !siphonMana.LoopedEffectCue.Equals(
                "SiphonManaLoop", StringComparison.OrdinalIgnoreCase))
            errors.Add("AHdr looped ability audio binding is missing");
        if (!catalog.TryGetAnimationEventCue("DFOO", out var deathCue) ||
            !deathCue.Equals("FootmanDeath", StringComparison.OrdinalIgnoreCase))
            errors.Add("DFOO animation audio binding is missing");
        var footmanMetadata = War3RuntimeAssets.LoadMetadata(
            "Units\\Human\\Footman\\Footman.mdx");
        if (!footmanMetadata.EventObjects.Any(value =>
                value.TryGetSoundEventCode(out var eventCode) &&
                eventCode.Equals("DFOO", StringComparison.OrdinalIgnoreCase)))
            errors.Add("Footman SNDX timeline metadata is missing");

        var checks = new[]
        {
            ("InterfaceClick", War3AudioSemantic.Interface, (NVector2?)null),
            ("FootmanWhat", War3AudioSemantic.Selection, (NVector2?)NVector2.Zero),
            ("FootmanYes", War3AudioSemantic.Command, (NVector2?)NVector2.Zero),
            ("FootmanDeath", War3AudioSemantic.Death, (NVector2?)NVector2.Zero),
            ("InnerFireCast", War3AudioSemantic.Ability,
                (NVector2?)NVector2.Zero),
            ("SiphonManaLoop", War3AudioSemantic.Ability,
                (NVector2?)NVector2.Zero),
            ("MetalMediumSliceMetal", War3AudioSemantic.Impact,
                (NVector2?)NVector2.Zero),
            ("AxeMediumChopWood", War3AudioSemantic.Impact,
                (NVector2?)NVector2.Zero),
            ("RallyPointPlace", War3AudioSemantic.Notification,
                (NVector2?)null),
            ("WayPoint", War3AudioSemantic.Notification, (NVector2?)null),
            ("PlaceBuildingDefault", War3AudioSemantic.Notification,
                (NVector2?)null),
            ("ConstructingBuildingDefault", War3AudioSemantic.Ambient,
                (NVector2?)NVector2.Zero),
            ("JobDoneSoundHuman", War3AudioSemantic.Notification,
                (NVector2?)null),
            ("ResearchCompleteHuman", War3AudioSemantic.Notification,
                (NVector2?)null),
            ("UpgradeCompleteHuman", War3AudioSemantic.Notification,
                (NVector2?)null),
            ("InterfaceError", War3AudioSemantic.Interface, (NVector2?)null)
        };
        ulong sequence = 1;
        foreach (var (cueId, semantic, position) in checks)
        {
            var request = new War3AudioCueRequest(
                cueId, semantic, sequence++, position, 1);
            if (!catalog.TryResolve(request, out var cue))
            {
                errors.Add($"cue '{cueId}' did not resolve");
                continue;
            }
            if (!ResourceLoader.Exists(cue.ResourcePath) ||
                ResourceLoader.Load<AudioStream>(cue.ResourcePath) is null)
                errors.Add($"resource '{cue.ResourcePath}' did not load");
            if (cueId == "FootmanWhat" &&
                (MathF.Abs(cue.MinimumDistance - 75f) > 0.01f ||
                 MathF.Abs(cue.MaximumDistance - 250f) > 0.01f ||
                 MathF.Abs(cue.CutoffDistance - 2_500f) > 0.01f))
            {
                errors.Add(
                    $"unexpected Footman distance model " +
                    $"{cue.MinimumDistance}/{cue.MaximumDistance}/" +
                    $"{cue.CutoffDistance}");
            }
        }

        var audience = War3AudioAudiencePolicy.Default;
        if (audience.CanHear(War3AudioSemantic.UnitReady, 0, 1) ||
            audience.CanHear(War3AudioSemantic.Command, 0, 1) ||
            audience.CanHear(War3AudioSemantic.Notification, 0, 1) ||
            !audience.CanHear(War3AudioSemantic.UnitReady, 0, 0) ||
            !audience.CanHear(War3AudioSemantic.Notification, 0, 0) ||
            !audience.CanHear(War3AudioSemantic.Impact, 0, 1))
            errors.Add("audio audience policy rejected an ownership boundary");
        if (!War3AudioRangePolicy.IsAudible(99f, 10f) ||
            War3AudioRangePolicy.IsAudible(101f, 10f))
            errors.Add("audio cutoff policy rejected a distance boundary");
        var admission = new War3AudioAdmissionPolicy();
        var animationRequest = new War3AudioCueRequest(
            "FootmanDeath", War3AudioSemantic.Animation, 1,
            NVector2.Zero, 7);
        if (!admission.TryAdmit(animationRequest, 0, 1_000) ||
            admission.TryAdmit(animationRequest, 0, 1_020) ||
            !admission.TryAdmit(animationRequest, 0, 1_040) ||
            admission.TryAdmit(
                animationRequest,
                War3AudioAdmissionPolicy.ConcurrentLimit(
                    War3AudioSemantic.Animation),
                2_000))
            errors.Add("audio admission policy rejected cooldown/concurrency");
        var recordingPlayback = new RecordingAudioPlayback();
        var abilityController = new War3WorldAudioController(
            catalog, recordingPlayback, 0);
        if (!abilityController.StartAbility(
                "Ainf", 0, NVector2.Zero, 17, 10, out var innerFireSession) ||
            innerFireSession.HasLoop || recordingPlayback.Played != 1)
            errors.Add("one-shot ability audio lifecycle is invalid");
        if (!abilityController.StartAbility(
                "AHdr", 0, NVector2.Zero, 17, 11, out var siphonSession) ||
            !siphonSession.HasLoop || recordingPlayback.LoopsStarted != 1)
            errors.Add("looped ability audio lifecycle did not start");
        abilityController.StopAbility(siphonSession, 0.1f);
        if (recordingPlayback.LoopsStopped != 1)
            errors.Add("looped ability audio lifecycle did not stop");
        var playedBeforeTreeHarvest = recordingPlayback.Played;
        if (!abilityController.PlayImpactMaterial(
                "hpea", "Wood", 0, NVector2.Zero, 23, 12,
                weaponSlot: 1) ||
            recordingPlayback.Played != playedBeforeTreeHarvest + 1)
            errors.Add("data-driven hpea tree-harvest impact did not play");

        var abilityEvents = new AbilityEventStream(4);
        abilityEvents.Publish(
            10, AbilityEventKind.Started, "AHdr", 7,
            AbilityTargetKind.Unit, 8, NVector2.Zero);
        abilityEvents.Publish(
            11, AbilityEventKind.Impact, "AHdr", 7,
            AbilityTargetKind.Unit, 8, NVector2.One);
        abilityEvents.Publish(
            12, AbilityEventKind.Interrupted, "AHdr", 7,
            AbilityTargetKind.Unit, 8, NVector2.One,
            AbilityEndReason.TargetInvalid);
        var abilityBatch = abilityEvents.ReadAfter(0);
        if (abilityBatch.LostEvents != 0 || abilityBatch.Events.Length != 3 ||
            abilityBatch.Events[0].Kind != AbilityEventKind.Started ||
            abilityBatch.Events[1].Kind != AbilityEventKind.Impact ||
            abilityBatch.Events[2].Kind != AbilityEventKind.Interrupted ||
            abilityBatch.Events[2].EndReason != AbilityEndReason.TargetInvalid)
            errors.Add("authoritative ability event lifecycle is invalid");

        var runtimeResourceCount = 0;
        var runtimeMusicCount = 0;
        var runtimeAbilityCount = 0;
        var runtimeAnimationEventCount = 0;
        try
        {
            using var runtime = JsonDocument.Parse(File.ReadAllText(
                ProjectSettings.GlobalizePath(
                    "res://assets/generated/warcraft3_audio/runtime_manifest.json")));
            if (!runtime.RootElement.GetProperty("schema").GetString()!.Equals(
                    "war3-audio-runtime-pack/v1", StringComparison.Ordinal))
                errors.Add("runtime pack schema is invalid");
            var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cue in runtime.RootElement.GetProperty("cues")
                         .EnumerateArray())
            foreach (var resource in cue.GetProperty("resources").EnumerateArray())
            {
                var relative = resource.GetString();
                if (!string.IsNullOrWhiteSpace(relative)) resources.Add(relative);
            }
            if (runtime.RootElement.TryGetProperty("music", out var music))
            {
                foreach (var track in music.EnumerateArray())
                {
                    var relative = track.GetProperty("resource").GetString();
                    if (string.IsNullOrWhiteSpace(relative)) continue;
                    runtimeMusicCount++;
                    resources.Add(relative);
                }
            }
            if (runtime.RootElement.TryGetProperty("abilities", out var abilities))
            {
                var hasInnerFire = false;
                var hasSiphonMana = false;
                foreach (var ability in abilities.EnumerateArray())
                {
                    runtimeAbilityCount++;
                    var id = ability.GetProperty("id").GetString();
                    hasInnerFire |= id == "Ainf" &&
                        ability.GetProperty("effect").GetString() == "InnerFireCast";
                    hasSiphonMana |= id == "AHdr" &&
                        ability.GetProperty("loopedEffect").GetString() ==
                        "SiphonManaLoop";
                }
                if (!hasInnerFire || !hasSiphonMana)
                    errors.Add("runtime ability audio bindings are incomplete");
            }
            if (runtime.RootElement.TryGetProperty(
                    "animationEvents", out var animationEvents))
            {
                var hasFootmanDeath = false;
                foreach (var animationEvent in animationEvents.EnumerateArray())
                {
                    runtimeAnimationEventCount++;
                    hasFootmanDeath |=
                        animationEvent.GetProperty("eventCode").GetString() ==
                        "DFOO" &&
                        animationEvent.GetProperty("cue").GetString() ==
                        "FootmanDeath";
                }
                if (!hasFootmanDeath)
                    errors.Add("runtime animation event bindings are incomplete");
            }
            runtimeResourceCount = resources.Count;
            foreach (var relative in resources)
            {
                var resourcePath =
                    "res://assets/generated/warcraft3_audio/" + relative;
                if (!ResourceLoader.Exists(resourcePath))
                    errors.Add($"runtime resource '{relative}' was not imported");
            }
            var playlist = War3MusicPlaylist.Load(ProjectSettings.GlobalizePath(
                "res://assets/generated/warcraft3_audio/runtime_manifest.json"));
            if (playlist.Count != runtimeMusicCount ||
                !playlist.Tracks.Any(value => value.Id == "Human1"))
                errors.Add("human music playlist is invalid");
        }
        catch (Exception exception) when (exception is IOException or JsonException or
                                          KeyNotFoundException)
        {
            errors.Add($"runtime manifest is invalid: {exception.Message}");
        }

        if (errors.Count == 0)
            GD.Print(
                $"WAR3_AUDIO_SMOKE PASS cues={catalog.CueCount} " +
                $"units={catalog.UnitBindingCount} " +
                $"abilities={catalog.AbilityBindingCount} " +
                $"animation_events={catalog.AnimationEventBindingCount} " +
                $"checked={checks.Length} " +
                $"runtime={runtimeResourceCount} " +
                $"runtime_abilities={runtimeAbilityCount} " +
                $"runtime_animation_events={runtimeAnimationEventCount} " +
                $"music={runtimeMusicCount}");
        else
            GD.PushError("WAR3_AUDIO_SMOKE FAIL " + string.Join("; ", errors));
        GetTree().Quit(errors.Count == 0 ? 0 : 1);
    }

    private Vector3 ToAudioWorld(NVector2 position) =>
        SimPlane3DTransform.ToWorld(position, GroundWorldHeight(position) + 0.7f);

    private void ConsumeAudioEvents()
    {
        if (_simulation is null || _worldAudio is null) return;
        var combat = _simulation.CombatEvents.ReadAfter(_audioCombatCursor);
        _audioCombatCursor = combat.LatestSequence;
        if (combat.LostEvents > 0) ReportLostAudioEvents(combat.LostEvents);
        foreach (var value in combat.Events)
        {
            switch (value.Kind)
            {
                case CombatEventKind.Impact:
                    if (TryUnitObjectId(value.AttackerUnit, out var attacker) &&
                        TryUnitPlayerId(
                            value.AttackerUnit, out var attackerPlayer))
                    {
                        if (TryTargetObjectId(
                                value.TargetKind,
                                value.TargetId,
                                out var target))
                        {
                            _worldAudio.PlayImpact(
                                attacker, target, attackerPlayer,
                                value.WorldPosition,
                                value.AttackerUnit, value.Sequence);
                        }
                        else if (TryCombatObjectImpactMaterial(
                                     value.TargetKind,
                                     value.TargetId,
                                     out var material))
                        {
                            _worldAudio.PlayImpactMaterial(
                                attacker, material, attackerPlayer,
                                value.WorldPosition,
                                value.AttackerUnit, value.Sequence);
                        }
                    }
                    break;
                case CombatEventKind.TargetDestroyed
                    when value.TargetKind == CombatTargetKind.Unit:
                    if (TryUnitObjectId(value.TargetId, out var destroyed) &&
                        TryUnitPlayerId(value.TargetId, out var destroyedPlayer))
                    {
                        StopAbilityAudioForCaster(value.TargetId);
                        _worldAudio.StopEmitter(value.TargetId, 0.1f);
                        _worldAudio.PlayUnitDeath(
                            destroyed, destroyedPlayer, value.WorldPosition,
                            value.TargetId, value.Sequence);
                    }
                    break;
            }
        }

        var abilities = _simulation.AbilityEvents.ReadAfter(_audioAbilityCursor);
        _audioAbilityCursor = abilities.LatestSequence;
        if (abilities.LostEvents > 0) ReportLostAudioEvents(abilities.LostEvents);
        foreach (var value in abilities.Events)
            ConsumeAbilityAudioEvent(value);

        var gameplay = _simulation.GameplayEvents.ReadAfter(_audioGameplayCursor);
        _audioGameplayCursor = gameplay.LatestSequence;
        if (gameplay.LostEvents > 0) ReportLostAudioEvents(gameplay.LostEvents);
        foreach (var value in gameplay.Events)
        {
            switch (value.Kind)
            {
                case GameplayEventKind.UnitProduced
                    when TryUnitObjectId(value.Unit, out var objectId) &&
                         TryUnitPlayerId(value.Unit, out var sourcePlayer):
                    _worldAudio.PlayUnitReady(
                        objectId, sourcePlayer, value.WorldPosition,
                        value.Unit, value.Sequence);
                    break;
                case GameplayEventKind.ProductionRallyChanged:
                    _worldAudio.PlayRallyPoint(value.Player, value.Sequence);
                    break;
                case GameplayEventKind.ConstructionStarted or
                    GameplayEventKind.ConstructionResumed
                    when TryUnitPlayerId(value.Unit, out var builderPlayer):
                    _worldAudio.PlayConstructionStarted(
                        builderPlayer,
                        value.WorldPosition,
                        BuildingAudioEmitter(value.Building),
                        value.Sequence);
                    break;
                case GameplayEventKind.BuildingUpgradeStarted:
                    _worldAudio.PlayConstructionStarted(
                        value.Player,
                        value.WorldPosition,
                        BuildingAudioEmitter(value.Building),
                        value.Sequence);
                    break;
                case GameplayEventKind.ConstructionCompleted
                    when TryGameplayEventPlayer(value, out var ownerPlayer):
                    _worldAudio.PlayConstructionCompleted(
                        ownerPlayer, value.Sequence);
                    break;
                case GameplayEventKind.ResearchCompleted:
                    _worldAudio.PlayResearchComplete(
                        value.Player, value.Sequence, upgrade: true);
                    break;
                case GameplayEventKind.BuildingUpgradeCompleted:
                    _worldAudio.PlayResearchComplete(
                        value.Player, value.Sequence, upgrade: true);
                    break;
            }
        }
    }

    private void ConsumeAbilityAudioEvent(in AbilityEvent value)
    {
        if (_worldAudio is null)
            return;
        var emitter = value.CasterUnit >= 0
            ? value.CasterUnit
            : BuildingAudioEmitter(value.CasterBuilding);
        var hasPlayer = value.CasterUnit >= 0
            ? TryUnitPlayerId(value.CasterUnit, out var sourcePlayer)
            : TryBuildingPlayerId(value.CasterBuilding, out sourcePlayer);
        if (!hasPlayer) return;

        _audioAbilityEvents++;
        var key = new AbilityAudioKey(emitter, value.AbilityId);
        switch (value.Kind)
        {
            case AbilityEventKind.Started:
                StopAbilityAudio(key, 0.05f);
                if (_worldAudio.StartAbility(
                        value.AbilityId,
                        sourcePlayer,
                        value.WorldPosition,
                        emitter,
                        value.Sequence,
                        out var session) && session.HasLoop)
                {
                    _activeAbilityAudio[key] = session;
                }
                break;
            case AbilityEventKind.Ended:
                StopAbilityAudio(key, 0.12f);
                break;
            case AbilityEventKind.Interrupted:
                StopAbilityAudio(key, 0.05f);
                break;
        }

        if (_audioAbilityEvents == 1)
        {
            GD.Print(
                $"WAR3_AUDIO ability_events=active ability={value.AbilityId} " +
                $"kind={value.Kind}");
        }
    }

    private void StopAbilityAudio(AbilityAudioKey key, float fadeSeconds)
    {
        if (_worldAudio is null ||
            !_activeAbilityAudio.Remove(key, out var session))
            return;
        _worldAudio.StopAbility(session, fadeSeconds);
    }

    private void StopAbilityAudioForCaster(int casterUnit)
    {
        foreach (var key in _activeAbilityAudio.Keys
                     .Where(value => value.CasterUnit == casterUnit)
                     .ToArray())
            StopAbilityAudio(key, 0.05f);
    }

    private static int BuildingAudioEmitter(int building) =>
        building < 0 ? -1 : 1_000_000 + building;

    private bool TryGameplayEventPlayer(
        in GameplayEvent value,
        out int playerId)
    {
        if (value.Player >= 0)
        {
            playerId = value.Player;
            return true;
        }
        if (TryUnitPlayerId(value.Unit, out playerId)) return true;
        if (_simulation is not null && value.Building >= 0)
        {
            var building = new GameplayBuildingId(value.Building);
            if (_simulation.Construction.IsAlive(building))
            {
                playerId = _simulation.Construction.Observe(building).PlayerId;
                return true;
            }
        }
        playerId = -1;
        return false;
    }

    private bool TryBuildingPlayerId(int buildingId, out int playerId)
    {
        if (_simulation is not null && buildingId >= 0)
        {
            var building = new GameplayBuildingId(buildingId);
            if (_simulation.Construction.IsAlive(building))
            {
                playerId = _simulation.Construction.Observe(building).PlayerId;
                return true;
            }
        }
        playerId = -1;
        return false;
    }

    private void PlayInterfaceAudio() => _worldAudio?.PlayInterfaceClick();

    private void OnAnimationAudioEvent(War3AnimationAudioEvent value)
    {
        if (_worldAudio is null || IsDeathTimeline(value.SequenceName)) return;
        _audioAnimationEvents++;
        if (!_worldAudio.PlayAnimationEvent(
            value.EventCode,
            value.SourcePlayerId,
            value.WorldPosition,
            value.EmitterId,
            value.Sequence))
            return;
        _audioAnimationPlayed++;
        if (_audioAnimationPlayed == 1)
            GD.Print(
                $"WAR3_AUDIO animation_timeline=active " +
                $"event={value.EventCode} sequence={value.SequenceName} " +
                $"received={_audioAnimationEvents}");
    }

    private void OnTreeHarvestFeedback(War3TreeHarvestFeedbackEvent value)
    {
        if (_worldAudio is null) return;
        _audioTreeHarvestEvents++;
        if (!_worldAudio.PlayImpactMaterial(
                value.WorkerObjectId,
                "Wood",
                value.SourcePlayerId,
                value.WorldPosition,
                value.WorkerUnit,
                value.Sequence,
                value.WeaponSlot))
            return;
        _audioTreeHarvestPlayed++;
        if (_audioTreeHarvestPlayed == 1)
            GD.Print(
                $"WAR3_AUDIO tree_harvest=active worker={value.WorkerObjectId} " +
                $"weapon_slot={value.WeaponSlot} family={value.SoundFamily}");
    }

    private static bool IsDeathTimeline(string sequenceName) =>
        sequenceName.StartsWith("Death", StringComparison.OrdinalIgnoreCase) ||
        sequenceName.StartsWith("Dissipate", StringComparison.OrdinalIgnoreCase) ||
        sequenceName.StartsWith("Decay", StringComparison.OrdinalIgnoreCase);

    private void PlaySelectionAudio()
    {
        if (_simulation is null || _worldAudio is null) return;
        var unit = _selectedUnits
            .Where(value => (uint)value < (uint)_simulation.Units.Count &&
                            _simulation.Units.Alive[value])
            .Order()
            .FirstOrDefault(-1);
        if (unit < 0 || !TryUnitObjectId(unit, out var objectId)) return;
        _worldAudio.PlayUnitSelection(
            objectId, _simulation.Combat.Teams[unit],
            _simulation.Units.Positions[unit], unit);
    }

    private void PlayCommandAudio(bool attack, bool queued = false)
    {
        if (_simulation is null || _worldAudio is null) return;
        var unit = _selectedUnits
            .Where(value => (uint)value < (uint)_simulation.Units.Count &&
                            _simulation.Units.Alive[value])
            .Order()
            .FirstOrDefault(-1);
        if (unit < 0 || !TryUnitObjectId(unit, out var objectId)) return;
        _worldAudio.PlayUnitCommand(
            objectId, _simulation.Combat.Teams[unit], attack,
            _simulation.Units.Positions[unit], unit);
        if (queued)
            _worldAudio.PlayWaypoint(_simulation.Combat.Teams[unit]);
    }

    private bool TryTargetObjectId(
        CombatTargetKind kind,
        int id,
        out string objectId)
    {
        if (kind == CombatTargetKind.Unit)
            return TryUnitObjectId(id, out objectId);
        if (kind == CombatTargetKind.Building && _simulation is not null)
        {
            var building = _simulation.CreateGameplayBuildingOverview()
                .FirstOrDefault(value => value.Id.Value == id);
            if (building.Type.Name is not null &&
                (uint)building.Type.Id < (uint)War3HumanContent.Buildings.Count)
            {
                objectId = War3HumanContent.Buildings[building.Type.Id].ObjectId;
                return true;
            }
        }
        objectId = string.Empty;
        return false;
    }

    private bool TryCombatObjectImpactMaterial(
        CombatTargetKind kind,
        int id,
        out string material)
    {
        material = string.Empty;
        if (kind != CombatTargetKind.Object || _simulation is null ||
            (uint)id >= (uint)_simulation.CombatObjects.Count)
            return false;
        material = _simulation.ObserveCombatObject(new CombatObjectId(id))
            .Profile.Kind switch
        {
            CombatObjectKind.Tree => "Wood",
            CombatObjectKind.Wall => "Stone",
            _ => string.Empty
        };
        return material.Length > 0;
    }

    private bool TryUnitObjectId(int unit, out string objectId)
    {
        objectId = string.Empty;
        if (_simulation is null || _production is null ||
            (uint)unit >= (uint)_simulation.Units.Count)
            return false;
        objectId = War3HumanContent.ResolveUnit(
            _simulation, _production, unit).ObjectId;
        return !string.IsNullOrWhiteSpace(objectId);
    }

    private bool TryUnitPlayerId(int unit, out int playerId)
    {
        playerId = -1;
        if (_simulation is null ||
            (uint)unit >= (uint)_simulation.Units.Count)
            return false;
        playerId = _simulation.Combat.Teams[unit];
        return playerId >= 0;
    }

    private void ReportLostAudioEvents(int count)
    {
        _audioLostEvents += count;
        GD.PushWarning(
            $"WAR3_AUDIO event_overflow lost={count} total={_audioLostEvents}");
    }

    private sealed class RecordingAudioPlayback : IWar3AudioPlayback
    {
        public int Played { get; private set; }
        public int LoopsStarted { get; private set; }
        public int LoopsStopped { get; private set; }

        public bool Play(
            in War3ResolvedAudioCue cue,
            in War3AudioCueRequest request)
        {
            Played++;
            return true;
        }

        public bool StartLoop(
            in War3ResolvedAudioCue cue,
            in War3AudioCueRequest request,
            out War3AudioLoopHandle handle)
        {
            handle = new War3AudioLoopHandle(request.EmitterId, request.CueId);
            LoopsStarted++;
            return handle.IsValid;
        }

        public void StopLoop(
            War3AudioLoopHandle handle,
            float fadeSeconds = 0f) => LoopsStopped++;

        public void StopEmitter(int emitterId, float fadeSeconds = 0f) { }
        public void StopAll() { }
        public War3AudioRuntimeSnapshot Snapshot() => default;
    }

    private readonly record struct AbilityAudioKey(
        int CasterUnit,
        string AbilityId);
}
