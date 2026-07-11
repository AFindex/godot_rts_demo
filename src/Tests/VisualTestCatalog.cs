using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

/// <summary>
/// Black-box business scenarios. This file intentionally does not reference
/// RtsSimulation, UnitStore, path objects, steering, portals or choke internals.
/// </summary>
public static class VisualTestCatalog
{
    public static readonly string[] CaseIds =
    [
        "single-unit",
        "attack-move-engage-resume",
        "attack-move-leash-resume",
        "attack-move-command-isolation",
        "attack-move-cancel",
        "combat-melee-slots",
        "combat-ranged-ring",
        "combat-stop-hold-acquire",
        "combat-multi-retarget",
        "queued-waypoints",
        "queued-command-replace",
        "queued-capacity-limit",
        "control-group-recall",
        "smart-command-sequence",
        "operation-selection-camera",
        "minimap-interaction",
        "command-log-replay",
        "command-replay-divergence",
        "replay-package-world",
        "replay-checkpoint-resume",
        "replay-checkpoint-choke",
        "replay-hot-snapshot",
        "economy-replay-persistence",
        "open-field",
        "dense-formation",
        "opposing-streams",
        "crossing-streams",
        "command-replace",
        "rapid-reissue",
        "destination-convergence",
        "destination-outer-ring",
        "destination-overtake",
        "destination-corner-mixed",
        "clearance-portal-choice",
        "clearance-dynamic-gap",
        "building-footprint-sizes",
        "building-placement-rules",
        "building-connectivity-guard",
        "building-size-navigation",
        "gameplay-profile-resource-runtime",
        "clearance-bake-resource-runtime",
        "clearance-editor-preview",
        "clearance-incremental-chunks",
        "resource-hot-reload",
        "clearance-bake-live-commit",
        "resource-file-watch-workflow",
        "building-connectivity-diff-preview",
        "economy-dual-resource",
        "economy-expansion-saturation",
        "player-visibility-authority",
        "match-capability-elimination",
        "ai-modular-skirmish",
        "construction-gameplay-buildings",
        "building-type-resource-runtime",
        "production-queue-exit-rally",
        "production-replay-persistence",
        "production-catalog-resource-runtime",
        "production-rally-smart-targets",
        "production-building-prerequisites",
        "technology-research-upgrades",
        "construction-replay-persistence",
        "combat-attack-building",
        "shared-target-reservations",
        "stop-command",
        "hold-command",
        "mixed-radii",
        "boundary-target",
        "dynamic-local-invalidation",
        "dynamic-building-detour",
        "dynamic-building-remove",
        "dynamic-portal-reroute",
        "dynamic-group-reroute",
        "navigation-resource-runtime",
        "portal-choke",
        "reverse-choke",
        "bidirectional-choke-balanced",
        "bidirectional-choke-asymmetric",
        "bidirectional-choke-waves",
        "hold-blocked-choke",
        "temporary-blocker-recovery",
        "unreachable-recovery-limit",
        "large-group-192"
    ];

    public static VisualTestSession Create(
        string caseId,
        NavigationMapSnapshot? navigationMap = null,
        GameplayProfileCatalogSnapshot? gameplayProfiles = null,
        ClearanceBakeSnapshot? clearanceBake = null,
        RuntimeResourceSetSnapshot? hotReloadCandidate = null,
        BuildingTypeCatalogSnapshot? buildingTypes = null,
        ProductionCatalogSnapshot? productionCatalog = null,
        TechnologyCatalogSnapshot? technologyCatalog = null) => caseId switch
    {
        "single-unit" => CreateSingleUnit(),
        "attack-move-engage-resume" => CreateAttackMoveEngageResume(),
        "attack-move-leash-resume" => CreateAttackMoveLeashResume(),
        "attack-move-command-isolation" => CreateAttackMoveCommandIsolation(),
        "attack-move-cancel" => CreateAttackMoveCancel(),
        "combat-melee-slots" => CreateCombatMeleeSlots(),
        "combat-ranged-ring" => CreateCombatRangedRing(),
        "combat-stop-hold-acquire" => CreateCombatStopHoldAcquire(),
        "combat-multi-retarget" => CreateCombatMultiRetarget(),
        "queued-waypoints" => CreateQueuedWaypoints(),
        "queued-command-replace" => CreateQueuedCommandReplace(),
        "queued-capacity-limit" => CreateQueuedCapacityLimit(),
        "control-group-recall" => CreateControlGroupRecall(),
        "smart-command-sequence" => CreateSmartCommandSequence(),
        "operation-selection-camera" => CreateOperationSelectionCamera(),
        "minimap-interaction" => CreateMinimapInteraction(),
        "command-log-replay" => CreateCommandLogReplay(),
        "command-replay-divergence" => CreateCommandReplayDivergence(),
        "replay-package-world" => CreateReplayPackageWorld(
            navigationMap, gameplayProfiles, clearanceBake),
        "replay-checkpoint-resume" => CreateReplayCheckpointResume(
            navigationMap, gameplayProfiles, clearanceBake),
        "replay-checkpoint-choke" => CreateReplayCheckpointChoke(
            navigationMap, gameplayProfiles, clearanceBake),
        "replay-hot-snapshot" => CreateReplayHotSnapshot(
            navigationMap, gameplayProfiles, clearanceBake),
        "economy-replay-persistence" => CreateEconomyReplayPersistence(
            navigationMap, gameplayProfiles, clearanceBake),
        "open-field" => CreateOpenField(),
        "dense-formation" => CreateDenseFormation(),
        "opposing-streams" => CreateOpposingStreams(),
        "crossing-streams" => CreateCrossingStreams(),
        "command-replace" => CreateCommandReplace(),
        "rapid-reissue" => CreateRapidReissue(),
        "destination-convergence" => CreateDestinationConvergence(),
        "destination-outer-ring" => CreateDestinationOuterRing(),
        "destination-overtake" => CreateDestinationOvertake(),
        "destination-corner-mixed" => CreateDestinationCornerMixed(),
        "clearance-portal-choice" => CreateClearancePortalChoice(),
        "clearance-dynamic-gap" => CreateClearanceDynamicGap(),
        "building-footprint-sizes" => CreateBuildingFootprintSizes(),
        "building-placement-rules" => CreateBuildingPlacementRules(),
        "building-connectivity-guard" => CreateBuildingConnectivityGuard(),
        "building-size-navigation" => CreateBuildingSizeNavigation(),
        "gameplay-profile-resource-runtime" =>
            CreateGameplayProfileResourceRuntime(gameplayProfiles),
        "clearance-bake-resource-runtime" =>
            CreateClearanceBakeResourceRuntime(
                navigationMap, gameplayProfiles, clearanceBake),
        "clearance-editor-preview" =>
            CreateClearanceEditorPreview(
                navigationMap, gameplayProfiles, clearanceBake),
        "clearance-incremental-chunks" =>
            CreateClearanceIncrementalChunks(
                navigationMap, gameplayProfiles, clearanceBake),
        "resource-hot-reload" => CreateResourceHotReload(
            navigationMap,
            gameplayProfiles,
            clearanceBake,
            hotReloadCandidate),
        "clearance-bake-live-commit" => CreateClearanceBakeLiveCommit(
            navigationMap, gameplayProfiles, clearanceBake),
        "resource-file-watch-workflow" => CreateResourceFileWatchWorkflow(
            navigationMap, gameplayProfiles, clearanceBake),
        "building-connectivity-diff-preview" =>
            CreateBuildingConnectivityDiffPreview(),
        "economy-dual-resource" => CreateDualResourceEconomy(),
        "economy-expansion-saturation" => CreateExpansionSaturation(),
        "player-visibility-authority" => CreatePlayerVisibilityAuthority(
            navigationMap, gameplayProfiles, clearanceBake),
        "match-capability-elimination" => CreateMatchCapabilityElimination(
            navigationMap, gameplayProfiles, clearanceBake),
        "ai-modular-skirmish" => CreateModularAiSkirmish(),
        "construction-gameplay-buildings" => CreateConstructionGameplayBuildings(),
        "building-type-resource-runtime" =>
            CreateBuildingTypeResourceRuntime(buildingTypes),
        "production-queue-exit-rally" => CreateProductionQueueExitRally(),
        "production-replay-persistence" => CreateProductionReplayPersistence(
            navigationMap, gameplayProfiles, clearanceBake),
        "production-catalog-resource-runtime" =>
            CreateProductionCatalogResourceRuntime(productionCatalog),
        "production-rally-smart-targets" =>
            CreateProductionRallySmartTargets(
                navigationMap, gameplayProfiles, clearanceBake),
        "production-building-prerequisites" =>
            CreateProductionBuildingPrerequisites(
                navigationMap, gameplayProfiles, clearanceBake),
        "technology-research-upgrades" =>
            CreateTechnologyResearchUpgrades(
                navigationMap, gameplayProfiles, clearanceBake,
                technologyCatalog),
        "construction-replay-persistence" => CreateConstructionReplayPersistence(
            navigationMap, gameplayProfiles, clearanceBake),
        "combat-attack-building" => CreateCombatAttackBuilding(
            navigationMap, gameplayProfiles, clearanceBake),
        "shared-target-reservations" => CreateSharedTargetReservations(),
        "stop-command" => CreateStopCommand(),
        "hold-command" => CreateHoldCommand(),
        "mixed-radii" => CreateMixedRadii(),
        "boundary-target" => CreateBoundaryTarget(),
        "dynamic-local-invalidation" => CreateDynamicLocalInvalidation(),
        "dynamic-building-detour" => CreateDynamicBuildingDetour(),
        "dynamic-building-remove" => CreateDynamicBuildingRemove(),
        "dynamic-portal-reroute" => CreateDynamicPortalReroute(),
        "dynamic-group-reroute" => CreateDynamicGroupReroute(),
        "navigation-resource-runtime" => CreateNavigationResourceRuntime(navigationMap),
        "portal-choke" => CreatePortalChoke(reverse: false),
        "reverse-choke" => CreatePortalChoke(reverse: true),
        "bidirectional-choke-balanced" => CreateBidirectionalChokeBalanced(),
        "bidirectional-choke-asymmetric" => CreateBidirectionalChokeAsymmetric(),
        "bidirectional-choke-waves" => CreateBidirectionalChokeWaves(),
        "hold-blocked-choke" => CreateHoldBlockedChoke(),
        "temporary-blocker-recovery" => CreateTemporaryBlockerRecovery(),
        "unreachable-recovery-limit" => CreateUnreachableRecoveryLimit(),
        "large-group-192" => CreateLargeGroup(),
        _ => throw new ArgumentException(
            $"Unknown visual test '{caseId}'. Expected: {string.Join(", ", CaseIds)}.",
            nameof(caseId))
    };

    private static VisualTestSession CreateSingleUnit()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 8);
        var units = new[] { rig.Spawn(new Vector2(100f, 200f)) };
        rig.Move(units, new Vector2(900f, 500f));
        return ArrivalScenario(
            "single-unit", "Single unit direct move", 480, rig, units, 1, 8f);
    }

    private static VisualTestSession CreateAttackMoveEngageResume()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 8);
        var attacker = rig.SpawnCombat(new Vector2(100f, 350f), team: 1);
        var defender = rig.SpawnCombat(new Vector2(510f, 255f), team: 2);
        TestUnitId[] visible = [attacker, defender];
        rig.AttackMove([attacker], new Vector2(1060f, 350f));
        return new VisualTestSession(
            "attack-move-engage-resume",
            "AttackMove acquires, kills, then resumes route",
            840,
            rig,
            visible,
            runtime =>
            {
                var attack = runtime.Observe(attacker);
                var attackerCombat = runtime.ObserveCombat(attacker);
                var defenderCombat = runtime.ObserveCombat(defender);
                var passed = attackerCombat.Alive && !defenderCombat.Alive &&
                             attack.Position.X >= 970f;
                return new ScenarioResult(
                    passed,
                    $"defender_alive={defenderCombat.Alive}, " +
                    $"attacker_x={attack.Position.X:F1}, state={attackerCombat.State}");
            });
    }

    private static VisualTestSession CreateAttackMoveLeashResume()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 8);
        var attacker = rig.SpawnCombat(new Vector2(100f, 350f), team: 1);
        var durableTarget = new TestCombatProfile(
            MaximumHealth: 500f,
            AttackDamage: 0f,
            AttackRange: 20f,
            AcquisitionRange: 100f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 180f);
        var defender = rig.SpawnCombat(
            new Vector2(360f, 255f), team: 2, durableTarget,
            maximumSpeed: 220f, acceleration: 900f);
        TestUnitId[] visible = [attacker, defender];
        rig.AttackMove([attacker], new Vector2(1060f, 350f));
        return new VisualTestSession(
            "attack-move-leash-resume",
            "AttackMove abandons target beyond leash and resumes",
            780,
            rig,
            visible,
            runtime =>
            {
                var attack = runtime.Observe(attacker);
                var defenderCombat = runtime.ObserveCombat(defender);
                var passed = defenderCombat.Alive && attack.Position.X >= 970f;
                return new ScenarioResult(
                    passed,
                    $"target_alive={defenderCombat.Alive}, attacker_x={attack.Position.X:F1}");
            })
            .At(105, "Target retreats beyond leash", runtime =>
                runtime.Move([defender], new Vector2(1060f, 650f)));
    }

    private static VisualTestSession CreateAttackMoveCommandIsolation()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 12);
        var mover = rig.SpawnCombat(new Vector2(90f, 190f), team: 1);
        var attackMover = rig.SpawnCombat(new Vector2(90f, 500f), team: 1);
        var moveLaneEnemy = rig.SpawnCombat(new Vector2(500f, 120f), team: 2);
        var attackLaneEnemy = rig.SpawnCombat(new Vector2(500f, 430f), team: 2);
        TestUnitId[] visible = [mover, attackMover, moveLaneEnemy, attackLaneEnemy];
        rig.Move([mover], new Vector2(1060f, 190f));
        rig.AttackMove([attackMover], new Vector2(1060f, 500f));
        return new VisualTestSession(
            "attack-move-command-isolation",
            "Move ignores enemies while AttackMove engages",
            780,
            rig,
            visible,
            runtime =>
            {
                var moverSnapshot = runtime.Observe(mover);
                var attackSnapshot = runtime.Observe(attackMover);
                var ignoredEnemy = runtime.ObserveCombat(moveLaneEnemy);
                var engagedEnemy = runtime.ObserveCombat(attackLaneEnemy);
                var passed = ignoredEnemy.Alive && !engagedEnemy.Alive &&
                             moverSnapshot.Position.X >= 970f &&
                             attackSnapshot.Position.X >= 970f;
                return new ScenarioResult(
                    passed,
                    $"move_enemy_alive={ignoredEnemy.Alive}, " +
                    $"attack_enemy_alive={engagedEnemy.Alive}, " +
                    $"move_x={moverSnapshot.Position.X:F1}, " +
                    $"attack_x={attackSnapshot.Position.X:F1}");
            });
    }

    private static VisualTestSession CreateAttackMoveCancel()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(900f, 600f), 12);
        var durable = new TestCombatProfile(
            MaximumHealth: 500f,
            AttackDamage: 0f,
            AttackRange: 20f,
            AcquisitionRange: 100f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 180f);
        var stopped = rig.SpawnCombat(new Vector2(90f, 180f), team: 1);
        var held = rig.SpawnCombat(new Vector2(90f, 420f), team: 1);
        var upperEnemy = rig.SpawnCombat(
            new Vector2(330f, 120f), team: 2, durable);
        var lowerEnemy = rig.SpawnCombat(
            new Vector2(330f, 360f), team: 2, durable);
        TestUnitId[] visible = [stopped, held, upperEnemy, lowerEnemy];
        rig.AttackMove([stopped], new Vector2(810f, 180f));
        rig.AttackMove([held], new Vector2(810f, 420f));
        return new VisualTestSession(
            "attack-move-cancel",
            "Stop and Hold cancel AttackMove engagement and resume intent",
            300,
            rig,
            visible,
            runtime =>
            {
                var stoppedUnit = runtime.Observe(stopped);
                var heldUnit = runtime.Observe(held);
                var stoppedCombat = runtime.ObserveCombat(stopped);
                var heldCombat = runtime.ObserveCombat(held);
                var stoppedCancelledRoute = stoppedUnit.Position.X < 700f;
                var heldCancelledRoute = heldUnit.Position.X < 400f;
                var passed = stoppedCancelledRoute && heldCancelledRoute &&
                             heldUnit.State == TestUnitState.Holding &&
                             heldUnit.Velocity.LengthSquared() < 0.25f &&
                             heldCombat.Target is null &&
                             runtime.ObserveCombat(upperEnemy).Alive &&
                             runtime.ObserveCombat(lowerEnemy).Alive;
                return new ScenarioResult(
                    passed,
                    $"stop={stoppedUnit.State}/{stoppedCombat.State}, " +
                    $"hold={heldUnit.State}/{heldCombat.State}, " +
                    $"x={stoppedUnit.Position.X:F1}/{heldUnit.Position.X:F1}, " +
                    $"targets={stoppedCombat.Target}/{heldCombat.Target}");
            })
            .At(90, "Stop cancels upper engagement", runtime =>
                runtime.Stop([stopped]))
            .At(90, "Hold cancels lower engagement", runtime =>
                runtime.Hold([held]));
    }

    private static VisualTestSession CreateCombatMeleeSlots()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 16);
        var melee = new TestCombatProfile(
            MaximumHealth: 80f,
            AttackDamage: 1f,
            AttackRange: 8f,
            AcquisitionRange: 240f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0.15f,
            LeashDistance: 420f,
            Positioning: TestCombatPositioning.Melee);
        var durable = new TestCombatProfile(
            MaximumHealth: 5000f,
            AttackDamage: 0f,
            AttackRange: 2f,
            AcquisitionRange: 80f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 100f,
            Positioning: TestCombatPositioning.Melee);
        var attackers = new TestUnitId[8];
        for (var index = 0; index < attackers.Length; index++)
        {
            attackers[index] = rig.SpawnCombat(
                new Vector2(180f + index % 4 * 20f, 270f + index / 4 * 80f),
                team: 1, melee);
        }
        var target = rig.SpawnCombat(
            new Vector2(600f, 350f), team: 2, durable, radius: 24f);
        var visible = attackers.Append(target).ToArray();
        rig.AttackMove(attackers, new Vector2(1040f, 350f));
        return new VisualTestSession(
            "combat-melee-slots",
            "Melee attackers reserve unique contact slots",
            900,
            rig,
            visible,
            runtime => EvaluateAttackPositions(runtime, attackers, target, 7, 29f, 40f));
    }

    private static VisualTestSession CreateCombatRangedRing()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 20);
        var ranged = new TestCombatProfile(
            MaximumHealth: 60f,
            AttackDamage: 1f,
            AttackRange: 120f,
            AcquisitionRange: 280f,
            AttackCooldownSeconds: 0.8f,
            AttackWindupSeconds: 0.12f,
            LeashDistance: 480f,
            Positioning: TestCombatPositioning.Ranged);
        var durable = new TestCombatProfile(
            MaximumHealth: 5000f,
            AttackDamage: 0f,
            AttackRange: 2f,
            AcquisitionRange: 80f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 100f,
            Positioning: TestCombatPositioning.Melee);
        var attackers = new TestUnitId[10];
        for (var index = 0; index < attackers.Length; index++)
        {
            attackers[index] = rig.SpawnCombat(
                new Vector2(150f + index % 5 * 20f, 220f + index / 5 * 180f),
                team: 1, ranged);
        }
        var target = rig.SpawnCombat(new Vector2(620f, 350f), team: 2, durable);
        var visible = attackers.Append(target).ToArray();
        rig.AttackMove(attackers, new Vector2(1050f, 350f));
        return new VisualTestSession(
            "combat-ranged-ring",
            "Ranged attackers reserve a stable firing ring",
            660,
            rig,
            visible,
            runtime => EvaluateAttackPositions(runtime, attackers, target, 8, 80f, 118f));
    }

    private static VisualTestSession CreateCombatStopHoldAcquire()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(900f, 600f), 12);
        var durable = new TestCombatProfile(
            MaximumHealth: 500f,
            AttackDamage: 0f,
            AttackRange: 20f,
            AcquisitionRange: 80f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 120f);
        var stopped = rig.SpawnCombat(new Vector2(100f, 180f), team: 1);
        var held = rig.SpawnCombat(new Vector2(100f, 420f), team: 1);
        var stopEnemy = rig.SpawnCombat(new Vector2(230f, 180f), team: 2, durable);
        var nearHoldEnemy = rig.SpawnCombat(new Vector2(140f, 420f), team: 2, durable);
        var farHoldEnemy = rig.SpawnCombat(new Vector2(310f, 500f), team: 2, durable);
        TestUnitId[] visible = [stopped, held, stopEnemy, nearHoldEnemy, farHoldEnemy];
        rig.Stop([stopped]);
        rig.Hold([held]);
        return new VisualTestSession(
            "combat-stop-hold-acquire",
            "Stop pursues local targets; Hold attacks in range without chasing",
            360,
            rig,
            visible,
            runtime =>
            {
                var stopUnit = runtime.Observe(stopped);
                var holdUnit = runtime.Observe(held);
                var stopCombat = runtime.ObserveCombat(stopped);
                var holdCombat = runtime.ObserveCombat(held);
                var nearHealth = runtime.ObserveCombat(nearHoldEnemy).Health;
                var farHealth = runtime.ObserveCombat(farHoldEnemy).Health;
                var passed = stopUnit.Position.X > 140f &&
                             stopCombat.State is TestCombatState.Chasing or TestCombatState.Attacking &&
                             Vector2.Distance(holdUnit.Position, new Vector2(100f, 420f)) < 2f &&
                             holdCombat.State == TestCombatState.Attacking &&
                             nearHealth < 500f && farHealth >= 499.9f;
                return new ScenarioResult(
                    passed,
                    $"stop_x={stopUnit.Position.X:F1}/{stopCombat.State}, " +
                    $"hold_delta={Vector2.Distance(holdUnit.Position, new Vector2(100f, 420f)):F1}, " +
                    $"near_hp={nearHealth:F0}, far_hp={farHealth:F0}");
            });
    }

    private static VisualTestSession CreateCombatMultiRetarget()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 20);
        var attackerProfile = new TestCombatProfile(
            MaximumHealth: 80f,
            AttackDamage: 12f,
            AttackRange: 36f,
            AcquisitionRange: 210f,
            AttackCooldownSeconds: 0.48f,
            AttackWindupSeconds: 0.1f,
            LeashDistance: 360f);
        var defenderProfile = new TestCombatProfile(
            MaximumHealth: 60f,
            AttackDamage: 0f,
            AttackRange: 20f,
            AcquisitionRange: 80f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 100f);
        var attackers = new TestUnitId[8];
        for (var index = 0; index < attackers.Length; index++)
        {
            attackers[index] = rig.SpawnCombat(
                new Vector2(100f + index % 4 * 20f, 280f + index / 4 * 70f),
                team: 1, attackerProfile);
        }
        var defenders = new TestUnitId[4];
        for (var index = 0; index < defenders.Length; index++)
        {
            defenders[index] = rig.SpawnCombat(
                new Vector2(390f + index * 145f, 310f + (index & 1) * 80f),
                team: 2, defenderProfile);
        }
        var visible = attackers.Concat(defenders).ToArray();
        rig.AttackMove(attackers, new Vector2(1080f, 350f));
        return new VisualTestSession(
            "combat-multi-retarget",
            "Multiple attackers retarget through an enemy line then resume",
            900,
            rig,
            visible,
            runtime =>
            {
                var dead = defenders.Count(unit => !runtime.ObserveCombat(unit).Alive);
                var resumed = attackers.Count(unit => runtime.Observe(unit).Position.X >= 930f);
                return new ScenarioResult(
                    dead == defenders.Length && resumed >= 6,
                    $"defeated={dead}/{defenders.Length}, resumed={resumed}/{attackers.Length}");
            });
    }

    private static ScenarioResult EvaluateAttackPositions(
        MovementTestRig runtime,
        IReadOnlyList<TestUnitId> attackers,
        TestUnitId target,
        int minimumAttacking,
        float minimumRadius,
        float maximumRadius)
    {
        var targetPosition = runtime.Observe(target).Position;
        var attacking = 0;
        var positioned = new List<Vector2>();
        var maximumSlotError = 0f;
        foreach (var attacker in attackers)
        {
            var combat = runtime.ObserveCombat(attacker);
            if (combat.State == TestCombatState.Attacking)
            {
                attacking++;
            }
            if (combat.HasAttackPosition)
            {
                positioned.Add(combat.AttackPosition);
                maximumSlotError = MathF.Max(
                    maximumSlotError,
                    Vector2.Distance(runtime.Observe(attacker).Position, combat.AttackPosition));
            }
        }

        var unique = true;
        var radiusInBand = true;
        for (var first = 0; first < positioned.Count; first++)
        {
            var radius = Vector2.Distance(positioned[first], targetPosition);
            radiusInBand &= radius >= minimumRadius && radius <= maximumRadius;
            for (var second = first + 1; second < positioned.Count; second++)
            {
                unique &= Vector2.Distance(positioned[first], positioned[second]) >= 14f;
            }
        }

        var passed = attacking >= minimumAttacking &&
                     positioned.Count >= minimumAttacking && unique && radiusInBand;
        return new ScenarioResult(
            passed,
            $"attacking={attacking}/{attackers.Count}, slots={positioned.Count}, " +
            $"unique={unique}, radiusBand={radiusInBand}, maxSlotError={maximumSlotError:F1}");
    }

    private static VisualTestSession CreateQueuedWaypoints()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 8);
        var unit = rig.Spawn(new Vector2(100f, 100f));
        rig.Move([unit], new Vector2(420f, 100f));
        rig.Move([unit], new Vector2(420f, 430f), queued: true);
        rig.Move([unit], new Vector2(930f, 430f), queued: true);
        return new VisualTestSession(
            "queued-waypoints",
            "Per-unit Shift queue executes three waypoints in order",
            780,
            rig,
            [unit],
            runtime =>
            {
                var snapshot = runtime.Observe(unit);
                var orders = runtime.ObserveOrders(unit);
                var passed = Vector2.Distance(
                                 snapshot.Position, snapshot.AssignedTarget) < 8f &&
                             orders.PendingOrders == 0 &&
                             orders.CompletedQueuedOrders == 2 &&
                             orders.QueueOverflows == 0;
                return new ScenarioResult(
                    passed,
                    $"position={snapshot.Position.X:F1},{snapshot.Position.Y:F1}, " +
                    $"active={orders.ActiveOrder}, pending={orders.PendingOrders}, " +
                    $"completed={orders.CompletedQueuedOrders}");
            });
    }

    private static VisualTestSession CreateQueuedCommandReplace()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 8);
        var unit = rig.Spawn(new Vector2(100f, 100f));
        rig.Move([unit], new Vector2(1050f, 100f));
        rig.Move([unit], new Vector2(1050f, 600f), queued: true);
        rig.Move([unit], new Vector2(200f, 600f), queued: true);
        return new VisualTestSession(
            "queued-command-replace",
            "Non-queued command replaces the complete Shift queue",
            600,
            rig,
            [unit],
            runtime =>
            {
                var snapshot = runtime.Observe(unit);
                var orders = runtime.ObserveOrders(unit);
                var passed = Vector2.Distance(
                                 snapshot.Position, new Vector2(230f, 520f)) < 8f &&
                             orders.PendingOrders == 0 &&
                             orders.CompletedQueuedOrders == 0;
                return new ScenarioResult(
                    passed,
                    $"position={snapshot.Position.X:F1},{snapshot.Position.Y:F1}, " +
                    $"pending={orders.PendingOrders}, completed={orders.CompletedQueuedOrders}");
            })
            .At(90, "Immediate command clears queued waypoints", runtime =>
                runtime.Move([unit], new Vector2(230f, 520f)));
    }

    private static VisualTestSession CreateQueuedCapacityLimit()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 4);
        var unit = rig.Spawn(
            new Vector2(80f, 350f), maximumSpeed: 60f, acceleration: 360f);
        rig.Move([unit], new Vector2(1120f, 350f));
        for (var index = 0; index < rig.MaximumQueuedOrders + 2; index++)
        {
            var target = new Vector2(
                900f + index % 4 * 40f,
                160f + index / 4 * 90f);
            rig.Move([unit], target, queued: true);
        }
        return new VisualTestSession(
            "queued-capacity-limit",
            "Per-unit command queue has a bounded overflow policy",
            120,
            rig,
            [unit],
            runtime =>
            {
                var orders = runtime.ObserveOrders(unit);
                return new ScenarioResult(
                    orders.PendingOrders == runtime.MaximumQueuedOrders &&
                    orders.QueueOverflows == 2,
                    $"pending={orders.PendingOrders}/" +
                    $"{runtime.MaximumQueuedOrders}, " +
                    $"overflows={orders.QueueOverflows}");
            });
    }

    private static VisualTestSession CreateControlGroupRecall()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 12);
        var first = rig.SpawnGrid(new Vector2(100f, 180f), 2, 2, 22f);
        var added = rig.SpawnGrid(new Vector2(100f, 300f), 1, 2, 22f);
        rig.AssignControlGroup(1, first);
        rig.AddToControlGroup(1, added);
        var recalled = rig.RecallControlGroup(1);
        rig.Move(recalled, new Vector2(920f, 350f));
        var visible = first.Concat(added).ToArray();
        return new VisualTestSession(
            "control-group-recall",
            "Ctrl assign, Shift add, and recall a control group",
            720,
            rig,
            visible,
            runtime =>
            {
                var current = runtime.RecallControlGroup(1);
                var arrived = current.Count(unit =>
                    Vector2.Distance(
                        runtime.Observe(unit).Position,
                        runtime.Observe(unit).AssignedTarget) < 8f);
                return new ScenarioResult(
                    current.Length == 6 && arrived == 6,
                    $"members={current.Length}, arrived={arrived}/6");
            });
    }

    private static VisualTestSession CreateSmartCommandSequence()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 8);
        var attackerProfile = new TestCombatProfile(
            MaximumHealth: 80f,
            AttackDamage: 30f,
            AttackRange: 40f,
            AcquisitionRange: 180f,
            AttackCooldownSeconds: 0.4f,
            AttackWindupSeconds: 0.08f,
            LeashDistance: 300f);
        var weakTarget = new TestCombatProfile(
            MaximumHealth: 30f,
            AttackDamage: 0f,
            AttackRange: 20f,
            AcquisitionRange: 80f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 100f);
        var attacker = rig.SpawnCombat(
            new Vector2(100f, 350f), team: 1, attackerProfile);
        var friendly = rig.SpawnCombat(
            new Vector2(330f, 220f), team: 1, attackerProfile);
        var enemy = rig.SpawnCombat(
            new Vector2(570f, 350f), team: 2, weakTarget);
        rig.SmartCommandUnit([attacker], friendly);
        rig.SmartCommandUnit([attacker], enemy, queued: true);
        rig.SmartCommandGround(
            [attacker], new Vector2(1020f, 500f), queued: true);
        return new VisualTestSession(
            "smart-command-sequence",
            "SmartCommand resolves friendly position, enemy attack, then ground move",
            900,
            rig,
            [attacker, friendly, enemy],
            runtime =>
            {
                var attackerSnapshot = runtime.Observe(attacker);
                var enemySnapshot = runtime.ObserveCombat(enemy);
                var orders = runtime.ObserveOrders(attacker);
                var passed = !enemySnapshot.Alive &&
                             Vector2.Distance(
                                 attackerSnapshot.Position,
                                 new Vector2(1020f, 500f)) < 10f &&
                             orders.PendingOrders == 0 &&
                             orders.CompletedQueuedOrders == 2;
                return new ScenarioResult(
                    passed,
                    $"enemy_alive={enemySnapshot.Alive}, " +
                    $"position={attackerSnapshot.Position.X:F1},{attackerSnapshot.Position.Y:F1}, " +
                    $"completed={orders.CompletedQueuedOrders}");
            });
    }

    private static VisualTestSession CreateOperationSelectionCamera()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 16);
        var units = new[]
        {
            rig.Spawn(new Vector2(120f, 120f), radius: 8f),
            rig.Spawn(new Vector2(260f, 180f), radius: 8f),
            rig.Spawn(new Vector2(360f, 260f), radius: 10f),
            rig.Spawn(new Vector2(900f, 500f), radius: 8f)
        };
        rig.Move(units, new Vector2(950f, 550f));
        return new VisualTestSession(
            "operation-selection-camera",
            "Same-type selection, edge pan, cursor zoom and group focus",
            360,
            rig,
            units,
            runtime =>
            {
                var result = runtime.VerifyOperationInteractions();
                return new ScenarioResult(
                    result.Passed,
                    $"point={result.PointSelection}, same={result.SameTypeCount}, " +
                    $"box={result.BoxSelectionCount}, zoom={result.ZoomAnchorStable}, " +
                    $"edge={result.EdgePanMoved}, double={result.GroupDoubleTap}, " +
                    $"focus={result.FocusPosition.X:F0},{result.FocusPosition.Y:F0}");
            });
    }

    private static VisualTestSession CreateMinimapInteraction()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 16);
        var units = rig.SpawnGrid(new Vector2(100f, 160f), 2, 4, 24f);
        rig.PlaceBuilding(new Vector2(360f, 100f), new Vector2(32f, 32f));
        rig.PlaceBuilding(new Vector2(620f, 180f), new Vector2(64f, 48f));
        rig.PlaceBuilding(new Vector2(720f, 380f), new Vector2(112f, 80f));
        rig.PlaceBuilding(new Vector2(300f, 510f), new Vector2(160f, 120f));
        rig.Move(units, new Vector2(980f, 520f));
        return new VisualTestSession(
            "minimap-interaction",
            "Decoupled minimap transform, viewport and command intents",
            480,
            rig,
            units,
            runtime =>
            {
                var result = runtime.VerifyMinimapInteractions();
                return new ScenarioResult(
                    result.Passed,
                    $"roundtrip={result.RoundTrip}, viewport={result.ViewportMapped}, " +
                    $"focus={result.FocusResolved}, command={result.CommandResolved}, " +
                    $"outside={result.OutsideRejected}, " +
                    $"world={result.CommandWorld.X:F0},{result.CommandWorld.Y:F0}");
            });
    }

    private static VisualTestSession CreateCommandLogReplay()
    {
        var fixture = CreateReplayFixture();
        fixture.Rig.StartCommandRecording();
        ConfigureReplayCommands(fixture);
        return BuildReplaySession(
            "command-log-replay",
            "Versioned command log round-trip and exact fixed-tick replay",
            fixture,
            runtime =>
            {
                var log = runtime.CaptureCommandLog();
                var roundTrip = log.TryCanonicalRoundTrip(out var decoded);
                var replayFixture = CreateReplayFixture();
                var trace = replayFixture.Rig.Replay(decoded!, runtime.Tick);
                var exact = runtime.StateHash == trace.FinalHash;
                var rejected = log.RejectsUnsupportedVersion() &&
                               log.RejectsTruncatedPayload();
                return new ScenarioResult(
                    roundTrip && exact && rejected && log.EntryCount == 7 &&
                    decoded!.StableHash == log.StableHash,
                    $"entries={log.EntryCount}, bytes={log.CanonicalByteCount}, " +
                    $"log={log.StableHash:X16}, state={runtime.StateHash:X16}, " +
                    $"exact={exact}, rejected={rejected}");
            });
    }

    private static VisualTestSession CreateCommandReplayDivergence()
    {
        var fixture = CreateReplayFixture();
        fixture.Rig.StartCommandRecording();
        ConfigureReplayCommands(fixture);
        return BuildReplaySession(
            "command-replay-divergence",
            "Periodic state hashes locate the first divergent replay sample",
            fixture,
            runtime =>
            {
                var log = runtime.CaptureCommandLog();
                var changed = log.WithTargetOffset(
                    log.EntryCount - 1, new Vector2(0f, 90f));
                var baselineFixture = CreateReplayFixture();
                var changedFixture = CreateReplayFixture();
                var baseline = baselineFixture.Rig.Replay(log, runtime.Tick);
                var divergent = changedFixture.Rig.Replay(changed, runtime.Tick);
                var firstDivergence = baseline.FindFirstDivergence(divergent);
                var passed = firstDivergence >= 330 && firstDivergence <= 390 &&
                             baseline.FinalHash != divergent.FinalHash &&
                             baseline.SampleCount == divergent.SampleCount;
                return new ScenarioResult(
                    passed,
                    $"first_divergence={firstDivergence}, " +
                    $"baseline={baseline.FinalHash:X16}, " +
                    $"changed={divergent.FinalHash:X16}, " +
                    $"samples={baseline.SampleCount}");
            });
    }

    private static VisualTestSession CreateReplayPackageWorld(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, clearanceBake);
        var units = rig.SpawnGrid(
            new Vector2(100f, 300f), 2, 4, 24f,
            radius: 7.5f, maximumSpeed: 132f, acceleration: 720f);
        rig.PlaceBuilding(new Vector2(300f, 150f), new Vector2(48f, 48f));
        rig.StartReplayPackageRecording();
        rig.Move(units, new Vector2(1160f, 350f));

        var dynamicBuilding = default(TestBuildingId);
        var session = new VisualTestSession(
            "replay-package-world",
            "Replay package rebuilds initial units, resources and dynamic world commands",
            720,
            rig,
            units,
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var roundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var replay = runtime.ReplayPackage(decoded!, runtime.Tick);
                var exact = replay.FinalHash == runtime.StateHash;
                var rejected = package.RejectsUnsupportedVersion() &&
                               package.RejectsTruncatedPayload() &&
                               runtime.RejectsReplayPackageResourceMismatch(package);
                var passed = roundTrip && exact && rejected &&
                             package.InitialUnitCount == units.Length &&
                             package.InitialBuildingCount == 1 &&
                             package.WorldCommandCount == 2 &&
                             package.UnitCommandCount == 2 &&
                             runtime.BuildingCount == 1;
                return new ScenarioResult(
                    passed,
                    $"units={package.InitialUnitCount}, world={package.WorldCommandCount}, " +
                    $"orders={package.UnitCommandCount}, bytes={package.CanonicalByteCount}, " +
                    $"package={package.StableHash:X16}, state={runtime.StateHash:X16}, " +
                    $"exact={exact}, rejected={rejected}");
            });
        session.At(90, "Place dynamic reroute building", runtime =>
            dynamicBuilding = runtime.PlaceBuilding(
                new Vector2(800f, 353f), new Vector2(72f, 96f)));
        session.At(270, "Remove dynamic building", runtime =>
            runtime.RemoveBuilding(dynamicBuilding));
        return session.At(360, "Issue post-world command", runtime =>
            runtime.Move(units.Take(4).ToArray(), new Vector2(1110f, 530f)));
    }

    private static VisualTestSession CreateReplayCheckpointResume(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, clearanceBake);
        var units = rig.SpawnGrid(
            new Vector2(100f, 300f), 2, 4, 24f,
            radius: 7.5f, maximumSpeed: 132f, acceleration: 720f);
        rig.PlaceBuilding(new Vector2(300f, 150f), new Vector2(48f, 48f));
        rig.StartReplayPackageRecording();
        rig.Move(units, new Vector2(1160f, 350f));

        const long checkpointTick = 240;
        var checkpointStateHash = 0UL;
        var dynamicBuilding = default(TestBuildingId);
        var session = new VisualTestSession(
            "replay-checkpoint-resume",
            "Versioned checkpoint seeks to a middle tick and resumes exact replay",
            720,
            rig,
            units,
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var checkpoint = runtime.CreateReplayCheckpoint(
                    package, checkpointTick, checkpointStateHash);
                var roundTrip = checkpoint.TryCanonicalRoundTrip(out var decoded);
                var baseline = runtime.ReplayPackage(package, runtime.Tick);
                var resumed = runtime.ResumeReplayPackage(
                    package, decoded!, runtime.Tick);
                var exact = baseline.MatchesFrom(resumed, checkpointTick) &&
                            resumed.FinalHash == runtime.StateHash;
                var rejected = checkpoint.RejectsUnsupportedVersion() &&
                               checkpoint.RejectsTruncatedPayload() &&
                               runtime.RejectsReplayCheckpointStateMismatch(
                                   package, checkpoint);
                return new ScenarioResult(
                    roundTrip && exact && rejected &&
                    checkpoint.CanonicalByteCount == 36 &&
                    resumed.SampleCount == 17,
                    $"tick={checkpoint.Tick}, bytes={checkpoint.CanonicalByteCount}, " +
                    $"checkpoint={checkpoint.StableHash:X16}, samples={resumed.SampleCount}, " +
                    $"state={resumed.FinalHash:X16}, exact={exact}, rejected={rejected}");
            });
        session.At(90, "Place pre-checkpoint building", runtime =>
            dynamicBuilding = runtime.PlaceBuilding(
                new Vector2(800f, 353f), new Vector2(72f, 96f)));
        session.At(checkpointTick, "Capture checkpoint state", runtime =>
            checkpointStateHash = runtime.StateHash);
        session.At(270, "Remove post-checkpoint building", runtime =>
            runtime.RemoveBuilding(dynamicBuilding));
        return session.At(360, "Issue post-checkpoint command", runtime =>
            runtime.Move(units.Take(4).ToArray(), new Vector2(1110f, 530f)));
    }

    private static VisualTestSession CreateReplayCheckpointChoke(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            48, navigationMap, gameplayProfiles, clearanceBake);
        var west = rig.SpawnGrid(new Vector2(100f, 250f), 4, 4, 20f);
        var east = rig.SpawnGrid(new Vector2(1120f, 390f), 4, 4, 20f);
        var all = west.Concat(east).ToArray();
        rig.StartReplayPackageRecording();
        rig.Move(west, new Vector2(1160f, 300f));
        rig.Move(east, new Vector2(90f, 410f));

        const long checkpointTick = 240;
        var checkpointStateHash = 0UL;
        var session = new VisualTestSession(
            "replay-checkpoint-choke",
            "Checkpoint covers private bidirectional choke traffic state",
            780,
            rig,
            all,
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var checkpoint = runtime.CreateReplayCheckpoint(
                    package, checkpointTick, checkpointStateHash);
                var baseline = runtime.ReplayPackage(package, runtime.Tick);
                var resumed = runtime.ResumeReplayPackage(
                    package, checkpoint, runtime.Tick);
                var traffic = runtime.ObserveChokeTraffic();
                var exact = baseline.MatchesFrom(resumed, checkpointTick) &&
                            resumed.FinalHash == runtime.StateHash;
                return new ScenarioResult(
                    exact && traffic.ConflictTicks == 0 &&
                    resumed.SampleCount == 19,
                    $"tick={checkpoint.Tick}, samples={resumed.SampleCount}, " +
                    $"direction={traffic.ActiveDirection}, conflicts={traffic.ConflictTicks}, " +
                    $"state={resumed.FinalHash:X16}, exact={exact}");
            });
        return session.At(checkpointTick, "Capture active choke checkpoint", runtime =>
            checkpointStateHash = runtime.StateHash);
    }

    private static VisualTestSession CreateReplayHotSnapshot(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, clearanceBake);
        var attackerProfile = new TestCombatProfile(
            MaximumHealth: 100f,
            AttackDamage: 1f,
            AttackRange: 40f,
            AcquisitionRange: 175f,
            AttackCooldownSeconds: 0.6f,
            AttackWindupSeconds: 0.1f,
            LeashDistance: 320f);
        var durableTarget = new TestCombatProfile(
            MaximumHealth: 5000f,
            AttackDamage: 0f,
            AttackRange: 20f,
            AcquisitionRange: 60f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 80f);
        var units = new TestUnitId[8];
        for (var index = 0; index < units.Length; index++)
        {
            units[index] = rig.SpawnCombat(
                new Vector2(100f + index % 4 * 24f, 300f + index / 4 * 24f),
                team: 1,
                attackerProfile,
                maximumSpeed: 132f);
        }
        var enemy = rig.SpawnCombat(
            new Vector2(700f, 350f), team: 2, durableTarget);
        rig.PlaceBuilding(new Vector2(300f, 150f), new Vector2(48f, 48f));
        rig.StartReplayPackageRecording();
        rig.AttackMove(units, new Vector2(1160f, 350f));
        rig.Move(units, new Vector2(1080f, 540f), queued: true);

        const long snapshotTick = 240;
        TestRuntimeStateCapture? runtimeCapture = null;
        var dynamicBuilding = default(TestBuildingId);
        var session = new VisualTestSession(
            "replay-hot-snapshot",
            "Deep runtime snapshot restores directly without replaying earlier ticks",
            720,
            rig,
            units.Append(enemy).ToArray(),
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var hot = runtime.BindHotSnapshot(package, runtimeCapture!);
                var roundTrip = hot.TryCanonicalRoundTrip(out var decoded);
                var baseline = runtime.ReplayPackage(package, runtime.Tick);
                var restored = runtime.ResumeHotSnapshot(
                    package, decoded!, runtime.Tick);
                var exact = baseline.MatchesFrom(restored, snapshotTick) &&
                            restored.FinalHash == runtime.StateHash;
                var rejected = hot.RejectsUnsupportedVersion() &&
                               hot.RejectsTruncatedPayload() &&
                               runtime.RejectsHotSnapshotPackageMismatch(
                                   package, hot) &&
                               runtime.RejectsHotSnapshotStateMismatch(
                                   package, hot);
                return new ScenarioResult(
                    roundTrip && decoded!.StableHash == hot.StableHash &&
                    exact && rejected && hot.FormatVersion == 11 &&
                    hot.Tick == snapshotTick && restored.SampleCount == 17,
                    $"tick={hot.Tick}, bytes={hot.CanonicalByteCount}, " +
                    $"snapshot={hot.StableHash:X16}, " +
                    $"samples={restored.SampleCount}, state={restored.FinalHash:X16}, " +
                    $"exact={exact}, rejected={rejected}");
            });
        session.At(90, "Place pre-snapshot building", runtime =>
            dynamicBuilding = runtime.PlaceBuilding(
                new Vector2(800f, 353f), new Vector2(72f, 96f)));
        session.At(snapshotTick, "Capture direct runtime snapshot", runtime =>
            runtimeCapture = runtime.CaptureRuntimeState());
        session.At(270, "Remove post-snapshot building", runtime =>
            runtime.RemoveBuilding(dynamicBuilding));
        return session.At(360, "Issue post-snapshot command", runtime =>
            runtime.Move(units.Take(4).ToArray(), new Vector2(1110f, 530f)));
    }

    private static VisualTestSession CreateEconomyReplayPersistence(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            24, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 300, 0, 15, 6);
        rig.AddResourceDropOff(1, new Vector2(170f, 350f));
        var mineralA = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(430f, 250f), 240, 5, 0.4f, 2);
        var mineralB = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(475f, 390f), 240, 5, 0.4f, 2);
        var gas = rig.AddResourceNode(
            TestEconomyResourceKind.VespeneGas,
            new Vector2(470f, 520f), 240, 4, 0.5f, 3,
            requiresRefinery: true,
            operational: false);
        var workers = new TestUnitId[6];
        for (var index = 0; index < workers.Length; index++)
        {
            workers[index] = rig.SpawnWorker(
                new Vector2(115f + index * 17f, 315f + index % 2 * 28f), 1);
        }

        rig.StartReplayPackageRecording();
        rig.Gather(1, workers[0], mineralA);
        rig.Gather(1, workers[1], mineralA);
        rig.Gather(1, workers[2], mineralB);
        rig.Gather(1, workers[3], mineralB);

        const long hotTick = 240;
        const long checkpointTick = 300;
        TestRuntimeStateCapture? runtimeCapture = null;
        var checkpointStateHash = 0UL;
        var session = new VisualTestSession(
            "economy-replay-persistence",
            "Economy command log, replay, checkpoint and active-loop hot restore",
            900,
            rig,
            workers,
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var economyLog = runtime.CaptureEconomyCommandLog();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var logRoundTrip = economyLog.TryCanonicalRoundTrip(out var decodedLog);
                var baseline = runtime.ReplayPackage(decoded!, runtime.Tick);
                var checkpoint = runtime.CreateReplayCheckpoint(
                    package, checkpointTick, checkpointStateHash);
                var resumed = runtime.ResumeReplayPackage(
                    package, checkpoint, runtime.Tick);
                var hot = runtime.BindHotSnapshot(package, runtimeCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var hotResumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var exact = baseline.FinalHash == runtime.StateHash &&
                            baseline.MatchesFrom(resumed, checkpointTick) &&
                            baseline.MatchesFrom(hotResumed, hotTick) &&
                            resumed.FinalHash == runtime.StateHash &&
                            hotResumed.FinalHash == runtime.StateHash;
                var rejected = economyLog.RejectsUnsupportedVersion() &&
                               economyLog.RejectsTruncatedPayload() &&
                               package.RejectsUnsupportedVersion() &&
                               package.RejectsTruncatedPayload() &&
                               hot.RejectsUnsupportedVersion() &&
                               hot.RejectsTruncatedPayload();
                var economy = runtime.ObservePlayerEconomy(1);
                var resourcesFlowed = economy.Minerals > 300 &&
                                      economy.VespeneGas > 0;
                var passed = packageRoundTrip && logRoundTrip && hotRoundTrip &&
                             decodedLog!.StableHash == economyLog.StableHash &&
                             package.FormatVersion == 11 && hot.FormatVersion == 11 &&
                             package.EconomyCommandCount == 7 &&
                             package.UnitCommandCount == 0 &&
                             exact && rejected && resourcesFlowed;
                return new ScenarioResult(
                    passed,
                    $"economyOrders={package.EconomyCommandCount}, " +
                    $"derivedMoves={package.UnitCommandCount}, " +
                    $"resources={economy.Minerals}/{economy.VespeneGas}, " +
                    $"hot={hot.CanonicalByteCount}B@{hot.Tick}, " +
                    $"exact={exact}, rejected={rejected}");
            });
        session.At(90, "Complete refinery and assign gas workers", runtime =>
        {
            runtime.SetRefineryOperational(gas, true);
            runtime.Gather(1, workers[4], gas);
            runtime.Gather(1, workers[5], gas);
        });
        session.At(hotTick, "Capture workers inside active gather loop", runtime =>
            runtimeCapture = runtime.CaptureRuntimeState());
        session.At(checkpointTick, "Capture deterministic replay checkpoint", runtime =>
            checkpointStateHash = runtime.StateHash);
        return session
            .Highlight(
                new SimRect(new Vector2(395f, 210f), new Vector2(515f, 425f)),
                "MINERAL LOOP: replayed from player Gather intent",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(425f, 475f), new Vector2(515f, 565f)),
                "REFINERY: operational transition is recorded",
                TestDiagnosticKind.Accepted);
    }

    private static VisualTestSession BuildReplaySession(
        string id,
        string name,
        ReplayFixture fixture,
        Func<MovementTestRig, ScenarioResult> evaluate)
    {
        var session = new VisualTestSession(
            id,
            name,
            840,
            fixture.Rig,
            fixture.Allies.Concat(fixture.Enemies).ToArray(),
            evaluate);
        session.At(90, "Queue locked target", runtime =>
            runtime.AttackTarget(
                fixture.Allies.Take(2).ToArray(),
                fixture.Enemies[0],
                queued: true));
        session.At(150, "Hold one unit and clear its queue", runtime =>
            runtime.Hold([fixture.Allies[5]]));
        session.At(210, "Queue from completed Hold", runtime =>
            runtime.SmartCommandGround(
                [fixture.Allies[5]],
                new Vector2(780f, 590f),
                queued: true));
        session.At(300, "Replace two units with target attack", runtime =>
            runtime.AttackTarget(
                fixture.Allies.Take(2).ToArray(),
                fixture.Enemies[1]));
        return session.At(330, "Queue final ground order", runtime =>
            runtime.Move(
                fixture.Allies.Take(2).ToArray(),
                new Vector2(980f, 560f),
                queued: true));
    }

    private static void ConfigureReplayCommands(ReplayFixture fixture)
    {
        fixture.Rig.Move(fixture.Allies, new Vector2(350f, 180f));
        fixture.Rig.AttackMove(
            fixture.Allies, new Vector2(1050f, 350f), queued: true);
    }

    private static ReplayFixture CreateReplayFixture()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 16);
        var attackerProfile = new TestCombatProfile(
            MaximumHealth: 100f,
            AttackDamage: 1f,
            AttackRange: 42f,
            AcquisitionRange: 190f,
            AttackCooldownSeconds: 0.55f,
            AttackWindupSeconds: 0.1f,
            LeashDistance: 330f);
        var durableTarget = new TestCombatProfile(
            MaximumHealth: 5000f,
            AttackDamage: 0f,
            AttackRange: 20f,
            AcquisitionRange: 80f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 100f);
        var allies = new TestUnitId[6];
        for (var index = 0; index < allies.Length; index++)
        {
            allies[index] = rig.SpawnCombat(
                new Vector2(90f + index % 3 * 22f, 260f + index / 3 * 75f),
                team: 1,
                attackerProfile);
        }
        var enemies = new TestUnitId[3];
        for (var index = 0; index < enemies.Length; index++)
        {
            enemies[index] = rig.SpawnCombat(
                new Vector2(520f + index * 190f, 310f + (index & 1) * 80f),
                team: 2,
                durableTarget);
        }
        return new ReplayFixture(rig, allies, enemies);
    }

    private readonly record struct ReplayFixture(
        MovementTestRig Rig,
        TestUnitId[] Allies,
        TestUnitId[] Enemies);

    private static VisualTestSession CreateOpenField()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 96);
        var units = rig.SpawnGrid(new Vector2(70f, 170f), 6, 8, 19f);
        rig.Move(units, new Vector2(1010f, 360f));
        return ArrivalScenario(
            "open-field", "Open field arrival (48 units)", 960, rig, units, 46, 10f);
    }

    private static VisualTestSession CreateDenseFormation()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 96);
        var units = rig.SpawnGrid(new Vector2(70f, 135f), 8, 10, 18f);
        rig.Move(units, new Vector2(1000f, 390f));
        return ArrivalScenario(
            "dense-formation", "Dense formation arrival (80 units)", 1080,
            rig, units, 72, 16f, 3f);
    }

    private static VisualTestSession CreateOpposingStreams()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 80);
        var left = rig.SpawnGrid(new Vector2(80f, 250f), 4, 6, 19f);
        var right = rig.SpawnGrid(new Vector2(1025f, 250f), 4, 6, 19f);
        rig.Move(left, new Vector2(1020f, 350f));
        rig.Move(right, new Vector2(180f, 350f));
        var all = left.Concat(right).ToArray();
        return ArrivalScenario(
            "opposing-streams", "Opposing streams (24 vs 24)", 1200,
            rig, all, 44, 12f, 3f);
    }

    private static VisualTestSession CreateCrossingStreams()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 64);
        var horizontal = rig.SpawnGrid(new Vector2(90f, 300f), 4, 4, 20f);
        var vertical = rig.SpawnGrid(new Vector2(575f, 90f), 4, 4, 20f);
        rig.Move(horizontal, new Vector2(1080f, 360f));
        rig.Move(vertical, new Vector2(640f, 620f));
        var all = horizontal.Concat(vertical).ToArray();
        return ArrivalScenario(
            "crossing-streams", "Perpendicular crossing streams", 900,
            rig, all, 28, 14f, 3f);
    }

    private static VisualTestSession CreateCommandReplace()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 48);
        var units = rig.SpawnGrid(new Vector2(90f, 120f), 4, 5, 20f);
        rig.Move(units, new Vector2(1080f, 180f));
        var session = ArrivalScenario(
            "command-replace", "Replace an active move command", 780,
            rig, units, 18, 14f, 3f);
        return session.At(
            150,
            "Redirect to final target",
            runtime => runtime.Move(units, new Vector2(690f, 610f)));
    }

    private static VisualTestSession CreateRapidReissue()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 48);
        var units = rig.SpawnGrid(new Vector2(100f, 250f), 4, 6, 20f);
        rig.Move(units, new Vector2(1050f, 180f));
        var session = ArrivalScenario(
            "rapid-reissue", "Rapid command replacement", 960,
            rig, units, 21, 16f, 3f);
        session.At(60, "Command 2", runtime =>
            runtime.Move(units, new Vector2(500f, 600f)));
        session.At(120, "Command 3", runtime =>
            runtime.Move(units, new Vector2(250f, 120f)));
        return session.At(180, "Final command", runtime =>
            runtime.Move(units, new Vector2(1040f, 560f)));
    }

    private static VisualTestSession CreateDestinationConvergence()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 112);
        var all = rig.SpawnGrid(new Vector2(65f, 125f), 8, 10, 18f);
        var blockers = all.Where((_, index) => index % 5 == 0).ToArray();
        var movers = all.Where((_, index) => index % 5 != 0).ToArray();
        rig.Move(all, new Vector2(980f, 360f));

        var session = new VisualTestSession(
            "destination-convergence",
            "Destination convergence after temporary formation blockers",
            1800,
            rig,
            all,
            runtime =>
            {
                var arrival = EvaluateArrival(runtime, all, 75, 18f, 3f, 0, 0);
                var moverArrivals = CountArrivals(runtime, movers, 18f);
                var blockerArrivals = CountArrivals(runtime, blockers, 18f);
                var diagnostics = runtime.ObserveMovementDiagnostics();
                var converged = moverArrivals >= 59 && blockerArrivals >= 15;
                return new ScenarioResult(
                    arrival.Passed && converged,
                    $"movers={moverArrivals}/{movers.Length}, " +
                    $"released={blockerArrivals}/{blockers.Length}, " +
                    $"slotSwaps={diagnostics.DestinationSlotSwaps}, " +
                    $"localRematch={diagnostics.DestinationLocalRematches}/" +
                    $"{diagnostics.DestinationLocalRematchedUnits}, " +
                    $"yield={diagnostics.DestinationYieldEvents}, " +
                    $"activeYield={diagnostics.ActiveDestinationYields}, " +
                    $"overflow={diagnostics.DestinationOverflowAssignments}, " +
                    $"converged={converged}, {arrival.Summary}");
            });
        session.At(300, "Freeze units inside the moving formation", runtime =>
            runtime.Hold(blockers));
        return session.At(510, "Release blockers toward a separate target", runtime =>
            runtime.Move(blockers, new Vector2(930f, 590f)));
    }

    private static VisualTestSession CreateDestinationOuterRing()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 112);
        var all = rig.SpawnGrid(new Vector2(65f, 125f), 8, 10, 18f);
        var target = new Vector2(980f, 360f);
        rig.Move(all, target);

        var byDepth = rig.Observe(all)
            .OrderByDescending(unit => Vector2.DistanceSquared(unit.AssignedTarget, target))
            .ThenBy(unit => unit.Id.Value)
            .ToArray();
        var outer = byDepth.Take(48).Select(unit => unit.Id).ToArray();
        var inner = byDepth.Skip(48).Select(unit => unit.Id).ToArray();
        rig.Stop(inner);

        var session = new VisualTestSession(
            "destination-outer-ring",
            "Inner reservations approach after the outer ring settles",
            2700,
            rig,
            all,
            runtime =>
            {
                var arrival = EvaluateArrival(runtime, all, 78, 18f, 3f, 0, 0);
                var outerArrivals = CountArrivals(runtime, outer, 18f);
                var innerArrivals = CountArrivals(runtime, inner, 18f);
                var diagnostics = runtime.ObserveMovementDiagnostics();
                var bothLayersConverged = outerArrivals >= 47 && innerArrivals >= 31;
                return new ScenarioResult(
                    arrival.Passed && bothLayersConverged,
                    $"outer={outerArrivals}/{outer.Length}, " +
                    $"inner={innerArrivals}/{inner.Length}, " +
                    $"localRematch={diagnostics.DestinationLocalRematches}/" +
                    $"{diagnostics.DestinationLocalRematchedUnits}, " +
                    $"yield={diagnostics.DestinationYieldEvents}, " +
                    $"activeYield={diagnostics.ActiveDestinationYields}, " +
                    $"overflow={diagnostics.DestinationOverflowAssignments}, " +
                    $"maxStall={diagnostics.MaximumDestinationStallTicks}, " +
                    $"maxNear={diagnostics.MaximumDestinationNearTicks}, " +
                    $"layersConverged={bothLayersConverged}, {arrival.Summary}");
            });
        return session.At(
            720,
            "Release units with inner reservations after outer ring settles",
            runtime => runtime.Move(inner, target));
    }

    private static VisualTestSession CreateDestinationOvertake()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 112);
        var units = new TestUnitId[80];
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 10; column++)
            {
                var index = row * 10 + column;
                var maximumSpeed = column < 5 ? 152f : 104f;
                units[index] = rig.Spawn(
                    new Vector2(65f + column * 18f, 125f + row * 18f),
                    7.5f,
                    maximumSpeed,
                    720f);
            }
        }

        rig.Move(units, new Vector2(990f, 370f));
        return new VisualTestSession(
            "destination-overtake",
            "Fast rear units overtake before destination convergence",
            2100,
            rig,
            units,
            runtime =>
            {
                var arrival = EvaluateArrival(runtime, units, 77, 20f, 3f, 0, 0);
                var diagnostics = runtime.ObserveMovementDiagnostics();
                return new ScenarioResult(
                    arrival.Passed,
                    $"slotSwaps={diagnostics.DestinationSlotSwaps}, " +
                    $"localRematch={diagnostics.DestinationLocalRematches}/" +
                    $"{diagnostics.DestinationLocalRematchedUnits}, " +
                    $"yield={diagnostics.DestinationYieldEvents}, " +
                    $"overflow={diagnostics.DestinationOverflowAssignments}, " +
                    arrival.Summary);
            });
    }

    private static VisualTestSession CreateDestinationCornerMixed()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 112);
        var units = new TestUnitId[80];
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 10; column++)
            {
                var index = row * 10 + column;
                var radius = ((row + column) % 3) switch
                {
                    0 => 5.5f,
                    1 => 7.5f,
                    _ => 10f
                };
                units[index] = rig.Spawn(
                    new Vector2(70f + column * 24f, 390f + row * 24f),
                    radius);
            }
        }

        rig.Move(units, new Vector2(1165f, 55f));
        return new VisualTestSession(
            "destination-corner-mixed",
            "Mixed radii converge at a clamped corner destination",
            2400,
            rig,
            units,
            runtime =>
            {
                var arrival = EvaluateArrival(runtime, units, 77, 22f, 3.5f, 0, 0);
                var diagnostics = runtime.ObserveMovementDiagnostics();
                return new ScenarioResult(
                    arrival.Passed,
                    $"localRematch={diagnostics.DestinationLocalRematches}/" +
                    $"{diagnostics.DestinationLocalRematchedUnits}, " +
                    $"yield={diagnostics.DestinationYieldEvents}, " +
                    $"overflow={diagnostics.DestinationOverflowAssignments}, " +
                    arrival.Summary);
            });
    }

    private static VisualTestSession CreateClearancePortalChoice()
    {
        var rig = MovementTestRig.CreateClearanceChoiceMap(16);
        var small = rig.Spawn(new Vector2(100f, 330f), radius: 5.5f);
        var large = rig.Spawn(new Vector2(100f, 390f), radius: 10f);
        var units = new[] { small, large };
        var smallUsedNarrow = false;
        var largeUsedWide = false;
        rig.Move([small], new Vector2(1100f, 330f));
        rig.Move([large], new Vector2(1100f, 390f));

        var session = new VisualTestSession(
            "clearance-portal-choice",
            "Small unit uses narrow portal while large unit takes wide route",
            1200,
            rig,
            units,
            runtime =>
            {
                var arrival = EvaluateArrival(runtime, units, 2, 12f, 1f, 0, 0);
                return new ScenarioResult(
                    arrival.Passed && smallUsedNarrow && largeUsedWide,
                    $"smallNarrow={smallUsedNarrow}, largeWide={largeUsedWide}, " +
                    arrival.Summary);
            });
        foreach (var tick in new long[] { 150, 210, 270 })
        {
            session.At(tick, "Observe clearance-specific portal routes", runtime =>
            {
                smallUsedNarrow |= runtime.Observe(small).Position.Y < 285f;
                largeUsedWide |= runtime.Observe(large).Position.Y > 485f;
            });
        }

        return session;
    }

    private static VisualTestSession CreateClearanceDynamicGap()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 16);
        var small = rig.Spawn(new Vector2(100f, 350f), radius: 5.5f);
        var large = rig.Spawn(new Vector2(100f, 450f), radius: 10f);
        var units = new[] { small, large };
        rig.PlaceBuilding(new Vector2(600f, 169f), new Vector2(200f, 338f));
        rig.PlaceBuilding(new Vector2(600f, 531f), new Vector2(200f, 338f));
        rig.Move([small], new Vector2(1100f, 350f));

        var session = new VisualTestSession(
            "clearance-dynamic-gap",
            "Dynamic 24px gap accepts small unit and rejects large unit",
            1800,
            rig,
            units,
            runtime =>
            {
                var smallSnapshot = runtime.Observe(small);
                var largeSnapshot = runtime.Observe(large);
                var recovery = runtime.ObserveRecovery([large]);
                var smallArrived =
                    Vector2.Distance(smallSnapshot.Position, smallSnapshot.AssignedTarget) <= 12f;
                var largeRejected =
                    recovery.MaximumStage == TestRecoveryStage.Unreachable &&
                    largeSnapshot.Position.X < 480f;
                return new ScenarioResult(
                    smallArrived && largeRejected && runtime.IsInsideWorld(small) &&
                    runtime.IsInsideWorld(large),
                    $"smallArrived={smallArrived}, largeRejected={largeRejected}, " +
                    $"largeStage={recovery.MaximumStage}, " +
                    $"largeX={largeSnapshot.Position.X:0.0}");
            });
        return session.At(
            600,
            "Large unit attempts the same dynamic gap",
            runtime => runtime.Move([large], new Vector2(1100f, 350f)));
    }

    private static VisualTestSession CreateBuildingFootprintSizes()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1400f, 900f), 8);
        var results = new[]
        {
            rig.TryPlaceBuilding(
                new Vector2(150f, 160f),
                TestBuildingFootprintClass.Small,
                TestMovementClass.Large),
            rig.TryPlaceBuilding(
                new Vector2(340f, 170f),
                TestBuildingFootprintClass.Medium,
                TestMovementClass.Large),
            rig.TryPlaceBuilding(
                new Vector2(580f, 190f),
                TestBuildingFootprintClass.Large,
                TestMovementClass.Large),
            rig.TryPlaceBuilding(
                new Vector2(900f, 220f),
                TestBuildingFootprintClass.Huge,
                TestMovementClass.Large)
        };
        return new VisualTestSession(
            "building-footprint-sizes",
            "Small, medium, large and huge business footprints",
            240,
            rig,
            [],
            runtime =>
            {
                var allAccepted = results.All(result => result.Succeeded);
                var sizes = string.Join(
                    "/", results.Select(result =>
                        $"{result.Size.X:0}x{result.Size.Y:0}"));
                return new ScenarioResult(
                    allAccepted && runtime.BuildingCount == 4,
                    $"accepted={results.Count(result => result.Succeeded)}/4, " +
                    $"sizes={sizes}, buildings={runtime.BuildingCount}");
            });
    }

    private static VisualTestSession CreateBuildingPlacementRules()
    {
        var rig = MovementTestRig.CreateClearanceChoiceMap(8);
        var unit = rig.Spawn(new Vector2(1000f, 600f));
        var anchor = rig.TryPlaceBuilding(
            new Vector2(300f, 350f),
            TestBuildingFootprintClass.Medium,
            TestMovementClass.Large);
        var dynamicOverlap = rig.TryPlaceBuilding(
            new Vector2(300f, 350f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var staticOverlap = rig.TryPlaceBuilding(
            new Vector2(600f, 350f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var outside = rig.TryPlaceBuilding(
            new Vector2(5f, 100f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var unitOverlap = rig.TryPlaceBuilding(
            new Vector2(1000f, 600f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var narrowGap = rig.TryPlaceBuilding(
            new Vector2(36f, 100f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var flushWall = rig.TryPlaceBuilding(
            new Vector2(16f, 100f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        return new VisualTestSession(
            "building-placement-rules",
            "Business placement rejects overlap, occupancy and fake gaps",
            360,
            rig,
            [unit],
            runtime =>
            {
                var codesCorrect = anchor.Succeeded && flushWall.Succeeded &&
                    dynamicOverlap.Code ==
                        TestBuildingPlacementCode.DynamicFootprintOverlap &&
                    staticOverlap.Code ==
                        TestBuildingPlacementCode.StaticObstacleOverlap &&
                    outside.Code == TestBuildingPlacementCode.OutsideWorld &&
                    unitOverlap.Code == TestBuildingPlacementCode.UnitOverlap &&
                    narrowGap.Code ==
                        TestBuildingPlacementCode.InsufficientClearance;
                return new ScenarioResult(
                    codesCorrect && runtime.BuildingCount == 2,
                    $"anchor={anchor.Code}, dynamic={dynamicOverlap.Code}, " +
                    $"static={staticOverlap.Code}, outside={outside.Code}, " +
                    $"unit={unitOverlap.Code}, gap={narrowGap.Code}, " +
                    $"flush={flushWall.Code}, buildings={runtime.BuildingCount}");
            });
    }

    private static VisualTestSession CreateBuildingSizeNavigation()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1400f, 900f), 64);
        var placements = new[]
        {
            rig.TryPlaceBuilding(
                new Vector2(350f, 270f),
                TestBuildingFootprintClass.Small,
                TestMovementClass.Medium),
            rig.TryPlaceBuilding(
                new Vector2(560f, 520f),
                TestBuildingFootprintClass.Medium,
                TestMovementClass.Medium),
            rig.TryPlaceBuilding(
                new Vector2(800f, 300f),
                TestBuildingFootprintClass.Large,
                TestMovementClass.Medium),
            rig.TryPlaceBuilding(
                new Vector2(1050f, 560f),
                TestBuildingFootprintClass.Huge,
                TestMovementClass.Medium)
        };
        var units = rig.SpawnGrid(new Vector2(70f, 350f), 4, 6, 20f);
        rig.Move(units, new Vector2(1300f, 450f));
        return new VisualTestSession(
            "building-size-navigation",
            "Formation navigates a mixed-size building field",
            1800,
            rig,
            units,
            runtime =>
            {
                var arrival = EvaluateArrival(runtime, units, 22, 18f, 3f, 4, 4);
                var accepted = placements.Count(result => result.Succeeded);
                return new ScenarioResult(
                    arrival.Passed && accepted == placements.Length,
                    $"accepted={accepted}/{placements.Length}, {arrival.Summary}");
            });
    }

    private static VisualTestSession CreateBuildingConnectivityGuard()
    {
        var rig = MovementTestRig.CreateConnectivityGuardMap(24);
        var rejected = rig.TryPlaceBuilding(
            new Vector2(400f, 250f),
            TestBuildingFootprintClass.Large,
            TestMovementClass.Large);
        var accepted = rig.TryPlaceBuilding(
            new Vector2(650f, 100f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var units = rig.SpawnGrid(
            new Vector2(100f, 225f), 2, 4, 18f, 7.5f);
        rig.Move(units, new Vector2(680f, 250f));
        return new VisualTestSession(
            "building-connectivity-guard",
            "Placement preserves the only global navigation corridor",
            1200,
            rig,
            units,
            runtime =>
            {
                var arrival = EvaluateArrival(
                    runtime, units, units.Length, 14f, 1f, 1, 1);
                var policyCorrect =
                    rejected.Code ==
                        TestBuildingPlacementCode.DisconnectsNavigation &&
                    accepted.Succeeded;
                return new ScenarioResult(
                    policyCorrect && arrival.Passed,
                    $"blocking={rejected.Code}, safe={accepted.Code}, " +
                    arrival.Summary);
            })
            .Highlight(
                new SimRect(
                    new Vector2(344f, 210f),
                    new Vector2(456f, 290f)),
                "REJECT: disconnects Large navigation",
                TestDiagnosticKind.Rejected);
    }

    private static VisualTestSession CreateGameplayProfileResourceRuntime(
        GameplayProfileCatalogSnapshot? profiles)
    {
        profiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 16);
        var units = new TestUnitId[profiles.UnitProfiles.Length];
        for (var index = 0; index < units.Length; index++)
        {
            units[index] = rig.Spawn(
                new Vector2(90f, 130f + index * 95f),
                profiles.Unit(index));
            rig.Move(
                [units[index]],
                new Vector2(1080f, 130f + index * 95f));
        }

        var buildingResults = new TestBuildingPlacementResult[
            profiles.BuildingProfiles.Length];
        for (var index = 0; index < buildingResults.Length; index++)
        {
            buildingResults[index] = rig.TryPlaceBuilding(
                new Vector2(240f + index * 250f, 570f),
                profiles.Building(index));
        }

        return new VisualTestSession(
            "gameplay-profile-resource-runtime",
            "Godot gameplay profile Resource drives pure C# runtime data",
            1200,
            rig,
            units,
            runtime =>
            {
                var arrival = EvaluateArrival(
                    runtime, units, units.Length, 12f, 1f, 4, 4);
                var snapshots = runtime.Observe(units);
                var radiiMatch = true;
                for (var index = 0; index < snapshots.Length; index++)
                {
                    radiiMatch &= MathF.Abs(
                        snapshots[index].Radius -
                        profiles.Unit(index).PhysicalRadius) <= 0.001f;
                }

                var buildingsAccepted = buildingResults.All(
                    result => result.Succeeded);
                return new ScenarioResult(
                    arrival.Passed && radiiMatch && buildingsAccepted &&
                    profiles.StableHash != 0UL,
                    $"format={profiles.FormatVersion}, hash={profiles.StableHashText}, " +
                    $"radii={radiiMatch}, buildings={buildingsAccepted}, " +
                    arrival.Summary);
            });
    }

    private static VisualTestSession CreateClearanceEditorPreview(
        NavigationMapSnapshot? navigation,
        GameplayProfileCatalogSnapshot? profiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigation ??= DemoMapDefinition.CreateSnapshot();
        profiles ??= DemoGameplayProfiles.CreateSnapshot();
        clearanceBake ??= ClearanceBakeSnapshot.Build(navigation);
        var preview = ClearancePreviewSnapshot.Create(
            navigation, profiles, clearanceBake);
        var rig = MovementTestRig.CreateChokeMap(8, navigation);
        return new VisualTestSession(
            "clearance-editor-preview",
            "Editor clearance overlay for classes, portals and buildings",
            600,
            rig,
            [],
            _ =>
            {
                var allDemoEdgesSupportLarge = preview.Portals.All(
                    portal => portal.LargeTraversable);
                var valid = preview.Classes.Length == 3 &&
                            preview.Connectivity.Length == 3 &&
                            preview.Portals.Length == navigation.PortalEdges.Length &&
                            preview.Buildings.Length == profiles.BuildingProfiles.Length &&
                            allDemoEdgesSupportLarge;
                return new ScenarioResult(
                    valid,
                    $"classes={preview.Classes.Length}, " +
                    $"components={string.Join('/', preview.Classes.Select(value => value.ConnectedComponents))}, " +
                    $"portals={preview.Portals.Length}, " +
                    $"buildings={preview.Buildings.Length}, " +
                    $"largeEdges={allDemoEdgesSupportLarge}");
            });
    }

    private static VisualTestSession CreateClearanceBakeResourceRuntime(
        NavigationMapSnapshot? navigation,
        GameplayProfileCatalogSnapshot? profiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigation ??= DemoMapDefinition.CreateSnapshot();
        profiles ??= DemoGameplayProfiles.CreateSnapshot();
        clearanceBake ??= ClearanceBakeSnapshot.Build(navigation);
        var preview = ClearancePreviewSnapshot.Create(
            navigation, profiles, clearanceBake);
        var rig = MovementTestRig.CreateChokeMap(8, navigation);
        return new VisualTestSession(
            "clearance-bake-resource-runtime",
            "Versioned clearance bake drives static connectivity preview",
            600,
            rig,
            [],
            _ =>
            {
                var componentsMatch = true;
                var usesBake = true;
                for (var classIndex = 0; classIndex < 3; classIndex++)
                {
                    componentsMatch &=
                        preview.Classes[classIndex].ConnectedComponents ==
                        clearanceBake.Layer((MovementClass)classIndex)
                            .ComponentCount;
                    usesBake &= preview.Classes[classIndex].ConnectivitySource ==
                                NavigationConnectivitySource.StaticBake;
                }

                var valid = clearanceBake.FormatVersion ==
                                ClearanceBakeSnapshot.CurrentFormatVersion &&
                            clearanceBake.SourceNavigationHash ==
                                navigation.StableHash &&
                            clearanceBake.Layers.Length == 3 &&
                            preview.BakeChunks.Length ==
                                clearanceBake.ChunkCount &&
                            componentsMatch && usesBake;
                return new ScenarioResult(
                    valid,
                    $"format={clearanceBake.FormatVersion}, " +
                    $"hash={clearanceBake.StableHashText}, " +
                    $"grid={clearanceBake.Columns}x{clearanceBake.Rows}, " +
                    $"chunks={clearanceBake.ChunkColumns}x{clearanceBake.ChunkRows}, " +
                    $"previewChunks={preview.BakeChunks.Length}, " +
                    $"layers={clearanceBake.Layers.Length}, " +
                    $"components={componentsMatch}, baked={usesBake}");
            });
    }

    private static VisualTestSession CreateClearanceIncrementalChunks(
        NavigationMapSnapshot? navigation,
        GameplayProfileCatalogSnapshot? profiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigation ??= DemoMapDefinition.CreateSnapshot();
        profiles ??= DemoGameplayProfiles.CreateSnapshot();
        clearanceBake ??= ClearanceBakeSnapshot.Build(navigation);
        var preview = ClearancePreviewSnapshot.Create(
            navigation,
            profiles,
            clearanceBake,
            ClearanceIncrementalSelfTest.ChangedArea);
        var rig = MovementTestRig.CreateChokeMap(8, navigation);
        return new VisualTestSession(
            "clearance-incremental-chunks",
            "Dirty-chunk clearance resampling matches full topology",
            600,
            rig,
            [],
            _ =>
            {
                var result = ClearanceIncrementalSelfTest.Run(
                    navigation, clearanceBake);
                return new ScenarioResult(
                    result.Passed && preview.DirtyBakeChunks.Length > 0,
                    $"dirtyPreview={preview.DirtyBakeChunks.Length}, " +
                    result.Summary);
            });
    }

    private static VisualTestSession CreateResourceHotReload(
        NavigationMapSnapshot? navigation,
        GameplayProfileCatalogSnapshot? profiles,
        ClearanceBakeSnapshot? clearanceBake,
        RuntimeResourceSetSnapshot? generatedCandidate)
    {
        navigation ??= DemoMapDefinition.CreateSnapshot();
        profiles ??= DemoGameplayProfiles.CreateSnapshot();
        clearanceBake ??= ClearanceBakeSnapshot.Build(navigation);
        var rig = MovementTestRig.CreateChokeMap(8, navigation);
        return new VisualTestSession(
            "resource-hot-reload",
            "Atomic Resource reload diff and rebuild policy",
            600,
            rig,
            [],
            _ =>
            {
                var result = ResourceReloadSelfTest.Run(
                    navigation,
                    profiles,
                    clearanceBake,
                    generatedCandidate);
                return new ScenarioResult(result.Passed, result.Summary);
            });
    }

    private static VisualTestSession CreateClearanceBakeLiveCommit(
        NavigationMapSnapshot? navigation,
        GameplayProfileCatalogSnapshot? profiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigation ??= DemoMapDefinition.CreateSnapshot();
        profiles ??= DemoGameplayProfiles.CreateSnapshot();
        clearanceBake ??= ClearanceBakeSnapshot.Build(navigation);
        var rig = MovementTestRig.CreateBakeReloadMap(
            32, navigation, profiles, clearanceBake);
        var units = rig.SpawnGrid(new Vector2(100f, 300f), 2, 4, 22f);
        rig.Move(units, new Vector2(1160f, 353f));
        var committed = new TestClearanceBakeCommitSnapshot(
            ClearanceBakeCommitCode.MissingBaseline, 0UL, 0UL, 0, 0);
        var mismatch = committed;
        return new VisualTestSession(
                "clearance-bake-live-commit",
                "Atomic Bake-only cache commit while units are moving",
                900,
                rig,
                units,
                runtime =>
                {
                    var arrival = EvaluateArrival(
                        runtime, units, units.Length, 14f, 1f, 0, 0);
                    var reloadValid = committed.Succeeded &&
                                      committed.PreviousHash !=
                                          committed.CandidateHash &&
                                      committed.ReplannedUnits == units.Length &&
                                      committed.ReloadCount == 1;
                    var rejectionValid =
                        mismatch.Code ==
                            ClearanceBakeCommitCode.NavigationMismatch &&
                        mismatch.PreviousHash == committed.CandidateHash &&
                        mismatch.ReloadCount == 1;
                    return new ScenarioResult(
                        arrival.Passed && reloadValid && rejectionValid,
                        $"commit={committed.Code}, replanned=" +
                        $"{committed.ReplannedUnits}, reloads={committed.ReloadCount}, " +
                        $"mismatch={mismatch.Code}, " + arrival.Summary);
                })
            .At(120, "Commit Bake-only candidate", runtime =>
            {
                committed = runtime.CommitClearanceBakeVariant();
            })
            .At(180, "Reject mismatched Bake", runtime =>
            {
                mismatch = runtime.CommitMismatchedClearanceBake();
            });
    }

    private static VisualTestSession CreateBuildingConnectivityDiffPreview()
    {
        var navigation =
            BuildingConnectivityDiffSelfTest.CreateNavigationFixture();
        var bake = ClearanceBakeSnapshot.Build(navigation);
        var diff = BuildingConnectivityDiffSnapshot.Create(
            navigation,
            BuildingConnectivityDiffSelfTest.BlockingFootprint,
            bake);
        var rig = MovementTestRig.CreateChokeMap(8, navigation);
        return new VisualTestSession(
                "building-connectivity-diff-preview",
                "Placement before-after connectivity diff for all classes",
                600,
                rig,
                [],
                _ =>
                {
                    var result = BuildingConnectivityDiffSelfTest.Run();
                    return new ScenarioResult(
                        result.Passed && !diff.PreservedForAll,
                        result.Summary);
                })
            .Highlight(
                BuildingConnectivityDiffSelfTest.BlockingFootprint,
                "REJECT: splits Small / Medium / Large",
                TestDiagnosticKind.Rejected);
    }

    private static VisualTestSession CreateResourceFileWatchWorkflow(
        NavigationMapSnapshot? navigation,
        GameplayProfileCatalogSnapshot? profiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigation ??= DemoMapDefinition.CreateSnapshot();
        profiles ??= DemoGameplayProfiles.CreateSnapshot();
        clearanceBake ??= ClearanceBakeSnapshot.Build(navigation);
        var rig = MovementTestRig.CreateBakeReloadMap(
            32, navigation, profiles, clearanceBake);
        var units = rig.SpawnGrid(new Vector2(100f, 300f), 2, 4, 22f);
        rig.Move(units, new Vector2(1160f, 353f));
        return new VisualTestSession(
            "resource-file-watch-workflow",
            "Debounced file watch and safe Bake-only auto commit",
            900,
            rig,
            units,
            runtime =>
            {
                var workflow = ResourceReloadWorkflowSelfTest.Run();
                var arrival = EvaluateArrival(
                    runtime, units, units.Length, 14f, 1f, 0, 0);
                return new ScenarioResult(
                    workflow.Passed && arrival.Passed,
                    $"{workflow.Summary}, {arrival.Summary}");
            });
    }

    private static VisualTestSession CreateDualResourceEconomy()
    {
        var fixture = EconomySelfTest.CreateScenario();
        return new VisualTestSession(
                "economy-dual-resource",
                "Dual-resource worker saturation, refinery and depletion loop",
                1200,
                fixture.Rig,
                fixture.Workers,
                _ =>
                {
                    var result = EconomySelfTest.Evaluate(fixture);
                    return new ScenarioResult(result.Passed, result.Summary);
                })
            .Highlight(
                new SimRect(
                    new Vector2(400f, 210f),
                    new Vector2(540f, 390f)),
                "MINERALS: 1 active worker per patch",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(
                    new Vector2(425f, 455f),
                    new Vector2(515f, 545f)),
                "VESPENE: refinery required, capacity 3",
                TestDiagnosticKind.Accepted);
    }

    private static VisualTestSession CreateExpansionSaturation()
    {
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(1300f, 720f), 32);
        rig.RegisterPlayer(1, 3000, 500, 20);
        var leftNodes = new[]
        {
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(390f, 300f), 4000, 5, 0.45f, 2),
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(410f, 410f), 4000, 5, 0.45f, 2)
        };
        rig.AddResourceNode(TestEconomyResourceKind.Minerals,
            new Vector2(890f, 300f), 4000, 5, 0.45f, 2);
        rig.AddResourceNode(TestEconomyResourceKind.Minerals,
            new Vector2(870f, 410f), 4000, 5, 0.45f, 2);
        var workers = Enumerable.Range(0, 8)
            .Select(index => rig.SpawnWorker(
                new Vector2(120f + index % 4 * 24f, 250f + index / 4 * 28f), 1))
            .ToArray();
        var builders = new[]
        {
            rig.SpawnWorker(new Vector2(145f, 355f), 1),
            rig.SpawnWorker(new Vector2(925f, 355f), 1)
        };
        var fastTownHall = DemoBuildingTypes.CommandCenter with
        {
            BuildSeconds = 1f
        };
        rig.StartCommandRecording();
        var main = rig.Build(1, builders[0], fastTownHall, new Vector2(250f, 355f));
        var expansion = rig.Build(
            1, builders[1], fastTownHall, new Vector2(1030f, 355f));

        TestEconomyBaseId mainBase = default;
        TestEconomyBaseId expansionBase = default;
        var basesRegistered = false;
        var overSaturated = false;
        var invalidTransferRejected = false;
        var transferSucceeded = false;
        var balanced = false;
        var expansionDestroyed = false;
        var session = new VisualTestSession(
            "economy-expansion-saturation",
            "Town Hall expansion, saturation and deterministic worker transfer",
            900,
            rig,
            workers.Concat(builders).ToArray(),
            runtime =>
            {
                var bases = runtime.ObserveEconomyBases(1);
                var mainState = bases.Single(value => value.Id == mainBase);
                var expansionState = bases.Single(value => value.Id == expansionBase);
                var log = runtime.CaptureEconomyCommandLog();
                var logValid = log.EntryCount == 9 &&
                               log.TryCanonicalRoundTrip(out var roundTrip) &&
                               roundTrip?.StableHash == log.StableHash;
                var passed = main.Succeeded && expansion.Succeeded &&
                             basesRegistered && overSaturated &&
                             invalidTransferRejected && transferSucceeded &&
                             balanced && expansionDestroyed &&
                             mainState.Operational &&
                             !expansionState.Operational && logValid;
                return new ScenarioResult(
                    passed,
                    $"bases={bases.Length}, initial=8/4+0/4, balanced={balanced}, " +
                    $"transfer={transferSucceeded}, destroyed={!expansionState.Operational}, " +
                    $"log={log.EntryCount}/v{log.FormatVersion}, hash={log.StableHash:X16}");
            })
            .Highlight(
                new SimRect(new Vector2(150f, 240f), new Vector2(450f, 470f)),
                "MAIN: 4 ideal workers, initially 8 assigned",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(850f, 240f), new Vector2(1130f, 470f)),
                "EXPANSION: receives 4 workers, then Town Hall is destroyed",
                TestDiagnosticKind.Accepted);
        session.At(90, "Register completed Town Halls and saturate the main", runtime =>
        {
            var bases = runtime.ObserveEconomyBases(1);
            basesRegistered = bases.Length == 2 &&
                              bases.All(value => value.Operational &&
                                  value.MineralNodes == 2 && value.IdealWorkers == 4);
            if (!basesRegistered)
                return;
            mainBase = bases.Single(value => value.TownHall == main.BuildingId).Id;
            expansionBase = bases.Single(
                value => value.TownHall == expansion.BuildingId).Id;
            for (var index = 0; index < workers.Length; index++)
                runtime.Gather(1, workers[index], leftNodes[index % leftNodes.Length]);
        });
        session.At(180, "Observe over-saturation and reject a same-base transfer", runtime =>
        {
            var bases = runtime.ObserveEconomyBases(1);
            overSaturated = bases.Single(value => value.Id == mainBase).AssignedWorkers == 8 &&
                            bases.Single(value => value.Id == expansionBase).AssignedWorkers == 0;
            invalidTransferRejected = runtime.TransferWorkers(
                1, mainBase, mainBase, 2).Code == TestWorkerTransferCommandCode.SameBase;
        });
        session.At(240, "Transfer the four closest eligible workers", runtime =>
        {
            var result = runtime.TransferWorkers(1, mainBase, expansionBase, 4);
            transferSucceeded = result.Succeeded && result.TransferredWorkers == 4;
        });
        session.At(360, "Verify both mineral lines are exactly saturated", runtime =>
        {
            var bases = runtime.ObserveEconomyBases(1);
            balanced = bases.All(value =>
                value.AssignedWorkers == 4 && value.IdealWorkers == 4 &&
                MathF.Abs(value.Saturation - 1f) < 0.001f);
        });
        session.At(720, "Destroy expansion and disable its DropOff", runtime =>
            expansionDestroyed = runtime.DamageBuilding(
                expansion.BuildingId, 100000f));
        return session;
    }

    private static VisualTestSession CreatePlayerVisibilityAuthority(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            24, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 1200, 200, 20);
        rig.RegisterPlayer(2, 1200, 200, 20);
        var scout = rig.SpawnCombat(
            new Vector2(120f, 350f), 1,
            maximumSpeed: 170f, acceleration: 760f);
        var enemy = rig.SpawnCombat(new Vector2(930f, 350f), 2);
        var enemyBuilder = rig.SpawnWorker(new Vector2(930f, 500f), 2);
        var minerals = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(600f, 280f), 2400, 5, 0.5f, 2);
        rig.AddResourceDropOff(2, new Vector2(1030f, 500f));
        rig.StartReplayPackageRecording();
        var enemyBarracks = rig.Build(
            2,
            enemyBuilder,
            DemoBuildingTypes.Barracks with { BuildSeconds = 1f },
            new Vector2(1030f, 500f));

        var initialBoundary = false;
        var ownershipRejected = false;
        var hiddenTargetRejected = false;
        var visibleResourceObserved = false;
        var visibleEntitiesObserved = false;
        var visibleAttackAccepted = false;
        var hiddenAgain = false;
        var explorationRetained = false;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 480;
        var visible = new[] { scout, enemy, enemyBuilder };
        var session = new VisualTestSession(
            "player-visibility-authority",
            "Player view, explored fog and authoritative command ownership",
            900,
            rig,
            visible,
            runtime =>
            {
                var view = runtime.ObservePlayerView(1);
                var package = runtime.CaptureReplayPackage();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var replay = runtime.ReplayPackage(decoded!, runtime.Tick);
                var hot = runtime.BindHotSnapshot(package, hotCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var exact = replay.FinalHash == runtime.StateHash &&
                            resumed.FinalHash == runtime.StateHash &&
                            replay.MatchesFrom(resumed, hotTick);
                var versions = package.FormatVersion == 11 &&
                               hot.FormatVersion == 11;
                var passed = enemyBarracks.Succeeded && initialBoundary &&
                             ownershipRejected && hiddenTargetRejected &&
                             visibleResourceObserved &&
                             visibleEntitiesObserved && visibleAttackAccepted &&
                             hiddenAgain && explorationRetained &&
                             packageRoundTrip && hotRoundTrip && exact && versions;
                return new ScenarioResult(
                    passed,
                    $"initial={initialBoundary}, authority={ownershipRejected}/" +
                    $"{hiddenTargetRejected}, visible={visibleResourceObserved}/" +
                    $"{visibleEntitiesObserved}/" +
                    $"{visibleAttackAccepted}, hiddenAgain={hiddenAgain}, " +
                    $"fog={view.HiddenCells}/{view.ExploredCells}/{view.VisibleCells}, " +
                    $"versions=package{package.FormatVersion}/hot{hot.FormatVersion}, " +
                    $"exact={exact}");
            })
            .Highlight(
                new SimRect(new Vector2(40f, 220f), new Vector2(360f, 500f)),
                "OWN TERRITORY: always queryable",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(850f, 220f), new Vector2(1120f, 590f)),
                "ENEMY: hidden -> visible -> hidden, no live-state leak",
                TestDiagnosticKind.Accepted);
        session.At(30, "Reject enemy control and hidden entity targeting", runtime =>
        {
            var view = runtime.ObservePlayerView(1);
            initialBoundary = view.Units.SequenceEqual([scout]) &&
                              view.Buildings.Length == 0 &&
                              view.Resources.Length == 0;
            ownershipRejected = runtime.PlayerMove(
                1, [enemy], new Vector2(700f, 350f)) ==
                TestPlayerOrderCommandCode.WrongOwner;
            hiddenTargetRejected = runtime.PlayerAttackUnit(1, [scout], enemy) ==
                                   TestPlayerOrderCommandCode.TargetNotVisible;
        });
        session.At(60, "Scout into unexplored territory through the mineral field",
            runtime => runtime.PlayerMove(1, [scout], new Vector2(850f, 350f)));
        session.At(240, "Observe the mineral field without leaking hidden enemies", runtime =>
        {
            var view = runtime.ObservePlayerView(1);
            visibleResourceObserved = view.Resources.Any(value =>
                value.NodeId == minerals &&
                value.Visibility == TestMapVisibility.Visible &&
                value.KnownRemaining == 2400);
        });
        session.At(420, "Observe and legally target visible enemy entities", runtime =>
        {
            var view = runtime.ObservePlayerView(1);
            visibleEntitiesObserved = view.Units.Contains(enemy) &&
                                      view.Buildings.Contains(enemyBarracks.BuildingId);
            visibleAttackAccepted = runtime.PlayerAttackUnit(1, [scout], enemy) ==
                                    TestPlayerOrderCommandCode.Success;
            runtime.PlayerMove(1, [scout], new Vector2(120f, 350f));
        });
        session.At(hotTick, "Capture explored fog while the scout returns", runtime =>
            hotCapture = runtime.CaptureRuntimeState());
        session.At(720, "Verify dynamic enemies hide but exploration remains", runtime =>
        {
            var view = runtime.ObservePlayerView(1);
            hiddenAgain = !view.Units.Contains(enemy) &&
                          !view.Buildings.Contains(enemyBarracks.BuildingId);
            explorationRetained = view.ExploredCells > 0 &&
                                  view.VisibleCells > 0 &&
                                  view.HiddenCells > 0 &&
                                  view.Resources.Any(value =>
                                      value.NodeId == minerals &&
                                      value.Visibility == TestMapVisibility.Explored &&
                                      value.KnownRemaining == -1);
        });
        return session;
    }

    private static VisualTestSession CreateMatchCapabilityElimination(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            40, navigationMap, gameplayProfiles, clearanceBake);
        for (var player = 1; player <= 3; player++)
            rig.RegisterPlayer(player, 3000, 500, 30);
        var builders = new[]
        {
            rig.SpawnWorker(new Vector2(120f, 500f), 1),
            rig.SpawnWorker(new Vector2(930f, 570f), 2),
            rig.SpawnWorker(new Vector2(400f, 360f), 2),
            rig.SpawnWorker(new Vector2(800f, 210f), 3)
        };
        var commandCenter = DemoBuildingTypes.CommandCenter with
        {
            BuildSeconds = 0.5f,
            MaximumHealth = 240f
        };
        var barracks = DemoBuildingTypes.Barracks with
        {
            BuildSeconds = 0.5f,
            MaximumHealth = 220f
        };
        rig.StartMatch(1, 2, 3);
        rig.StartReplayPackageRecording();
        var playerOneBase = rig.Build(
            1, builders[0], commandCenter, new Vector2(250f, 500f));
        var playerTwoBase = rig.Build(
            2, builders[1], commandCenter, new Vector2(1050f, 570f));
        var playerTwoProduction = rig.Build(
            2, builders[2], barracks, new Vector2(400f, 260f));
        var playerThreeBase = rig.Build(
            3, builders[3], barracks, new Vector2(800f, 140f));

        var established = false;
        var townHallLossIsNotDefeat = false;
        var defeatedCommandRejected = false;
        var matchCompleted = false;
        var completedCommandRejected = false;
        var establishedStateHash = 0UL;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 420;
        var visible = builders;
        var session = new VisualTestSession(
            "match-capability-elimination",
            "Explicit participants, production capability and terminal elimination",
            600,
            rig,
            visible,
            runtime =>
            {
                var match = runtime.ObserveMatch();
                var package = runtime.CaptureReplayPackage();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var establishedReplay = runtime.ReplayPackage(decoded!, 60);
                var hot = runtime.BindHotSnapshot(package, hotCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var exact = establishedReplay.FinalHash == establishedStateHash &&
                            resumed.FinalHash == runtime.StateHash;
                var winner = match.Phase == TestMatchPhase.Completed &&
                             match.WinnerPlayerId == 1 &&
                             match.Players.Single(value => value.PlayerId == 1).Status ==
                                 TestMatchPlayerStatus.Victorious &&
                             match.Players.Where(value => value.PlayerId != 1).All(
                                 value => value.Status == TestMatchPlayerStatus.Defeated);
                var passed = playerOneBase.Succeeded && playerTwoBase.Succeeded &&
                             playerTwoProduction.Succeeded && playerThreeBase.Succeeded &&
                             established && townHallLossIsNotDefeat &&
                             defeatedCommandRejected && matchCompleted &&
                             completedCommandRejected && winner &&
                             packageRoundTrip && hotRoundTrip && exact &&
                             package.FormatVersion == 11 && hot.FormatVersion == 11;
                return new ScenarioResult(
                    passed,
                    $"build={playerOneBase.Code}/{playerTwoBase.Code}/" +
                    $"{playerTwoProduction.Code}/{playerThreeBase.Code}, " +
                    $"established={established}, townHallLoss={townHallLossIsNotDefeat}, " +
                    $"defeatedReject={defeatedCommandRejected}, winner={match.WinnerPlayerId}, " +
                    $"completed={matchCompleted}/{completedCommandRejected}@{match.CompletedTick}, " +
                    $"versions=package{package.FormatVersion}/hot{hot.FormatVersion}, exact={exact}");
            })
            .Highlight(
                new SimRect(new Vector2(760f, 480f), new Vector2(1180f, 640f)),
                "PLAYER 2: losing Town Hall is critical, last Barracks is elimination",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(1020f, 80f), new Vector2(1230f, 210f)),
                "PLAYER 3: final remaining opponent determines victory",
                TestDiagnosticKind.Accepted);
        session.At(60, "All participants establish at least one active structure", runtime =>
        {
            var match = runtime.ObserveMatch();
            established = match.Phase == TestMatchPhase.Running &&
                          match.Players.All(value => value.EstablishedPresence) &&
                          match.Players.Single(value => value.PlayerId == 2)
                              .HasAnyProduction;
            establishedStateHash = runtime.StateHash;
        });
        session.At(120, "Destroy player 2 Town Hall first", runtime =>
            runtime.DamageBuilding(playerTwoBase.BuildingId, 100000f));
        session.At(180, "Town Hall loss leaves Barracks production and player active", runtime =>
        {
            var player = runtime.ObserveMatch().Players.Single(value =>
                value.PlayerId == 2);
            townHallLossIsNotDefeat = player.Status == TestMatchPlayerStatus.Active &&
                                      player.TownHalls == 0 &&
                                      player.ProductionFacilities == 1 &&
                                      player.ActiveBuildings == 1 &&
                                      player.IsEliminationRisk;
            runtime.DamageBuilding(playerTwoProduction.BuildingId, 100000f);
        });
        session.At(260, "Eliminated participant cannot issue further commands", runtime =>
            defeatedCommandRejected = runtime.PlayerMove(
                2, [builders[1]], new Vector2(900f, 400f)) ==
                TestPlayerOrderCommandCode.PlayerDefeated);
        session.At(300, "Destroy the last opposing participant", runtime =>
            runtime.DamageBuilding(playerThreeBase.BuildingId, 100000f));
        session.At(hotTick, "Capture terminal match state", runtime =>
            hotCapture = runtime.CaptureRuntimeState());
        session.At(480, "Winner is terminal and all new player commands are rejected", runtime =>
        {
            var match = runtime.ObserveMatch();
            matchCompleted = match.Phase == TestMatchPhase.Completed &&
                             match.WinnerPlayerId == 1;
            completedCommandRejected = runtime.PlayerMove(
                1, [builders[0]], new Vector2(500f, 300f)) ==
                TestPlayerOrderCommandCode.MatchCompleted;
        });
        return session;
    }

    private static VisualTestSession CreateModularAiSkirmish()
    {
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(1280f, 720f), 64);
        rig.RegisterPlayer(1, 3500, 1000, 15, 6);
        rig.RegisterPlayer(2, 500, 0, 15, 1);
        var workers = Enumerable.Range(0, 6).Select(index =>
            rig.SpawnWorker(
                new Vector2(70f + (index % 2) * 20f, 290f + index * 24f),
                1)).ToArray();
        var defenderBuilder = rig.SpawnWorker(new Vector2(1210f, 360f), 2);
        var localMinerals = new[]
        {
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(390f, 285f), 3000, 8, 0.35f, 2),
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(420f, 360f), 3000, 8, 0.35f, 2),
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(390f, 435f), 3000, 8, 0.35f, 2)
        };
        rig.AddResourceNode(TestEconomyResourceKind.Minerals,
            new Vector2(650f, 360f), 3500, 8, 0.35f, 3);
        var gas = rig.AddResourceNode(TestEconomyResourceKind.VespeneGas,
            new Vector2(320f, 535f), 3500, 8, 0.4f, 3,
            requiresRefinery: true, operational: false);
        rig.AddResourceNode(TestEconomyResourceKind.Minerals,
            new Vector2(900f, 300f), 2500, 8, 0.4f, 2);

        var fastTownHall = DemoBuildingTypes.CommandCenter with
        {
            BuildSeconds = 0.5f,
            MaximumHealth = 1200f,
            SupplyProvided = 0
        };
        var fragileEnemyTownHall = fastTownHall with { MaximumHealth = 360f };
        rig.StartMatch(1, 2);
        rig.StartCommandRecording();
        var playerBase = rig.Build(
            1, workers[0], fastTownHall, new Vector2(220f, 360f));
        var enemyBase = rig.Build(
            2, defenderBuilder, fragileEnemyTownHall, new Vector2(1050f, 360f));
        for (var index = 1; index < workers.Length; index++)
            rig.Gather(1, workers[index], localMinerals[(index - 1) % 3]);

        var attached = false;
        var developed = false;
        var mobilized = false;
        var visible = workers.Append(defenderBuilder).ToArray();
        var session = new VisualTestSession(
            "ai-modular-skirmish",
            "Modular AI economy, infrastructure, technology, scouting and assault",
            3600,
            rig,
            visible,
            runtime =>
            {
                var match = runtime.ObserveMatch();
                var ai = runtime.ObserveAi();
                var player = match.Players.Single(value => value.PlayerId == 1);
                var supplyBuildings = runtime.CountPlayerBuildings(
                    1, DemoBuildingTypes.SupplyDepot.Id);
                var barracksBuildings = runtime.CountPlayerBuildings(
                    1, DemoBuildingTypes.Barracks.Id);
                var refineryBuildings = runtime.CountPlayerBuildings(
                    1, DemoBuildingTypes.Refinery.Id);
                var academyBuildings = runtime.CountPlayerBuildings(
                    1, DemoBuildingTypes.Academy.Id);
                var townHalls = runtime.CountPlayerBuildings(
                    1, DemoBuildingTypes.CommandCenter.Id);
                var infrastructure = supplyBuildings >= 1 &&
                                     barracksBuildings >= 1 &&
                                     refineryBuildings >= 1 &&
                                     academyBuildings >= 1;
                var expanded = townHalls >= 2;
                var technology = runtime.TechnologyLevel(
                    1, DemoTechnologies.InfantryWeapons.Id) >= 1;
                var economyLog = runtime.CaptureEconomyCommandLog();
                var constructionLog = runtime.CaptureConstructionCommandLog();
                var productionLog = runtime.CaptureProductionCommandLog();
                var unitLog = runtime.CaptureCommandLog();
                var economy = runtime.ObservePlayerEconomy(1);
                var commandCoverage = economyLog.EntryCount >= 5 &&
                                      constructionLog.EntryCount >= 6 &&
                                      productionLog.EntryCount >= 5 &&
                                      unitLog.EntryCount >= 2;
                var victory = match.Phase == TestMatchPhase.Completed &&
                              match.WinnerPlayerId == 1;
                var passed = playerBase.Succeeded && enemyBase.Succeeded &&
                             attached && developed && mobilized &&
                             infrastructure && expanded && technology &&
                             player.Workers >= 10 && player.CombatUnits >= 4 &&
                             commandCoverage && victory && ai.StateBytes > 0 &&
                             ai.LastDecisionTick > 300;
                return new ScenarioResult(
                    passed,
                    $"phase={ai.Phase}, workers={player.Workers}, army={player.CombatUnits}, " +
                    $"buildings=S{supplyBuildings}/B{barracksBuildings}/" +
                    $"R{refineryBuildings}/A{academyBuildings}/CC{townHalls}, " +
                    $"expand={expanded}, tech={technology}, " +
                    $"bank={economy.Minerals}/{economy.VespeneGas} " +
                    $"supply={economy.SupplyUsed}/{economy.SupplyCapacity}, " +
                    $"commands=e{economyLog.EntryCount}/b{constructionLog.EntryCount}/" +
                    $"p{productionLog.EntryCount}/u{unitLog.EntryCount}, " +
                    $"victory={victory}@{match.CompletedTick}, aiState={ai.StateBytes}B");
            })
            .RenderSpawnedUnits()
            .Highlight(
                new SimRect(new Vector2(20f, 160f), new Vector2(540f, 590f)),
                "AI BASE: economy / supply / production / refinery / academy",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(570f, 180f), new Vector2(900f, 560f)),
                "EXPANSION: discovered resources drive second Town Hall",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(930f, 250f), new Vector2(1160f, 470f)),
                "ASSAULT: explicit visible-building target closes the match",
                TestDiagnosticKind.Rejected);
        session.At(60, "Attach modular AI after both players establish", runtime =>
        {
            attached = runtime.ObserveMatch().Players.All(value =>
                value.EstablishedPresence);
            runtime.AttachDemoAi(1, targetWorkers: 10, attackArmySize: 4,
                buildingSeconds: 1.2f);
        });
        session.At(900, "Economy and infrastructure planners operate concurrently", runtime =>
        {
            var ai = runtime.ObserveAi();
            developed = (ai.Phase is TestAiStrategicPhase.Developing or
                            TestAiStrategicPhase.Mobilizing or
                            TestAiStrategicPhase.Attacking) &&
                        runtime.CountPlayerBuildings(
                            1, DemoBuildingTypes.Barracks.Id) >= 1;
        });
        session.At(1800, "Scouting and army production unlock expansion and attack", runtime =>
        {
            var player = runtime.ObserveMatch().Players.Single(value =>
                value.PlayerId == 1);
            mobilized = player.CombatUnits >= 4 &&
                        runtime.ObserveAi().LastDecisionTick >= 1788;
        });
        return session;
    }

    private static VisualTestSession CreateConstructionGameplayBuildings()
    {
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(1200f, 700f), 32);
        rig.RegisterPlayer(1, 1000, 300, 15, 6);
        rig.RegisterPlayer(2, 50, 0, 10, 1);
        var gas = rig.AddResourceNode(
            TestEconomyResourceKind.VespeneGas,
            new Vector2(780f, 520f),
            1000,
            4,
            0.5f,
            3,
            requiresRefinery: true,
            operational: false);
        var workers = new[]
        {
            rig.SpawnWorker(new Vector2(160f, 170f), 1),
            rig.SpawnWorker(new Vector2(390f, 180f), 1),
            rig.SpawnWorker(new Vector2(780f, 180f), 1),
            rig.SpawnWorker(new Vector2(620f, 520f), 1),
            rig.SpawnWorker(new Vector2(180f, 520f), 1),
            rig.SpawnWorker(new Vector2(100f, 600f), 1),
            rig.SpawnWorker(new Vector2(100f, 650f), 2)
        };
        var supply = rig.Build(
            1, workers[0], DemoBuildingTypes.SupplyDepot,
            new Vector2(280f, 170f));
        var barracks = rig.Build(
            1, workers[1], DemoBuildingTypes.Barracks,
            new Vector2(520f, 180f));
        var commandCenter = rig.Build(
            1, workers[2], DemoBuildingTypes.CommandCenter,
            new Vector2(950f, 180f));
        var refinery = rig.Build(
            1, workers[3], DemoBuildingTypes.Refinery,
            new Vector2(0f, 0f), gas);
        var canceled = rig.Build(
            1, workers[4], DemoBuildingTypes.SupplyDepot,
            new Vector2(320f, 520f));
        var insufficient = rig.Build(
            2, workers[6], DemoBuildingTypes.Barracks,
            new Vector2(520f, 620f));
        var wrongOwner = rig.Build(
            1, workers[6], DemoBuildingTypes.SupplyDepot,
            new Vector2(500f, 600f));
        var missingRefineryNode = rig.Build(
            1, workers[5], DemoBuildingTypes.Refinery,
            new Vector2(600f, 500f));
        var busyBuilder = rig.Build(
            1, workers[0], DemoBuildingTypes.SupplyDepot,
            new Vector2(160f, 350f));
        var rawRemovalRejected = !rig.RemoveBuilding(
            rig.ObserveGameplayBuilding(supply.BuildingId).FootprintId);
        var commandRules =
            supply.Succeeded && barracks.Succeeded && commandCenter.Succeeded &&
            refinery.Succeeded && canceled.Succeeded &&
            insufficient.Code == TestConstructionCommandCode.InsufficientMinerals &&
            wrongOwner.Code == TestConstructionCommandCode.WrongOwner &&
            missingRefineryNode.Code ==
                TestConstructionCommandCode.RefineryNodeRequired &&
            busyBuilder.Code == TestConstructionCommandCode.BuilderBusy &&
            rawRemovalRejected;

        var canceledSuccessfully = false;
        var interrupted = false;
        var resumed = false;
        var damaged = false;
        var completedBeforeDestruction = false;
        var destroyed = false;
        var session = new VisualTestSession(
            "construction-gameplay-buildings",
            "Owned buildings with costs, builders, progress, refund and refinery binding",
            960,
            rig,
            workers,
            runtime =>
            {
                var supplyState = runtime.ObserveGameplayBuilding(supply.BuildingId);
                var barracksState = runtime.ObserveGameplayBuilding(barracks.BuildingId);
                var commandState = runtime.ObserveGameplayBuilding(commandCenter.BuildingId);
                var refineryState = runtime.ObserveGameplayBuilding(refinery.BuildingId);
                var canceledState = runtime.ObserveGameplayBuilding(canceled.BuildingId);
                var economy = runtime.ObservePlayerEconomy(1);
                var gasState = runtime.ObserveResourceNode(gas);
                var liveCompleted = new[]
                {
                    supplyState, commandState, refineryState
                }.All(value => value.State == TestBuildingLifecycleState.Completed);
                var sizes = new[]
                {
                    supplyState.Size, barracksState.Size,
                    commandState.Size, refineryState.Size
                }.Distinct().Count() == 4;
                var passed = commandRules && canceledSuccessfully && interrupted &&
                             resumed && damaged && liveCompleted &&
                             completedBeforeDestruction && destroyed && sizes &&
                             barracksState.State == TestBuildingLifecycleState.Destroyed &&
                             canceledState.State == TestBuildingLifecycleState.Canceled &&
                             runtime.GameplayBuildingCount == 3 &&
                             runtime.BuildingCount == 3 &&
                             economy.Minerals == 250 && economy.VespeneGas == 300 &&
                             economy.SupplyCapacity == 38 &&
                             gasState.Operational && supplyState.Health == 300f;
                return new ScenarioResult(
                    passed,
                    $"completed={liveCompleted}+destroyed, active={runtime.GameplayBuildingCount}, " +
                    $"sizes={sizes}, resources={economy.Minerals}/{economy.VespeneGas}, " +
                    $"supply={economy.SupplyUsed}/{economy.SupplyCapacity}, " +
                    $"refund={canceledSuccessfully}, pause/resume={interrupted}/{resumed}, " +
                    $"refinery={gasState.Operational}, hp={supplyState.Health:0}");
            });
        session.At(120, "Cancel unfinished building and refund 75%", runtime =>
            canceledSuccessfully = runtime.CancelConstruction(
                1, canceled.BuildingId));
        session.At(180, "Player order interrupts continuous construction", runtime =>
        {
            runtime.Move([workers[1]], new Vector2(390f, 330f));
            interrupted = runtime.ObserveGameplayBuilding(barracks.BuildingId).State ==
                          TestBuildingLifecycleState.WaitingForBuilder;
        });
        session.At(240, "Resume construction with the same worker", runtime =>
            resumed = runtime.ResumeConstruction(
                1, barracks.BuildingId, workers[1]));
        session.At(720, "Damage a completed supply building", runtime =>
            damaged = runtime.DamageBuilding(supply.BuildingId, 100f));
        session.At(840, "Destroy a completed production building", runtime =>
        {
            completedBeforeDestruction =
                runtime.ObserveGameplayBuilding(barracks.BuildingId).State ==
                TestBuildingLifecycleState.Completed;
            destroyed = runtime.DamageBuilding(barracks.BuildingId, 2000f);
        });
        return session
            .Highlight(
                new SimRect(new Vector2(245f, 135f), new Vector2(1050f, 250f)),
                "SUPPLY / PRODUCTION / TOWN HALL: distinct gameplay entities",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(730f, 470f), new Vector2(830f, 570f)),
                "REFINERY: bound to Vespene node",
                TestDiagnosticKind.Accepted);
    }

    private static VisualTestSession CreateBuildingTypeResourceRuntime(
        BuildingTypeCatalogSnapshot? catalog)
    {
        catalog ??= DemoBuildingTypes.CreateCatalog();
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(1200f, 700f), 16);
        rig.RegisterPlayer(1, 1000, 300, 15, 4);
        var gas = rig.AddResourceNode(
            TestEconomyResourceKind.VespeneGas,
            new Vector2(800f, 520f), 1000, 4, 0.5f, 3,
            requiresRefinery: true, operational: false);
        var workers = new[]
        {
            rig.SpawnWorker(new Vector2(160f, 170f), 1),
            rig.SpawnWorker(new Vector2(400f, 180f), 1),
            rig.SpawnWorker(new Vector2(820f, 180f), 1),
            rig.SpawnWorker(new Vector2(680f, 520f), 1)
        };
        var orders = new[]
        {
            rig.Build(1, workers[0], catalog.Type(0), new Vector2(280f, 170f)),
            rig.Build(1, workers[1], catalog.Type(1), new Vector2(520f, 180f)),
            rig.Build(1, workers[2], catalog.Type(2), new Vector2(950f, 180f)),
            rig.Build(1, workers[3], catalog.Type(3), Vector2.Zero, gas)
        };
        return new VisualTestSession(
                "building-type-resource-runtime",
                "Versioned building catalog drives four real construction lifecycles",
                900,
                rig,
                workers,
                runtime =>
                {
                    var buildings = orders.Select(order =>
                        runtime.ObserveGameplayBuilding(order.BuildingId)).ToArray();
                    var completed = buildings.All(value =>
                        value.State == TestBuildingLifecycleState.Completed);
                    var distinctSizes = buildings.Select(value => value.Size)
                        .Distinct().Count();
                    var economy = runtime.ObservePlayerEconomy(1);
                    var refinery = runtime.ObserveResourceNode(gas);
                    var passed = orders.All(order => order.Succeeded) && completed &&
                                 distinctSizes == 4 && catalog.FormatVersion == 1 &&
                                 catalog.StableHash != 0UL && economy.Minerals == 275 &&
                                 economy.SupplyCapacity == 38 && refinery.Operational;
                    return new ScenarioResult(
                        passed,
                        $"catalog=f{catalog.FormatVersion}/{catalog.StableHashText}, " +
                        $"types={catalog.Types.Length}, completed={completed}, " +
                        $"sizes={distinctSizes}, minerals={economy.Minerals}, " +
                        $"supply={economy.SupplyCapacity}, refinery={refinery.Operational}");
                })
            .Highlight(
                new SimRect(new Vector2(245f, 100f), new Vector2(1050f, 250f)),
                "RESOURCE TYPES: 48 / 112 / 160 width",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(750f, 470f), new Vector2(850f, 570f)),
                "RESOURCE CONTRACT: refinery binds gas node",
                TestDiagnosticKind.Accepted);
    }

    private static VisualTestSession CreateProductionQueueExitRally()
    {
        var catalog = DemoProductionCatalog.CreateSnapshot();
        var marine = catalog.Recipe(0);
        var marauder = catalog.Recipe(1);
        var workerRecipe = catalog.Recipe(2);
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(1200f, 700f), 32);
        rig.RegisterPlayer(1, 1000, 300, 10, 0);
        var builder = rig.SpawnWorker(new Vector2(360f, 180f), 1);
        var barracks = rig.Build(
            1, builder, DemoBuildingTypes.Barracks, new Vector2(520f, 180f));
        var blockers = Array.Empty<TestUnitId>();
        var ordersAccepted = false;
        var wrongProducerRejected = false;
        var supplyBlocked = false;
        var queueFull = false;
        var canceled = false;
        var waitedForExit = false;
        var cleanupOrderAccepted = false;
        var producerDestroyed = false;
        var producedUnitKilled = false;
        var rally = new Vector2(1000f, 400f);
        var session = new VisualTestSession(
            "production-queue-exit-rally",
            "Reserved supply, cancel, blocked exit, delayed spawn and rally",
            1320,
            rig,
            [builder],
            runtime =>
            {
                var queue = runtime.ObserveProduction(barracks.BuildingId);
                var economy = runtime.ObservePlayerEconomy(1);
                var first = runtime.Observe(new TestUnitId(13));
                var second = runtime.Observe(new TestUnitId(14));
                var rallied = Vector2.Distance(first.Position, rally) < 45f &&
                              Vector2.Distance(second.Position, rally) < 45f;
                var building = runtime.ObserveGameplayBuilding(barracks.BuildingId);
                var passed = barracks.Succeeded && ordersAccepted &&
                             wrongProducerRejected && supplyBlocked && queueFull && canceled &&
                             waitedForExit && cleanupOrderAccepted &&
                             producerDestroyed && queue.OrderCount == 0 &&
                             building.State == TestBuildingLifecycleState.Destroyed &&
                             runtime.UnitCount == 15 && economy.Minerals == 750 &&
                             economy.VespeneGas == 300 && economy.SupplyUsed == 1 &&
                             producedUnitKilled &&
                             rallied;
                return new ScenarioResult(
                    passed,
                    $"catalog={catalog.StableHashText}, orders={ordersAccepted}, " +
                    $"waited={waitedForExit}, units={runtime.UnitCount}, " +
                    $"queue={queue.OrderCount}, resources={economy.Minerals}/" +
                    $"{economy.VespeneGas}, supply={economy.SupplyUsed}/" +
                    $"{economy.SupplyCapacity}, rallied={rallied}, " +
                    $"destroyed={producerDestroyed}");
            });
        session.At(510, "Seal all Barracks exit candidates and reserve queue supply", runtime =>
        {
            Vector2[] positions =
            [
                new(589.5f, 180f), new(450.5f, 180f),
                new(520f, 233.5f), new(520f, 126.5f),
                new(589.5f, 233.5f), new(589.5f, 126.5f),
                new(450.5f, 233.5f), new(450.5f, 126.5f),
                new(589.5f, 160f), new(589.5f, 200f),
                new(450.5f, 160f), new(450.5f, 200f)
            ];
            blockers = positions.Select(value => runtime.Spawn(value)).ToArray();
            runtime.SetRallyPoint(1, barracks.BuildingId, rally);
            var first = runtime.Train(1, barracks.BuildingId, marine);
            var canceledOrder = runtime.Train(1, barracks.BuildingId, marauder);
            var second = runtime.Train(1, barracks.BuildingId, marine);
            var canceledThird = runtime.Train(1, barracks.BuildingId, marine);
            var canceledFourth = runtime.Train(1, barracks.BuildingId, marine);
            queueFull = runtime.Train(1, barracks.BuildingId, marine).Code ==
                        TestProductionCommandCode.QueueFull;
            wrongProducerRejected = runtime.Train(
                1, barracks.BuildingId, workerRecipe).Code ==
                TestProductionCommandCode.WrongProducerType;
            canceled = runtime.CancelProduction(1, canceledOrder.OrderId) &&
                       runtime.CancelProduction(1, canceledThird.OrderId) &&
                       runtime.CancelProduction(1, canceledFourth.OrderId);
            var oversized = marauder with
            {
                Cost = marauder.Cost with { Supply = 9 }
            };
            supplyBlocked = runtime.Train(1, barracks.BuildingId, oversized).Code ==
                            TestProductionCommandCode.SupplyBlocked;
            ordersAccepted = first.Succeeded && canceledOrder.Succeeded &&
                             second.Succeeded && canceledThird.Succeeded &&
                             canceledFourth.Succeeded;
        });
        session.At(720, "Completed unit remains reserved while all exits are blocked", runtime =>
        {
            var queue = runtime.ObserveProduction(barracks.BuildingId);
            waitedForExit = queue.ActiveState ==
                            TestProductionOrderState.WaitingForExit &&
                            queue.ActiveProgress == 1f && runtime.UnitCount == 13;
        });
        session.At(750, "Open exits; produced units should rally", runtime =>
            runtime.Move(blockers, new Vector2(1000f, 620f)));
        session.At(1120, "Queue one more unit before producer destruction", runtime =>
            cleanupOrderAccepted = runtime.Train(
                1, barracks.BuildingId, marine).Succeeded);
        session.At(1140, "Destroy producer and refund unfinished queue", runtime =>
            producerDestroyed = runtime.DamageBuilding(
                barracks.BuildingId, 2000f));
        session.At(1180, "Produced unit death releases its reserved supply", runtime =>
            producedUnitKilled = runtime.DamageUnit(
                new TestUnitId(13), 1000f));
        return session
            .Highlight(
                new SimRect(new Vector2(430f, 105f), new Vector2(610f, 255f)),
                "BLOCKED EXIT: finished order waits without duplication",
                TestDiagnosticKind.Rejected)
            .Highlight(
                new SimRect(new Vector2(900f, 340f), new Vector2(1080f, 470f)),
                "RALLY: spawned units receive system Move",
                TestDiagnosticKind.Accepted);
    }

    private static VisualTestSession CreateProductionReplayPersistence(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var catalog = DemoProductionCatalog.CreateSnapshot();
        var marine = catalog.Recipe(0) with { ProductionSeconds = 1f };
        var marauder = catalog.Recipe(1);
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 1000, 300, 10, 0);
        var builder = rig.SpawnWorker(new Vector2(230f, 260f), 1);
        rig.StartReplayPackageRecording();
        var fastBarracks = DemoBuildingTypes.Barracks with { BuildSeconds = 1f };
        var barracks = rig.Build(
            1, builder, fastBarracks, new Vector2(400f, 260f));
        var rally = new Vector2(650f, 400f);
        var blockers = Array.Empty<TestBuildingId>();
        var commandsIssued = false;
        var canceled = false;
        var issueDiagnostic = string.Empty;
        TestRuntimeStateCapture? producingCapture = null;
        TestRuntimeStateCapture? waitingCapture = null;
        TestRuntimeStateCapture? spawnedCapture = null;
        var checkpointHash = 0UL;
        const long producingTick = 270;
        const long checkpointTick = 300;
        const long waitingTick = 330;
        const long spawnedTick = 420;
        var session = new VisualTestSession(
            "production-replay-persistence",
            "Train/Cancel/Rally replay with producing and blocked-exit hot restore",
            540,
            rig,
            [builder],
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var log = runtime.CaptureProductionCommandLog();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var logRoundTrip = log.TryCanonicalRoundTrip(out var decodedLog);
                var baseline = runtime.ReplayPackage(decoded!, runtime.Tick);
                var checkpoint = runtime.CreateReplayCheckpoint(
                    package, checkpointTick, checkpointHash);
                var checkpointReplay = runtime.ResumeReplayPackage(
                    package, checkpoint, runtime.Tick);
                var producingHot = runtime.BindHotSnapshot(
                    package, producingCapture!);
                var producingRoundTrip = producingHot.TryCanonicalRoundTrip(
                    out var decodedProducing);
                var producingReplay = runtime.ResumeHotSnapshot(
                    package, decodedProducing!, runtime.Tick);
                var waitingHot = runtime.BindHotSnapshot(package, waitingCapture!);
                var waitingRoundTrip = waitingHot.TryCanonicalRoundTrip(
                    out var decodedWaiting);
                var waitingReplay = runtime.ResumeHotSnapshot(
                    package, decodedWaiting!, runtime.Tick);
                var spawnedHot = runtime.BindHotSnapshot(package, spawnedCapture!);
                var spawnedRoundTrip = spawnedHot.TryCanonicalRoundTrip(
                    out var decodedSpawned);
                var spawnedReplay = runtime.ResumeHotSnapshot(
                    package, decodedSpawned!, runtime.Tick);
                var checkpointExact = baseline.MatchesFrom(
                    checkpointReplay, checkpointTick);
                var producingExact = baseline.MatchesFrom(
                    producingReplay, producingTick);
                var waitingExact = baseline.MatchesFrom(
                    waitingReplay, waitingTick);
                var spawnedExact = baseline.MatchesFrom(
                    spawnedReplay, spawnedTick);
                var exact = baseline.FinalHash == runtime.StateHash &&
                            checkpointExact && producingExact && waitingExact &&
                            spawnedExact &&
                            checkpointReplay.FinalHash == runtime.StateHash &&
                            producingReplay.FinalHash == runtime.StateHash &&
                            waitingReplay.FinalHash == runtime.StateHash;
                exact &= spawnedReplay.FinalHash == runtime.StateHash;
                var rejected = log.RejectsUnsupportedVersion() &&
                               log.RejectsTruncatedPayload() &&
                               package.RejectsUnsupportedVersion() &&
                               package.RejectsTruncatedPayload() &&
                               producingHot.RejectsUnsupportedVersion() &&
                               producingHot.RejectsTruncatedPayload() &&
                               waitingHot.RejectsUnsupportedVersion() &&
                               waitingHot.RejectsTruncatedPayload() &&
                               spawnedHot.RejectsUnsupportedVersion() &&
                               spawnedHot.RejectsTruncatedPayload();
                var economy = runtime.ObservePlayerEconomy(1);
                var queue = runtime.ObserveProduction(barracks.BuildingId);
                var rallied = runtime.UnitCount >= 2 && Vector2.Distance(
                                  runtime.Observe(new TestUnitId(1)).Position,
                                  rally) < 45f;
                var passed = barracks.Succeeded && commandsIssued && canceled &&
                             packageRoundTrip && logRoundTrip &&
                             producingRoundTrip && waitingRoundTrip &&
                             spawnedRoundTrip &&
                             decodedLog!.StableHash == log.StableHash &&
                             package.FormatVersion == 11 &&
                             producingHot.FormatVersion == 11 &&
                             waitingHot.FormatVersion == 11 &&
                             package.ConstructionCommandCount == 1 &&
                             package.ProductionCommandCount == 4 &&
                             package.WorldCommandCount == 8 &&
                             package.UnitCommandCount == 0 &&
                             runtime.UnitCount == 2 && queue.OrderCount == 0 &&
                             economy.Minerals == 800 && economy.VespeneGas == 300 &&
                             economy.SupplyUsed == 1 && rallied && exact && rejected;
                return new ScenarioResult(
                    passed,
                    $"productionOrders={package.ProductionCommandCount}, " +
                    $"world={package.WorldCommandCount}, units={runtime.UnitCount}, " +
                    $"hot={producingHot.CanonicalByteCount}/" +
                    $"{waitingHot.CanonicalByteCount}/" +
                    $"{spawnedHot.CanonicalByteCount}B, exact={exact}, " +
                    $"hash={runtime.StateHash:X}/{baseline.FinalHash:X}/" +
                    $"{checkpointReplay.FinalHash:X}/{producingReplay.FinalHash:X}/" +
                    $"{waitingReplay.FinalHash:X}/{spawnedReplay.FinalHash:X}, " +
                    $"traces={checkpointExact}/{producingExact}/{waitingExact}/" +
                    $"{spawnedExact}, rejected={rejected}, " +
                    $"rallied={rallied}, issue={issueDiagnostic}");
            });
        session.At(240, "Record blocked exits, Rally, Train and Cancel", runtime =>
        {
            blockers =
            [
                runtime.PlaceBuilding(new Vector2(470f, 260f), new Vector2(20f, 130f)),
                runtime.PlaceBuilding(new Vector2(330f, 260f), new Vector2(20f, 130f)),
                runtime.PlaceBuilding(new Vector2(400f, 315f), new Vector2(120f, 20f)),
                runtime.PlaceBuilding(new Vector2(400f, 205f), new Vector2(120f, 20f))
            ];
            var rallySet = runtime.SetRallyPoint(1, barracks.BuildingId, rally);
            var first = runtime.Train(1, barracks.BuildingId, marine);
            var canceledOrder = runtime.Train(1, barracks.BuildingId, marauder);
            canceled = runtime.CancelProduction(1, canceledOrder.OrderId);
            issueDiagnostic =
                $"building={runtime.ObserveGameplayBuilding(barracks.BuildingId).State}, " +
                $"rally={rallySet}, train={first.Code}/{canceledOrder.Code}, " +
                $"cancel={canceled}";
            commandsIssued = rallySet && first.Succeeded &&
                             canceledOrder.Succeeded;
        });
        session.At(producingTick, "Capture active production hot state", runtime =>
            producingCapture = runtime.CaptureRuntimeState());
        session.At(checkpointTick, "Capture production checkpoint", runtime =>
            checkpointHash = runtime.StateHash);
        session.At(waitingTick, "Capture completed order blocked at exit", runtime =>
            waitingCapture = runtime.CaptureRuntimeState());
        session.At(360, "Record removal of all exit blockers", runtime =>
        {
            foreach (var blocker in blockers) runtime.RemoveBuilding(blocker);
        });
        session.At(spawnedTick, "Capture spawned unit population ledger", runtime =>
            spawnedCapture = runtime.CaptureRuntimeState());
        return session
            .Highlight(
                new SimRect(new Vector2(310f, 185f), new Vector2(490f, 335f)),
                "HOT STATE: producing -> waiting for exit",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(590f, 340f), new Vector2(710f, 470f)),
                "REPLAYED RALLY FUTURE STATE",
                TestDiagnosticKind.Accepted);
    }

    private static VisualTestSession CreateProductionCatalogResourceRuntime(
        ProductionCatalogSnapshot? catalog)
    {
        catalog ??= DemoProductionCatalog.CreateSnapshot();
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(1200f, 700f), 16);
        rig.RegisterPlayer(1, 2000, 500, 20, 0);
        var barracksBuilder = rig.SpawnWorker(new Vector2(400f, 180f), 1);
        var townHallBuilder = rig.SpawnWorker(new Vector2(820f, 180f), 1);
        var barracks = rig.Build(
            1, barracksBuilder, DemoBuildingTypes.Barracks,
            new Vector2(520f, 180f));
        var townHall = rig.Build(
            1, townHallBuilder, DemoBuildingTypes.CommandCenter,
            new Vector2(950f, 180f));
        var issued = false;
        var session = new VisualTestSession(
            "production-catalog-resource-runtime",
            "Loaded Unit/Recipe Resource drives combat and worker production",
            1320,
            rig,
            [barracksBuilder, townHallBuilder],
            runtime =>
            {
                var economy = runtime.ObservePlayerEconomy(1);
                var unitTypes = runtime.UnitCount == 5 &&
                    runtime.ObserveCombat(new TestUnitId(2)).MaximumHealth ==
                        catalog.UnitType(0).Combat.MaximumHealth &&
                    runtime.ObserveCombat(new TestUnitId(4)).MaximumHealth ==
                        catalog.UnitType(1).Combat.MaximumHealth;
                var worker = runtime.UnitCount == 5 &&
                    runtime.ObserveWorkerEconomy(new TestUnitId(3)).State ==
                        TestWorkerEconomyState.Idle;
                var passed = barracks.Succeeded && townHall.Succeeded && issued &&
                             catalog.StableHash ==
                                 DemoProductionCatalog.CreateSnapshot().StableHash &&
                             unitTypes && worker && economy.Minerals == 1250 &&
                             economy.VespeneGas == 475 && economy.SupplyUsed == 4;
                return new ScenarioResult(
                    passed,
                    $"catalog=f{catalog.FormatVersion}/{catalog.StableHashText}, " +
                    $"units={runtime.UnitCount}, profiles={unitTypes}, " +
                    $"worker={worker}, resources={economy.Minerals}/" +
                    $"{economy.VespeneGas}, supply={economy.SupplyUsed}/" +
                    $"{economy.SupplyCapacity}");
            });
        session.At(720, "Train every loaded recipe from matching producers", runtime =>
        {
            runtime.SetRallyPoint(1, barracks.BuildingId, new Vector2(700f, 400f));
            runtime.SetRallyPoint(1, townHall.BuildingId, new Vector2(1050f, 400f));
            issued = runtime.Train(1, barracks.BuildingId, catalog.Recipe(0)).Succeeded &&
                     runtime.Train(1, barracks.BuildingId, catalog.Recipe(1)).Succeeded &&
                     runtime.Train(1, townHall.BuildingId, catalog.Recipe(2)).Succeeded;
        });
        return session
            .Highlight(
                new SimRect(new Vector2(450f, 120f), new Vector2(1050f, 260f)),
                "RESOURCE RECIPES: Barracks + Command Center",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(650f, 330f), new Vector2(1100f, 460f)),
                "RESOURCE UNIT TYPES: combat + worker semantics",
                TestDiagnosticKind.Info);
    }

    private static VisualTestSession CreateProductionRallySmartTargets(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var catalog = DemoProductionCatalog.CreateSnapshot();
        var workerRecipe = catalog.Recipe(2) with { ProductionSeconds = 1f };
        var marineRecipe = catalog.Recipe(0) with { ProductionSeconds = 1f };
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 2000, 500, 20, 0);
        var minerals = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(1050f, 570f), 2000, 5, 0.35f, 2);
        rig.AddResourceDropOff(1, new Vector2(850f, 570f));
        var townHallBuilder = rig.SpawnWorker(new Vector2(100f, 500f), 1);
        var barracksBuilder = rig.SpawnWorker(new Vector2(230f, 260f), 1);
        var leader = rig.SpawnCombat(new Vector2(700f, 300f), 1);
        rig.StartReplayPackageRecording();
        var fastTownHall = DemoBuildingTypes.CommandCenter with { BuildSeconds = 1f };
        var fastBarracks = DemoBuildingTypes.Barracks with { BuildSeconds = 1f };
        var townHall = rig.Build(
            1, townHallBuilder, fastTownHall, new Vector2(250f, 500f));
        var barracks = rig.Build(
            1, barracksBuilder, fastBarracks, new Vector2(400f, 260f));
        var issued = false;
        var issueDetails = string.Empty;
        var friendlyProtocolObserved = false;
        var groundSet = false;
        TestRuntimeStateCapture? activeCapture = null;
        const long hotTick = 780;
        var session = new VisualTestSession(
            "production-rally-smart-targets",
            "Ground/resource/friendly Rally targets survive replay and hot restore",
            1320,
            rig,
            [townHallBuilder, barracksBuilder, leader],
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var log = runtime.CaptureProductionCommandLog();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var logRoundTrip = log.TryCanonicalRoundTrip(out var decodedLog);
                var hot = runtime.BindHotSnapshot(package, activeCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var baseline = runtime.ReplayPackage(decoded!, runtime.Tick);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var exact = baseline.FinalHash == runtime.StateHash &&
                            resumed.FinalHash == runtime.StateHash &&
                            baseline.MatchesFrom(resumed, hotTick);
                var produced = runtime.UnitCount == 5;
                var worker = produced
                    ? runtime.ObserveWorkerEconomy(new TestUnitId(3))
                    : default;
                var resourceRally = runtime.ObserveProductionRally(
                    townHall.BuildingId);
                var friendlyRally = runtime.ObserveProductionRally(
                    barracks.BuildingId);
                var marine = produced
                    ? runtime.Observe(new TestUnitId(4))
                    : default;
                var gatherStarted = worker.State is not
                    TestWorkerEconomyState.None and not TestWorkerEconomyState.Idle &&
                    worker.TargetNode == minerals;
                var resolvedFriendlyTarget = Vector2.Distance(
                    marine.AssignedTarget, new Vector2(700f, 300f)) < 0.1f;
                var protocol = resourceRally.Kind ==
                                   TestRallyTargetKind.ResourceNode &&
                               resourceRally.ResourceNode == minerals &&
                               friendlyRally.Kind ==
                                   TestRallyTargetKind.Ground &&
                               friendlyProtocolObserved && groundSet;
                var rejected = log.RejectsUnsupportedVersion() &&
                               log.RejectsTruncatedPayload() &&
                               package.RejectsUnsupportedVersion() &&
                               package.RejectsTruncatedPayload() &&
                               hot.RejectsUnsupportedVersion() &&
                               hot.RejectsTruncatedPayload();
                var passed = townHall.Succeeded && barracks.Succeeded && issued &&
                             produced && protocol && gatherStarted &&
                             resolvedFriendlyTarget &&
                             packageRoundTrip && logRoundTrip && hotRoundTrip &&
                             decodedLog!.StableHash == log.StableHash &&
                             package.FormatVersion == 11 && hot.FormatVersion == 11 &&
                             package.ProductionCommandCount == 5 &&
                             exact && rejected;
                return new ScenarioResult(
                    passed,
                    $"targets={resourceRally.Kind}/{friendlyRally.Kind}, " +
                    $"gather={worker.State}@node{worker.TargetNode.Value}, " +
                    $"remaining={runtime.ObserveResourceNode(minerals).Remaining}, " +
                    $"marine={marine.Position}->{marine.AssignedTarget}, " +
                    $"protocol={protocol}, " +
                    $"persistence={packageRoundTrip}/{hotRoundTrip}/{exact}, " +
                    $"versions=log4/package{package.FormatVersion}/hot{hot.FormatVersion}, " +
                    $"buildings={runtime.ObserveGameplayBuilding(townHall.BuildingId).State}/" +
                    $"{runtime.ObserveGameplayBuilding(barracks.BuildingId).State}, " +
                    $"issued={issued}({issueDetails}), ids={townHall.BuildingId.Value}/" +
                    $"{barracks.BuildingId.Value}, ok={townHall.Succeeded}/" +
                    $"{barracks.Succeeded}, units={runtime.UnitCount}");
            });
        session.At(720, "Bind resource and friendly-unit Rally targets", runtime =>
        {
            var resourceSet = runtime.SetRallyResource(
                1, townHall.BuildingId, minerals);
            var friendlySet = runtime.SetRallyFriendlyUnit(
                1, barracks.BuildingId, leader);
            runtime.Move([leader], new Vector2(900f, 350f));
            var worker = runtime.Train(1, townHall.BuildingId, workerRecipe);
            var marine = runtime.Train(1, barracks.BuildingId, marineRecipe);
            issued = resourceSet && friendlySet && worker.Succeeded && marine.Succeeded;
            issueDetails = $"{resourceSet}/{friendlySet}/{worker.Code}/{marine.Code}";
        });
        session.At(hotTick, "Capture entity-target Rally while both orders produce",
            runtime => activeCapture = runtime.CaptureRuntimeState());
        session.At(1000, "Replace friendly-unit Rally with a ground target", runtime =>
        {
            var friendly = runtime.ObserveProductionRally(barracks.BuildingId);
            friendlyProtocolObserved = friendly.Kind ==
                                       TestRallyTargetKind.FriendlyUnit &&
                                       friendly.Unit == leader;
            groundSet = runtime.SetRallyPoint(
                1, barracks.BuildingId, new Vector2(800f, 500f));
        });
        return session
            .Highlight(
                new SimRect(new Vector2(170f, 190f), new Vector2(500f, 590f)),
                "SMART RALLY: typed targets live in production state",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(730f, 300f), new Vector2(1120f, 620f)),
                "RESOURCE -> Gather / FRIENDLY -> spawn-time position",
                TestDiagnosticKind.Accepted);
    }

    private static VisualTestSession CreateProductionBuildingPrerequisites(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var baseRecipe = DemoProductionCatalog.CreateSnapshot().Recipe(1);
        var advancedRecipe = baseRecipe with
        {
            Name = "Train Advanced Marauder",
            Cost = new EconomyCost(125, 50, 2),
            ProductionSeconds = 1f,
            Requirements =
            [
                new ProductionRequirementProfile(
                    ProductionRequirementKind.CompletedBuilding,
                    DemoBuildingTypes.Barracks.Id, 2),
                new ProductionRequirementProfile(
                    ProductionRequirementKind.CompletedBuilding,
                    DemoBuildingTypes.CommandCenter.Id, 1)
            ]
        };
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 3000, 500, 20, 0);
        var barracksBuilder = rig.SpawnWorker(new Vector2(230f, 260f), 1);
        var townHallBuilder = rig.SpawnWorker(new Vector2(100f, 500f), 1);
        var expansionBuilder = rig.SpawnWorker(new Vector2(850f, 500f), 1);
        rig.StartReplayPackageRecording();
        var fastBarracks = DemoBuildingTypes.Barracks with { BuildSeconds = 1f };
        var fastTownHall = DemoBuildingTypes.CommandCenter with { BuildSeconds = 1f };
        var firstBarracks = rig.Build(
            1, barracksBuilder, fastBarracks, new Vector2(400f, 260f));
        var townHall = rig.Build(
            1, townHallBuilder, fastTownHall, new Vector2(250f, 500f));
        TestConstructionResult secondBarracks = default;
        var missingRejected = false;
        var firstAvailability = default(TestProductionAvailabilitySnapshot);
        var unlockedAvailability = default(TestProductionAvailabilitySnapshot);
        var trained = false;
        TestRuntimeStateCapture? activeCapture = null;
        const long hotTick = 1080;
        var session = new VisualTestSession(
            "production-building-prerequisites",
            "Multi-building prerequisites gate production and persist exactly",
            1320,
            rig,
            [barracksBuilder, townHallBuilder, expansionBuilder],
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var log = runtime.CaptureProductionCommandLog();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var logRoundTrip = log.TryCanonicalRoundTrip(out var decodedLog);
                var hot = runtime.BindHotSnapshot(package, activeCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var baseline = runtime.ReplayPackage(decoded!, runtime.Tick);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var exact = baseline.FinalHash == runtime.StateHash &&
                            resumed.FinalHash == runtime.StateHash &&
                            baseline.MatchesFrom(resumed, hotTick);
                var requirements = unlockedAvailability.Requirements;
                var countsExact = requirements.Length == 2 &&
                    requirements[0].BuildingTypeId == DemoBuildingTypes.Barracks.Id &&
                    requirements[0].CurrentCount == 2 &&
                    requirements[0].RequiredCount == 2 &&
                    requirements[1].BuildingTypeId ==
                        DemoBuildingTypes.CommandCenter.Id &&
                    requirements[1].CurrentCount == 1 &&
                    requirements.All(value => value.Satisfied);
                var economy = runtime.ObservePlayerEconomy(1);
                var rejected = log.RejectsUnsupportedVersion() &&
                               log.RejectsTruncatedPayload() &&
                               package.RejectsUnsupportedVersion() &&
                               package.RejectsTruncatedPayload() &&
                               hot.RejectsUnsupportedVersion() &&
                               hot.RejectsTruncatedPayload();
                var passed = firstBarracks.Succeeded && townHall.Succeeded &&
                             secondBarracks.Succeeded && missingRejected &&
                             firstAvailability.Code ==
                                 TestProductionCommandCode.MissingPrerequisite &&
                             unlockedAvailability.Available && countsExact && trained &&
                             runtime.UnitCount == 4 && economy.Minerals == 2175 &&
                             economy.VespeneGas == 450 && economy.SupplyUsed == 2 &&
                             packageRoundTrip && logRoundTrip && hotRoundTrip &&
                             decodedLog!.StableHash == log.StableHash &&
                             package.FormatVersion == 11 && hot.FormatVersion == 11 &&
                             package.ConstructionCommandCount == 3 &&
                             package.ProductionCommandCount == 1 && exact && rejected;
                return new ScenarioResult(
                    passed,
                    $"gate={firstAvailability.Code}->{unlockedAvailability.Code}, " +
                    $"counts={string.Join('/', requirements.Select(value =>
                        $"{value.CurrentCount}/{value.RequiredCount}"))}, " +
                    $"build={firstBarracks.Succeeded}/{townHall.Succeeded}/" +
                    $"{secondBarracks.Succeeded}, train={trained}, " +
                    $"resources={economy.Minerals}/{economy.VespeneGas}, " +
                    $"persistence={packageRoundTrip}/{hotRoundTrip}/{exact}, " +
                    $"versions=log4/package{package.FormatVersion}/hot{hot.FormatVersion}");
            });
        session.At(650, "Reject advanced unit while one Barracks is missing", runtime =>
        {
            firstAvailability = runtime.ObserveProductionAvailability(
                1, firstBarracks.BuildingId, advancedRecipe);
            missingRejected = runtime.Train(
                1, firstBarracks.BuildingId, advancedRecipe).Code ==
                TestProductionCommandCode.MissingPrerequisite;
        });
        session.At(700, "Construct the second required Barracks", runtime =>
            secondBarracks = runtime.Build(
                1, expansionBuilder, fastBarracks, new Vector2(800f, 150f)));
        session.At(1050, "Availability snapshot unlocks and accepts production", runtime =>
        {
            unlockedAvailability = runtime.ObserveProductionAvailability(
                1, firstBarracks.BuildingId, advancedRecipe);
            trained = runtime.Train(
                1, firstBarracks.BuildingId, advancedRecipe).Succeeded;
        });
        session.At(hotTick, "Capture active advanced production with prerequisites",
            runtime => activeCapture = runtime.CaptureRuntimeState());
        return session
            .Highlight(
                new SimRect(new Vector2(320f, 80f), new Vector2(880f, 590f)),
                "TECH GRAPH: 2 Barracks + 1 Command Center",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(320f, 190f), new Vector2(480f, 340f)),
                "PRODUCTION AVAILABILITY: locked -> available",
                TestDiagnosticKind.Accepted);
    }

    private static VisualTestSession CreateTechnologyResearchUpgrades(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake,
        TechnologyCatalogSnapshot? technologyCatalog)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        technologyCatalog ??= DemoTechnologies.CreateCatalog();
        var weapon = technologyCatalog.Technology(0) with
        {
            ResearchSeconds = 1f,
            MaximumLevel = 2
        };
        var assault = technologyCatalog.Technology(1) with
        {
            ResearchSeconds = 1f
        };
        var fortification = technologyCatalog.Technology(2) with
        {
            ResearchSeconds = 1f
        };
        var rig = MovementTestRig.CreateReplayPackageMap(
            24, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 3000, 2000, 20, 0);
        var builder = rig.SpawnWorker(new Vector2(230f, 260f), 1);
        rig.StartReplayPackageRecording();
        var academy = rig.Build(
            1, builder,
            DemoBuildingTypes.Academy with { BuildSeconds = 1f },
            new Vector2(400f, 260f));
        var prerequisiteRejected = false;
        var duplicateRejected = false;
        var cancelRefunded = false;
        var queuedConflictRejected = false;
        var completedConflictRejected = false;
        var maximumRejected = false;
        var commandsIssued = false;
        TestRuntimeStateCapture? activeCapture = null;
        const long hotTick = 750;
        var session = new VisualTestSession(
            "technology-research-upgrades",
            "Research levels, cancellation, prerequisites and exclusivity persist",
            1320,
            rig,
            [builder],
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var log = runtime.CaptureProductionCommandLog();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var logRoundTrip = log.TryCanonicalRoundTrip(out var decodedLog);
                var hot = runtime.BindHotSnapshot(package, activeCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var baseline = runtime.ReplayPackage(decoded!, runtime.Tick);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var exact = baseline.FinalHash == runtime.StateHash &&
                            resumed.FinalHash == runtime.StateHash &&
                            baseline.MatchesFrom(resumed, hotTick);
                var economy = runtime.ObservePlayerEconomy(1);
                var assaultAvailability = runtime.ObserveResearchAvailability(
                    1, academy.BuildingId, assault);
                var rejected = log.RejectsUnsupportedVersion() &&
                               log.RejectsTruncatedPayload() &&
                               package.RejectsUnsupportedVersion() &&
                               package.RejectsTruncatedPayload() &&
                               hot.RejectsUnsupportedVersion() &&
                               hot.RejectsTruncatedPayload();
                var passed = academy.Succeeded && prerequisiteRejected &&
                             duplicateRejected && cancelRefunded &&
                             queuedConflictRejected && completedConflictRejected &&
                             maximumRejected && commandsIssued &&
                             runtime.TechnologyLevel(1, weapon.Id) == 2 &&
                             runtime.TechnologyLevel(1, assault.Id) == 1 &&
                             runtime.TechnologyLevel(1, fortification.Id) == 0 &&
                             runtime.ResearchQueueCount(academy.BuildingId) == 0 &&
                             assaultAvailability.Code ==
                                 TestResearchCommandCode.MaximumLevel &&
                             economy.Minerals == 2475 &&
                             economy.VespeneGas == 1575 &&
                             packageRoundTrip && logRoundTrip && hotRoundTrip &&
                             decodedLog!.StableHash == log.StableHash &&
                             package.FormatVersion == 11 && hot.FormatVersion == 11 &&
                             package.ConstructionCommandCount == 1 &&
                             package.ProductionCommandCount == 5 &&
                             technologyCatalog.StableHash ==
                                 DemoTechnologies.CreateCatalog().StableHash &&
                             exact && rejected;
                return new ScenarioResult(
                    passed,
                    $"catalog=f{technologyCatalog.FormatVersion}/" +
                    $"{technologyCatalog.StableHashText}, " +
                    $"levels=weapon{runtime.TechnologyLevel(1, weapon.Id)}/" +
                    $"assault{runtime.TechnologyLevel(1, assault.Id)}/" +
                    $"fort{runtime.TechnologyLevel(1, fortification.Id)}, " +
                    $"rules={prerequisiteRejected}/{duplicateRejected}/" +
                    $"{queuedConflictRejected}/{completedConflictRejected}/" +
                    $"{maximumRejected}, cancel={cancelRefunded}, " +
                    $"resources={economy.Minerals}/{economy.VespeneGas}, " +
                    $"persistence={packageRoundTrip}/{hotRoundTrip}/{exact}, " +
                    $"versions=log4/package{package.FormatVersion}/hot{hot.FormatVersion}");
            });
        session.At(700, "Reject doctrine, cancel first weapon order, then requeue", runtime =>
        {
            prerequisiteRejected = runtime.Research(
                1, academy.BuildingId, assault).Code ==
                TestResearchCommandCode.MissingPrerequisite;
            var first = runtime.Research(1, academy.BuildingId, weapon);
            duplicateRejected = runtime.Research(
                1, academy.BuildingId, weapon).Code ==
                TestResearchCommandCode.AlreadyQueued;
            cancelRefunded = first.Succeeded &&
                             runtime.CancelResearch(1, first.OrderId);
            var replacement = runtime.Research(1, academy.BuildingId, weapon);
            commandsIssued = replacement.Succeeded;
        });
        session.At(hotTick, "Capture active level-one research",
            runtime => activeCapture = runtime.CaptureRuntimeState());
        session.At(820, "Queue Assault Doctrine and reject its exclusive sibling", runtime =>
        {
            var assaultOrder = runtime.Research(1, academy.BuildingId, assault);
            queuedConflictRejected = runtime.Research(
                1, academy.BuildingId, fortification).Code ==
                TestResearchCommandCode.MutuallyExclusive;
            commandsIssued &= assaultOrder.Succeeded;
        });
        session.At(900, "Reject exclusive doctrine after Assault completes", runtime =>
            completedConflictRejected = runtime.Research(
                1, academy.BuildingId, fortification).Code ==
                TestResearchCommandCode.MutuallyExclusive);
        session.At(940, "Research Infantry Weapons level two", runtime =>
            commandsIssued &= runtime.Research(
                1, academy.BuildingId, weapon).Succeeded);
        session.At(1040, "Reject research beyond configured maximum level", runtime =>
            maximumRejected = runtime.Research(
                1, academy.BuildingId, weapon).Code ==
                TestResearchCommandCode.MaximumLevel);
        return session
            .Highlight(
                new SimRect(new Vector2(330f, 210f), new Vector2(470f, 320f)),
                "ACADEMY: formal research queue",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(500f, 180f), new Vector2(900f, 400f)),
                "LEVELS + PREREQUISITES + EXCLUSIVE DOCTRINES",
                TestDiagnosticKind.Accepted);
    }

    private static VisualTestSession CreateConstructionReplayPersistence(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            24, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 1500, 300, 15, 6);
        var gas = rig.AddResourceNode(
            TestEconomyResourceKind.VespeneGas,
            new Vector2(1050f, 570f), 1000, 4, 0.5f, 3,
            requiresRefinery: true, operational: false);
        var workers = new[]
        {
            rig.SpawnWorker(new Vector2(120f, 150f), 1),
            rig.SpawnWorker(new Vector2(230f, 260f), 1),
            rig.SpawnWorker(new Vector2(100f, 500f), 1),
            rig.SpawnWorker(new Vector2(930f, 570f), 1),
            rig.SpawnWorker(new Vector2(360f, 500f), 1)
        };
        rig.StartReplayPackageRecording();
        var supply = rig.Build(
            1, workers[0], DemoBuildingTypes.SupplyDepot,
            new Vector2(260f, 150f));
        var barracks = rig.Build(
            1, workers[1], DemoBuildingTypes.Barracks,
            new Vector2(400f, 260f));
        var commandCenter = rig.Build(
            1, workers[2], DemoBuildingTypes.CommandCenter,
            new Vector2(250f, 500f));
        var refinery = rig.Build(
            1, workers[3], DemoBuildingTypes.Refinery,
            Vector2.Zero, gas);
        var canceled = rig.Build(
            1, workers[4], DemoBuildingTypes.SupplyDepot,
            new Vector2(440f, 500f));
        var issued = new[]
        {
            supply, barracks, commandCenter, refinery, canceled
        }.All(value => value.Succeeded);

        const long hotTick = 300;
        const long checkpointTick = 360;
        TestRuntimeStateCapture? runtimeCapture = null;
        var checkpointHash = 0UL;
        var canceledSuccessfully = false;
        var resumed = false;
        var session = new VisualTestSession(
            "construction-replay-persistence",
            "Build, cancel, resume, checkpoint and active-construction hot restore",
            900,
            rig,
            workers,
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var log = runtime.CaptureConstructionCommandLog();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var logRoundTrip = log.TryCanonicalRoundTrip(out var decodedLog);
                var baseline = runtime.ReplayPackage(decoded!, runtime.Tick);
                var checkpoint = runtime.CreateReplayCheckpoint(
                    package, checkpointTick, checkpointHash);
                var checkpointReplay = runtime.ResumeReplayPackage(
                    package, checkpoint, runtime.Tick);
                var hot = runtime.BindHotSnapshot(package, runtimeCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var hotReplay = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var exact = baseline.FinalHash == runtime.StateHash &&
                            baseline.MatchesFrom(checkpointReplay, checkpointTick) &&
                            baseline.MatchesFrom(hotReplay, hotTick) &&
                            checkpointReplay.FinalHash == runtime.StateHash &&
                            hotReplay.FinalHash == runtime.StateHash;
                var rejected = log.RejectsUnsupportedVersion() &&
                               log.RejectsTruncatedPayload() &&
                               package.RejectsUnsupportedVersion() &&
                               package.RejectsTruncatedPayload() &&
                               hot.RejectsUnsupportedVersion() &&
                               hot.RejectsTruncatedPayload();
                var economy = runtime.ObservePlayerEconomy(1);
                var completed = new[]
                {
                    supply.BuildingId, barracks.BuildingId,
                    commandCenter.BuildingId, refinery.BuildingId
                }.All(id => runtime.ObserveGameplayBuilding(id).State ==
                            TestBuildingLifecycleState.Completed);
                var passed = issued && canceledSuccessfully && resumed &&
                             packageRoundTrip && logRoundTrip && hotRoundTrip &&
                             decodedLog!.StableHash == log.StableHash &&
                             package.FormatVersion == 11 && hot.FormatVersion == 11 &&
                             package.ConstructionCommandCount == 7 &&
                             package.WorldCommandCount == 0 &&
                             package.UnitCommandCount == 1 &&
                             completed && economy.Minerals == 750 &&
                             economy.SupplyCapacity == 38 && exact && rejected;
                return new ScenarioResult(
                    passed,
                    $"buildOrders={package.ConstructionCommandCount}, " +
                    $"derivedWorld={package.WorldCommandCount}, unitOrders={package.UnitCommandCount}, " +
                    $"resources={economy.Minerals}/{economy.VespeneGas}, " +
                    $"hot={hot.CanonicalByteCount}B@{hot.Tick}, " +
                    $"exact={exact}, rejected={rejected}");
            });
        session.At(120, "Cancel unfinished building", runtime =>
            canceledSuccessfully = runtime.CancelConstruction(
                1, canceled.BuildingId));
        session.At(180, "Interrupt Barracks builder with player Move", runtime =>
            runtime.Move([workers[1]], new Vector2(240f, 350f)));
        session.At(240, "Record Resume construction command", runtime =>
            resumed = runtime.ResumeConstruction(
                1, barracks.BuildingId, workers[1]));
        session.At(hotTick, "Capture active construction hot snapshot", runtime =>
            runtimeCapture = runtime.CaptureRuntimeState());
        session.At(checkpointTick, "Capture construction checkpoint hash", runtime =>
            checkpointHash = runtime.StateHash);
        return session
            .Highlight(
                new SimRect(new Vector2(150f, 100f), new Vector2(490f, 570f)),
                "CONSTRUCTION FUTURE STATE: replay/checkpoint/hot snapshot",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(1000f, 520f), new Vector2(1100f, 620f)),
                "REFINERY BINDING PERSISTS",
                TestDiagnosticKind.Accepted);
    }

    private static VisualTestSession CreateCombatAttackBuilding(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 0, 0, 20, 8);
        rig.RegisterPlayer(2, 500, 0, 10, 1);
        var builder = rig.SpawnWorker(new Vector2(520f, 350f), 2);
        var attackers = new TestUnitId[8];
        var attackerProfile = new TestCombatProfile(
            MaximumHealth: 100f,
            AttackDamage: 25f,
            AttackRange: 48f,
            AcquisitionRange: 220f,
            AttackCooldownSeconds: 0.45f,
            AttackWindupSeconds: 0.08f,
            LeashDistance: 500f);
        for (var index = 0; index < attackers.Length; index++)
        {
            attackers[index] = rig.SpawnCombat(
                new Vector2(100f + index % 4 * 24f, 310f + index / 4 * 28f),
                1,
                attackerProfile,
                maximumSpeed: 150f);
        }
        var targetType = new BuildingTypeProfile(
            20,
            "Fortified Command Structure",
            BuildingFunctionKind.Supply,
            new Vector2(160f, 120f),
            MovementClass.Large,
            new EconomyCost(400, 0),
            1.5f,
            2000f,
            8,
            0.75f,
            ConstructionMethodKind.ContinuousWorker);
        rig.StartReplayPackageRecording();
        var target = rig.Build(
            2, builder, targetType, new Vector2(400f, 350f));

        const long attackTick = 120;
        const long hotTick = 300;
        TestRuntimeStateCapture? hotCapture = null;
        var attackIssued = false;
        var targetCompletedBeforeAttack = false;
        var session = new VisualTestSession(
            "combat-attack-building",
            "Explicit building target chase, perimeter attack, destruction and replay",
            720,
            rig,
            attackers.Append(builder).ToArray(),
            runtime =>
            {
                var building = runtime.ObserveGameplayBuilding(target.BuildingId);
                var economy = runtime.ObservePlayerEconomy(2);
                var package = runtime.CaptureReplayPackage();
                var replay = runtime.ReplayPackage(package, runtime.Tick);
                var hot = runtime.BindHotSnapshot(package, hotCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var exact = replay.FinalHash == runtime.StateHash &&
                            replay.MatchesFrom(resumed, hotTick) &&
                            resumed.FinalHash == runtime.StateHash;
                var passed = target.Succeeded && attackIssued &&
                             targetCompletedBeforeAttack &&
                             building.State == TestBuildingLifecycleState.Destroyed &&
                             building.Health == 0f && runtime.GameplayBuildingCount == 0 &&
                             runtime.BuildingCount == 0 &&
                             economy.SupplyCapacity == 10 &&
                             package.ConstructionCommandCount == 1 &&
                             package.UnitCommandCount == 1 &&
                             hotRoundTrip && hot.FormatVersion == 11 && exact;
                return new ScenarioResult(
                    passed,
                    $"state={building.State}, hp={building.Health:0}, " +
                    $"footprints={runtime.BuildingCount}, supply={economy.SupplyCapacity}, " +
                    $"orders={package.UnitCommandCount}, hot={hot.CanonicalByteCount}B, " +
                    $"exact={exact}");
            });
        session.At(attackTick, "Attack completed enemy building", runtime =>
        {
            targetCompletedBeforeAttack =
                runtime.ObserveGameplayBuilding(target.BuildingId).State ==
                TestBuildingLifecycleState.Completed;
            runtime.SmartCommandBuilding(attackers, target.BuildingId);
            attackIssued = true;
        });
        session.At(hotTick, "Capture active building engagement", runtime =>
            hotCapture = runtime.CaptureRuntimeState());
        return session.Highlight(
            new SimRect(new Vector2(305f, 275f), new Vector2(495f, 425f)),
            "BUILDING ATTACK TARGET: chase perimeter, damage, destroy",
            TestDiagnosticKind.Rejected);
    }

    private static VisualTestSession CreateStopCommand()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 32);
        var units = rig.SpawnGrid(new Vector2(100f, 270f), 4, 4, 20f);
        rig.Move(units, new Vector2(1080f, 350f));
        var session = new VisualTestSession(
            "stop-command",
            "Stop while moving",
            420,
            rig,
            units,
            runtime => EvaluateStopped(runtime, units, TestUnitState.Idle));
        return session.At(180, "Stop", runtime => runtime.Stop(units));
    }

    private static VisualTestSession CreateSharedTargetReservations()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 64);
        var firstWave = rig.SpawnGrid(new Vector2(80f, 160f), 4, 4, 20f);
        var secondWave = rig.SpawnGrid(new Vector2(80f, 460f), 4, 4, 20f);
        var all = firstWave.Concat(secondWave).ToArray();
        var sharedTarget = new Vector2(1000f, 350f);
        rig.Move(firstWave, sharedTarget);
        var session = new VisualTestSession(
            "shared-target-reservations",
            "Separate destination slots across command waves",
            1320,
            rig,
            all,
            runtime =>
            {
                var arrival = EvaluateArrival(runtime, all, 29, 20f, 3f, 0, 0);
                var snapshots = runtime.Observe(all);
                var minimumClearance = MinimumAssignedTargetClearance(snapshots);
                var reservationsAreUnique = minimumClearance >= 1.5f;
                return new ScenarioResult(
                    arrival.Passed && reservationsAreUnique,
                    $"targetClearance={minimumClearance:0.00}, " +
                    $"unique={reservationsAreUnique}, {arrival.Summary}");
            });
        return session.At(
            180,
            "Second command to same target",
            runtime => runtime.Move(secondWave, sharedTarget));
    }

    private static VisualTestSession CreateHoldCommand()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 32);
        var units = rig.SpawnGrid(new Vector2(100f, 360f), 4, 4, 20f);
        rig.Move(units, new Vector2(1080f, 320f));
        var session = new VisualTestSession(
            "hold-command",
            "Hold position while moving",
            420,
            rig,
            units,
            runtime => EvaluateStopped(runtime, units, TestUnitState.Holding));
        return session.At(180, "Hold position", runtime => runtime.Hold(units));
    }

    private static VisualTestSession CreateMixedRadii()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 64);
        var units = new TestUnitId[30];
        for (var row = 0; row < 5; row++)
        {
            for (var column = 0; column < 6; column++)
            {
                var index = row * 6 + column;
                var radius = ((row + column) % 3) switch
                {
                    0 => 5.5f,
                    1 => 7.5f,
                    _ => 10f
                };
                units[index] = rig.Spawn(
                    new Vector2(90f + column * 24f, 230f + row * 24f), radius);
            }
        }

        rig.Move(units, new Vector2(1010f, 380f));
        return ArrivalScenario(
            "mixed-radii", "Mixed unit radii formation", 1020,
            rig, units, 27, 18f, 3.5f);
    }

    private static VisualTestSession CreateBoundaryTarget()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 48);
        var units = rig.SpawnGrid(new Vector2(160f, 180f), 4, 5, 20f);
        rig.Move(units, new Vector2(1500f, 850f));
        return ArrivalScenario(
            "boundary-target", "Target outside world boundary", 960,
            rig, units, 18, 16f, 3f);
    }

    private static VisualTestSession CreatePortalChoke(bool reverse)
    {
        var rig = MovementTestRig.CreateChokeMap(64);
        var units = reverse
            ? rig.SpawnGrid(new Vector2(1050f, 500f), 4, 6, 19f)
            : rig.SpawnGrid(new Vector2(120f, 225f), 4, 6, 19f);
        rig.Move(units, reverse ? new Vector2(150f, 350f) : new Vector2(1110f, 350f));
        return ArrivalScenario(
            reverse ? "reverse-choke" : "portal-choke",
            reverse
                ? "Reverse portal and choke traversal"
                : "Portal route and six-lane choke (24 units)",
            1500,
            rig,
            units,
            19,
            18f,
            3f);
    }

    private static VisualTestSession CreateDynamicBuildingDetour()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 48);
        var units = rig.SpawnGrid(new Vector2(90f, 270f), 4, 5, 20f);
        rig.Move(units, new Vector2(1080f, 350f));
        var session = ArrivalScenario(
            "dynamic-building-detour",
            "Place building across active routes",
            1200,
            rig,
            units,
            17,
            18f,
            3f,
            minimumNavigationRevision: 1,
            expectedBuildingCount: 1);
        return session.At(
            150,
            "Place blocking building",
            runtime => runtime.PlaceBuilding(
                new Vector2(600f, 350f),
                new Vector2(110f, 280f)));
    }

    private static VisualTestSession CreateDynamicLocalInvalidation()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 64);
        var upper = rig.SpawnGrid(new Vector2(80f, 130f), 4, 4, 20f);
        var lower = rig.SpawnGrid(new Vector2(80f, 470f), 4, 4, 20f);
        var all = upper.Concat(lower).ToArray();
        rig.Move(upper, new Vector2(1080f, 180f));
        rig.Move(lower, new Vector2(1080f, 520f));
        var observedInvalidations = -1;
        var session = new VisualTestSession(
            "dynamic-local-invalidation",
            "Invalidate only routes crossing a new building",
            1200,
            rig,
            all,
            runtime =>
            {
                var arrival = EvaluateArrival(
                    runtime, all, 28, 18f, 3f, 1, 1);
                var invalidationWasLocal = observedInvalidations > 0 &&
                                           observedInvalidations <= upper.Length;
                return new ScenarioResult(
                    arrival.Passed && invalidationWasLocal,
                    $"{arrival.Summary}, invalidated={observedInvalidations}/" +
                    $"{all.Length}, local={invalidationWasLocal}");
            });
        session.At(
            150,
            "Block upper route only",
            runtime => runtime.PlaceBuilding(
                new Vector2(600f, 180f),
                new Vector2(100f, 150f)));
        return session.At(
            151,
            "Observe local invalidation",
            runtime => observedInvalidations = runtime.LastNavigationInvalidations);
    }

    private static VisualTestSession CreateDynamicBuildingRemove()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 48);
        var wall = rig.PlaceBuilding(
            new Vector2(600f, 350f),
            new Vector2(80f, 700f));
        var units = rig.SpawnGrid(new Vector2(90f, 270f), 4, 5, 20f);
        rig.Move(units, new Vector2(1080f, 350f));
        var session = ArrivalScenario(
            "dynamic-building-remove",
            "Remove building and recover blocked commands",
            1200,
            rig,
            units,
            18,
            18f,
            3f,
            minimumNavigationRevision: 2,
            expectedBuildingCount: 0);
        return session.At(
            300,
            "Remove blocking building",
            runtime => runtime.RemoveBuilding(wall));
    }

    private static VisualTestSession CreateDynamicPortalReroute()
    {
        var rig = MovementTestRig.CreateChokeMap(64);
        var units = rig.SpawnGrid(new Vector2(120f, 225f), 4, 6, 19f);
        rig.Move(units, new Vector2(1110f, 350f));
        var session = ArrivalScenario(
            "dynamic-portal-reroute",
            "Close upper portal route while moving",
            1800,
            rig,
            units,
            18,
            20f,
            3f,
            minimumNavigationRevision: 1,
            expectedBuildingCount: 1);
        return session.At(
            240,
            "Close upper route",
            runtime => runtime.PlaceBuilding(
                new Vector2(920f, 220f),
                new Vector2(90f, 110f)));
    }

    private static VisualTestSession CreateDynamicGroupReroute()
    {
        var rig = MovementTestRig.CreateChokeMap(96);
        var units = rig.SpawnGrid(new Vector2(75f, 175f), 6, 8, 18.5f);
        rig.Move(units, new Vector2(1110f, 350f));
        var observedInvalidations = 0;
        var session = new VisualTestSession(
            "dynamic-group-reroute",
            "Large formation reroutes after its active portal closes",
            3000,
            rig,
            units,
            runtime =>
            {
                var arrival = EvaluateArrival(runtime, units, 40, 22f, 3f, 1, 1);
                var diagnostics = runtime.ObserveMovementDiagnostics();
                var activeRouteWasInvalidated = observedInvalidations == units.Length;
                return new ScenarioResult(
                    arrival.Passed && activeRouteWasInvalidated,
                    $"routePlans={diagnostics.GroupRoutePlans}, " +
                    $"sharedAssignments={diagnostics.SharedRouteAssignments}, " +
                    $"invalidated={observedInvalidations}, " +
                    $"activeRouteClosed={activeRouteWasInvalidated}, " +
                    arrival.Summary);
            });
        session.At(
            240,
            "Close the active lower portal",
            runtime => runtime.PlaceBuilding(
                new Vector2(920f, 475f),
                new Vector2(90f, 110f)));
        return session.At(
            241,
            "Observe group route invalidation",
            runtime => observedInvalidations = runtime.LastNavigationInvalidations);
    }

    private static VisualTestSession CreateNavigationResourceRuntime(
        NavigationMapSnapshot? navigationMap)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        var rig = MovementTestRig.CreateChokeMap(64, navigationMap);
        var units = rig.SpawnGrid(new Vector2(120f, 225f), 4, 6, 19f);
        rig.Move(units, new Vector2(1110f, 350f));
        return new VisualTestSession(
            "navigation-resource-runtime",
            "Runtime navigation snapshot loaded from Godot Resource",
            1500,
            rig,
            units,
            runtime =>
            {
                var arrival = EvaluateArrival(runtime, units, 19, 18f, 3f, 0, 0);
                var dataValid = runtime.NavigationFormatVersion ==
                                NavigationMapSnapshot.CurrentFormatVersion &&
                                runtime.NavigationDataHash != 0UL;
                return new ScenarioResult(
                    arrival.Passed && dataValid,
                    $"format={runtime.NavigationFormatVersion}, " +
                    $"hash={runtime.NavigationDataHash:X16}, dataValid={dataValid}, " +
                    arrival.Summary);
            });
    }

    private static VisualTestSession CreateLargeGroup()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 224);
        var units = rig.SpawnGrid(new Vector2(55f, 125f), 12, 16, 17.5f, 7f);
        rig.Move(units, new Vector2(1010f, 390f));
        return ArrivalScenario(
            "large-group-192", "Large group stress (192 units)", 1500,
            rig, units, 165, 22f, 3.5f);
    }

    private static VisualTestSession CreateBidirectionalChokeBalanced()
    {
        var rig = MovementTestRig.CreateChokeMap(96);
        var positive = rig.SpawnGrid(new Vector2(120f, 235f), 4, 4, 20f);
        var negative = rig.SpawnGrid(new Vector2(1060f, 485f), 4, 4, 20f);
        rig.Move(positive, new Vector2(1110f, 350f));
        rig.Move(negative, new Vector2(150f, 350f));
        return BidirectionalScenario(
            "bidirectional-choke-balanced",
            "Balanced bidirectional choke traffic (16 vs 16)",
            2700,
            rig,
            positive,
            negative,
            14,
            14);
    }

    private static VisualTestSession CreateBidirectionalChokeAsymmetric()
    {
        var rig = MovementTestRig.CreateChokeMap(112);
        var positive = rig.SpawnGrid(new Vector2(125f, 285f), 2, 4, 20f);
        var negative = rig.SpawnGrid(new Vector2(1035f, 455f), 4, 8, 19f);
        rig.Move(positive, new Vector2(1110f, 350f));
        rig.Move(negative, new Vector2(150f, 350f));
        return BidirectionalScenario(
            "bidirectional-choke-asymmetric",
            "Asymmetric bidirectional choke traffic (8 vs 32)",
            3300,
            rig,
            positive,
            negative,
            7,
            27);
    }

    private static VisualTestSession CreateBidirectionalChokeWaves()
    {
        var rig = MovementTestRig.CreateChokeMap(128);
        var positiveFirst = rig.SpawnGrid(new Vector2(120f, 205f), 3, 4, 20f);
        var negativeFirst = rig.SpawnGrid(new Vector2(1060f, 485f), 3, 4, 20f);
        var positiveSecond = rig.SpawnGrid(new Vector2(90f, 500f), 3, 4, 20f);
        var negativeSecond = rig.SpawnGrid(new Vector2(1090f, 130f), 3, 4, 20f);
        var positive = positiveFirst.Concat(positiveSecond).ToArray();
        var negative = negativeFirst.Concat(negativeSecond).ToArray();
        rig.Move(positiveFirst, new Vector2(1110f, 240f));
        rig.Move(negativeFirst, new Vector2(150f, 240f));
        var session = BidirectionalScenario(
            "bidirectional-choke-waves",
            "Staggered bidirectional choke waves",
            3900,
            rig,
            positive,
            negative,
            20,
            20);
        session.At(
            360,
            "Positive second wave",
            runtime => runtime.Move(positiveSecond, new Vector2(1110f, 520f)));
        return session.At(
            600,
            "Negative second wave",
            runtime => runtime.Move(negativeSecond, new Vector2(150f, 520f)));
    }

    private static VisualTestSession BidirectionalScenario(
        string id,
        string displayName,
        int durationTicks,
        MovementTestRig rig,
        TestUnitId[] positive,
        TestUnitId[] negative,
        int minimumPositiveArrivals,
        int minimumNegativeArrivals)
    {
        var all = positive.Concat(negative).ToArray();
        return new VisualTestSession(
            id,
            displayName,
            durationTicks,
            rig,
            all,
            runtime =>
            {
                var shared = EvaluateArrival(
                    runtime,
                    all,
                    minimumPositiveArrivals + minimumNegativeArrivals,
                    20f,
                    3f,
                    0,
                    0);
                var positiveArrivals = CountArrivals(runtime, positive, 20f);
                var negativeArrivals = CountArrivals(runtime, negative, 20f);
                var bothDirectionsPassed = positiveArrivals >= minimumPositiveArrivals &&
                                           negativeArrivals >= minimumNegativeArrivals;
                var traffic = runtime.ObserveChokeTraffic();
                var maximumWait = Math.Max(
                    traffic.MaximumPositiveWaitTicks,
                    traffic.MaximumNegativeWaitTicks);
                var trafficSafe = traffic.ConflictTicks == 0 &&
                                  maximumWait < durationTicks * 3 / 4;
                var recovery = runtime.ObserveRecovery(all);
                return new ScenarioResult(
                    shared.Passed && bothDirectionsPassed && trafficSafe,
                    $"positive={positiveArrivals}/{positive.Length}, " +
                    $"negative={negativeArrivals}/{negative.Length}, " +
                    $"both={bothDirectionsPassed}, conflicts={traffic.ConflictTicks}, " +
                    $"maxWait={maximumWait}, trafficSafe={trafficSafe}, " +
                    $"recovery={recovery.TotalEvents}/{recovery.UnreachableUnits}, " +
                    $"{shared.Summary}");
            });
    }

    private static VisualTestSession CreateHoldBlockedChoke()
    {
        var rig = MovementTestRig.CreateChokeMap(96);
        var blocker = rig.Spawn(new Vector2(630f, 353f), radius: 10f);
        rig.Hold(new[] { blocker });
        var positive = rig.SpawnGrid(new Vector2(120f, 235f), 3, 4, 20f);
        var negative = rig.SpawnGrid(new Vector2(1060f, 485f), 3, 4, 20f);
        var movingUnits = positive.Concat(negative).ToArray();
        var visibleUnits = movingUnits.Append(blocker).ToArray();
        rig.Move(positive, new Vector2(1110f, 300f));
        rig.Move(negative, new Vector2(150f, 300f));
        var session = new VisualTestSession(
            "hold-blocked-choke",
            "Hold unit blocks and releases choke",
            3000,
            rig,
            visibleUnits,
            runtime =>
            {
                var arrival = EvaluateArrival(
                    runtime, movingUnits, 20, 20f, 3f, 0, 0);
                var positiveArrivals = CountArrivals(runtime, positive, 20f);
                var negativeArrivals = CountArrivals(runtime, negative, 20f);
                var traffic = runtime.ObserveChokeTraffic();
                var blockerWasDetected = traffic.BlockedTicks >= 500;
                var safe = traffic.ConflictTicks == 0 && blockerWasDetected &&
                           positiveArrivals >= 10 && negativeArrivals >= 10;
                return new ScenarioResult(
                    arrival.Passed && safe,
                    $"positive={positiveArrivals}/{positive.Length}, " +
                    $"negative={negativeArrivals}/{negative.Length}, " +
                    $"blockedTicks={traffic.BlockedTicks}, " +
                    $"conflicts={traffic.ConflictTicks}, safe={safe}, {arrival.Summary}");
            });
        return session.At(
            600,
            "Release Hold blocker",
            runtime => runtime.Move(new[] { blocker }, new Vector2(800f, 353f)));
    }

    private static VisualTestSession CreateTemporaryBlockerRecovery()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 32);
        var mover = rig.Spawn(new Vector2(150f, 350f));
        var blockers = new TestUnitId[12];
        for (var index = 0; index < blockers.Length; index++)
        {
            var angle = index / (float)blockers.Length * MathF.Tau;
            blockers[index] = rig.Spawn(
                new Vector2(150f, 350f) +
                new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 35f,
                radius: 9.5f);
        }

        rig.Hold(blockers);
        var moverGroup = new[] { mover };
        rig.Move(moverGroup, new Vector2(1000f, 350f));
        var visible = blockers.Append(mover).ToArray();
        var session = new VisualTestSession(
            "temporary-blocker-recovery",
            "Recover after temporary unit enclosure",
            960,
            rig,
            visible,
            runtime =>
            {
                var arrival = EvaluateArrival(runtime, moverGroup, 1, 12f, 3f, 0, 0);
                var recovery = runtime.ObserveRecovery(moverGroup);
                var recovered = recovery.TotalEvents >= 1 &&
                                recovery.UnreachableUnits == 0;
                return new ScenarioResult(
                    arrival.Passed && recovered,
                    $"events={recovery.TotalEvents}, unreachable={recovery.UnreachableUnits}, " +
                    $"stage={recovery.MaximumStage}, recovered={recovered}, {arrival.Summary}");
            });
        return session.At(
            180,
            "Release enclosing Hold units",
            runtime => runtime.Move(blockers, new Vector2(180f, 100f)));
    }

    private static VisualTestSession CreateUnreachableRecoveryLimit()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 16);
        rig.PlaceBuilding(new Vector2(600f, 350f), new Vector2(80f, 700f));
        var units = rig.SpawnGrid(new Vector2(100f, 310f), 2, 2, 20f);
        rig.Move(units, new Vector2(1050f, 350f));
        return new VisualTestSession(
            "unreachable-recovery-limit",
            "Unreachable target stops retrying",
            720,
            rig,
            units,
            runtime =>
            {
                var snapshots = runtime.Observe(units);
                var recovery = runtime.ObserveRecovery(units);
                var idle = snapshots.Count(unit => unit.State == TestUnitState.Idle);
                var bounded = recovery.UnreachableUnits == units.Length &&
                              recovery.TotalEvents <= units.Length * 2 &&
                              recovery.PendingPathRequests == 0;
                return new ScenarioResult(
                    idle == units.Length && bounded,
                    $"idle={idle}/{units.Length}, events={recovery.TotalEvents}, " +
                    $"unreachable={recovery.UnreachableUnits}, " +
                    $"pending={recovery.PendingPathRequests}, bounded={bounded}");
            });
    }

    private static int CountArrivals(
        MovementTestRig rig,
        IReadOnlyList<TestUnitId> units,
        float tolerance)
    {
        var snapshots = rig.Observe(units);
        return snapshots.Count(unit =>
            Vector2.DistanceSquared(unit.Position, unit.AssignedTarget) <=
            tolerance * tolerance);
    }

    private static float MinimumAssignedTargetClearance(
        IReadOnlyList<TestUnitSnapshot> units)
    {
        var minimum = float.PositiveInfinity;
        for (var left = 0; left < units.Count; left++)
        {
            for (var right = left + 1; right < units.Count; right++)
            {
                var clearance = Vector2.Distance(
                    units[left].AssignedTarget,
                    units[right].AssignedTarget) -
                    units[left].Radius - units[right].Radius;
                minimum = MathF.Min(minimum, clearance);
            }
        }

        return minimum;
    }

    private static VisualTestSession ArrivalScenario(
        string id,
        string displayName,
        int durationTicks,
        MovementTestRig rig,
        TestUnitId[] units,
        int minimumArrivals,
        float arrivalTolerance,
        float maximumAllowedOverlap = 2.5f,
        int minimumNavigationRevision = 0,
        int? expectedBuildingCount = null) =>
        new(
            id,
            displayName,
            durationTicks,
            rig,
            units,
            runtime => EvaluateArrival(
                runtime,
                units,
                minimumArrivals,
                arrivalTolerance,
                maximumAllowedOverlap,
                minimumNavigationRevision,
                expectedBuildingCount));

    private static ScenarioResult EvaluateArrival(
        MovementTestRig rig,
        IReadOnlyList<TestUnitId> units,
        int minimumArrivals,
        float tolerance,
        float maximumAllowedOverlap,
        int minimumNavigationRevision,
        int? expectedBuildingCount)
    {
        var snapshots = rig.Observe(units);
        var arrived = snapshots.Count(unit =>
            Vector2.DistanceSquared(unit.Position, unit.AssignedTarget) <= tolerance * tolerance);
        var maximumDistance = snapshots.Max(unit =>
            Vector2.Distance(unit.Position, unit.AssignedTarget));
        var moving = snapshots.Count(unit => unit.State == TestUnitState.Moving);
        var unreachable = rig.ObserveRecovery(units).UnreachableUnits;
        var overlap = MaximumOverlap(snapshots);
        var finite = AllFinite(snapshots);
        var inside = units.All(rig.IsInsideWorld);
        var revisionValid = rig.NavigationRevision >= minimumNavigationRevision;
        var buildingCountValid = expectedBuildingCount is null ||
                                 rig.BuildingCount == expectedBuildingCount.Value;
        var passed = arrived >= minimumArrivals && overlap <= maximumAllowedOverlap &&
                     finite && inside && revisionValid && buildingCountValid;
        return new ScenarioResult(
            passed,
            $"arrived={arrived}/{units.Count}, overlap={overlap:0.00}, " +
            $"finite={finite}, inside={inside}, revision={rig.NavigationRevision}, " +
            $"buildings={rig.BuildingCount}, moving={moving}, " +
            $"unreachable={unreachable}, maxDistance={maximumDistance:0.0}");
    }

    private static ScenarioResult EvaluateStopped(
        MovementTestRig rig,
        IReadOnlyList<TestUnitId> units,
        TestUnitState expectedState)
    {
        var snapshots = rig.Observe(units);
        var correctState = snapshots.Count(unit => unit.State == expectedState);
        var settled = snapshots.Count(unit => unit.Velocity.Length() <= 0.5f);
        var closeToStopPoint = snapshots.Count(unit =>
            Vector2.Distance(unit.Position, unit.AssignedTarget) <= 20f);
        var overlap = MaximumOverlap(snapshots);
        var passed = correctState == units.Count && settled == units.Count &&
                     closeToStopPoint == units.Count && overlap <= 3f && AllFinite(snapshots);
        return new ScenarioResult(
            passed,
            $"state={correctState}/{units.Count}, settled={settled}/{units.Count}, " +
            $"stopRadius={closeToStopPoint}/{units.Count}, overlap={overlap:0.00}");
    }

    private static float MaximumOverlap(IReadOnlyList<TestUnitSnapshot> units)
    {
        var maximum = 0f;
        for (var left = 0; left < units.Count; left++)
        {
            for (var right = left + 1; right < units.Count; right++)
            {
                var minimumDistance = units[left].Radius + units[right].Radius;
                var distance = Vector2.Distance(units[left].Position, units[right].Position);
                maximum = MathF.Max(maximum, minimumDistance - distance);
            }
        }

        return MathF.Max(0f, maximum);
    }

    private static bool AllFinite(IReadOnlyList<TestUnitSnapshot> units) =>
        units.All(unit =>
            float.IsFinite(unit.Position.X) && float.IsFinite(unit.Position.Y) &&
            float.IsFinite(unit.Velocity.X) && float.IsFinite(unit.Velocity.Y));
}
