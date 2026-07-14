using System.Numerics;
using RtsDemo.AI;
using RtsDemo.Presentation;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

/// <summary>
/// Black-box business scenarios. This file intentionally does not reference
/// RtsSimulation, UnitStore, path objects, steering, portals or choke internals.
/// </summary>
public static partial class VisualTestCatalog
{
    public static readonly string[] CaseIds =
    [
        "frontend-test-browser",
        "single-unit",
        "group-move-terminal-stability",
        "dynamic-blockage-priority-matrix",
        "dynamic-blockage-continuous-waves",
        "friendly-building-radial-interaction",
        "semantic-construction-contact-matrix",
        "semantic-construction-resume-matrix",
        "semantic-real-refinery-cycle",
        "semantic-unreachable-queue-release",
        "semantic-follow-body-range",
        "semantic-production-exit-restoration",
        "combat-idle-auto-acquire",
        "attack-move-squad-slot-resume",
        "attack-move-engage-resume",
        "combat-event-stream",
        "combat-damage-matrix",
        "combat-projectile-flight",
        "combat-projectile-presentation",
        "combat-mobile-fire",
        "combat-target-selection",
        "combat-contact-priority",
        "combat-building-defense",
        "attack-move-leash-resume",
        "attack-move-command-isolation",
        "attack-move-cancel",
        "combat-ranged-ring",
        "combat-stop-hold-acquire",
        "combat-multi-retarget",
        "queued-waypoints",
        "queued-command-replace",
        "queued-capacity-limit",
        "control-group-recall",
        "control-group-mixed-steal",
        "smart-command-sequence",
        "smart-command-gameplay-context",
        "smart-command-shift-worker-tasks",
        "operation-selection-camera",
        "operation-mixed-command-card",
        "operation-target-command-mode",
        "operation-build-placement-mode",
        "operation-production-group-batch",
        "minimap-interaction",
        "command-log-replay",
        "command-replay-divergence",
        "replay-package-world",
        "replay-checkpoint-resume",
        "replay-checkpoint-choke",
        "replay-hot-snapshot",
        "economy-replay-persistence",
        "economy-explicit-return-cargo",
        "economy-return-cargo-dropoff-loss",
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
        "economy-auto-patch-distribution",
        "economy-mule-independent-mining",
        "economy-assignment-lifecycle",
        "economy-mining-income-curve",
        "economy-mineral-walk-collision-matrix",
        "economy-mass-mining",
        "economy-expansion-saturation",
        "player-visibility-authority",
        "construction-player-known-placement",
        "concealment-detection-construction",
        "active-burrow-detection-lifecycle",
        "alliance-shared-vision-team-victory",
        "match-capability-elimination",
        "ai-modular-skirmish",
        "ai-dual-runtime-replay",
        "ai-continuous-encounter",
        "construction-gameplay-buildings",
        "construction-reservation-hard-commit",
        "construction-multi-unit-eviction",
        "construction-blocker-policy-matrix",
        "construction-queued-builds",
        "construction-under-build-defense",
        "building-type-resource-runtime",
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
        TechnologyCatalogSnapshot? technologyCatalog = null,
        AiConfigurationCatalogSnapshot? aiConfigurations = null) => caseId switch
    {
        "frontend-test-browser" => CreateFrontendTestBrowser(),
        "single-unit" => CreateSingleUnit(),
        "group-move-terminal-stability" =>
            CreateGroupMoveTerminalStability(),
        "dynamic-blockage-priority-matrix" =>
            CreateDynamicBlockagePriorityMatrix(gameplayProfiles),
        "dynamic-blockage-continuous-waves" =>
            CreateDynamicBlockageContinuousWaves(),
        "friendly-building-radial-interaction" =>
            CreateFriendlyBuildingRadialInteraction(),
        "semantic-construction-contact-matrix" =>
            CreateSemanticConstructionContactMatrix(),
        "semantic-construction-resume-matrix" =>
            CreateSemanticConstructionResumeMatrix(),
        "semantic-real-refinery-cycle" =>
            CreateSemanticRealRefineryCycle(),
        "semantic-unreachable-queue-release" =>
            CreateSemanticUnreachableQueueRelease(),
        "semantic-follow-body-range" =>
            CreateSemanticFollowBodyRange(),
        "semantic-production-exit-restoration" =>
            CreateSemanticProductionExitRestoration(),
        "combat-idle-auto-acquire" => CreateCombatIdleAutoAcquire(),
        "attack-move-squad-slot-resume" =>
            CreateAttackMoveSquadSlotResume(),
        "attack-move-engage-resume" => CreateAttackMoveEngageResume(),
        "combat-event-stream" => CreateCombatEventStream(),
        "combat-damage-matrix" => CreateCombatDamageMatrix(productionCatalog),
        "combat-projectile-flight" => CreateCombatProjectileFlight(
            navigationMap, gameplayProfiles, clearanceBake),
        "combat-projectile-presentation" =>
            CreateCombatProjectilePresentation(),
        "combat-mobile-fire" => CreateCombatMobileFire(gameplayProfiles),
        "combat-target-selection" =>
            CreateCombatTargetSelection(gameplayProfiles),
        "combat-contact-priority" =>
            CreateCombatContactPriority(gameplayProfiles),
        "combat-building-defense" => CreateCombatBuildingDefense(
            navigationMap, gameplayProfiles, clearanceBake,
            buildingTypes, technologyCatalog),
        "attack-move-leash-resume" => CreateAttackMoveLeashResume(),
        "attack-move-command-isolation" => CreateAttackMoveCommandIsolation(),
        "attack-move-cancel" => CreateAttackMoveCancel(),
        "combat-ranged-ring" => CreateCombatRangedRing(),
        "combat-stop-hold-acquire" => CreateCombatStopHoldAcquire(),
        "combat-multi-retarget" => CreateCombatMultiRetarget(),
        "queued-waypoints" => CreateQueuedWaypoints(),
        "queued-command-replace" => CreateQueuedCommandReplace(),
        "queued-capacity-limit" => CreateQueuedCapacityLimit(),
        "control-group-recall" => CreateControlGroupRecall(),
        "control-group-mixed-steal" => CreateControlGroupMixedSteal(
            navigationMap, gameplayProfiles, clearanceBake),
        "smart-command-sequence" => CreateSmartCommandSequence(),
        "smart-command-gameplay-context" => CreateSmartCommandGameplayContext(
            navigationMap, gameplayProfiles, clearanceBake),
        "smart-command-shift-worker-tasks" => CreateSmartCommandShiftWorkerTasks(
            navigationMap, gameplayProfiles, clearanceBake),
        "operation-selection-camera" => CreateOperationSelectionCamera(),
        "operation-mixed-command-card" => CreateOperationMixedCommandCard(
            navigationMap, gameplayProfiles, clearanceBake),
        "operation-target-command-mode" => CreateOperationTargetCommandMode(
            navigationMap, gameplayProfiles, clearanceBake),
        "operation-build-placement-mode" => CreateOperationBuildPlacementMode(
            navigationMap, gameplayProfiles, clearanceBake),
        "operation-production-group-batch" => CreateOperationProductionGroupBatch(
            navigationMap, gameplayProfiles, clearanceBake),
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
        "economy-explicit-return-cargo" => CreateExplicitReturnCargo(
            navigationMap, gameplayProfiles, clearanceBake),
        "economy-return-cargo-dropoff-loss" =>
            CreateReturnCargoDropOffLoss(),
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
        "economy-auto-patch-distribution" =>
            CreateAutoPatchDistribution(
                navigationMap, gameplayProfiles, clearanceBake),
        "economy-mule-independent-mining" =>
            CreateMuleIndependentMining(
                navigationMap, gameplayProfiles, clearanceBake),
        "economy-assignment-lifecycle" =>
            CreateEconomyAssignmentLifecycle(
                navigationMap, gameplayProfiles, clearanceBake),
        "economy-mining-income-curve" => CreateMiningIncomeCurve(),
        "economy-mineral-walk-collision-matrix" =>
            CreateMineralWalkCollisionMatrix(),
        "economy-mass-mining" => CreateMassMiningEconomy(),
        "economy-expansion-saturation" => CreateExpansionSaturation(),
        "player-visibility-authority" => CreatePlayerVisibilityAuthority(
            navigationMap, gameplayProfiles, clearanceBake),
        "construction-player-known-placement" =>
            CreateConstructionPlayerKnownPlacement(),
        "concealment-detection-construction" =>
            CreateConcealmentDetectionConstruction(),
        "active-burrow-detection-lifecycle" =>
            CreateActiveBurrowDetectionLifecycle(),
        "alliance-shared-vision-team-victory" =>
            CreateAllianceSharedVisionTeamVictory(),
        "match-capability-elimination" => CreateMatchCapabilityElimination(
            navigationMap, gameplayProfiles, clearanceBake),
        "ai-modular-skirmish" => CreateModularAiSkirmish(aiConfigurations),
        "ai-dual-runtime-replay" => CreateDualAiRuntimeReplay(
            navigationMap, gameplayProfiles, clearanceBake, aiConfigurations),
        "ai-continuous-encounter" => CreateContinuousAiEncounter(
            gameplayProfiles, aiConfigurations),
        "construction-gameplay-buildings" => CreateConstructionGameplayBuildings(),
        "construction-reservation-hard-commit" =>
            CreateConstructionReservationHardCommit(
                navigationMap, gameplayProfiles, clearanceBake),
        "construction-multi-unit-eviction" =>
            CreateConstructionMultiUnitEviction(gameplayProfiles),
        "construction-blocker-policy-matrix" =>
            CreateConstructionBlockerPolicyMatrix(gameplayProfiles),
        "construction-queued-builds" => CreateConstructionQueuedBuilds(
            navigationMap, gameplayProfiles, clearanceBake),
        "construction-under-build-defense" =>
            CreateConstructionUnderBuildDefense(
                navigationMap, gameplayProfiles, clearanceBake),
        "building-type-resource-runtime" =>
            CreateBuildingTypeResourceRuntime(buildingTypes),
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

    private static VisualTestSession CreateFrontendTestBrowser()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 8);
        return new VisualTestSession(
            "frontend-test-browser",
            "Launch screen test browser and Chinese descriptions",
            240,
            rig,
            [],
            _ =>
            {
                var entries = TestShowcaseCatalog.Build(CaseIds);
                var categories = TestShowcaseCatalog.Categories(entries);
                var filtered = TestShowcaseCatalog.Filter(
                    entries, "寻路", TestShowcaseCatalog.AllCategories);
                return new ScenarioResult(
                    entries.Length == CaseIds.Length &&
                    categories.Length >= 6 && filtered.Length > 0,
                    $"entries={entries.Length}, categories={categories.Length}, " +
                    $"pathfinding-results={filtered.Length}");
            });
    }

    private static VisualTestSession CreateSingleUnit()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(1200f, 700f), 8);
        var units = new[] { rig.Spawn(new Vector2(100f, 200f)) };
        rig.Move(units, new Vector2(900f, 500f));
        return ArrivalScenario(
            "single-unit", "Single unit direct move", 480, rig, units, 1, 8f);
    }

    private static VisualTestSession CreateGroupMoveTerminalStability()
    {
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(1200f, 700f), 64);
        var units = rig.SpawnGrid(
            new Vector2(90f, 220f), 6, 8, 18f);
        rig.Move(units, new Vector2(970f, 350f));
        TestUnitSnapshot[]? settled = null;
        var settledTick = -1L;
        var session = new VisualTestSession(
                "group-move-terminal-stability",
                "A completed group Move stays hard-settled without late slot jitter",
                1200,
                rig,
                units,
                runtime =>
                {
                    var final = runtime.Observe(units);
                    var arrived = final.Count(value =>
                        value.State == TestUnitState.Arrived);
                    var stopped = final.Count(value =>
                        value.Velocity.Length() <= 0.25f);
                    var maximumLateDrift = settled is null
                        ? float.PositiveInfinity
                        : final.Max(value => Vector2.Distance(
                            value.Position,
                            settled.Single(previous =>
                                previous.Id == value.Id).Position));
                    var passed = arrived == units.Length &&
                                 stopped == units.Length &&
                                 settledTick >= 0 &&
                                 runtime.Tick - settledTick >= 240 &&
                                 maximumLateDrift <= 1.5f;
                    return new ScenarioResult(
                        passed,
                        $"arrived={arrived}/{units.Length}, " +
                        $"stopped={stopped}/{units.Length}, " +
                        $"settled-tick={settledTick}, " +
                        $"late-drift={maximumLateDrift:0.00}");
                });
        for (var tick = 600; tick < session.DurationTicks - 240; tick += 10)
        {
            session.At(tick, "Capture the first fully settled formation", runtime =>
            {
                if (settled is not null)
                    return;
                var snapshots = runtime.Observe(units);
                if (snapshots.All(value =>
                        value.State == TestUnitState.Arrived))
                {
                    settled = snapshots;
                    settledTick = runtime.Tick;
                }
            });
        }
        return session;
    }

    private static VisualTestSession CreateDynamicBlockagePriorityMatrix(
        GameplayProfileCatalogSnapshot? gameplayProfiles)
    {
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var corridorCenters = new[] { 100f, 285f, 470f, 655f };
        var corridorHalfWidth = 18f;
        var cursor = 0f;
        var obstacles = new List<SimRect>();
        foreach (var center in corridorCenters)
        {
            var wallEnd = center - corridorHalfWidth;
            if (wallEnd > cursor)
            {
                obstacles.Add(new SimRect(
                    new Vector2(0f, cursor),
                    new Vector2(1200f, wallEnd)));
            }
            cursor = center + corridorHalfWidth;
        }
        if (cursor < 760f)
        {
            obstacles.Add(new SimRect(
                new Vector2(0f, cursor),
                new Vector2(1200f, 760f)));
        }
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                new SimRect(Vector2.Zero, new Vector2(1200f, 760f)),
                obstacles.ToArray(), [], [], [],
                out var navigationMap,
                out var validation) || navigationMap is null)
            throw new InvalidOperationException(
                $"Priority corridor map invalid: {validation.FirstError}.");
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, clearanceBake: null);

        var equalMover = rig.Spawn(new Vector2(120f, corridorCenters[0]));
        var equalBlocker = rig.Spawn(new Vector2(500f, corridorCenters[0]));
        var highMover = rig.Spawn(
            new Vector2(120f, corridorCenters[1]), radius: 10f);
        var lowBlocker = rig.Spawn(
            new Vector2(500f, corridorCenters[1]), radius: 5.5f);
        var lowMover = rig.Spawn(
            new Vector2(120f, corridorCenters[2]), radius: 5.5f);
        var highBlocker = rig.Spawn(
            new Vector2(500f, corridorCenters[2]), radius: 10f);
        var holdMover = rig.Spawn(new Vector2(120f, corridorCenters[3]));
        var holdBlocker = rig.Spawn(new Vector2(500f, corridorCenters[3]));
        var visible = new[]
        {
            equalMover, equalBlocker, highMover, lowBlocker,
            lowMover, highBlocker, holdMover, holdBlocker
        };
        var initialEqualBlocker = rig.Observe(equalBlocker).Position;
        var initialLowBlocker = rig.Observe(lowBlocker).Position;
        var initialHighBlocker = rig.Observe(highBlocker).Position;
        var initialHoldBlocker = rig.Observe(holdBlocker).Position;
        rig.StartReplayPackageRecording();
        rig.Hold([holdBlocker]);
        rig.Move([equalMover], new Vector2(1030f, corridorCenters[0]));
        rig.Move([highMover], new Vector2(1030f, corridorCenters[1]));
        rig.Move([lowMover], new Vector2(1030f, corridorCenters[2]));
        rig.Move([holdMover], new Vector2(1030f, corridorCenters[3]));

        TestRuntimeStateCapture? hotCapture = null;
        var lowSettledTick = -1L;
        var holdSettledTick = -1L;
        var lowWindowStartTick = -1L;
        var holdWindowStartTick = -1L;
        var lowWindowStartX = 0f;
        var holdWindowStartX = 0f;
        var lowBlockedWindowTicks = -1L;
        var holdBlockedWindowTicks = -1L;
        var session = new VisualTestSession(
                "dynamic-blockage-priority-matrix",
                "Universal priority push and three-second bounded blockage matrix",
                900,
                rig,
                visible,
                runtime =>
                {
                    var equal = runtime.Observe(equalMover);
                    var high = runtime.Observe(highMover);
                    var low = runtime.Observe(lowMover);
                    var hold = runtime.Observe(holdMover);
                    var equalShift = Vector2.Distance(
                        runtime.Observe(equalBlocker).Position,
                        initialEqualBlocker);
                    var lowShift = Vector2.Distance(
                        runtime.Observe(lowBlocker).Position,
                        initialLowBlocker);
                    var highDrift = Vector2.Distance(
                        runtime.Observe(highBlocker).Position,
                        initialHighBlocker);
                    var holdDrift = Vector2.Distance(
                        runtime.Observe(holdBlocker).Position,
                        initialHoldBlocker);
                    var diagnostics = runtime.ObserveMovementDiagnostics();
                    var package = runtime.CaptureReplayPackage();
                    var hot = runtime.BindHotSnapshot(package, hotCapture!);
                    var restored = runtime.ResumeHotSnapshot(
                        package, hot, runtime.Tick);
                    var exact = restored.FinalHash == runtime.StateHash;
                    var passThrough = equal.State == TestUnitState.Arrived &&
                                      equal.Position.X >= 900f &&
                                      high.State == TestUnitState.Arrived &&
                                      high.Position.X >= 900f &&
                                      equalShift >= 40f && lowShift >= 40f;
                    var bounded = low.State == TestUnitState.Arrived &&
                                  low.Position.X < 500f &&
                                  hold.State == TestUnitState.Arrived &&
                                  hold.Position.X < 500f &&
                                  highDrift <= 1f && holdDrift <= 1f;
                    var passed = passThrough && bounded &&
                                 diagnostics.PriorityPushPairs > 0 &&
                                 diagnostics.DynamicBlockageSettles == 2 &&
                                 lowSettledTick is >= 300 and <= 540 &&
                                 holdSettledTick is >= 300 and <= 720 &&
                                 exact;
                    return new ScenarioResult(
                        passed,
                        $"through={equal.Position.X:0}/{high.Position.X:0}, " +
                        $"shift={equalShift:0.0}/{lowShift:0.0}, " +
                        $"bounded={low.Position.X:0}/{hold.Position.X:0}, " +
                        $"anchor-drift={highDrift:0.00}/{holdDrift:0.00}, " +
                        $"push={diagnostics.PriorityPushPairs}/" +
                        $"{diagnostics.PriorityPushDisplacement:0.0}, " +
                        $"settles={diagnostics.DynamicBlockageSettles}@" +
                        $"{lowSettledTick}/{holdSettledTick} " +
                        $"windows={lowBlockedWindowTicks}/" +
                        $"{holdBlockedWindowTicks}, " +
                        $"target={low.AssignedTarget.X:0}/" +
                        $"{hold.AssignedTarget.X:0}, " +
                        $"hot={exact}/v{hot.FormatVersion}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(600f, 380f), 0.8f)
            .Highlight(
                new SimRect(new Vector2(80f, 75f), new Vector2(1080f, 125f)),
                "EQUAL PRIORITY: mover pushes idle blocker",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(80f, 260f), new Vector2(1080f, 310f)),
                "HIGH PRIORITY: large pushes small",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(80f, 445f), new Vector2(1080f, 495f)),
                "LOW PRIORITY: settle locally after 3 seconds",
                TestDiagnosticKind.Rejected)
            .Highlight(
                new SimRect(new Vector2(80f, 630f), new Vector2(1080f, 680f)),
                "HOLD: equal priority may not displace anchor",
                TestDiagnosticKind.Rejected);
        session.At(300, "Capture active dynamic blockage before bounded settle",
            runtime => hotCapture = runtime.CaptureRuntimeState());
        for (var tick = 120; tick < 900; tick += 5)
        {
            session.At(tick, "Observe bounded low-priority completion", runtime =>
            {
                ObserveBlockedCompletion(
                    runtime.Observe(lowMover),
                    runtime.Tick,
                    ref lowWindowStartTick,
                    ref lowWindowStartX,
                    ref lowSettledTick,
                    ref lowBlockedWindowTicks);
                ObserveBlockedCompletion(
                    runtime.Observe(holdMover),
                    runtime.Tick,
                    ref holdWindowStartTick,
                    ref holdWindowStartX,
                    ref holdSettledTick,
                    ref holdBlockedWindowTicks);
            });
        }
        return session;
    }

    private static void ObserveBlockedCompletion(
        TestUnitSnapshot snapshot,
        long tick,
        ref long windowStartTick,
        ref float windowStartX,
        ref long settledTick,
        ref long blockedWindowTicks)
    {
        if (settledTick >= 0)
            return;
        if (windowStartTick < 0)
        {
            windowStartTick = tick;
            windowStartX = snapshot.Position.X;
        }
        else if (snapshot.State != TestUnitState.Arrived &&
                 snapshot.Position.X - windowStartX >= 1f)
        {
            windowStartTick = tick;
            windowStartX = snapshot.Position.X;
        }
        if (snapshot.State != TestUnitState.Arrived)
            return;

        settledTick = tick;
        blockedWindowTicks = tick - windowStartTick;
    }

    private static VisualTestSession CreateDynamicBlockageContinuousWaves()
    {
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(1400f, 820f), 96);
        var waves = new[]
        {
            rig.SpawnGrid(new Vector2(80f, 110f), 4, 8, 18f),
            rig.SpawnGrid(new Vector2(80f, 315f), 1, 4, 18f),
            rig.SpawnGrid(new Vector2(80f, 430f), 1, 3, 18f),
            rig.SpawnGrid(new Vector2(80f, 535f), 1, 2, 18f),
            new[] { rig.Spawn(new Vector2(80f, 650f)) }
        };
        var all = waves.SelectMany(value => value).ToArray();
        var target = new Vector2(1110f, 410f);
        rig.Move(waves[0], target);
        TestUnitSnapshot[]? settled = null;
        var settledTick = -1L;
        var session = new VisualTestSession(
                "dynamic-blockage-continuous-waves",
                "Large, small and singleton waves share one crowded destination",
                1800,
                rig,
                all,
                runtime =>
                {
                    var snapshots = runtime.Observe(all);
                    var arrived = snapshots.Count(value =>
                        value.State == TestUnitState.Arrived);
                    var uniqueTargets = snapshots.Select(value => (
                            X: MathF.Round(value.AssignedTarget.X, 1),
                            Y: MathF.Round(value.AssignedTarget.Y, 1)))
                        .Distinct().Count();
                    var lateDrift = settled is null
                        ? float.PositiveInfinity
                        : snapshots.Max(value => Vector2.Distance(
                            value.Position,
                            settled.Single(previous =>
                                previous.Id == value.Id).Position));
                    var diagnostics = runtime.ObserveMovementDiagnostics();
                    var passed = arrived == all.Length &&
                                 uniqueTargets == all.Length &&
                                 settledTick >= 0 &&
                                 runtime.Tick - settledTick >= 240 &&
                                 lateDrift <= 1.5f;
                    return new ScenarioResult(
                        passed,
                        $"waves=32/4/3/2/1, arrived={arrived}/{all.Length}, " +
                        $"targets={uniqueTargets}/{all.Length}, " +
                        $"settled-tick={settledTick}, drift={lateDrift:0.00}, " +
                        $"push={diagnostics.PriorityPushPairs}, " +
                        $"fallback={diagnostics.DynamicBlockageSettles}, " +
                        $"yield={diagnostics.DestinationYieldEvents}, " +
                        $"overflow={diagnostics.DestinationOverflowAssignments}");
                })
            .RenderSpawnedUnits()
            .CameraKeyframe(0, new Vector2(620f, 410f), 0.72f)
            .CameraKeyframe(780, new Vector2(1060f, 410f), 0.82f)
            .Highlight(
                new SimRect(new Vector2(930f, 220f), new Vector2(1280f, 600f)),
                "SHARED DESTINATION / INDEPENDENT COMMAND WAVES",
                TestDiagnosticKind.Info);
        session.At(240, "Send four-unit reinforcement wave",
            runtime => runtime.Move(waves[1], target));
        session.At(420, "Send three-unit AttackMove wave",
            runtime => runtime.AttackMove(waves[2], target));
        session.At(600, "Send two-unit reinforcement wave",
            runtime => runtime.Move(waves[3], target));
        session.At(780, "Send singleton into the settled destination",
            runtime => runtime.Move(waves[4], target));
        for (var tick = 1080; tick < session.DurationTicks - 240; tick += 10)
        {
            session.At(tick, "Capture first stable all-wave settlement", runtime =>
            {
                if (settled is not null)
                    return;
                var snapshots = runtime.Observe(all);
                if (snapshots.All(value => value.State == TestUnitState.Arrived))
                {
                    settled = snapshots;
                    settledTick = runtime.Tick;
                }
            });
        }
        return session;
    }

    private static VisualTestSession CreateFriendlyBuildingRadialInteraction()
    {
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(1200f, 700f), 16);
        rig.RegisterPlayer(1, 1000, 0, 24, 1);
        var builder = rig.SpawnWorker(new Vector2(390f, 350f), 1);
        TestUnitId[] units =
        [
            rig.SpawnCombat(new Vector2(150f, 145f), 1),
            rig.SpawnCombat(new Vector2(160f, 555f), 1),
            rig.SpawnCombat(new Vector2(1050f, 165f), 1),
            rig.SpawnCombat(new Vector2(1040f, 535f), 1)
        ];
        var building = default(TestConstructionResult);
        var issued = TestPlayerOrderCommandCode.InvalidTarget;
        Vector2[]? origins = null;
        Vector2[]? targets = null;
        return new VisualTestSession(
                "friendly-building-radial-interaction",
                "Friendly-building interaction projects each unit radially to the rectangle",
                900,
                rig,
                units.Append(builder).ToArray(),
                runtime =>
                {
                    var snapshot = runtime.ObserveGameplayBuilding(
                        building.BuildingId);
                    var final = runtime.Observe(units);
                    var uniqueTargets = final
                        .Select(value => (
                            X: MathF.Round(value.AssignedTarget.X, 1),
                            Y: MathF.Round(value.AssignedTarget.Y, 1)))
                        .Distinct()
                        .Count();
                    var radial = origins is not null && targets is not null &&
                                 Enumerable.Range(0, units.Length).All(index =>
                                 {
                                     var fromCenter = origins[index] - snapshot.Center;
                                     var toTarget = targets[index] - snapshot.Center;
                                     var cross = MathF.Abs(
                                         fromCenter.X * toTarget.Y -
                                         fromCenter.Y * toTarget.X);
                                     var scale = MathF.Max(
                                         1f,
                                         fromCenter.Length() * toTarget.Length());
                                     return cross / scale <= 0.002f &&
                                            Vector2.Dot(fromCenter, toTarget) > 0f &&
                                            MathF.Abs(toTarget.X) > 5f &&
                                            MathF.Abs(toTarget.Y) > 5f;
                                 });
                    var contacts = final.Count(value =>
                    {
                        var gap = IndependentRectangleGap(
                            value.Position,
                            value.Radius,
                            snapshot.Center,
                            snapshot.Size);
                        var tolerance = IndependentNumericTolerance(
                            value.Position,
                            snapshot.Center,
                            snapshot.Size);
                        return MathF.Abs(gap) <= tolerance;
                    });
                    var penetrations = final.Count(value =>
                    {
                        var gap = IndependentRectangleGap(
                            value.Position,
                            value.Radius,
                            snapshot.Center,
                            snapshot.Size);
                        var tolerance = IndependentNumericTolerance(
                            value.Position,
                            snapshot.Center,
                            snapshot.Size);
                        return gap < -tolerance;
                    });
                    var passed = building.Succeeded &&
                                 snapshot.State ==
                                     TestBuildingLifecycleState.Completed &&
                                 issued == TestPlayerOrderCommandCode.Success &&
                                 radial && uniqueTargets == units.Length &&
                                 contacts == units.Length && penetrations == 0;
                    var radii = string.Join(
                        ',', final.Select(value => value.Radius.ToString("0.###")));
                    return new ScenarioResult(
                        passed,
                        $"building={snapshot.State}, command={issued}, " +
                        $"radial={radial}, targets={uniqueTargets}/{units.Length}, " +
                        $"contacts={contacts}/{units.Length}, " +
                        $"penetrations={penetrations}, " +
                        $"building-center={snapshot.Center.X:0.###},{snapshot.Center.Y:0.###}, " +
                        $"building-size={snapshot.Size.X:0.###},{snapshot.Size.Y:0.###}, " +
                        $"radii={radii}, " +
                        $"move={string.Join(',', units.Select(value => runtime.ObserveMovement(value).Result))}, " +
                        $"points={string.Join(';', Enumerable.Range(0, units.Length).Select(index => $"{origins![index].X:0},{origins[index].Y:0}>{targets![index].X:0},{targets[index].Y:0}@{final[index].Position.X:0},{final[index].Position.Y:0}"))}");
                })
            .At(1, "Construct a rectangular friendly building", runtime =>
                building = runtime.Build(
                    1,
                    builder,
                    DemoBuildingTypes.SupplyDepot with
                    {
                        Cost = default,
                        BuildSeconds = 0.05f,
                        ConstructionMethod =
                            ConstructionMethodKind.StartAndRelease
                    },
                    new Vector2(600f, 350f)))
            .At(300, "Right-click the same building from four diagonal bearings",
                runtime =>
                {
                    origins = runtime.Observe(units)
                        .Select(value => value.Position)
                        .ToArray();
                    issued = runtime.PlayerSmartFriendlyBuilding(
                        1, units, building.BuildingId);
                })
            .At(301, "Capture per-unit rectangle approach points", runtime =>
                targets = runtime.Observe(units)
                    .Select(value => value.AssignedTarget)
                    .ToArray());
    }

    private static VisualTestSession CreateCombatIdleAutoAcquire()
    {
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(900f, 600f), 16);
        var attackerProfile = new TestCombatProfile(
            MaximumHealth: 90f,
            AttackDamage: 20f,
            AttackRange: 90f,
            AcquisitionRange: 260f,
            AttackCooldownSeconds: 0.5f,
            AttackWindupSeconds: 0.1f,
            LeashDistance: 500f,
            ProjectileSpeed: 480f);
        var passiveProfile = new TestCombatProfile(
            MaximumHealth: 60f,
            AttackDamage: 0f,
            AttackRange: 10f,
            AcquisitionRange: 40f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 80f);
        var attackers = Enumerable.Range(0, 6)
            .Select(index => rig.SpawnCombat(
                new Vector2(210f, 225f + index * 30f), 1,
                attackerProfile))
            .ToArray();
        var targets = Enumerable.Range(0, 3)
            .Select(index => rig.SpawnCombat(
                new Vector2(445f, 255f + index * 75f), 2,
                passiveProfile))
            .ToArray();
        var visible = attackers.Concat(targets).ToArray();
        return new VisualTestSession(
            "combat-idle-auto-acquire",
            "Idle combat units acquire and destroy nearby hostiles without AttackMove",
            480,
            rig,
            visible,
            runtime =>
            {
                var destroyed = targets.Count(value =>
                    !runtime.ObserveCombat(value).Alive);
                var starts = runtime.ObserveCombatEvents().Events.Count(value =>
                    value.Kind == CombatEventKind.AttackStarted);
                var passed = destroyed == targets.Length && starts >= 9;
                return new ScenarioResult(
                    passed,
                    $"destroyed={destroyed}/{targets.Length}, " +
                    $"attack-starts={starts}, explicit-orders=0");
            });
    }

    private static VisualTestSession CreateAttackMoveSquadSlotResume()
    {
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(1200f, 700f), 24);
        var attackers = Enumerable.Range(0, 16)
            .Select(index => rig.SpawnCombat(
                new Vector2(
                    90f + index % 4 * 18f,
                    305f + index / 4 * 18f),
                1,
                new TestCombatProfile(
                    MaximumHealth: 90f,
                    AttackDamage: 18f,
                    AttackRange: 85f,
                    AcquisitionRange: 240f,
                    AttackCooldownSeconds: 0.55f,
                    AttackWindupSeconds: 0.1f,
                    LeashDistance: 450f)))
            .ToArray();
        var target = rig.SpawnCombat(
            new Vector2(510f, 350f),
            2,
            new TestCombatProfile(
                MaximumHealth: 90f,
                AttackDamage: 0f,
                AttackRange: 10f,
                AcquisitionRange: 40f,
                AttackCooldownSeconds: 1f,
                AttackWindupSeconds: 0f,
                LeashDistance: 80f));
        rig.AttackMove(attackers, new Vector2(1030f, 350f));
        TestUnitSnapshot[]? settled = null;
        var settledTick = -1L;
        var session = new VisualTestSession(
                "attack-move-squad-slot-resume",
                "AttackMove squad resumes its individual slots instead of one shared point",
                1200,
                rig,
                attackers.Append(target).ToArray(),
                runtime =>
                {
                    var final = runtime.Observe(attackers);
                    var uniqueTargets = final.Select(value => (
                            X: MathF.Round(value.AssignedTarget.X, 1),
                            Y: MathF.Round(value.AssignedTarget.Y, 1)))
                        .Distinct()
                        .Count();
                    var arrived = final.Count(value =>
                        value.State == TestUnitState.Arrived);
                    var maximumLateDrift = settled is null
                        ? float.PositiveInfinity
                        : final.Max(value => Vector2.Distance(
                            value.Position,
                            settled.Single(previous =>
                                previous.Id == value.Id).Position));
                    var diagnostics = runtime.ObserveMovementDiagnostics();
                    var unsettled = final
                        .Where(value => value.State != TestUnitState.Arrived)
                        .Select(value =>
                            $"u{value.Id.Value}:{value.State}/" +
                            $"{Vector2.Distance(value.Position, value.AssignedTarget):0.0}")
                        .ToArray();
                    var passed = !runtime.ObserveCombat(target).Alive &&
                                 uniqueTargets == attackers.Length &&
                                 arrived == attackers.Length &&
                                 settledTick >= 0 &&
                                 runtime.Tick - settledTick >= 240 &&
                                 maximumLateDrift <= 1.5f;
                    return new ScenarioResult(
                        passed,
                        $"target-alive={runtime.ObserveCombat(target).Alive}, " +
                        $"slots={uniqueTargets}/{attackers.Length}, " +
                        $"arrived={arrived}/{attackers.Length}, " +
                        $"settled-tick={settledTick}, " +
                        $"late-drift={maximumLateDrift:0.00}, " +
                        $"yield={diagnostics.DestinationYieldEvents}, " +
                        $"overflow={diagnostics.DestinationOverflowAssignments}, " +
                        $"unsettled={string.Join(',', unsettled)}");
                });
        for (var tick = 600; tick < session.DurationTicks - 240; tick += 10)
        {
            session.At(tick, "Capture the first fully settled resumed squad",
                runtime =>
                {
                    if (settled is not null)
                        return;
                    var snapshots = runtime.Observe(attackers);
                    if (snapshots.All(value =>
                            value.State == TestUnitState.Arrived))
                    {
                        settled = snapshots;
                        settledTick = runtime.Tick;
                    }
                });
        }
        return session;
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

    private static VisualTestSession CreateCombatEventStream()
    {
        var rig = MovementTestRig.CreateOpenField(new Vector2(900f, 520f), 8);
        var attackerProfile = new TestCombatProfile(
            MaximumHealth: 80f,
            AttackDamage: 12f,
            AttackRange: 90f,
            AcquisitionRange: 220f,
            AttackCooldownSeconds: 0.6f,
            AttackWindupSeconds: 0.15f,
            LeashDistance: 320f);
        var targetProfile = new TestCombatProfile(
            MaximumHealth: 25f,
            AttackDamage: 0f,
            AttackRange: 10f,
            AcquisitionRange: 40f,
            AttackCooldownSeconds: 1f,
            AttackWindupSeconds: 0f,
            LeashDistance: 80f);
        var attacker = rig.SpawnCombat(
            new Vector2(250f, 260f), 1, attackerProfile);
        var target = rig.SpawnCombat(
            new Vector2(520f, 260f), 2, targetProfile);
        rig.AttackTarget([attacker], target);
        return new VisualTestSession(
            "combat-event-stream",
            "Attack start, impact and destruction emit deterministic events",
            300,
            rig,
            [attacker, target],
            runtime =>
            {
                var batch = runtime.ObserveCombatEvents();
                var starts = batch.Events.Where(value =>
                    value.Kind == CombatEventKind.AttackStarted).ToArray();
                var impacts = batch.Events.Where(value =>
                    value.Kind == CombatEventKind.Impact).ToArray();
                var destroyed = batch.Events.Where(value =>
                    value.Kind == CombatEventKind.TargetDestroyed).ToArray();
                var ordered = batch.Events.Select(value => value.Sequence)
                    .SequenceEqual(Enumerable.Range(1, batch.Events.Length)
                        .Select(value => (ulong)value));
                var passed = !runtime.ObserveCombat(target).Alive &&
                             starts.Length == 3 && impacts.Length == 3 &&
                             destroyed.Length == 1 && ordered &&
                             impacts.Select(value => value.Damage)
                                 .SequenceEqual([12f, 12f, 1f]) &&
                             impacts.Select(value => value.RemainingHealth)
                                 .SequenceEqual([13f, 1f, 0f]) &&
                             batch.LostEvents == 0;
                return new ScenarioResult(
                    passed,
                    $"events={batch.Events.Length}, starts={starts.Length}, " +
                    $"impacts={string.Join(',', impacts.Select(value => value.Damage))}, " +
                    $"destroyed={destroyed.Length}, lost={batch.LostEvents}");
            });
    }

    private static VisualTestSession CreateCombatDamageMatrix(
        ProductionCatalogSnapshot? productionCatalog)
    {
        productionCatalog ??= DemoProductionCatalog.CreateSnapshot();
        var rig = MovementTestRig.CreateOpenField(new Vector2(960f, 640f), 12);
        var marauder = productionCatalog.UnitType(1).Combat;
        var single = new TestCombatProfile(
            180f, marauder.AttackDamage, 100f, 260f, 10f, 0.1f, 420f,
            Armor: marauder.Armor,
            Attributes: marauder.Attributes,
            AttacksPerVolley: marauder.AttacksPerVolley,
            BonusVs: marauder.BonusVs,
            BonusDamage: marauder.BonusDamage,
            BaseUpgradeDamage: marauder.BaseUpgradeDamage,
            BonusUpgradeDamage: marauder.BonusUpgradeDamage);
        var multi = single with
        {
            AttackDamage = 8f,
            AttacksPerVolley = 2,
            BonusDamage = 4f
        };
        var lightTarget = new TestCombatProfile(
            100f, 0f, 10f, 30f, 1f, 0f, 60f,
            Armor: 2f,
            Attributes: CombatAttribute.Light | CombatAttribute.Biological);
        var armoredTarget = lightTarget with
        {
            Attributes = CombatAttribute.Armored | CombatAttribute.Mechanical
        };
        var multiTarget = armoredTarget with { Armor = 3f };
        var attackers = new[]
        {
            rig.SpawnCombat(new Vector2(220f, 150f), 1, single),
            rig.SpawnCombat(new Vector2(220f, 320f), 1, single),
            rig.SpawnCombat(new Vector2(220f, 490f), 1, multi)
        };
        var targets = new[]
        {
            rig.SpawnCombat(new Vector2(560f, 150f), 2, lightTarget),
            rig.SpawnCombat(new Vector2(560f, 320f), 2, armoredTarget),
            rig.SpawnCombat(new Vector2(560f, 490f), 2, multiTarget)
        };
        for (var index = 0; index < attackers.Length; index++)
            rig.AttackTarget([attackers[index]], targets[index]);
        return new VisualTestSession(
            "combat-damage-matrix",
            "Catalog armor, attributes, bonus damage and multi-hit matrix",
            300,
            rig,
            [.. attackers, .. targets],
            runtime =>
            {
                var health = targets.Select(value =>
                    runtime.ObserveCombat(value).Health).ToArray();
                var impacts = runtime.ObserveCombatEvents().Events
                    .Where(value => value.Kind == CombatEventKind.Impact)
                    .OrderBy(value => value.TargetId)
                    .ToArray();
                var passed = health.SequenceEqual([80f, 70f, 82f]) &&
                             impacts.Length == 3 &&
                             impacts.Select(value => value.Damage)
                                 .SequenceEqual([20f, 30f, 18f]) &&
                             !impacts[0].BonusApplied &&
                             impacts[1].BonusApplied &&
                             impacts[2].AttacksApplied == 2 &&
                             impacts[2].DamagePerAttack == 9f;
                return new ScenarioResult(
                    passed,
                    $"health={string.Join(',', health)}, " +
                    $"damage={string.Join(',', impacts.Select(value => value.Damage))}, " +
                    $"catalog={productionCatalog.StableHashText}");
            });
    }

    private static VisualTestSession CreateCombatProjectileFlight(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            12, navigationMap, gameplayProfiles, clearanceBake);
        var attacker = rig.SpawnCombat(
            new Vector2(150f, 250f), 1,
            new TestCombatProfile(
                MaximumHealth: 100f,
                AttackDamage: 20f,
                AttackRange: 650f,
                AcquisitionRange: 700f,
                AttackCooldownSeconds: 10f,
                AttackWindupSeconds: 0.1f,
                LeashDistance: 900f,
                ProjectileSpeed: 120f));
        var target = rig.SpawnCombat(
            new Vector2(700f, 250f), 2,
            new TestCombatProfile(
                MaximumHealth: 100f,
                AttackDamage: 0f,
                AttackRange: 20f,
                AcquisitionRange: 20f,
                AttackCooldownSeconds: 10f,
                AttackWindupSeconds: 0f,
                LeashDistance: 80f),
            maximumSpeed: 90f);
        rig.StartReplayPackageRecording();
        rig.AttackTarget([attacker], target);

        const long hotTick = 180;
        TestRuntimeStateCapture? runtimeCapture = null;
        TestCombatProjectileSnapshot[] inFlight = [];
        return new VisualTestSession(
            "combat-projectile-flight",
            "Homing projectile flight survives hot snapshot and replay",
            480,
            rig,
            [attacker, target],
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var hot = runtime.BindHotSnapshot(package, runtimeCapture!);
                var restored = runtime.ResumeHotSnapshot(
                    package, hot, runtime.Tick);
                var events = runtime.ObserveCombatEvents().Events;
                var launched = events.SingleOrDefault(value =>
                    value.Kind == CombatEventKind.ProjectileLaunched);
                var impact = events.SingleOrDefault(value =>
                    value.Kind == CombatEventKind.Impact);
                var targetHealth = runtime.ObserveCombat(target).Health;
                var exact = restored.FinalHash == runtime.StateHash;
                var passed = inFlight.Length == 1 &&
                             inFlight[0].Position.X > 150f &&
                             inFlight[0].Position.X < 700f &&
                             launched.ProjectileId > 0 &&
                             impact.ProjectileId == launched.ProjectileId &&
                             targetHealth == 80f &&
                             runtime.ObserveCombatProjectiles().Length == 0 &&
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                             hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion && exact;
                return new ScenarioResult(
                    passed,
                    $"flight={inFlight.Length}@" +
                    $"{(inFlight.Length > 0 ? inFlight[0].Position : Vector2.Zero)}, " +
                    $"projectile={launched.ProjectileId}/{impact.ProjectileId}, " +
                    $"hp={targetHealth}, versions=package{package.FormatVersion}/" +
                    $"hot{hot.FormatVersion}, exact={exact}");
            })
            .At(hotTick, "Capture projectile in flight", runtime =>
            {
                inFlight = runtime.ObserveCombatProjectiles();
                runtimeCapture = runtime.CaptureRuntimeState();
            })
            .At(195, "Move target while projectile is in flight", runtime =>
                runtime.Move([target], new Vector2(820f, 360f)));
    }

    private static VisualTestSession CreateCombatProjectilePresentation()
    {
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(1000f, 700f), 12);
        var targetProfile = new TestCombatProfile(
            MaximumHealth: 100f,
            AttackDamage: 0f,
            AttackRange: 20f,
            AcquisitionRange: 20f,
            AttackCooldownSeconds: 20f,
            AttackWindupSeconds: 0f,
            LeashDistance: 80f,
            Attributes: CombatAttribute.Armored);
        var boltProfile = new TestCombatProfile(
            100f, 10f, 650f, 700f, 20f, 0.1f, 900f,
            ProjectileSpeed: 180f);
        var orbProfile = boltProfile with
        {
            BonusVs = CombatAttribute.Armored,
            BonusDamage = 5f
        };
        var volleyProfile = boltProfile with
        {
            AttackDamage = 8f,
            AttacksPerVolley = 2
        };
        var attackers = new[]
        {
            rig.SpawnCombat(new Vector2(150f, 160f), 1, boltProfile),
            rig.SpawnCombat(new Vector2(150f, 350f), 1, orbProfile),
            rig.SpawnCombat(new Vector2(150f, 540f), 1, volleyProfile)
        };
        var targets = new[]
        {
            rig.SpawnCombat(new Vector2(700f, 160f), 2, targetProfile),
            rig.SpawnCombat(new Vector2(700f, 350f), 2, targetProfile),
            rig.SpawnCombat(new Vector2(700f, 540f), 2, targetProfile)
        };
        for (var index = 0; index < attackers.Length; index++)
            rig.AttackTarget([attackers[index]], targets[index]);

        var styles = new HashSet<TestCombatProjectileVisualKind>();
        var impactProjectileIds = new HashSet<int>();
        var maximumTrail = 0;
        var lostEvents = 0;
        var finiteCues = true;
        var session = new VisualTestSession(
            "combat-projectile-presentation",
            "Decoupled bolt, orb, volley trails and impact cues",
            540,
            rig,
            [.. attackers, .. targets],
            runtime =>
            {
                var health = targets.Select(value =>
                    runtime.ObserveCombat(value).Health).ToArray();
                var passed = styles.SetEquals([
                                 TestCombatProjectileVisualKind.Bolt,
                                 TestCombatProjectileVisualKind.Orb,
                                 TestCombatProjectileVisualKind.Volley]) &&
                             maximumTrail ==
                                 CombatPresentationComposer.MaximumTrailPoints &&
                             impactProjectileIds.Count == 3 &&
                             impactProjectileIds.All(value => value > 0) &&
                             health.SequenceEqual([90f, 85f, 84f]) &&
                             lostEvents == 0 && finiteCues;
                return new ScenarioResult(
                    passed,
                    $"styles={string.Join(',', styles.Order())}, " +
                    $"trail={maximumTrail}, impacts=" +
                    $"{string.Join(',', impactProjectileIds.Order())}, " +
                    $"health={string.Join(',', health)}, lost={lostEvents}, " +
                    $"finite={finiteCues}");
            });
        for (var tick = 0; tick < 540; tick += 3)
        {
            session.At(tick, "Observe presentation snapshot", runtime =>
            {
                var frame = runtime.ObserveCombatPresentation(0.05f);
                foreach (var projectile in frame.Projectiles)
                {
                    styles.Add(projectile.VisualKind);
                    maximumTrail = Math.Max(maximumTrail,
                        projectile.Trail.Length);
                }
                foreach (var cue in frame.Cues.Where(value =>
                             value.Kind == TestCombatPresentationCueKind.Impact))
                {
                    impactProjectileIds.Add(cue.ProjectileId);
                    finiteCues &= float.IsFinite(cue.Position.X) &&
                                  float.IsFinite(cue.Position.Y);
                }
                lostEvents += frame.LostEvents;
            });
        }
        return session;
    }

    private static VisualTestSession CreateCombatMobileFire(
        GameplayProfileCatalogSnapshot? gameplayProfiles)
    {
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                new SimRect(Vector2.Zero, new Vector2(1000f, 700f)),
                [], [], [], [],
                out var navigationMap,
                out var navigationValidation) || navigationMap is null)
            throw new InvalidOperationException(
                $"Mobile fire map invalid: {navigationValidation.FirstError}");
        var rig = MovementTestRig.CreateReplayPackageMap(
            12, navigationMap, gameplayProfiles, clearanceBake: null);
        var rootedProfile = new TestCombatProfile(
            MaximumHealth: 100f,
            AttackDamage: 10f,
            AttackRange: 170f,
            AcquisitionRange: 350f,
            AttackCooldownSeconds: 20f,
            AttackWindupSeconds: 0.6f,
            LeashDistance: 900f,
            ProjectileSpeed: 220f);
        var mobileProfile = rootedProfile with
        {
            CanMoveDuringWindup = true,
            CanMoveDuringCooldown = true
        };
        var targetProfile = new TestCombatProfile(
            MaximumHealth: 200f,
            AttackDamage: 0f,
            AttackRange: 20f,
            AcquisitionRange: 20f,
            AttackCooldownSeconds: 20f,
            AttackWindupSeconds: 0f,
            LeashDistance: 80f);
        var attackers = new[]
        {
            rig.SpawnCombat(new Vector2(120f, 100f), 1, rootedProfile,
                maximumSpeed: 110f),
            rig.SpawnCombat(new Vector2(120f, 265f), 1, mobileProfile,
                maximumSpeed: 110f),
            rig.SpawnCombat(new Vector2(120f, 430f), 1, rootedProfile,
                maximumSpeed: 110f),
            rig.SpawnCombat(new Vector2(120f, 595f), 1, rootedProfile,
                maximumSpeed: 110f)
        };
        var targets = new[]
        {
            rig.SpawnCombat(new Vector2(520f, 100f), 2, targetProfile),
            rig.SpawnCombat(new Vector2(520f, 265f), 2, targetProfile),
            rig.SpawnCombat(new Vector2(520f, 430f), 2, targetProfile),
            rig.SpawnCombat(new Vector2(520f, 595f), 2, targetProfile)
        };
        rig.StartReplayPackageRecording();
        var goal = new Vector2(900f, 350f);
        rig.AttackMove([attackers[0]], new Vector2(goal.X, 100f));
        rig.AttackMove([attackers[1]], new Vector2(goal.X, 265f));
        rig.AttackTarget([attackers[2]], targets[2]);
        rig.AttackMove([attackers[3]], new Vector2(goal.X, 595f));

        var starts = new Dictionary<int, TestCombatEvent>();
        var launches = new Dictionary<int, TestCombatEvent>();
        var latestSequence = 0UL;
        var cancelIssued = false;
        var cancelWindupBefore = 0f;
        var cancelWindupAfter = -1f;
        var cooldownMoveIssued = false;
        var cooldownBeforeMove = 0f;
        var cooldownAfterMove = -1f;
        Vector2? rootedAfterCooldownSecond = null;
        Vector2? mobileAfterCooldownSecond = null;
        TestRuntimeStateCapture? runtimeCapture = null;

        var session = new VisualTestSession(
            "combat-mobile-fire",
            "Rooted windup, mobile fire and command cancellation",
            720,
            rig,
            [.. attackers, .. targets],
            runtime =>
            {
                var rootedTravel = starts.TryGetValue(attackers[0].Value, out var rs) &&
                                   launches.TryGetValue(attackers[0].Value, out var rl)
                    ? Vector2.Distance(rs.WorldPosition, rl.WorldPosition)
                    : -1f;
                var mobileTravel = starts.TryGetValue(attackers[1].Value, out var ms) &&
                                   launches.TryGetValue(attackers[1].Value, out var ml)
                    ? Vector2.Distance(ms.WorldPosition, ml.WorldPosition)
                    : -1f;
                var rootedCooldownTravel = rootedAfterCooldownSecond.HasValue &&
                                           launches.TryGetValue(attackers[0].Value,
                                               out var rootedLaunch)
                    ? Vector2.Distance(rootedLaunch.WorldPosition,
                        rootedAfterCooldownSecond.Value)
                    : -1f;
                var mobileCooldownTravel = mobileAfterCooldownSecond.HasValue &&
                                           launches.TryGetValue(attackers[1].Value,
                                               out var mobileLaunch)
                    ? Vector2.Distance(mobileLaunch.WorldPosition,
                        mobileAfterCooldownSecond.Value)
                    : -1f;
                var health = targets.Select(value =>
                    runtime.ObserveCombat(value).Health).ToArray();
                var package = runtime.CaptureReplayPackage();
                var hot = runtime.BindHotSnapshot(package, runtimeCapture!);
                var restored = runtime.ResumeHotSnapshot(
                    package, hot, runtime.Tick);
                var exact = restored.FinalHash == runtime.StateHash;
                var passed = rootedTravel is >= 0f and < 2f &&
                             mobileTravel > 20f &&
                             rootedCooldownTravel is >= 0f and < 2f &&
                             mobileCooldownTravel > 20f &&
                             cancelIssued && cancelWindupBefore > 0f &&
                             cancelWindupAfter == 0f &&
                             !launches.ContainsKey(attackers[2].Value) &&
                             cooldownMoveIssued && cooldownBeforeMove > 0f &&
                             cooldownAfterMove == cooldownBeforeMove &&
                             launches.ContainsKey(attackers[3].Value) &&
                             health.SequenceEqual([190f, 190f, 200f, 190f]) &&
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                             hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion && exact;
                return new ScenarioResult(
                    passed,
                    $"windupTravel={rootedTravel:0.0}/{mobileTravel:0.0}, " +
                    $"cooldownTravel={rootedCooldownTravel:0.0}/" +
                    $"{mobileCooldownTravel:0.0}, cancel=" +
                    $"{cancelWindupBefore:0.00}->{cancelWindupAfter:0.00}/" +
                    $"launch{launches.ContainsKey(attackers[2].Value)}, " +
                    $"cooldownMove={cooldownBeforeMove:0.00}->" +
                    $"{cooldownAfterMove:0.00}, health={string.Join(',', health)}, " +
                    $"versions=package{package.FormatVersion}/hot{hot.FormatVersion}, " +
                    $"exact={exact}");
            });

        session.At(180, "Capture mobile weapon runtime", runtime =>
            runtimeCapture = runtime.CaptureRuntimeState());

        for (var tick = 0; tick < 720; tick++)
        {
            session.At(tick, "Observe weapon movement constraints", runtime =>
            {
                var batch = runtime.ObserveCombatEvents(latestSequence);
                latestSequence = batch.LatestSequence;
                foreach (var combatEvent in batch.Events)
                {
                    if (combatEvent.Kind == CombatEventKind.AttackStarted)
                    {
                        starts.TryAdd(combatEvent.Attacker.Value, combatEvent);
                        if (combatEvent.Attacker == attackers[2] && !cancelIssued)
                        {
                            cancelWindupBefore = runtime.ObserveCombat(
                                attackers[2]).WindupRemaining;
                            runtime.Move([attackers[2]],
                                new Vector2(970f, 430f));
                            cancelWindupAfter = runtime.ObserveCombat(
                                attackers[2]).WindupRemaining;
                            cancelIssued = true;
                        }
                    }
                    if (combatEvent.Kind == CombatEventKind.ProjectileLaunched)
                    {
                        launches.TryAdd(combatEvent.Attacker.Value, combatEvent);
                        if (combatEvent.Attacker == attackers[3] &&
                            !cooldownMoveIssued)
                        {
                            cooldownBeforeMove = runtime.ObserveCombat(
                                attackers[3]).CooldownRemaining;
                            runtime.Move([attackers[3]],
                                new Vector2(900f, 595f));
                            cooldownAfterMove = runtime.ObserveCombat(
                                attackers[3]).CooldownRemaining;
                            cooldownMoveIssued = true;
                        }
                    }
                }

                if (launches.TryGetValue(attackers[0].Value, out var rooted) &&
                    runtime.Tick >= rooted.Tick + 60 &&
                    !rootedAfterCooldownSecond.HasValue)
                    rootedAfterCooldownSecond = runtime.Observe(attackers[0]).Position;
                if (launches.TryGetValue(attackers[1].Value, out var mobile) &&
                    runtime.Tick >= mobile.Tick + 60 &&
                    !mobileAfterCooldownSecond.HasValue)
                    mobileAfterCooldownSecond = runtime.Observe(attackers[1]).Position;
            });
        }
        return session;
    }

    private static VisualTestSession CreateCombatTargetSelection(
        GameplayProfileCatalogSnapshot? gameplayProfiles)
    {
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                new SimRect(Vector2.Zero, new Vector2(1000f, 700f)),
                [], [], [], [],
                out var navigationMap,
                out var navigationValidation) || navigationMap is null)
            throw new InvalidOperationException(
                $"Target selection map invalid: {navigationValidation.FirstError}");
        var rig = MovementTestRig.CreateReplayPackageMap(
            12, navigationMap, gameplayProfiles, clearanceBake: null);
        var attacker = rig.SpawnCombat(
            new Vector2(150f, 350f), 1,
            new TestCombatProfile(
                MaximumHealth: 100f,
                AttackDamage: 0f,
                AttackRange: 400f,
                AcquisitionRange: 400f,
                AttackCooldownSeconds: 2f,
                AttackWindupSeconds: 0.1f,
                LeashDistance: 500f,
                BonusVs: CombatAttribute.Armored));
        var passive = new TestCombatProfile(
            MaximumHealth: 100f,
            AttackDamage: 0f,
            AttackRange: 20f,
            AcquisitionRange: 20f,
            AttackCooldownSeconds: 10f,
            AttackWindupSeconds: 0f,
            LeashDistance: 80f,
            Attributes: CombatAttribute.Light);
        var preferred = passive with
        {
            AttackDamage = 1f,
            Attributes = CombatAttribute.Armored,
            AutoTargetPriority = 1
        };
        var initial = rig.SpawnCombat(
            new Vector2(250f, 350f), 2, passive);
        rig.StartReplayPackageRecording();
        rig.Hold([attacker]);

        TestUnitId? strong = null;
        TestUnitId? marginal = null;
        TestUnitId? targetAtThirty = null;
        var strongSwitchTick = -1L;
        var selectedMarginal = false;
        var explicitIssued = false;
        var explicitHeld = true;
        TestRuntimeStateCapture? runtimeCapture = null;
        TestCombatAutoTargetScore? initialScoreAtSpawn = null;
        TestCombatAutoTargetScore? strongScoreAtSpawn = null;
        TestCombatAutoTargetScore? marginalScoreAtSpawn = null;

        var session = new VisualTestSession(
            "combat-target-selection",
            "Priority scoring, retarget hysteresis and explicit target lock",
            300,
            rig,
            [attacker, initial],
            runtime =>
            {
                var initialScore = initialScoreAtSpawn!.Value;
                var strongScore = strongScoreAtSpawn!.Value;
                var marginalScore = marginalScoreAtSpawn!.Value;
                var package = runtime.CaptureReplayPackage();
                var hot = runtime.BindHotSnapshot(package, runtimeCapture!);
                var restored = runtime.ResumeHotSnapshot(
                    package, hot, runtime.Tick);
                var exact = restored.FinalHash == runtime.StateHash;
                var scoreParts = strongScore.Priority == 1 &&
                                 strongScore.WeaponBonusMatch &&
                                 strongScore.ArmedThreat &&
                                 strongScore.TotalScore < initialScore.TotalScore &&
                                 marginalScore.TotalScore < strongScore.TotalScore &&
                                 strongScore.TotalScore - marginalScore.TotalScore <
                                     2500f;
                var passed = targetAtThirty == initial &&
                             strongSwitchTick >= 50 &&
                             strongSwitchTick < 80 &&
                             !selectedMarginal && explicitIssued && explicitHeld &&
                             runtime.ObserveCombat(attacker).Target == initial &&
                             scoreParts && package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                             hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion && exact;
                return new ScenarioResult(
                    passed,
                    $"target30={targetAtThirty?.Value}, switch={strongSwitchTick}, " +
                    $"marginal={selectedMarginal}, explicit={explicitHeld}, " +
                    $"scores={initialScore.TotalScore:0}/" +
                    $"{strongScore.TotalScore:0}/{marginalScore.TotalScore:0}, " +
                    $"parts=p{strongScore.Priority}/b{strongScore.WeaponBonusMatch}/" +
                    $"t{strongScore.ArmedThreat}, versions=package" +
                    $"{package.FormatVersion}/hot{hot.FormatVersion}, exact={exact}");
            })
            .RenderSpawnedUnits();

        session.At(15, "Add clearly better armored threat", runtime =>
        {
            strong = runtime.SpawnCombat(
                new Vector2(150f, 480f), 2, preferred);
            initialScoreAtSpawn = runtime.PreviewAutoTargetScore(
                attacker, initial);
            strongScoreAtSpawn = runtime.PreviewAutoTargetScore(
                attacker, strong.Value);
        });
        session.At(80, "Add only marginally better candidate", runtime =>
        {
            marginal = runtime.SpawnCombat(
                new Vector2(150f, 223f), 2, preferred);
            strongScoreAtSpawn = runtime.PreviewAutoTargetScore(
                attacker, strong!.Value);
            marginalScoreAtSpawn = runtime.PreviewAutoTargetScore(
                attacker, marginal.Value);
        });
        session.At(150, "Player explicitly locks the original target", runtime =>
        {
            runtime.AttackTarget([attacker], initial);
            explicitIssued = true;
        });
        session.At(151, "Add extreme auto-priority candidate", runtime =>
            runtime.SpawnCombat(
                new Vector2(300f, 350f), 2,
                preferred with { AutoTargetPriority = 10 }));
        session.At(180, "Capture explicit lock and scoring runtime", runtime =>
            runtimeCapture = runtime.CaptureRuntimeState());

        for (var tick = 0; tick < 300; tick++)
        {
            session.At(tick, "Observe stable target selection", runtime =>
            {
                var target = runtime.ObserveCombat(attacker).Target;
                if (runtime.Tick == 30)
                    targetAtThirty = target;
                if (strong.HasValue && target == strong && strongSwitchTick < 0)
                    strongSwitchTick = runtime.Tick;
                if (marginal.HasValue && target == marginal)
                    selectedMarginal = true;
                if (explicitIssued && target != initial)
                    explicitHeld = false;
            });
        }
        return session;
    }

    private static VisualTestSession CreateCombatContactPriority(
        GameplayProfileCatalogSnapshot? gameplayProfiles)
    {
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                new SimRect(Vector2.Zero, new Vector2(1100f, 760f)),
                [], [], [], [],
                out var navigationMap,
                out var navigationValidation) || navigationMap is null)
            throw new InvalidOperationException(
                $"Combat contact map invalid: {navigationValidation.FirstError}");
        var rig = MovementTestRig.CreateReplayPackageMap(
            16, navigationMap, gameplayProfiles, clearanceBake: null);
        var targetProfile = new TestCombatProfile(
            MaximumHealth: 10000f,
            AttackDamage: 0f,
            AttackRange: 1f,
            AcquisitionRange: 1f,
            AttackCooldownSeconds: 20f,
            AttackWindupSeconds: 0f,
            LeashDistance: 50f);
        var fixedWindupProfile = new TestCombatProfile(
            MaximumHealth: 500f,
            AttackDamage: 1f,
            AttackRange: 300f,
            AcquisitionRange: 300f,
            AttackCooldownSeconds: 20f,
            AttackWindupSeconds: 5f,
            LeashDistance: 600f);
        var fixedCooldownProfile = fixedWindupProfile with
        {
            AttackWindupSeconds = 0f
        };
        var mobileProfile = fixedWindupProfile with
        {
            AttackRange = 400f,
            AcquisitionRange = 450f,
            CanMoveDuringWindup = true,
            CanMoveDuringCooldown = true
        };
        var meleeProfile = fixedWindupProfile with
        {
            AttackRange = 20f,
            AcquisitionRange = 80f,
            Positioning = TestCombatPositioning.Melee
        };
        var standardProfile = targetProfile;

        var fixedWindup = rig.SpawnCombat(
            new Vector2(240f, 100f), 1, fixedWindupProfile);
        var fixedCooldown = rig.SpawnCombat(
            new Vector2(240f, 260f), 1, fixedCooldownProfile);
        var mobile = rig.SpawnCombat(
            new Vector2(197f, 420f), 1, mobileProfile,
            maximumSpeed: 110f);
        var melee = rig.SpawnCombat(
            new Vector2(240f, 600f), 1, meleeProfile);
        var targets = new[]
        {
            rig.SpawnCombat(new Vector2(500f, 100f), 2, targetProfile),
            rig.SpawnCombat(new Vector2(500f, 260f), 2, targetProfile),
            rig.SpawnCombat(
                new Vector2(500f, 420f), 2,
                targetProfile with { AutoTargetPriority = 10 }),
            rig.SpawnCombat(new Vector2(272f, 600f), 2, targetProfile)
        };
        rig.StartReplayPackageRecording();
        rig.Hold([fixedWindup, fixedCooldown, melee]);
        rig.AttackMove([mobile], new Vector2(900f, 420f));

        TestCombatContactSnapshot? fixedWindupContact = null;
        TestCombatContactSnapshot? fixedCooldownContact = null;
        TestCombatContactSnapshot? mobileContact = null;
        TestCombatContactSnapshot? meleeContact = null;
        TestCombatContactSnapshot? standardContact = null;
        TestCombatContactResolution? fixedWindupPair = null;
        TestCombatContactResolution? fixedCooldownPair = null;
        TestCombatContactResolution? mobilePair = null;
        TestCombatContactResolution? meleePair = null;
        var contactOrigins = new Dictionary<int, Vector2>();
        var contactDisplacements = new Dictionary<int, float>();
        TestRuntimeStateCapture? runtimeCapture = null;

        var session = new VisualTestSession(
            "combat-contact-priority",
            "Combat contact roles and bounded collision resistance",
            180,
            rig,
            [fixedWindup, fixedCooldown, mobile, melee, .. targets],
            runtime =>
            {
                var windup = fixedWindupContact!.Value;
                var cooldown = fixedCooldownContact!.Value;
                var movingFire = mobileContact!.Value;
                var meleeLock = meleeContact!.Value;
                var standard = standardContact!.Value;
                var windupShare = fixedWindupPair!.Value.LeftCorrectionShare;
                var meleeShare = meleePair!.Value.LeftCorrectionShare;
                var cooldownShare = fixedCooldownPair!.Value.LeftCorrectionShare;
                var mobileShare = mobilePair!.Value.LeftCorrectionShare;
                var fixedTravel = contactDisplacements[fixedWindup.Value];
                var meleeTravel = contactDisplacements[melee.Value];
                var cooldownTravel = contactDisplacements[fixedCooldown.Value];
                var package = runtime.CaptureReplayPackage();
                var hot = runtime.BindHotSnapshot(package, runtimeCapture!);
                var restored = runtime.ResumeHotSnapshot(
                    package, hot, runtime.Tick);
                var exact = restored.FinalHash == runtime.StateHash;
                var roles = windup.Role == TestCombatContactRole.FixedWindup &&
                            cooldown.Role == TestCombatContactRole.FixedCooldown &&
                            movingFire.Role == TestCombatContactRole.MobileWeapon &&
                            meleeLock.Role == TestCombatContactRole.MeleeContact &&
                            standard.Role == TestCombatContactRole.Standard;
                var ranks = windup.ResistanceRank > meleeLock.ResistanceRank &&
                            meleeLock.ResistanceRank > cooldown.ResistanceRank &&
                            cooldown.ResistanceRank > movingFire.ResistanceRank &&
                            movingFire.ResistanceRank > standard.ResistanceRank;
                var shares = windupShare < meleeShare &&
                             meleeShare < cooldownShare &&
                             cooldownShare < mobileShare &&
                             mobileShare < 0.55f;
                var physical = fixedTravel < meleeTravel &&
                               meleeTravel < cooldownTravel &&
                               cooldownTravel < 1.5f;
                var passed = roles && ranks && shares && physical &&
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                             hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion && exact;
                return new ScenarioResult(
                    passed,
                    $"roles={windup.Role}/{meleeLock.Role}/{cooldown.Role}/" +
                    $"{movingFire.Role}/{standard.Role}, ranks=" +
                    $"{windup.ResistanceRank}/{meleeLock.ResistanceRank}/" +
                    $"{cooldown.ResistanceRank}/{movingFire.ResistanceRank}/" +
                    $"{standard.ResistanceRank}, shares=" +
                    $"{windupShare:0.000}/{meleeShare:0.000}/" +
                    $"{cooldownShare:0.000}/{mobileShare:0.000}, push=" +
                    $"{fixedTravel:0.00}/{meleeTravel:0.00}/{cooldownTravel:0.00}, " +
                    $"versions=package{package.FormatVersion}/hot" +
                    $"{hot.FormatVersion}, exact={exact}");
            })
            .RenderSpawnedUnits();

        session.At(10, "Inspect roles and add identical contact pressure", runtime =>
        {
            fixedWindupContact = runtime.PreviewCombatContact(fixedWindup);
            fixedCooldownContact = runtime.PreviewCombatContact(fixedCooldown);
            mobileContact = runtime.PreviewCombatContact(mobile);
            meleeContact = runtime.PreviewCombatContact(melee);
            foreach (var unit in new[] { fixedWindup, fixedCooldown, mobile, melee })
            {
                contactOrigins[unit.Value] = runtime.Observe(unit).Position;
                var pusher = runtime.SpawnCombat(
                    runtime.Observe(unit).Position - new Vector2(12f, 0f),
                    1,
                    standardProfile);
                var pair = runtime.PreviewCombatContact(unit, pusher);
                standardContact ??= pair.Right;
                if (unit == fixedWindup) fixedWindupPair = pair;
                else if (unit == fixedCooldown) fixedCooldownPair = pair;
                else if (unit == mobile) mobilePair = pair;
                else meleePair = pair;
            }
        });
        session.At(11, "Measure one-tick collision correction", runtime =>
        {
            foreach (var unit in new[] { fixedWindup, fixedCooldown, melee })
            {
                contactDisplacements[unit.Value] = Vector2.Distance(
                    contactOrigins[unit.Value], runtime.Observe(unit).Position);
            }
        });
        session.At(90, "Capture derived contact state boundary", runtime =>
            runtimeCapture = runtime.CaptureRuntimeState());
        return session;
    }

    private static VisualTestSession CreateCombatBuildingDefense(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake,
        BuildingTypeCatalogSnapshot? buildingTypes,
        TechnologyCatalogSnapshot? technologies)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        buildingTypes ??= DemoBuildingTypes.CreateCatalog();
        technologies ??= DemoTechnologies.CreateCatalog();
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(1200f, 700f), 24);
        rig.RegisterPlayer(2, 5000, 3000, 30, 4);
        var workers = new[]
        {
            rig.SpawnWorker(new Vector2(700f, 140f), 2),
            rig.SpawnWorker(new Vector2(680f, 310f), 2),
            rig.SpawnWorker(new Vector2(610f, 500f), 2),
            rig.SpawnWorker(new Vector2(350f, 500f), 2)
        };
        var supply = rig.Build(2, workers[0],
            buildingTypes.Type(0) with { BuildSeconds = 0.5f },
            new Vector2(760f, 140f));
        var barracks = rig.Build(2, workers[1],
            buildingTypes.Type(1) with { BuildSeconds = 0.5f },
            new Vector2(760f, 310f));
        var commandCenter = rig.Build(2, workers[2],
            buildingTypes.Type(2) with { BuildSeconds = 0.5f },
            new Vector2(720f, 500f));
        var academy = rig.Build(2, workers[3],
            buildingTypes.Type(4) with { BuildSeconds = 0.5f },
            new Vector2(430f, 500f));
        var attacker = rig.SpawnCombat(
            new Vector2(400f, 140f), 1,
            new TestCombatProfile(
                100f, 20f, 100f, 240f, 10f, 0.1f, 400f,
                BonusVs: CombatAttribute.Structure,
                BonusDamage: 10f),
            maximumSpeed: 400f,
            acceleration: 1600f);
        var targets = new[]
        {
            supply.BuildingId, barracks.BuildingId, commandCenter.BuildingId
        };
        float[] before = [];
        float[] after = [];
        var weapon = technologies.Technology(0) with { ResearchSeconds = 0.5f };
        var fortification = technologies.Technology(2) with
        {
            ResearchSeconds = 0.5f
        };
        return new VisualTestSession(
            "combat-building-defense",
            "Building armor, size and Fortification share formal damage rules",
            360,
            rig,
            [.. workers, attacker],
            runtime =>
            {
                var impact = runtime.ObserveCombatEvents().Events
                    .LastOrDefault(value => value.Kind == CombatEventKind.Impact &&
                                            value.TargetKind ==
                                                CombatTargetKind.Building);
                var supplyHealth = runtime.ObserveGameplayBuilding(
                    supply.BuildingId).Health;
                var observedArmor = targets.Select(value =>
                    runtime.ObserveGameplayBuilding(value).Armor).ToArray();
                var attackerUnit = runtime.Observe(attacker);
                var attackerCombat = runtime.ObserveCombat(attacker);
                var attackerMovement = runtime.ObserveMovement(attacker);
                var supplySnapshot = runtime.ObserveGameplayBuilding(
                    supply.BuildingId);
                var attackerGap = IndependentRectangleGap(
                    attackerUnit.Position,
                    attackerUnit.Radius,
                    supplySnapshot.Center,
                    supplySnapshot.Size);
                var catalogArmor = new[]
                {
                    buildingTypes.Type(0).Armor,
                    buildingTypes.Type(1).Armor,
                    buildingTypes.Type(2).Armor
                };
                var passed = new[] { supply, barracks, commandCenter, academy }
                                 .All(value => value.Succeeded) &&
                             before.SequenceEqual([30f, 29f, 28f]) &&
                             after.SequenceEqual([29f, 28f, 27f]) &&
                             runtime.TechnologyLevel(2, fortification.Id) == 1 &&
                             impact.Damage == 29f && impact.BonusApplied &&
                             supplyHealth == 371f;
                return new ScenarioResult(
                    passed,
                    $"armor={string.Join(',', catalogArmor)}/" +
                    $"{string.Join(',', observedArmor)}, damage=" +
                    $"{string.Join(',', before)}->" +
                    $"{string.Join(',', after)}, fort=" +
                    $"{runtime.TechnologyLevel(2, fortification.Id)}, " +
                    $"impact={impact.Damage}, hp={supplyHealth}, " +
                    $"attacker={attackerUnit.Position.X:0.##}," +
                    $"{attackerUnit.Position.Y:0.##}/gap{attackerGap:0.###}/" +
                    $"{attackerCombat.State}/target" +
                    $"{attackerCombat.Target}/{attackerMovement.GoalKind}:" +
                    $"{attackerMovement.Result}, " +
                    $"catalog={buildingTypes.StableHashText}");
            })
            .SelectBuildings(targets)
            .At(120, "Capture base structure armor and research weapon prerequisite",
                runtime =>
                {
                    before = targets.Select(value =>
                        runtime.PreviewCombatDamage(attacker, value).TotalDamage)
                        .ToArray();
                    runtime.Research(2, academy.BuildingId, weapon);
                })
            .At(160, "Research Fortification Doctrine", runtime =>
                runtime.Research(2, academy.BuildingId, fortification))
            .At(210, "Capture upgraded armor", runtime =>
                after = targets.Select(value =>
                    runtime.PreviewCombatDamage(attacker, value).TotalDamage)
                    .ToArray())
            .At(220, "Formal attack uses upgraded Supply Depot defense", runtime =>
                runtime.AttackBuilding([attacker], supply.BuildingId));
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
            "Stop and Hold cancel AttackMove travel, then guard locally",
            300,
            rig,
            visible,
            runtime =>
            {
                var stoppedUnit = runtime.Observe(stopped);
                var heldUnit = runtime.Observe(held);
                var stoppedCombat = runtime.ObserveCombat(stopped);
                var heldCombat = runtime.ObserveCombat(held);
                var stoppedCancelledRoute = stoppedUnit.Position.X < 400f;
                var heldCancelledRoute = heldUnit.Position.X < 400f;
                var passed = stoppedCancelledRoute && heldCancelledRoute &&
                             stoppedUnit.State == TestUnitState.Holding &&
                             heldUnit.State == TestUnitState.Holding &&
                             stoppedCombat.State == TestCombatState.Attacking &&
                             heldCombat.State == TestCombatState.Attacking &&
                             heldUnit.Velocity.LengthSquared() < 0.25f &&
                             stoppedCombat.Target == upperEnemy &&
                             heldCombat.Target == lowerEnemy &&
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
        var starts = new Vector2[attackers.Length];
        for (var index = 0; index < attackers.Length; index++)
        {
            starts[index] = new Vector2(512f, 269f + index * 18f);
            attackers[index] = rig.SpawnCombat(
                starts[index],
                team: 1, ranged);
        }
        var target = rig.SpawnCombat(new Vector2(620f, 350f), team: 2, durable);
        var visible = attackers.Append(target).ToArray();
        rig.AttackTarget(attackers, target);
        return new VisualTestSession(
            "combat-ranged-ring",
            "Ranged units already in range fire without reforming",
            240,
            rig,
            visible,
            runtime =>
            {
                var maximumDrift = attackers.Select((value, index) =>
                        Vector2.Distance(runtime.Observe(value).Position,
                            starts[index]))
                    .Max();
                var attacking = attackers.Count(value =>
                    runtime.ObserveCombat(value).State ==
                    TestCombatState.Attacking);
                var targetHealth = runtime.ObserveCombat(target).Health;
                var passed = maximumDrift < 2f && attacking == attackers.Length &&
                             targetHealth < durable.MaximumHealth;
                return new ScenarioResult(
                    passed,
                    $"attacking={attacking}/{attackers.Length}, " +
                    $"max_drift={maximumDrift:F2}, target_hp={targetHealth:F0}");
            });
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
                var snapshots = runtime.Observe(attackers);
                var resumed = snapshots.Count(unit => unit.Position.X >= 930f);
                var incomplete = string.Join(
                    ";",
                    snapshots
                        .Where(unit => unit.Position.X < 930f)
                        .Select(unit =>
                            $"{unit.Id.Value}@{unit.Position.X:F0}," +
                            $"{unit.Position.Y:F0}/{unit.State}->" +
                            $"{unit.AssignedTarget.X:F0},{unit.AssignedTarget.Y:F0}"));
                var diagnostics = runtime.ObserveMovementDiagnostics();
                return new ScenarioResult(
                    dead == defenders.Length && resumed >= 6,
                    $"defeated={dead}/{defenders.Length}, " +
                    $"resumed={resumed}/{attackers.Length}, " +
                    $"settles={diagnostics.DynamicBlockageSettles}, " +
                    $"incomplete=[{incomplete}]");
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
        var nonAttacking = new List<string>();
        var maximumSlotError = 0f;
        foreach (var attacker in attackers)
        {
            var combat = runtime.ObserveCombat(attacker);
            if (combat.State == TestCombatState.Attacking)
            {
                attacking++;
            }
            else
            {
                var snapshot = runtime.Observe(attacker);
                nonAttacking.Add(
                    $"{attacker.Value}:{combat.State}@" +
                    $"{Vector2.Distance(snapshot.Position, targetPosition):0.0}/" +
                    $"e{Vector2.Distance(snapshot.Position, combat.AttackPosition):0.0}");
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
            $"unique={unique}, radiusBand={radiusInBand}, maxSlotError={maximumSlotError:F1}, " +
            $"inactive=[{string.Join(',', nonAttacking)}]");
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
                    runtime.Observe(unit).State == TestUnitState.Arrived &&
                    Vector2.Distance(
                        runtime.Observe(unit).Position,
                        runtime.Observe(unit).AssignedTarget) <= 8.1f);
                return new ScenarioResult(
                    current.Length == 6 && arrived == 6,
                    $"members={current.Length}, arrived={arrived}/6");
            });
    }

    private static VisualTestSession CreateControlGroupMixedSteal(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            16, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 1200, 200, 20, 3);
        var workers = new[]
        {
            rig.SpawnWorker(new Vector2(170f, 230f), 1),
            rig.SpawnWorker(new Vector2(210f, 320f), 1)
        };
        var marine = rig.SpawnCombat(new Vector2(270f, 350f), 1);
        var barracks = rig.Build(
            1, workers[0],
            DemoBuildingTypes.Barracks with { BuildSeconds = 0.5f },
            new Vector2(390f, 260f));
        return new VisualTestSession(
            "control-group-mixed-steal",
            "Mixed unit/building groups support Alt assign and Alt+Shift add stealing",
            720,
            rig,
            [workers[0], workers[1], marine],
            runtime =>
            {
                var first = runtime.RecallMixedControlGroup(1);
                var second = runtime.RecallMixedControlGroup(2);
                var marineState = runtime.Observe(marine);
                var workerArrived = second.Units.Count(unit =>
                    Vector2.Distance(
                        runtime.Observe(unit).Position,
                        runtime.Observe(unit).AssignedTarget) < 10f);
                var marineArrived = Vector2.Distance(
                    marineState.Position, marineState.AssignedTarget) < 10f;
                var passed = barracks.Succeeded &&
                             first.Units.SequenceEqual([marine]) &&
                             first.Buildings.Length == 0 &&
                             second.Units.SequenceEqual(workers) &&
                             second.Buildings.SequenceEqual([barracks.BuildingId]) &&
                             workerArrived == 2 &&
                             marineArrived;
                return new ScenarioResult(
                    passed,
                    $"group1={first.Units.Length}u/{first.Buildings.Length}b, " +
                    $"group2={second.Units.Length}u/{second.Buildings.Length}b, " +
                    $"arrived={workerArrived + (marineArrived ? 1 : 0)}/3");
            })
            .SelectBuildings(barracks.BuildingId)
            .At(140, "Ctrl+1 stores two workers, Marine and Barracks", runtime =>
                runtime.AssignMixedControlGroup(
                    1, [workers[1], marine, workers[0]], [barracks.BuildingId]))
            .At(160, "Alt+2 steals one worker from group 1", runtime =>
                runtime.StealAssignControlGroup(2, [workers[0]], []))
            .At(180, "Alt+Shift+2 steals the second worker and Barracks", runtime =>
            {
                runtime.StealAddControlGroup(
                    2, [workers[1]], [barracks.BuildingId]);
                var first = runtime.RecallMixedControlGroup(1);
                var second = runtime.RecallMixedControlGroup(2);
                runtime.Move(first.Units, new Vector2(930f, 210f));
                runtime.Move(second.Units, new Vector2(930f, 480f));
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

    private static VisualTestSession CreateSmartCommandGameplayContext(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        const float workerMaximumSpeed = 150f;
        const long resumeTick = 150;
        var supplyProfile = DemoBuildingTypes.SupplyDepot with
        {
            BuildSeconds = 4f
        };
        var maximumMapTravelTicks = (long)Math.Ceiling(
            (navigationMap.WorldBounds.Width + navigationMap.WorldBounds.Height) /
            workerMaximumSpeed * 60f);
        var buildTicks = (long)Math.Ceiling(supplyProfile.BuildSeconds * 60f);
        var completionDeadline = resumeTick + maximumMapTravelTicks +
                                 buildTicks + 60;
        var durationTicks = checked((int)completionDeadline + 1);
        var rig = MovementTestRig.CreateReplayPackageMap(
            16, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 1000, 0, 15, 3);
        var mineral = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(330f, 380f), 2000, 5, 0.35f, 2);
        rig.AddResourceDropOff(1, new Vector2(230f, 380f));
        var firstWorker = rig.SpawnWorker(new Vector2(150f, 210f), 1);
        var secondWorker = rig.SpawnWorker(new Vector2(210f, 250f), 1);
        var marine = rig.SpawnCombat(new Vector2(190f, 330f), 1);
        rig.StartReplayPackageRecording();

        var supply = new TestConstructionResult(
            TestConstructionCommandCode.InvalidProfile,
            new TestGameplayBuildingId(-1),
            TestBuildingPlacementCode.InvalidFootprint);
        var gatherResult = TestPlayerOrderCommandCode.InvalidTarget;
        var queuedResult = TestPlayerOrderCommandCode.InvalidTarget;
        var resumeResult = TestPlayerOrderCommandCode.InvalidTarget;
        var interrupted = false;
        var resourceSplitObserved = false;
        var completionObserved = false;
        var completionTick = long.MinValue;
        return new VisualTestSession(
            "smart-command-gameplay-context",
            "Business SmartCommand splits mixed selections into gather, resume and move intents",
            durationTicks,
            rig,
            [firstWorker, secondWorker, marine],
            runtime =>
            {
                var building = runtime.ObserveGameplayBuilding(supply.BuildingId);
                var first = runtime.ObserveWorkerEconomy(firstWorker);
                var second = runtime.ObserveWorkerEconomy(secondWorker);
                var marineState = runtime.Observe(marine);
                var package = runtime.CaptureReplayPackage();
                var replay = runtime.ReplayPackage(package, runtime.Tick);
                var exact = replay.FinalHash == runtime.StateHash;
                var workersResolved = first.State == TestWorkerEconomyState.Idle &&
                                      second.State == TestWorkerEconomyState.Idle;
                var marineMoved = Vector2.Distance(
                                      marineState.Position, building.Center) < 90f &&
                                  Vector2.Distance(
                                      marineState.Position,
                                      new Vector2(190f, 330f)) > 100f;
                var passed = supply.Succeeded && interrupted &&
                             gatherResult == TestPlayerOrderCommandCode.Success &&
                             queuedResult == TestPlayerOrderCommandCode.Success &&
                             resumeResult == TestPlayerOrderCommandCode.Success &&
                             completionObserved &&
                             building.State == TestBuildingLifecycleState.Completed &&
                             building.BuilderUnit == firstWorker &&
                             resourceSplitObserved && workersResolved && marineMoved && exact &&
                             package.EconomyCommandCount == 2 &&
                             package.ConstructionCommandCount == 2;
                return new ScenarioResult(
                    passed,
                    $"smart={gatherResult}/{queuedResult}/{resumeResult}, " +
                    $"building={building.State}/builder{building.BuilderUnit.Value}, " +
                    $"split={resourceSplitObserved}, workers={first.State}/{second.State}, " +
                    $"marine={marineState.Position.X:F0},{marineState.Position.Y:F0}, " +
                    $"completedAt={completionTick}/{completionDeadline}, " +
                    $"commands=e{package.EconomyCommandCount}/b{package.ConstructionCommandCount}/" +
                    $"u{package.UnitCommandCount}, replay={exact}");
            })
            .At(1, "Place a real gameplay building", runtime =>
                supply = runtime.Build(
                    1, firstWorker,
                    supplyProfile,
                    new Vector2(400f, 260f)))
            .At(30, "Player Move interrupts the active builder", runtime =>
            {
                runtime.PlayerMove(1, [firstWorker], new Vector2(300f, 420f));
                interrupted = runtime.ObserveGameplayBuilding(supply.BuildingId).State ==
                              TestBuildingLifecycleState.WaitingForBuilder;
            })
            .At(45, "Right-click mineral: workers gather, marine moves", runtime =>
                gatherResult = runtime.PlayerSmartResource(
                    1, [firstWorker, secondWorker, marine], mineral))
            .At(90, "Queued resource task is accepted", runtime =>
                queuedResult = runtime.PlayerSmartResource(
                    1, [secondWorker], mineral, queued: true))
            .At(120, "Observe mixed-selection context split", runtime =>
            {
                var first = runtime.ObserveWorkerEconomy(firstWorker);
                var second = runtime.ObserveWorkerEconomy(secondWorker);
                resourceSplitObserved =
                    first.State is not TestWorkerEconomyState.None and
                        not TestWorkerEconomyState.Idle &&
                    second.State is not TestWorkerEconomyState.None and
                        not TestWorkerEconomyState.Idle &&
                    Vector2.Distance(
                        runtime.Observe(marine).Position,
                        new Vector2(330f, 380f)) < 45f;
            })
            .At(resumeTick, "Unordered mixed selection deterministically resumes lowest worker", runtime =>
                resumeResult = runtime.PlayerSmartFriendlyBuilding(
                    1, [secondWorker, marine, firstWorker], supply.BuildingId))
            .When(
                "Wait for resumed builder to finish the real construction cycle",
                completionDeadline,
                runtime => supply.Succeeded &&
                           runtime.ObserveGameplayBuilding(supply.BuildingId).State ==
                           TestBuildingLifecycleState.Completed,
                runtime =>
                {
                    completionObserved = true;
                    completionTick = runtime.Tick;
                })
            .FinishWhenConditionsCompleted();
    }

    private static VisualTestSession CreateSmartCommandShiftWorkerTasks(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            20, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 1200, 0, 15, 4);
        var durableMineral = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(330f, 380f), 2000, 5, 0.25f, 2);
        var expiringMineral = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(330f, 500f), 5, 5, 0.1f, 1);
        rig.AddResourceDropOff(1, new Vector2(230f, 420f));
        var gatherWorker = rig.SpawnWorker(new Vector2(120f, 150f), 1);
        var builderWorker = rig.SpawnWorker(new Vector2(230f, 260f), 1);
        var failureWorker = rig.SpawnWorker(new Vector2(100f, 500f), 1);
        var depletionWorker = rig.SpawnWorker(new Vector2(300f, 500f), 1);
        rig.StartReplayPackageRecording();

        var supply = default(TestConstructionResult);
        var resourceQueued = TestPlayerOrderCommandCode.InvalidTarget;
        var resumeQueued = TestPlayerOrderCommandCode.InvalidTarget;
        var failureQueued = TestPlayerOrderCommandCode.InvalidTarget;
        var pendingCaptured = false;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 60;
        return new VisualTestSession(
            "smart-command-shift-worker-tasks",
            "Versioned worker tasks queue Move to Gather/Resume and skip stale targets",
            900,
            rig,
            [gatherWorker, builderWorker, failureWorker, depletionWorker],
            runtime =>
            {
                var building = runtime.ObserveGameplayBuilding(supply.BuildingId);
                var gatherState = runtime.ObserveWorkerEconomy(gatherWorker);
                var gatherOrders = runtime.ObserveOrders(gatherWorker);
                var builderOrders = runtime.ObserveOrders(builderWorker);
                var failureOrders = runtime.ObserveOrders(failureWorker);
                var failurePosition = runtime.Observe(failureWorker).Position;
                var expired = runtime.ObserveResourceNode(expiringMineral).Remaining == 0;
                var package = runtime.CaptureReplayPackage();
                var commandLog = runtime.CaptureCommandLog();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decodedPackage);
                var logRoundTrip = commandLog.TryCanonicalRoundTrip(out var decodedLog);
                var hot = runtime.BindHotSnapshot(package, hotCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var replay = runtime.ReplayPackage(decodedPackage!, runtime.Tick);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var exact = replay.FinalHash == runtime.StateHash &&
                            resumed.FinalHash == runtime.StateHash &&
                            replay.MatchesFrom(resumed, hotTick);
                var gatherActive = gatherOrders.ActiveOrder ==
                                       TestOrderKind.GatherResource &&
                                   gatherState.TargetNode == durableMineral &&
                                   gatherState.State is not TestWorkerEconomyState.None and
                                       not TestWorkerEconomyState.Idle;
                var failedTaskSkipped = expired &&
                    Vector2.Distance(failurePosition, new Vector2(900f, 500f)) < 18f &&
                    failureOrders.PendingOrders == 0 &&
                    failureOrders.CompletedQueuedOrders == 2;
                var passed = supply.Succeeded && pendingCaptured &&
                             resourceQueued == TestPlayerOrderCommandCode.Success &&
                             resumeQueued == TestPlayerOrderCommandCode.Success &&
                             failureQueued == TestPlayerOrderCommandCode.Success &&
                             building.State == TestBuildingLifecycleState.Completed &&
                             builderOrders.PendingOrders == 0 &&
                             builderOrders.CompletedQueuedOrders == 1 &&
                             gatherActive && failedTaskSkipped &&
                             commandLog.FormatVersion ==
                                 SimulationCommandLogSnapshot.CurrentFormatVersion &&
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion && hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion &&
                             packageRoundTrip && logRoundTrip && hotRoundTrip &&
                             decodedLog!.StableHash == commandLog.StableHash && exact;
                return new ScenarioResult(
                    passed,
                    $"queued={resourceQueued}/{resumeQueued}/{failureQueued}, " +
                    $"pendingCapture={pendingCaptured}, gather={gatherOrders.ActiveOrder}/" +
                    $"{gatherState.State}@{gatherState.TargetNode.Value}, " +
                    $"build={building.State}/q{builderOrders.CompletedQueuedOrders}, " +
                    $"stale={expired}/{failureOrders.CompletedQueuedOrders}/" +
                    $"{failurePosition.X:F0},{failurePosition.Y:F0}, " +
                    $"versions=log{commandLog.FormatVersion}/package{package.FormatVersion}/" +
                    $"hot{hot.FormatVersion}, commands=e{package.EconomyCommandCount}/" +
                    $"b{package.ConstructionCommandCount}/u{package.UnitCommandCount}, " +
                    $"exact={exact}");
            })
            .At(1, "Place construction target", runtime =>
                supply = runtime.Build(
                    1, builderWorker,
                    DemoBuildingTypes.SupplyDepot with { BuildSeconds = 4f },
                    new Vector2(400f, 260f)))
            .At(30, "Interrupt builder with an immediate Move", runtime =>
                runtime.PlayerMove(
                    1, [builderWorker], new Vector2(180f, 430f)))
            .At(45, "Queue Move to Gather and Move to Resume", runtime =>
            {
                runtime.Gather(1, depletionWorker, expiringMineral);
                runtime.PlayerMove(1, [gatherWorker], new Vector2(650f, 380f));
                resourceQueued = runtime.PlayerSmartResource(
                    1, [gatherWorker], durableMineral, queued: true);
                resumeQueued = runtime.PlayerSmartFriendlyBuilding(
                    1, [builderWorker], supply.BuildingId, queued: true);

                runtime.PlayerMove(1, [failureWorker], new Vector2(650f, 500f));
                failureQueued = runtime.PlayerSmartResource(
                    1, [failureWorker], expiringMineral, queued: true);
                runtime.PlayerMove(
                    1, [failureWorker], new Vector2(900f, 500f), queued: true);
            })
            .At(hotTick, "Capture three pending cross-domain tasks", runtime =>
            {
                pendingCaptured = runtime.ObserveOrders(gatherWorker).PendingOrders == 1 &&
                                  runtime.ObserveOrders(builderWorker).PendingOrders == 1 &&
                                  runtime.ObserveOrders(failureWorker).PendingOrders == 2;
                hotCapture = runtime.CaptureRuntimeState();
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

    private static VisualTestSession CreateOperationMixedCommandCard(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            16, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 2000, 500, 20, 3);
        var workers = new[]
        {
            rig.SpawnWorker(new Vector2(180f, 220f), 1),
            rig.SpawnWorker(new Vector2(220f, 320f), 1)
        };
        var marine = rig.SpawnCombat(new Vector2(300f, 340f), 1);
        var barracks = rig.Build(
            1, workers[0],
            DemoBuildingTypes.Barracks with { BuildSeconds = 0.5f },
            new Vector2(400f, 260f));
        var recipe = DemoProductionCatalog.CreateSnapshot().Recipe(0) with
        {
            ProductionSeconds = 2f
        };
        var firstOrder = default(TestProductionResult);
        var canceled = false;
        var replacement = default(TestProductionResult);
        var initialUnits = rig.UnitCount;
        return new VisualTestSession(
            "operation-mixed-command-card",
            "Mixed worker, combat and building subgroups drive a snapshot-only command card",
            540,
            rig,
            [workers[0], workers[1], marine],
            runtime =>
            {
                var building = runtime.ObserveGameplayBuilding(barracks.BuildingId);
                var selection = GameplaySelectionSnapshot.Create(
                [
                    new(GameplaySelectionKind.Worker, workers[1].Value, 2,
                        "Worker", runtime.Observe(workers[1]).Position),
                    new(GameplaySelectionKind.CombatUnit, marine.Value, 0,
                        "Marine", runtime.Observe(marine).Position),
                    new(GameplaySelectionKind.Worker, workers[0].Value, 2,
                        "Worker", runtime.Observe(workers[0]).Position),
                    new(GameplaySelectionKind.Building, barracks.BuildingId.Value,
                        DemoBuildingTypes.Barracks.Id, "Barracks",
                        new Vector2(400f, 260f))
                ], new SelectionSubgroupKey(
                    GameplaySelectionKind.Building, DemoBuildingTypes.Barracks.Id));
                var card = CommandCardComposer.Compose(selection,
                [
                    new(selection.ActiveSubgroup!.Key,
                        CommandCardActionKind.Train,
                        barracks.BuildingId.Value, recipe.Id,
                        "Train Marine", true, "Success", 10)
                ]);
                var passed = barracks.Succeeded && firstOrder.Succeeded && canceled &&
                             replacement.Succeeded &&
                             building.State == TestBuildingLifecycleState.Completed &&
                             runtime.ObserveProduction(barracks.BuildingId).OrderCount == 0 &&
                             runtime.UnitCount == initialUnits + 1 &&
                             selection.Entities.Length == 4 &&
                             selection.Subgroups.Length == 3 &&
                             selection.ActiveSubgroup?.Name == "Barracks" &&
                             card.Actions.Length == 1 && card.Actions[0].Enabled;
                return new ScenarioResult(
                    passed,
                    $"selection={selection.Entities.Length}/groups{selection.Subgroups.Length}/" +
                    $"active{selection.ActiveSubgroup?.Name}, card={card.Actions.Length}, " +
                    $"orders={firstOrder.Code}/{canceled}/{replacement.Code}, " +
                    $"building={building.State}, units={runtime.UnitCount}");
            })
            .SelectBuildings(barracks.BuildingId)
            .At(150, "Command card availability follows completed Barracks", runtime =>
                firstOrder = runtime.Train(1, barracks.BuildingId, recipe))
            .At(180, "Cancel active production through the same business command", runtime =>
                canceled = firstOrder.Succeeded &&
                           runtime.CancelProduction(1, firstOrder.OrderId))
            .At(210, "Requeue Marine after refund", runtime =>
                replacement = runtime.Train(1, barracks.BuildingId, recipe));
    }

    private static VisualTestSession CreateOperationTargetCommandMode(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            16, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 1600, 300, 20, 3);
        var workers = new[]
        {
            rig.SpawnWorker(new Vector2(170f, 230f), 1),
            rig.SpawnWorker(new Vector2(210f, 330f), 1)
        };
        var marine = rig.SpawnCombat(new Vector2(270f, 350f), 1);
        var barracks = rig.Build(
            1, workers[0],
            DemoBuildingTypes.Barracks with { BuildSeconds = 0.5f },
            new Vector2(390f, 260f));
        var move = rig.BeginTargetCommand(
            1, TestTargetCommandKind.Move, workers, [],
            "Move / Shift multi-target");
        var attackMove = rig.BeginTargetCommand(
            1, TestTargetCommandKind.AttackMove, [marine], [],
            "Attack Move");
        var rally = rig.BeginTargetCommand(
            1, TestTargetCommandKind.Rally, [], [barracks.BuildingId],
            "Set Rally");
        var moveFirst = default(TestTargetCommandResult);
        var moveSecond = default(TestTargetCommandResult);
        var canceled = default(TestTargetCommandResult);
        var rallyResult = default(TestTargetCommandResult);
        var attackResult = default(TestTargetCommandResult);
        var workerTarget = new Vector2(900f, 470f);
        var marineTarget = new Vector2(870f, 220f);
        var rallyPoint = new Vector2(760f, 350f);
        return new VisualTestSession(
            "operation-target-command-mode",
            "Command card target mode queues Move and issues Attack Move and Rally",
            1080,
            rig,
            [workers[0], workers[1], marine],
            runtime =>
            {
                var workersArrived = workers.Count(unit =>
                {
                    var state = runtime.Observe(unit);
                    return Vector2.Distance(state.Position, state.AssignedTarget) < 10f &&
                           Vector2.Distance(state.AssignedTarget, workerTarget) < 60f;
                });
                var marineState = runtime.Observe(marine);
                var marineArrived = Vector2.Distance(
                        marineState.Position, marineState.AssignedTarget) < 10f &&
                    Vector2.Distance(marineState.AssignedTarget, marineTarget) < 60f;
                var rallyState = runtime.ObserveProductionRally(barracks.BuildingId);
                var queuedCompleted = workers.All(unit =>
                    runtime.ObserveOrders(unit).CompletedQueuedOrders >= 1);
                var passed = barracks.Succeeded &&
                             moveFirst.Issued && moveFirst.Queued &&
                             moveFirst.KeepTargeting &&
                             moveSecond.Issued && moveSecond.KeepTargeting &&
                             canceled.Canceled && !canceled.KeepTargeting &&
                             rallyResult.Issued && !rallyResult.KeepTargeting &&
                             attackResult.Issued && !attackResult.KeepTargeting &&
                             workersArrived == 2 && marineArrived && queuedCompleted &&
                             rallyState.Kind == TestRallyTargetKind.Ground &&
                             Vector2.Distance(rallyState.Position, rallyPoint) < 0.1f;
                return new ScenarioResult(
                    passed,
                    $"move={moveFirst.Issued}/{moveSecond.Issued}/" +
                    $"cancel{canceled.Canceled}, attack={attackResult.Issued}, " +
                    $"rally={rallyState.Kind}, arrived={workersArrived + (marineArrived ? 1 : 0)}/3");
            })
            .SelectBuildings(barracks.BuildingId)
            .ShowTargetCommandPreview(move)
            .At(150, "Move target mode: Shift keeps targeting", runtime =>
                moveFirst = runtime.ResolveTargetCommand(
                    move, new Vector2(570f, 180f), shiftPressed: true))
            .At(180, "Second Shift target appends a waypoint", runtime =>
                moveSecond = runtime.ResolveTargetCommand(
                    move, workerTarget, shiftPressed: true))
            .At(210, "Right click cancels target mode without issuing", runtime =>
                canceled = runtime.ResolveTargetCommand(
                    move, default, shiftPressed: false, cancel: true))
            .At(240, "Rally target applies through the production command", runtime =>
                rallyResult = runtime.ResolveTargetCommand(
                    rally, rallyPoint, shiftPressed: false))
            .At(270, "Attack Move target uses the player command gate", runtime =>
                attackResult = runtime.ResolveTargetCommand(
                    attackMove, marineTarget, shiftPressed: false));
    }

    private static VisualTestSession CreateOperationBuildPlacementMode(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            12, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 1200, 300, 20, 2);
        var workers = new[]
        {
            rig.SpawnWorker(new Vector2(120f, 500f), 1),
            rig.SpawnWorker(new Vector2(250f, 220f), 1)
        };
        var request = rig.BeginTargetCommand(
            1, TestTargetCommandKind.Build, workers, [],
            "Build Supply Depot", DemoBuildingTypes.SupplyDepot.Id);
        var initialBuildings = rig.GameplayBuildingCount;
        var invalidPreview = default(TestBuildTargetPreview);
        var invalidIssue = default(TestTargetCommandResult);
        var validPreview = default(TestBuildTargetPreview);
        var validIssue = default(TestTargetCommandResult);
        var countAfterInvalid = -1;
        var invalidCenter = new Vector2(620f, 200f);
        var validCenter = new Vector2(400f, 220f);
        return new VisualTestSession(
            "operation-build-placement-mode",
            "Build target preview rejects invalid ground and commits with the nearest worker",
            480,
            rig,
            workers,
            runtime =>
            {
                var depot = validIssue.Issued
                    ? runtime.ObserveGameplayBuilding(validIssue.Building)
                    : default;
                var passed = invalidPreview.Code ==
                                 TestConstructionCommandCode.PlacementRejected &&
                             invalidPreview.PlacementCode ==
                                 TestBuildingPlacementCode.StaticObstacleOverlap &&
                             !invalidIssue.Issued && invalidIssue.KeepTargeting &&
                             countAfterInvalid == initialBuildings &&
                             validPreview.CanPlace &&
                             validPreview.Builder == workers[1] &&
                             validIssue.Issued && !validIssue.KeepTargeting &&
                             runtime.GameplayBuildingCount == initialBuildings + 1 &&
                             depot.BuilderUnit == workers[1] &&
                             depot.State == TestBuildingLifecycleState.Completed;
                return new ScenarioResult(
                    passed,
                    $"invalid={invalidPreview.PlacementCode}/keep{invalidIssue.KeepTargeting}, " +
                    $"valid={validPreview.CanPlace}/builder{validPreview.Builder.Value}, " +
                    $"issued={validIssue.Issued}, state={depot.State}");
            })
            .ShowTargetCommandPreview(request, invalidCenter)
            .At(30, "Static obstacle preview is rejected without mutation", runtime =>
            {
                invalidPreview = runtime.PreviewBuildTarget(
                    request, invalidCenter);
                invalidIssue = runtime.ResolveTargetCommand(
                    request, invalidCenter, shiftPressed: false);
                countAfterInvalid = runtime.GameplayBuildingCount;
            })
            .MoveTargetCommandPreviewPointerAt(
                90, "Valid preview chooses the nearest eligible worker", validCenter)
            .At(90, "Valid preview chooses the nearest eligible worker", runtime =>
            {
                validPreview = runtime.PreviewBuildTarget(request, validCenter);
            })
            .At(150, "Confirm valid footprint through the construction command", runtime =>
            {
                validIssue = runtime.ResolveTargetCommand(
                    request, validCenter, shiftPressed: false);
            });
    }

    private static VisualTestSession CreateOperationProductionGroupBatch(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            24, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 4000, 1000, 30, 3);
        var workers = new[]
        {
            rig.SpawnWorker(new Vector2(100f, 150f), 1),
            rig.SpawnWorker(new Vector2(100f, 350f), 1),
            rig.SpawnWorker(new Vector2(280f, 550f), 1)
        };
        var profile = DemoBuildingTypes.Barracks with { BuildSeconds = 0.5f };
        var builds = new[]
        {
            rig.Build(1, workers[0], profile, new Vector2(300f, 150f)),
            rig.Build(1, workers[1], profile, new Vector2(300f, 350f)),
            rig.Build(1, workers[2], profile, new Vector2(420f, 550f))
        };
        var producers = builds.Select(value => value.BuildingId).ToArray();
        var recipe = DemoProductionCatalog.CreateSnapshot().Recipe(0) with
        {
            ProductionSeconds = 2f
        };
        var initialUnits = rig.UnitCount;
        var first = default(TestProductionBatchResult);
        var second = default(TestProductionBatchResult);
        var afterSecond = default(TestProductionGroupSnapshot);
        var canceled = -1;
        var afterCancel = default(TestProductionGroupSnapshot);
        return new VisualTestSession(
            "operation-production-group-batch",
            "Three Barracks fan out Train and cancel the newest order per producer",
            600,
            rig,
            workers,
            runtime =>
            {
                var finalGroup = runtime.ObserveProductionGroup(producers);
                var passed = builds.All(value => value.Succeeded) &&
                             first.Producers == 3 && first.Planned == 3 &&
                             first.Succeeded == 3 &&
                             second.Planned == 3 && second.Succeeded == 3 &&
                             afterSecond.TotalOrders == 6 &&
                             afterSecond.QueueLengths.SequenceEqual([2, 2, 2]) &&
                             canceled == 3 && afterCancel.TotalOrders == 3 &&
                             afterCancel.QueueLengths.SequenceEqual([1, 1, 1]) &&
                             finalGroup.TotalOrders == 0 &&
                             runtime.UnitCount == initialUnits + 3;
                return new ScenarioResult(
                    passed,
                    $"batch={first.Succeeded}/{second.Succeeded}, " +
                    $"queued={afterSecond.TotalOrders}->{afterCancel.TotalOrders}->" +
                    $"{finalGroup.TotalOrders}, cancel={canceled}, " +
                    $"spawned={runtime.UnitCount - initialUnits}");
            })
            .SelectBuildings(producers)
            .RenderSpawnedUnits()
            .At(120, "Train Marine fans out to all three Barracks", runtime =>
                first = runtime.TrainBatch(1, producers, recipe))
            .At(150, "Second click appends one Marine to every queue", runtime =>
            {
                second = runtime.TrainBatch(1, producers, recipe);
                afterSecond = runtime.ObserveProductionGroup(producers);
            })
            .At(180, "Cancel newest removes one matching order per Barracks", runtime =>
            {
                canceled = runtime.CancelNewestProductionBatch(
                    1, producers, recipe.Id);
                afterCancel = runtime.ObserveProductionGroup(producers);
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
                    exact && rejected && hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion &&
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
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion && hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion &&
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

    private static VisualTestSession CreateExplicitReturnCargo(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            16, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 0, 0, 10, 4);
        rig.AddResourceDropOff(1, new Vector2(170f, 350f));
        var mineral = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(430f, 270f), 1_000, 5, 0.3f, 3);
        var gas = rig.AddResourceNode(
            TestEconomyResourceKind.VespeneGas,
            new Vector2(455f, 455f), 1_000, 4, 0.35f, 3,
            requiresRefinery: true, operational: true);
        var workers = new[]
        {
            rig.SpawnWorker(new Vector2(375f, 245f), 1),
            rig.SpawnWorker(new Vector2(390f, 275f), 1),
            rig.SpawnWorker(new Vector2(405f, 305f), 1),
            rig.SpawnWorker(new Vector2(420f, 335f), 1)
        };

        rig.StartReplayPackageRecording();
        var noCargoRejected = rig.ReturnCargo(1, workers[0]) ==
                              TestReturnCargoCommandCode.NoCargo;
        foreach (var worker in workers)
            rig.Gather(1, worker, mineral);

        var explicitReturnIssued = false;
        var explicitReturnResumed = false;
        var stopIssued = false;
        var stoppedReturnIssued = false;
        var stoppedReturnEndedIdle = false;
        var retargetIssued = false;
        var retargetPreservedCargo = false;
        var reverseRetargetIssued = false;
        var reverseRetargetPreservedCargo = false;
        var queuedReturnIssued = false;
        var queuedReturnCompleted = false;
        var gasDelivered = false;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 300;

        var session = new VisualTestSession(
                "economy-explicit-return-cargo",
                "Return Cargo preserves carried resources and deterministic gather intent",
                720,
                rig,
                workers,
                runtime =>
                {
                    var package = runtime.CaptureReplayPackage();
                    var economyLog = runtime.CaptureEconomyCommandLog();
                    var commandLog = runtime.CaptureCommandLog();
                    var packageRoundTrip = package.TryCanonicalRoundTrip(
                        out var decodedPackage);
                    var economyRoundTrip = economyLog.TryCanonicalRoundTrip(
                        out var decodedEconomy);
                    var baseline = packageRoundTrip
                        ? runtime.ReplayPackage(decodedPackage!, runtime.Tick)
                        : null;
                    var exactReplay = baseline is not null &&
                                      baseline.FinalHash == runtime.StateHash;
                    var hotExact = false;
                    var hotVersion = 0;
                    if (hotCapture is not null)
                    {
                        var hot = runtime.BindHotSnapshot(package, hotCapture);
                        hotVersion = hot.FormatVersion;
                        if (hot.TryCanonicalRoundTrip(out var decodedHot))
                        {
                            var resumed = runtime.ResumeHotSnapshot(
                                package, decodedHot!, runtime.Tick);
                            hotExact = resumed.FinalHash == runtime.StateHash &&
                                       baseline is not null &&
                                       baseline.MatchesFrom(resumed, hotTick);
                        }
                    }
                    var versions =
                        economyLog.FormatVersion ==
                            EconomyCommandLogSnapshot.CurrentFormatVersion &&
                        commandLog.FormatVersion ==
                            SimulationCommandLogSnapshot.CurrentFormatVersion &&
                        package.FormatVersion ==
                            SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                        hotVersion == SimulationHotSnapshot.CurrentFormatVersion;
                    var passed = noCargoRejected && explicitReturnIssued &&
                                 explicitReturnResumed && stopIssued &&
                                 stoppedReturnIssued && stoppedReturnEndedIdle &&
                                 retargetIssued && retargetPreservedCargo &&
                                 reverseRetargetIssued &&
                                 reverseRetargetPreservedCargo &&
                                 queuedReturnIssued && queuedReturnCompleted &&
                                 gasDelivered && packageRoundTrip &&
                                 economyRoundTrip &&
                                 decodedEconomy!.StableHash == economyLog.StableHash &&
                                 package.EconomyCommandCount == 8 &&
                                 package.UnitCommandCount == 3 &&
                                 exactReplay && hotExact && versions;
                    var economy = runtime.ObservePlayerEconomy(1);
                    return new ScenarioResult(
                        passed,
                        $"return={explicitReturnIssued}/{explicitReturnResumed}, " +
                        $"stop-return={stopIssued}/{stoppedReturnIssued}/" +
                        $"{stoppedReturnEndedIdle}, retarget={retargetIssued}/" +
                        $"{retargetPreservedCargo}/{reverseRetargetIssued}/" +
                        $"{reverseRetargetPreservedCargo}, queued=" +
                        $"{queuedReturnIssued}/{queuedReturnCompleted}, resources=" +
                        $"{economy.Minerals}/{economy.VespeneGas}, " +
                        $"logs=e{package.EconomyCommandCount}v" +
                        $"{economyLog.FormatVersion}/u{package.UnitCommandCount}v" +
                        $"{commandLog.FormatVersion}, replay={exactReplay}, " +
                        $"hot={hotExact}/v{hotVersion}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(330f, 350f), 1.08f)
            .CameraKeyframe(220, new Vector2(300f, 350f), 1.18f)
            .CameraKeyframe(440, new Vector2(385f, 365f), 1.12f)
            .CameraKeyframe(700, new Vector2(330f, 350f), 1.08f)
            .Highlight(
                new SimRect(new Vector2(105f, 275f), new Vector2(235f, 425f)),
                "DROP-OFF: explicit return completes before next intent",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(385f, 215f), new Vector2(485f, 325f)),
                "MINERALS: return resumes only when gather intent remains",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(405f, 405f), new Vector2(505f, 505f)),
                "GAS: carried minerals survive cross-resource retarget",
                TestDiagnosticKind.Accepted);

        for (var tick = 1; tick < session.DurationTicks; tick++)
        {
            session.At(tick, "Observe and exercise Return Cargo contract", runtime =>
            {
                var first = runtime.ObserveWorkerEconomy(workers[0]);
                var second = runtime.ObserveWorkerEconomy(workers[1]);
                var third = runtime.ObserveWorkerEconomy(workers[2]);
                var fourth = runtime.ObserveWorkerEconomy(workers[3]);

                if (!explicitReturnIssued &&
                    first.State == TestWorkerEconomyState.ReturningCargo &&
                    first.CargoAmount > 0)
                {
                    explicitReturnIssued = runtime.ReturnCargo(1, workers[0]) ==
                                           TestReturnCargoCommandCode.Success;
                }
                else if (explicitReturnIssued && first.CargoAmount == 0 &&
                         first.TargetNode == mineral &&
                         first.State is TestWorkerEconomyState.GoingToResource or
                             TestWorkerEconomyState.Gathering)
                {
                    explicitReturnResumed = true;
                }

                if (!stopIssued &&
                    second.State == TestWorkerEconomyState.ReturningCargo &&
                    second.CargoAmount > 0)
                {
                    runtime.Stop([workers[1]]);
                    stopIssued = true;
                }
                else if (stopIssued && !stoppedReturnIssued &&
                         second.State == TestWorkerEconomyState.Idle &&
                         second.CargoAmount > 0)
                {
                    stoppedReturnIssued = runtime.ReturnCargo(1, workers[1]) ==
                                          TestReturnCargoCommandCode.Success;
                }
                else if (stoppedReturnIssued && second.CargoAmount == 0 &&
                         second.State == TestWorkerEconomyState.Idle)
                {
                    stoppedReturnEndedIdle = true;
                }

                if (!retargetIssued &&
                    third.State == TestWorkerEconomyState.ReturningCargo &&
                    third.CargoAmount > 0)
                {
                    var cargo = third.CargoAmount;
                    retargetIssued = runtime.Gather(1, workers[2], gas) ==
                                     TestGatherCommandCode.Success;
                    var after = runtime.ObserveWorkerEconomy(workers[2]);
                    retargetPreservedCargo = retargetIssued &&
                                             after.CargoAmount == cargo &&
                                             after.TargetNode == gas &&
                                             after.State ==
                                                 TestWorkerEconomyState.ReturningCargo;
                }
                else if (retargetIssued && !reverseRetargetIssued &&
                         third.State == TestWorkerEconomyState.ReturningCargo &&
                         third.CargoKind == TestEconomyResourceKind.VespeneGas &&
                         third.CargoAmount > 0)
                {
                    var cargo = third.CargoAmount;
                    reverseRetargetIssued =
                        runtime.Gather(1, workers[2], mineral) ==
                        TestGatherCommandCode.Success;
                    var after = runtime.ObserveWorkerEconomy(workers[2]);
                    reverseRetargetPreservedCargo = reverseRetargetIssued &&
                        after.CargoKind == TestEconomyResourceKind.VespeneGas &&
                        after.CargoAmount == cargo &&
                        after.TargetNode == mineral &&
                        after.State == TestWorkerEconomyState.ReturningCargo;
                }
                gasDelivered |= runtime.ObservePlayerEconomy(1).VespeneGas > 0;
                if (!queuedReturnIssued &&
                    fourth.State == TestWorkerEconomyState.ReturningCargo &&
                    fourth.CargoAmount > 0)
                {
                    runtime.Move([workers[3]], new Vector2(545f, 350f));
                    queuedReturnIssued = runtime.ReturnCargo(
                        1, workers[3], queued: true) ==
                        TestReturnCargoCommandCode.Success &&
                        runtime.ObserveOrders(workers[3]).PendingOrders == 1;
                }
                else if (queuedReturnIssued && fourth.CargoAmount == 0 &&
                         fourth.State == TestWorkerEconomyState.Idle)
                {
                    queuedReturnCompleted = true;
                }
                if (runtime.Tick == hotTick)
                    hotCapture = runtime.CaptureRuntimeState();
            });
        }
        return session;
    }

    private static VisualTestSession CreateReturnCargoDropOffLoss()
    {
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(1_100f, 620f), 8);
        rig.RegisterPlayer(1, 0, 0, 5, 1);
        var nearDropOff = rig.AddResourceDropOff(
            1, new Vector2(180f, 310f));
        var farDropOff = rig.AddResourceDropOff(
            1, new Vector2(850f, 310f));
        var mineral = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(470f, 310f), 500, 5, 0.25f, 1);
        var worker = rig.SpawnWorker(new Vector2(425f, 310f), 1);
        rig.Gather(1, worker, mineral);

        var explicitReturnIssued = false;
        var nearDisabled = false;
        var reroutedToFar = false;
        var allDisabled = false;
        var waitedWithCargo = false;
        var missingDropOffRejected = false;
        var reenabled = false;
        var resumed = false;
        var delivered = false;
        var waitStartedTick = -1L;

        var session = new VisualTestSession(
                "economy-return-cargo-dropoff-loss",
                "Return Cargo reroutes, waits without loss, and resumes after DropOff recovery",
                600,
                rig,
                [worker],
                runtime =>
                {
                    var state = runtime.ObserveWorkerEconomy(worker);
                    var recovery = runtime.ObserveRecovery([worker]);
                    var passed = explicitReturnIssued && nearDisabled &&
                                 reroutedToFar && allDisabled &&
                                 waitedWithCargo && missingDropOffRejected &&
                                 reenabled && resumed && delivered &&
                                 recovery.UnreachableUnits == 0;
                    return new ScenarioResult(
                        passed,
                        $"return={explicitReturnIssued}, reroute={reroutedToFar}, " +
                        $"wait={waitedWithCargo}, missing={missingDropOffRejected}, " +
                        $"resume={resumed}, delivered={delivered}, " +
                        $"minerals={runtime.ObservePlayerEconomy(1).Minerals}, " +
                        $"state={state.State}, unreachable=" +
                        $"{recovery.UnreachableUnits}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(515f, 310f), 0.88f)
            .CameraKeyframe(160, new Vector2(600f, 310f), 0.94f)
            .CameraKeyframe(300, new Vector2(515f, 310f), 0.88f)
            .CameraKeyframe(580, new Vector2(300f, 310f), 1.02f)
            .Highlight(
                new SimRect(new Vector2(125f, 255f), new Vector2(235f, 365f)),
                "PRIMARY DROP-OFF: disabled, later restored",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(795f, 255f), new Vector2(905f, 365f)),
                "SECONDARY DROP-OFF: deterministic reroute target",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(425f, 265f), new Vector2(515f, 355f)),
                "WAIT: cargo remains authoritative with no valid DropOff",
                TestDiagnosticKind.Rejected);

        for (var tick = 1; tick < session.DurationTicks; tick++)
        {
            session.At(tick, "Exercise DropOff loss and recovery", runtime =>
            {
                var state = runtime.ObserveWorkerEconomy(worker);
                if (!explicitReturnIssued &&
                    state.State == TestWorkerEconomyState.ReturningCargo &&
                    state.CargoAmount > 0)
                {
                    explicitReturnIssued = runtime.ReturnCargo(1, worker) ==
                                           TestReturnCargoCommandCode.Success;
                    runtime.SetResourceDropOffOperational(nearDropOff, false);
                    nearDisabled = true;
                    return;
                }

                if (nearDisabled && !reroutedToFar &&
                    state.State == TestWorkerEconomyState.ReturningCargo)
                {
                    var movement = runtime.ObserveMovement(worker);
                    reroutedToFar =
                        movement.GoalKind ==
                            UnitMovementGoalKind.DropOffBoundary &&
                        movement.TargetId == farDropOff.Value;
                    if (reroutedToFar)
                    {
                        runtime.SetResourceDropOffOperational(
                            farDropOff, false);
                        allDisabled = true;
                    }
                    return;
                }

                if (allDisabled && !waitedWithCargo &&
                    state.State == TestWorkerEconomyState.WaitingForDropOff &&
                    state.CargoAmount > 0)
                {
                    waitedWithCargo = true;
                    waitStartedTick = runtime.Tick;
                    missingDropOffRejected = runtime.ReturnCargo(1, worker) ==
                        TestReturnCargoCommandCode.MissingDropOff;
                    return;
                }

                if (waitedWithCargo && !reenabled &&
                    runtime.Tick >= waitStartedTick + 45)
                {
                    runtime.SetResourceDropOffOperational(nearDropOff, true);
                    reenabled = true;
                    return;
                }

                if (reenabled && !resumed &&
                    state.State == TestWorkerEconomyState.ReturningCargo)
                    resumed = true;
                delivered |= resumed && state.CargoAmount == 0 &&
                             runtime.ObservePlayerEconomy(1).Minerals >= 5;
            });
        }
        return session;
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
                var arrival = EvaluateArrival(runtime, all, 77, 18f, 3f, 0, 0);
                var outerArrivals = CountArrivals(runtime, outer, 18f);
                var innerArrivals = CountArrivals(runtime, inner, 18f);
                var diagnostics = runtime.ObserveMovementDiagnostics();
                var bothLayersConverged = outerArrivals >= 45 && innerArrivals >= 31;
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
        var anchor = rig.TryCommitHardFootprint(
            new Vector2(300f, 350f),
            TestBuildingFootprintClass.Medium,
            TestMovementClass.Large);
        var dynamicOverlap = rig.TryCommitHardFootprint(
            new Vector2(300f, 350f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var staticOverlap = rig.TryCommitHardFootprint(
            new Vector2(600f, 350f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var outside = rig.TryCommitHardFootprint(
            new Vector2(5f, 100f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var unitOverlap = rig.TryCommitHardFootprint(
            new Vector2(1000f, 600f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var narrowGap = rig.TryCommitHardFootprint(
            new Vector2(36f, 100f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var flushWall = rig.TryCommitHardFootprint(
            new Vector2(16f, 100f),
            TestBuildingFootprintClass.Small,
            TestMovementClass.Large);
        var invalid = rig.TryCommitHardFootprint(
            new Vector2(800f, 100f),
            new Vector2(float.NaN, 32f),
            TestMovementClass.Large);
        return new VisualTestSession(
            "building-placement-rules",
            "Static placement, dynamic start validation and hard commit layers",
            360,
            rig,
            [unit],
            runtime =>
            {
                var acceptedLayers = anchor.Succeeded && flushWall.Succeeded &&
                    anchor.Assessment.StaticCode == TestStaticPlacementCode.Success &&
                    anchor.Assessment.DynamicCode ==
                        TestDynamicStartValidationCode.Success &&
                    flushWall.Assessment.Succeeded;
                var earlyStaticLayers =
                    dynamicOverlap.Code ==
                        TestHardFootprintCommitCode.StaticPlacementRejected &&
                    dynamicOverlap.Assessment.StaticCode ==
                        TestStaticPlacementCode.DynamicFootprintOverlap &&
                    dynamicOverlap.Assessment.DynamicCode ==
                        TestDynamicStartValidationCode.NotEvaluated &&
                    staticOverlap.Assessment.StaticCode ==
                        TestStaticPlacementCode.StaticObstacleOverlap &&
                    staticOverlap.Assessment.DynamicCode ==
                        TestDynamicStartValidationCode.NotEvaluated &&
                    outside.Assessment.StaticCode ==
                        TestStaticPlacementCode.OutsideWorld &&
                    outside.Assessment.DynamicCode ==
                        TestDynamicStartValidationCode.NotEvaluated &&
                    invalid.Assessment.StaticCode ==
                        TestStaticPlacementCode.InvalidFootprint &&
                    invalid.Assessment.DynamicCode ==
                        TestDynamicStartValidationCode.NotEvaluated;
                var dynamicLayer =
                    unitOverlap.Code ==
                        TestHardFootprintCommitCode.DynamicOccupantRejected &&
                    unitOverlap.Assessment.StaticCode ==
                        TestStaticPlacementCode.Success &&
                    unitOverlap.Assessment.DynamicCode ==
                        TestDynamicStartValidationCode.UnitOverlap &&
                    unitOverlap.Assessment.CombinedCode ==
                        TestBuildingPlacementCode.UnitOverlap;
                var lateStaticLayer =
                    narrowGap.Code ==
                        TestHardFootprintCommitCode.StaticPlacementRejected &&
                    narrowGap.Assessment.StaticCode ==
                        TestStaticPlacementCode.InsufficientClearance &&
                    narrowGap.Assessment.DynamicCode ==
                        TestDynamicStartValidationCode.Success &&
                    narrowGap.Assessment.CombinedCode ==
                        TestBuildingPlacementCode.InsufficientClearance;
                var codesCorrect = acceptedLayers && earlyStaticLayers &&
                    dynamicLayer && lateStaticLayer;
                return new ScenarioResult(
                    codesCorrect && runtime.BuildingCount == 2,
                    $"accepted={acceptedLayers}, early-static={earlyStaticLayers}, " +
                    $"dynamic={dynamicLayer}, late-static={lateStaticLayer}, " +
                    $"codes={dynamicOverlap.Assessment.CombinedCode}/" +
                    $"{staticOverlap.Assessment.CombinedCode}/" +
                    $"{unitOverlap.Assessment.CombinedCode}/" +
                    $"{narrowGap.Assessment.CombinedCode}, " +
                    $"buildings={runtime.BuildingCount}");
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

    private static VisualTestSession CreateMineralWalkCollisionMatrix()
    {
        const int laneCount = 4;
        const int workersPerLane = 6;
        const int mainWorkerCount = laneCount * workersPerLane;
        Vector2 worldSize = new(1400f, 800f);
        float[] laneY = [150f, 320f, 480f, 650f];
        float[] crossingX = [520f, 720f, 920f, 700f];
        var rig = MovementTestRig.CreateEconomyMap(worldSize, 96);
        rig.RegisterPlayer(1, 4000, 0, 80, mainWorkerCount + 4);

        var inert = new TestCombatProfile(
            MaximumHealth: 10_000f,
            AttackDamage: 0f,
            AttackRange: 1f,
            AcquisitionRange: 1f,
            AttackCooldownSeconds: 10f,
            AttackWindupSeconds: 0f,
            LeashDistance: 1f);

        TestUnitId[] CreateBarrier(
            float x,
            float centerY,
            int count,
            float spacing,
            int team,
            float radius)
        {
            var result = new TestUnitId[count];
            var firstY = centerY - (count - 1) * spacing * 0.5f;
            for (var index = 0; index < count; index++)
            {
                result[index] = rig.SpawnCombat(
                    new Vector2(x, firstY + index * spacing),
                    team,
                    inert,
                    radius,
                    maximumSpeed: 100f,
                    acceleration: 720f);
            }
            rig.Hold(result);
            return result;
        }

        TestUnitId[][] blockersByLane =
        [
            CreateBarrier(crossingX[0], laneY[0], 11, 15f, team: 1, radius: 8f),
            CreateBarrier(crossingX[1], laneY[1], 11, 15f, team: 2, radius: 8f),
            CreateBarrier(crossingX[2], laneY[2], 7, 32f, team: 2, radius: 18f),
            []
        ];

        var buildingCenter = new Vector2(crossingX[3], laneY[3]);
        var buildingSize = DemoBuildingTypes.SupplyDepot.Size;
        var buildingBuilder = rig.SpawnWorker(
            buildingCenter + new Vector2(0f, 78f), 1,
            maximumSpeed: 220f, acceleration: 900f);
        var building = rig.Build(
            1,
            buildingBuilder,
            DemoBuildingTypes.SupplyDepot with { BuildSeconds = 0.15f },
            buildingCenter);

        var mainWorkers = new TestUnitId[mainWorkerCount];
        var workerLanes = new int[mainWorkerCount];
        var resources = new TestResourceNodeId[laneCount];
        for (var lane = 0; lane < laneCount; lane++)
        {
            rig.AddResourceDropOff(1, new Vector2(95f, laneY[lane]));
            resources[lane] = rig.AddResourceNode(
                TestEconomyResourceKind.Minerals,
                new Vector2(1290f, laneY[lane]),
                amount: 20_000,
                harvestBatch: 5,
                harvestSeconds: 0.2f,
                harvesterCapacity: workersPerLane);
            for (var worker = 0; worker < workersPerLane; worker++)
            {
                var index = lane * workersPerLane + worker;
                mainWorkers[index] = rig.SpawnWorker(
                    new Vector2(
                        145f + worker % 2 * 17f,
                        laneY[lane] + (worker - 2.5f) * 11f),
                    1,
                    maximumSpeed: 220f,
                    acceleration: 900f);
                workerLanes[index] = lane;
            }
        }

        var recoveryResource = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(1320f, 750f),
            amount: 5000,
            harvestBatch: 5,
            harvestSeconds: 0.2f,
            harvesterCapacity: 3);
        Vector2[] recoveryOrigins =
        [
            new(1040f, 750f), new(1120f, 750f), new(1200f, 750f)
        ];
        var recoveryWorkers = new TestUnitId[3];
        var recoveryBlockers = new TestUnitId[3];
        for (var index = 0; index < recoveryWorkers.Length; index++)
        {
            recoveryBlockers[index] = rig.SpawnCombat(
                recoveryOrigins[index], team: 2, inert,
                radius: 9f, maximumSpeed: 100f, acceleration: 720f);
            rig.Hold([recoveryBlockers[index]]);
            recoveryWorkers[index] = rig.SpawnWorker(
                recoveryOrigins[index], 1,
                maximumSpeed: 180f, acceleration: 900f);
            rig.Gather(1, recoveryWorkers[index], recoveryResource);
        }

        var acceptedGatherCommands = 0;
        var outboundCrossed = new HashSet<int>();
        var returnCrossed = new HashSet<int>();
        var collisionOverlapByLane = new bool[3];
        var recoveryTicks = new int[3];
        Array.Fill(recoveryTicks, -1);
        var recoveryCommandTick = -1L;
        var buildingPenetrationSamples = 0;
        var buildingPenetrationDiagnostics = new List<string>();
        var buildingDetourObserved = false;
        var buildingHalf = buildingSize * 0.5f;

        var session = new VisualTestSession(
                "economy-mineral-walk-collision-matrix",
                "Mineral Walk crosses units, respects buildings, and restores collision",
                900,
                rig,
                [.. mainWorkers, .. recoveryWorkers, buildingBuilder],
                runtime =>
                {
                    var buildingCompleted = building.Succeeded &&
                        runtime.ObserveGameplayBuilding(building.BuildingId).State ==
                        TestBuildingLifecycleState.Completed;
                    var recovery = runtime.ObserveRecovery(mainWorkers);
                    var orders = recoveryWorkers.Select(runtime.ObserveOrders).ToArray();
                    var recoveryStates = recoveryWorkers.Select(
                        runtime.ObserveWorkerEconomy).ToArray();
                    var restoredOrders =
                        orders[0].ActiveOrder == TestOrderKind.Stop &&
                        orders[1].ActiveOrder == TestOrderKind.Hold &&
                        orders[2].ActiveOrder == TestOrderKind.AttackTarget &&
                        recoveryStates.All(value =>
                            value.State == TestWorkerEconomyState.Idle);
                    var maximumRecoveryTicks = recoveryTicks.Max();
                    var laneDiagnostics = string.Join(
                        ";",
                        Enumerable.Range(0, laneCount).Select(lane =>
                        {
                            var members = mainWorkers
                                .Where((_, index) => workerLanes[index] == lane)
                                .ToArray();
                            var outbound = members.Count(worker =>
                                outboundCrossed.Contains(worker.Value));
                            var returned = members.Count(worker =>
                                returnCrossed.Contains(worker.Value));
                            var states = string.Join(
                                ",",
                                members.Select(worker =>
                                {
                                    var economy = runtime.ObserveWorkerEconomy(
                                        worker);
                                    var movement = runtime.ObserveMovement(worker);
                                    var unit = runtime.Observe(worker);
                                    return $"{worker.Value}:{economy.State}/" +
                                           $"{movement.GoalKind}#{movement.TargetId}/" +
                                           $"{movement.Result}@{unit.Position.X:0}," +
                                           $"{unit.Position.Y:0}";
                                }));
                            return $"{lane}:{outbound}/{returned}[{states}]";
                        }));
                    var passed = buildingCompleted &&
                                 acceptedGatherCommands == mainWorkerCount &&
                                 outboundCrossed.Count == mainWorkerCount &&
                                 returnCrossed.Count == mainWorkerCount &&
                                 collisionOverlapByLane.All(value => value) &&
                                 buildingPenetrationSamples == 0 &&
                                 buildingDetourObserved &&
                                 recoveryTicks.All(value => value is >= 0 and <= 120) &&
                                 restoredOrders &&
                                 recovery.UnreachableUnits == 0;
                    return new ScenarioResult(
                        passed,
                        $"gather={acceptedGatherCommands}/{mainWorkerCount}, " +
                        $"cross={outboundCrossed.Count}/{returnCrossed.Count}, " +
                        $"unit-overlap={string.Join('/', collisionOverlapByLane.Select(value => value ? 1 : 0))}, " +
                        $"building-penetration={buildingPenetrationSamples}, " +
                        $"detour={buildingDetourObserved}, recovery={string.Join('/', recoveryTicks)} " +
                        $"max={maximumRecoveryTicks}, orders={restoredOrders}, " +
                        $"unreachable={recovery.UnreachableUnits}, " +
                        $"penetration=[{string.Join(',', buildingPenetrationDiagnostics)}], " +
                        $"lanes={laneDiagnostics}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(700f, 400f), 0.73f)
            .CameraKeyframe(280, new Vector2(700f, 400f), 0.82f)
            .CameraKeyframe(560, new Vector2(900f, 520f), 0.88f)
            .CameraKeyframe(880, new Vector2(700f, 400f), 0.73f)
            .Highlight(
                new SimRect(new Vector2(490f, 65f), new Vector2(550f, 235f)),
                "FRIENDLY HOLD: harvest transit passes through",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(690f, 235f), new Vector2(750f, 405f)),
                "ENEMY UNITS: harvest transit passes through",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(880f, 365f), new Vector2(960f, 595f)),
                "LARGE UNITS: bidirectional collision suppression",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(
                    buildingCenter - buildingHalf - new Vector2(18f),
                    buildingCenter + buildingHalf + new Vector2(18f)),
                "BUILDING: hard footprint still blocks",
                TestDiagnosticKind.Rejected)
            .Highlight(
                new SimRect(new Vector2(1000f, 710f), new Vector2(1240f, 790f)),
                "STOP / HOLD / ATTACK: normal collision resumes",
                TestDiagnosticKind.Info);

        session.At(1, "Cancel three Mineral Walk probes with Stop, Hold and Attack", runtime =>
        {
            recoveryCommandTick = runtime.Tick;
            runtime.Stop([recoveryWorkers[0]]);
            runtime.Hold([recoveryWorkers[1]]);
            runtime.AttackTarget([recoveryWorkers[2]], recoveryBlockers[2]);
        });
        session.At(60, "Start 24 workers through four collision lanes", runtime =>
        {
            for (var index = 0; index < mainWorkers.Length; index++)
            {
                if (runtime.Gather(
                        1, mainWorkers[index], resources[workerLanes[index]]) ==
                    TestGatherCommandCode.Success)
                    acceptedGatherCommands++;
            }
        });

        for (var tick = 2; tick < session.DurationTicks; tick++)
        {
            session.At(tick, "Observe public Mineral Walk outcomes", runtime =>
            {
                for (var probe = 0; probe < recoveryWorkers.Length; probe++)
                {
                    if (recoveryTicks[probe] >= 0)
                        continue;
                    var worker = runtime.Observe(recoveryWorkers[probe]);
                    var blocker = runtime.Observe(recoveryBlockers[probe]);
                    if (Vector2.Distance(worker.Position, blocker.Position) >=
                        worker.Radius + blocker.Radius - 0.25f)
                    {
                        recoveryTicks[probe] = (int)(runtime.Tick - recoveryCommandTick);
                    }
                }

                for (var index = 0; index < mainWorkers.Length; index++)
                {
                    var workerId = mainWorkers[index];
                    var unit = runtime.Observe(workerId);
                    var economy = runtime.ObserveWorkerEconomy(workerId);
                    var lane = workerLanes[index];
                    if (economy.State == TestWorkerEconomyState.GoingToResource &&
                        unit.Position.X > crossingX[lane] + 45f)
                        outboundCrossed.Add(workerId.Value);
                    if (economy.State == TestWorkerEconomyState.ReturningCargo &&
                        unit.Position.X < crossingX[lane] - 45f)
                        returnCrossed.Add(workerId.Value);

                    if (lane < collisionOverlapByLane.Length &&
                        !collisionOverlapByLane[lane] &&
                        economy.State is TestWorkerEconomyState.GoingToResource or
                            TestWorkerEconomyState.ReturningCargo)
                    {
                        foreach (var blockerId in blockersByLane[lane])
                        {
                            var blocker = runtime.Observe(blockerId);
                            if (Vector2.Distance(unit.Position, blocker.Position) <
                                unit.Radius + blocker.Radius - 0.5f)
                            {
                                collisionOverlapByLane[lane] = true;
                                break;
                            }
                        }
                    }

                    if (lane != 3)
                        continue;
                    var delta = Vector2.Abs(unit.Position - buildingCenter);
                    var closest = Vector2.Clamp(
                        unit.Position,
                        buildingCenter - buildingHalf,
                        buildingCenter + buildingHalf);
                    var penetrationRadius = unit.Radius - 0.1f;
                    if (Vector2.DistanceSquared(unit.Position, closest) <
                        penetrationRadius * penetrationRadius)
                    {
                        buildingPenetrationSamples++;
                        if (buildingPenetrationDiagnostics.Count < 8)
                        {
                            buildingPenetrationDiagnostics.Add(
                                $"t{runtime.Tick}/u{workerId.Value}@" +
                                $"{unit.Position.X:0.00},{unit.Position.Y:0.00}");
                        }
                    }
                    if (MathF.Abs(unit.Position.X - buildingCenter.X) <
                            buildingHalf.X + unit.Radius + 12f &&
                        delta.Y >= buildingHalf.Y + unit.Radius - 1f)
                        buildingDetourObserved = true;
                }
            });
        }

        return session;
    }

    private static VisualTestSession CreateAutoPatchDistribution(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            48, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 0, 0, 64, 32);
        rig.AddResourceDropOff(1, new Vector2(175f, 350f));

        Vector2[] patchPositions =
        [
            new(350f, 220f), new(420f, 215f),
            new(485f, 250f), new(510f, 320f),
            new(500f, 395f), new(450f, 450f),
            new(375f, 465f), new(320f, 405f)
        ];
        var patches = patchPositions.Select(position => rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            position,
            amount: 10_000,
            harvestBatch: 5,
            harvestSeconds: 0.3f,
            harvesterCapacity: 2))
            .ToArray();
        var workers = new TestUnitId[32];
        for (var index = 0; index < workers.Length; index++)
        {
            workers[index] = rig.SpawnWorker(
                new Vector2(
                    220f + index % 4 * 15f,
                    270f + index / 4 * 18f),
                1,
                maximumSpeed: 180f,
                acceleration: 850f);
        }

        rig.StartReplayPackageRecording();
        var stage12 = false;
        var stage16 = false;
        var stage24 = false;
        var stage32 = false;
        var activeSlotInvariant = true;
        var assignedCounterInvariant = true;
        var waitingObserved = false;
        var sawReturning = new bool[workers.Length];
        var completedRoundTrip = new bool[workers.Length];
        var issued = 0;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 300;

        bool ValidateStage(MovementTestRig runtime, int expected)
        {
            var snapshots = patches.Select(runtime.ObserveResourceNode).ToArray();
            var assigned = snapshots.Select(value => value.AssignedNormal).ToArray();
            return snapshots.All(value =>
                       value.NormalActiveSlots == 1 &&
                       value.IdealNormalAssignments == 2 &&
                       value.ActiveMules == 0 && value.AssignedMules == 0) &&
                   assigned.Sum() == expected &&
                   assigned.Max() - assigned.Min() <= 1;
        }

        var session = new VisualTestSession(
                "economy-auto-patch-distribution",
                "One mineral click deterministically spreads 12/16/24/32 workers over eight patches",
                600,
                rig,
                workers,
                runtime =>
                {
                    var nodes = patches.Select(runtime.ObserveResourceNode).ToArray();
                    var package = runtime.CaptureReplayPackage();
                    var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                    var replay = packageRoundTrip
                        ? runtime.ReplayPackage(decoded!, runtime.Tick)
                        : null;
                    var replayExact = replay is not null &&
                                      replay.FinalHash == runtime.StateHash;
                    var hotExact = false;
                    var hotVersion = 0;
                    if (hotCapture is not null)
                    {
                        var hot = runtime.BindHotSnapshot(package, hotCapture);
                        hotVersion = hot.FormatVersion;
                        if (hot.TryCanonicalRoundTrip(out var decodedHot))
                        {
                            var resumed = runtime.ResumeHotSnapshot(
                                package, decodedHot!, runtime.Tick);
                            hotExact = resumed.FinalHash == runtime.StateHash &&
                                       replay is not null &&
                                       replay.MatchesFrom(resumed, hotTick);
                        }
                    }
                    var finalAssigned = nodes.Sum(value => value.AssignedNormal);
                    var spread = nodes.Max(value => value.AssignedNormal) -
                                 nodes.Min(value => value.AssignedNormal);
                    var versions =
                        package.FormatVersion ==
                            SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                        hotVersion == SimulationHotSnapshot.CurrentFormatVersion;
                    var income = runtime.ObservePlayerEconomy(1).Minerals;
                    var completedCycles = completedRoundTrip.Count(value => value);
                    var workerStateCounts = runtime.ObservePlayerWorkers(1)
                        .GroupBy(value => value.State)
                        .OrderBy(value => value.Key)
                        .Select(value => $"{value.Key}:{value.Count()}");
                    var movementResultCounts = workers
                        .Select(runtime.ObserveMovement)
                        .GroupBy(value => (value.GoalKind, value.Result))
                        .OrderBy(value => value.Key.GoalKind)
                        .ThenBy(value => value.Key.Result)
                        .Select(value =>
                            $"{value.Key.GoalKind}/{value.Key.Result}:{value.Count()}");
                    var failedMovement = workers
                        .Where(value => runtime.ObserveMovement(value).Result ==
                            UnitMovementLegResult.Unreachable)
                        .Select(value =>
                        {
                            var unit = runtime.Observe(value);
                            var move = runtime.ObserveMovement(value);
                            return $"u{value.Value}@{unit.Position.X:0},{unit.Position.Y:0}>" +
                                   $"{move.NavigationTarget.X:0},{move.NavigationTarget.Y:0}/" +
                                   $"n{move.TargetId}";
                        });
                    var passed = issued == workers.Length &&
                                 stage12 && stage16 && stage24 && stage32 &&
                                 activeSlotInvariant && assignedCounterInvariant &&
                                 waitingObserved && finalAssigned == workers.Length &&
                                 income > 0 && completedCycles >= 24 &&
                                 spread <= 1 && packageRoundTrip && replayExact &&
                                 hotExact && versions;
                    return new ScenarioResult(
                        passed,
                        $"stages={stage12}/{stage16}/{stage24}/{stage32}, " +
                        $"assigned={finalAssigned}, spread={spread}, " +
                        $"active-slot={activeSlotInvariant}, counters=" +
                        $"{assignedCounterInvariant}, waiting={waitingObserved}, " +
                        $"income={income}, cycles={completedCycles}/{workers.Length}, " +
                        $"orders={package.EconomyCommandCount}, " +
                        $"replay={replayExact}, hot={hotExact}/v{hotVersion}, " +
                        $"workers=[{string.Join(',', workerStateCounts)}], " +
                        $"movement=[{string.Join(',', movementResultCounts)}], " +
                        $"failed=[{string.Join(',', failedMovement)}]");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(345f, 345f), 1.04f)
            .CameraKeyframe(150, new Vector2(390f, 345f), 1.18f)
            .CameraKeyframe(330, new Vector2(390f, 345f), 1.28f)
            .CameraKeyframe(570, new Vector2(345f, 345f), 1.04f)
            .Highlight(
                new SimRect(new Vector2(290f, 185f), new Vector2(545f, 495f)),
                "8 PATCHES: 1 active slot / 2 ideal normal workers each",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(185f, 245f), new Vector2(290f, 435f)),
                "ONE CLICK: stable-ID batch allocation",
                TestDiagnosticKind.Info);

        void IssueStage(
            long tick,
            int start,
            int count,
            string label)
        {
            session.At(tick, label, runtime =>
            {
                var selected = workers.Skip(start).Take(count).ToArray();
                if (runtime.PlayerSmartResource(1, selected, patches[0]) ==
                    TestPlayerOrderCommandCode.Success)
                    issued += count;
            });
        }

        IssueStage(1, 0, 12, "Right-click one patch with 12 workers");
        IssueStage(61, 12, 4, "Raise the same cluster to 16 workers");
        IssueStage(121, 16, 8, "Raise the same cluster to 24 workers");
        IssueStage(181, 24, 8, "Raise the same cluster to 32 workers");
        session.At(2, "Verify 12-worker allocation", runtime =>
            stage12 = ValidateStage(runtime, 12));
        session.At(62, "Verify ideal 16-worker allocation", runtime =>
            stage16 = ValidateStage(runtime, 16));
        session.At(122, "Verify diminished 24-worker allocation", runtime =>
            stage24 = ValidateStage(runtime, 24));
        session.At(182, "Verify capped 32-worker allocation", runtime =>
            stage32 = ValidateStage(runtime, 32));
        session.At(hotTick, "Capture active allocation hot snapshot", runtime =>
            hotCapture = runtime.CaptureRuntimeState());

        for (var tick = 2; tick < session.DurationTicks; tick += 2)
        {
            session.At(tick, "Observe public assignment counters", runtime =>
            {
                var nodes = patches.Select(runtime.ObserveResourceNode).ToArray();
                activeSlotInvariant &= nodes.All(value =>
                    value.ActiveNormal <= value.NormalActiveSlots);
                waitingObserved |= nodes.Any(value => value.WaitingNormal > 0);
                var workerSnapshots = runtime.ObservePlayerWorkers(1);
                foreach (var worker in workerSnapshots)
                {
                    sawReturning[worker.Unit.Value] |= worker.State ==
                        TestWorkerEconomyState.ReturningCargo;
                    completedRoundTrip[worker.Unit.Value] |=
                        sawReturning[worker.Unit.Value] &&
                        worker.State == TestWorkerEconomyState.GoingToResource &&
                        worker.CargoAmount == 0;
                }
                var assignedFromWorkers = workerSnapshots
                    .Where(value => value.State is not TestWorkerEconomyState.Idle and
                        not TestWorkerEconomyState.None)
                    .GroupBy(value => value.TargetNode.Value)
                    .ToDictionary(group => group.Key, group => group.Count());
                for (var index = 0; index < patches.Length; index++)
                {
                    assignedCounterInvariant &=
                        nodes[index].AssignedNormal ==
                        assignedFromWorkers.GetValueOrDefault(patches[index].Value);
                }
            });
        }
        return session;
    }

    private static VisualTestSession CreateMuleIndependentMining(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            48, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 0, 0, 64, 18);
        rig.AddResourceDropOff(1, new Vector2(165f, 180f));
        var clusterCenter = new Vector2(1060f, 520f);
        rig.AddResourceDropOff(1, clusterCenter);

        var isolatedPatch = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(330f, 180f),
            amount: 10_000,
            harvestBatch: 5,
            harvestSeconds: 0.45f,
            harvesterCapacity: 2);
        var gas = rig.AddResourceNode(
            TestEconomyResourceKind.VespeneGas,
            new Vector2(330f, 285f),
            amount: 10_000,
            harvestBatch: 4,
            harvestSeconds: 0.5f,
            harvesterCapacity: 3,
            requiresRefinery: true,
            operational: true);

        var clusterPatches = Enumerable.Range(0, 8)
            .Select(index =>
            {
                var angle = index * MathF.Tau / 8f;
                return rig.AddResourceNode(
                    TestEconomyResourceKind.Minerals,
                    clusterCenter + new Vector2(
                        MathF.Cos(angle), MathF.Sin(angle)) * 100f,
                    amount: 10_000,
                    harvestBatch: 5,
                    harvestSeconds: 0.45f,
                    harvesterCapacity: 2);
            })
            .ToArray();

        var isolatedScvs = new[]
        {
            rig.SpawnWorker(new Vector2(245f, 155f), 1),
            rig.SpawnWorker(new Vector2(255f, 205f), 1)
        };
        var isolatedMules = new[]
        {
            rig.SpawnMule(new Vector2(225f, 130f), 1),
            rig.SpawnMule(new Vector2(225f, 230f), 1)
        };
        var clusterScvs = Enumerable.Range(0, 16)
            .Select(index => rig.SpawnWorker(
                new Vector2(
                    1010f + index % 4 * 17f,
                    470f + index / 4 * 30f),
                1))
            .ToArray();
        var clusterMules = Enumerable.Range(0, 8)
            .Select(index => rig.SpawnMule(
                new Vector2(
                    990f + index % 2 * 22f,
                    460f + index / 2 * 35f),
                1))
            .ToArray();
        var allUnits = isolatedScvs
            .Concat(isolatedMules)
            .Concat(clusterScvs)
            .Concat(clusterMules)
            .ToArray();

        rig.StartReplayPackageRecording();
        var normalOrdersIssued = false;
        var firstMuleIssued = false;
        var secondMuleIssued = false;
        var clusterMulesIssued = false;
        var muleGasRejected =
            rig.Gather(1, isolatedMules[0], gas) ==
            TestGatherCommandCode.CapabilityUnavailable;
        var samePatchDualActive = false;
        var secondMuleWaiting = false;
        var clusterMulesDistributed = false;
        var normalSaturationIntact = true;
        var activeSlotInvariant = true;
        var normalWorkerCountInvariant = true;
        var muleCapabilitiesVisible = true;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 360;

        var session = new VisualTestSession(
                "economy-mule-independent-mining",
                "MULE uses a separate one-per-patch mining channel without changing normal saturation",
                720,
                rig,
                allUnits,
                runtime =>
                {
                    var isolated = runtime.ObserveResourceNode(isolatedPatch);
                    var cluster = clusterPatches
                        .Select(runtime.ObserveResourceNode)
                        .ToArray();
                    var package = runtime.CaptureReplayPackage();
                    var packageRoundTrip = package.TryCanonicalRoundTrip(
                        out var decoded);
                    var replay = packageRoundTrip
                        ? runtime.ReplayPackage(decoded!, runtime.Tick)
                        : null;
                    var replayExact = replay is not null &&
                                      replay.FinalHash == runtime.StateHash;
                    var hotExact = false;
                    var hotVersion = 0;
                    if (hotCapture is not null)
                    {
                        var hot = runtime.BindHotSnapshot(package, hotCapture);
                        hotVersion = hot.FormatVersion;
                        if (hot.TryCanonicalRoundTrip(out var decodedHot))
                        {
                            var resumed = runtime.ResumeHotSnapshot(
                                package, decodedHot!, runtime.Tick);
                            hotExact = resumed.FinalHash == runtime.StateHash &&
                                       replay is not null &&
                                       replay.MatchesFrom(resumed, hotTick);
                        }
                    }
                    var normalAssigned = cluster.Sum(
                        value => value.AssignedNormal);
                    var muleAssigned = cluster.Sum(
                        value => value.AssignedMules);
                    var clusterDistribution = string.Join(
                        ",",
                        cluster.Select(value =>
                            $"{value.AssignedNormal}+{value.AssignedMules}"));
                    var missingNormal = string.Join(
                        ",",
                        clusterScvs
                            .Where(unit =>
                                runtime.ObserveWorkerEconomy(unit).TargetNode.Value < 0)
                            .Select(unit =>
                            {
                                var economy = runtime.ObserveWorkerEconomy(unit);
                                var movement = runtime.ObserveMovement(unit);
                                return $"{unit.Value}:{economy.State}/{movement.Result}";
                            }));
                    var missingMules = string.Join(
                        ",",
                        clusterMules
                            .Where(unit =>
                                runtime.ObserveWorkerEconomy(unit).TargetNode.Value < 0)
                            .Select(unit =>
                            {
                                var economy = runtime.ObserveWorkerEconomy(unit);
                                var movement = runtime.ObserveMovement(unit);
                                return $"{unit.Value}:{economy.State}/{movement.Result}";
                            }));
                    var versions = package.FormatVersion ==
                                       SimulationReplayPackageSnapshot
                                           .CurrentFormatVersion &&
                                   hotVersion ==
                                       SimulationHotSnapshot.CurrentFormatVersion;
                    var passed = muleGasRejected && normalOrdersIssued &&
                                 firstMuleIssued && secondMuleIssued &&
                                 clusterMulesIssued && samePatchDualActive &&
                                 secondMuleWaiting && clusterMulesDistributed &&
                                 normalSaturationIntact && activeSlotInvariant &&
                                 normalWorkerCountInvariant &&
                                 muleCapabilitiesVisible &&
                                 isolated.AssignedNormal == 2 &&
                                 isolated.AssignedMules == 2 &&
                                 normalAssigned == 16 && muleAssigned == 8 &&
                                 package.EconomyCommandCount == 28 &&
                                 packageRoundTrip && replayExact && hotExact &&
                                 versions;
                    return new ScenarioResult(
                        passed,
                        $"same-patch={samePatchDualActive}, second-waits=" +
                        $"{secondMuleWaiting}, cluster={normalAssigned}/16+" +
                        $"{muleAssigned}/8, spread={clusterMulesDistributed}, " +
                        $"gas-reject={muleGasRejected}, active=" +
                        $"{activeSlotInvariant}, workers=" +
                        $"{normalWorkerCountInvariant}, orders=" +
                        $"{package.EconomyCommandCount}, replay={replayExact}, " +
                        $"hot={hotExact}/v{hotVersion}, nodes=[{clusterDistribution}], " +
                        $"missing-normal=[{missingNormal}], missing-mules=[{missingMules}]");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(560f, 330f), 0.78f)
            .CameraKeyframe(100, new Vector2(280f, 180f), 1.45f)
            .CameraKeyframe(250, new Vector2(280f, 180f), 1.55f)
            .CameraKeyframe(390, clusterCenter, 1.05f)
            .CameraKeyframe(690, new Vector2(600f, 340f), 0.8f)
            .Highlight(
                new SimRect(new Vector2(285f, 125f), new Vector2(375f, 235f)),
                "1 NORMAL SLOT + 1 MULE SLOT",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(930f, 390f), new Vector2(1190f, 650f)),
                "8 PATCHES: normal 16/16 remains unchanged",
                TestDiagnosticKind.Info);

        session.At(1, "Start two SCVs on the isolated patch", runtime =>
        {
            normalOrdersIssued =
                runtime.Gather(1, isolatedScvs[0], isolatedPatch) ==
                    TestGatherCommandCode.Success &&
                runtime.Gather(1, isolatedScvs[1], isolatedPatch) ==
                    TestGatherCommandCode.Success &&
                runtime.PlayerSmartResource(
                    1, clusterScvs, clusterPatches[0]) ==
                    TestPlayerOrderCommandCode.Success;
        });
        session.At(90, "Add one MULE to mine alongside the active SCV", runtime =>
            firstMuleIssued =
                runtime.Gather(1, isolatedMules[0], isolatedPatch) ==
                TestGatherCommandCode.Success);
        session.At(180, "Add a second MULE to the same isolated patch", runtime =>
            secondMuleIssued =
                runtime.Gather(1, isolatedMules[1], isolatedPatch) ==
                TestGatherCommandCode.Success);
        session.At(270, "Batch eight MULEs onto one patch in the main cluster", runtime =>
            clusterMulesIssued = runtime.PlayerSmartResource(
                1, clusterMules, clusterPatches[0]) ==
                TestPlayerOrderCommandCode.Success);
        session.At(hotTick, "Capture both gatherer channels in a hot snapshot", runtime =>
            hotCapture = runtime.CaptureRuntimeState());

        for (var tick = 2; tick < session.DurationTicks; tick += 2)
        {
            session.At(tick, "Observe independent gatherer channels", runtime =>
            {
                var isolated = runtime.ObserveResourceNode(isolatedPatch);
                samePatchDualActive |=
                    isolated.ActiveNormal == 1 && isolated.ActiveMules == 1;
                var muleStates = isolatedMules
                    .Select(runtime.ObserveWorkerEconomy)
                    .ToArray();
                secondMuleWaiting |= isolated.AssignedMules == 2 &&
                    isolated.ActiveMules == 1 && muleStates.Any(value =>
                        value.State == TestWorkerEconomyState.WaitingForResource);
                muleCapabilitiesVisible &= muleStates.All(value =>
                    value.Capability == TestGathererCapability.Mule);

                var cluster = clusterPatches
                    .Select(runtime.ObserveResourceNode)
                    .ToArray();
                activeSlotInvariant &= cluster
                    .Append(isolated)
                    .All(value =>
                        value.ActiveNormal <= value.NormalActiveSlots &&
                        value.ActiveMules <= 1);
                if (clusterMulesIssued)
                {
                    clusterMulesDistributed |= cluster.All(value =>
                        value.AssignedMules == 1);
                    normalSaturationIntact &=
                        cluster.Sum(value => value.AssignedNormal) == 16 &&
                        cluster.All(value => value.AssignedNormal == 2);
                }
                normalWorkerCountInvariant &=
                    runtime.ObservePlayerWorkers(1).Length == 18;
            });
        }
        return session;
    }

    private static VisualTestSession CreateEconomyAssignmentLifecycle(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 3000, 0, 30, 15);
        rig.AddResourceDropOff(1, new Vector2(270f, 350f));
        var workers = Enumerable.Range(0, 15)
            .Select(index => rig.SpawnWorker(
                new Vector2(
                    295f + index % 5 * 18f,
                    285f + index / 5 * 52f),
                1))
            .ToArray();
        var center = new Vector2(400f, 350f);
        var patches = Enumerable.Range(0, 8)
            .Select(index =>
            {
                var angle = index * MathF.Tau / 8f;
                return rig.AddResourceNode(
                    TestEconomyResourceKind.Minerals,
                    center + new Vector2(
                        MathF.Cos(angle), MathF.Sin(angle)) * 90f,
                    amount: index == 0 ? 15 : 10_000,
                    harvestBatch: 5,
                    harvestSeconds: 0.4f,
                    harvesterCapacity: 2);
            })
            .ToArray();
        var workerRecipe = DemoProductionCatalog.CreateSnapshot().Recipe(2) with
        {
            ProductionSeconds = 0.5f
        };
        var townHallType = DemoBuildingTypes.CommandCenter with
        {
            BuildSeconds = 0.2f
        };

        rig.StartReplayPackageRecording();
        var townHall = rig.Build(
            1, workers[0], townHallType, new Vector2(175f, 350f));
        var batchIssued = false;
        var rallyIssued = false;
        var initialDistribution = false;
        var rallyFilledGap = false;
        var depletedAndShrunk = false;
        var assignedInvariant = true;
        var underfilledNode = new TestResourceNodeId(-1);
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 300;

        var session = new VisualTestSession(
                "economy-assignment-lifecycle",
                "Resource Rally fills the least-loaded patch and depletion shrinks assignment counters",
                1200,
                rig,
                workers,
                runtime =>
                {
                    var nodes = patches.Select(runtime.ObserveResourceNode).ToArray();
                    var depleted = nodes[0];
                    var remaining = nodes.Skip(1).ToArray();
                    var package = runtime.CaptureReplayPackage();
                    var packageRoundTrip = package.TryCanonicalRoundTrip(
                        out var decoded);
                    var replay = packageRoundTrip
                        ? runtime.ReplayPackage(decoded!, runtime.Tick)
                        : null;
                    var replayExact = replay is not null &&
                                      replay.FinalHash == runtime.StateHash;
                    var hotExact = false;
                    var hotVersion = 0;
                    if (hotCapture is not null)
                    {
                        var hot = runtime.BindHotSnapshot(package, hotCapture);
                        hotVersion = hot.FormatVersion;
                        if (hot.TryCanonicalRoundTrip(out var decodedHot))
                        {
                            var resumed = runtime.ResumeHotSnapshot(
                                package, decodedHot!, runtime.Tick);
                            hotExact = resumed.FinalHash == runtime.StateHash &&
                                       replay is not null &&
                                       replay.MatchesFrom(resumed, hotTick);
                        }
                    }
                    var remainingAssigned = remaining.Sum(
                        value => value.AssignedNormal);
                    var depletedWorkerStates = Enumerable.Range(
                            0, runtime.UnitCount)
                        .Select(index => runtime.ObserveWorkerEconomy(
                            new TestUnitId(index)))
                        .Where(value => value.TargetNode == patches[0])
                        .Select(value => value.State)
                        .ToArray();
                    var remainingSpread = remaining.Max(
                                              value => value.AssignedNormal) -
                                          remaining.Min(
                                              value => value.AssignedNormal);
                    var versions = package.FormatVersion ==
                                       SimulationReplayPackageSnapshot
                                           .CurrentFormatVersion &&
                                   hotVersion ==
                                       SimulationHotSnapshot.CurrentFormatVersion;
                    var passed = townHall.Succeeded && batchIssued &&
                                 rallyIssued && initialDistribution &&
                                 rallyFilledGap && depletedAndShrunk &&
                                 assignedInvariant && depleted.Remaining == 0 &&
                                 depleted.ActiveNormal == 0 &&
                                 depleted.AssignedNormal == 0 &&
                                 depleted.WaitingNormal == 0 &&
                                 remainingAssigned == 16 &&
                                 remainingSpread <= 1 &&
                                 packageRoundTrip && replayExact && hotExact &&
                                 versions;
                    return new ScenarioResult(
                        passed,
                        $"initial={initialDistribution}, rally=" +
                        $"{rallyFilledGap}@node{underfilledNode.Value}, " +
                        $"depleted={depletedAndShrunk}/left{depleted.Remaining}/" +
                        $"{depleted.ActiveNormal}:{depleted.AssignedNormal}:" +
                        $"{depleted.WaitingNormal}, remaining=" +
                        $"{remainingAssigned}/spread{remainingSpread}, " +
                        $"states={string.Join('/', depletedWorkerStates)}, " +
                        $"counters={assignedInvariant}, replay={replayExact}, " +
                        $"hot={hotExact}/v{hotVersion}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(330f, 350f), 1.08f)
            .CameraKeyframe(210, new Vector2(390f, 350f), 1.28f)
            .CameraKeyframe(420, new Vector2(390f, 350f), 1.38f)
            .CameraKeyframe(690, new Vector2(390f, 350f), 1.32f)
            .CameraKeyframe(1170, new Vector2(330f, 350f), 1.08f)
            .Highlight(
                new SimRect(new Vector2(285f, 235f), new Vector2(515f, 465f)),
                "RALLY FILL -> DEPLETION SHRINK",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(90f, 265f), new Vector2(260f, 435f)),
                "COMMAND CENTER: resource Rally producer",
                TestDiagnosticKind.Info);

        session.At(120, "Distribute fifteen SCVs across eight patches", runtime =>
            batchIssued = runtime.PlayerSmartResource(
                1, workers, patches[0]) == TestPlayerOrderCommandCode.Success);
        session.At(122, "Capture the single underfilled mineral patch", runtime =>
        {
            var nodes = patches.Select(runtime.ObserveResourceNode).ToArray();
            initialDistribution = nodes.Sum(value => value.AssignedNormal) == 15 &&
                                  nodes.Count(value => value.AssignedNormal == 1) == 1 &&
                                  nodes.Count(value => value.AssignedNormal == 2) == 7;
            if (initialDistribution)
            {
                underfilledNode = nodes.Single(
                    value => value.AssignedNormal == 1).Id;
            }
        });
        session.At(180, "Rally a produced SCV to the already occupied clicked patch", runtime =>
        {
            var set = runtime.SetRallyResource(
                1, townHall.BuildingId, patches[0]);
            var train = runtime.Train(
                1, townHall.BuildingId, workerRecipe);
            rallyIssued = set && train.Succeeded;
        });
        session.At(260, "Verify Rally selected the previously underfilled patch", runtime =>
        {
            if (runtime.UnitCount <= workers.Length)
                return;
            var produced = runtime.ObserveWorkerEconomy(
                new TestUnitId(workers.Length));
            var nodes = patches.Select(runtime.ObserveResourceNode).ToArray();
            rallyFilledGap = produced.TargetNode == underfilledNode &&
                             nodes.All(value => value.AssignedNormal == 2);
        });
        session.At(hotTick, "Capture the saturated pre-depletion assignment state", runtime =>
            hotCapture = runtime.CaptureRuntimeState());

        for (var tick = 122; tick < session.DurationTicks; tick += 2)
        {
            session.At(tick, "Observe assignment lifecycle counters", runtime =>
            {
                var nodes = patches.Select(runtime.ObserveResourceNode).ToArray();
                assignedInvariant &= nodes.All(value =>
                    value.ActiveNormal <= value.NormalActiveSlots &&
                    value.WaitingNormal <= value.AssignedNormal &&
                    value.ActiveNormal <= value.AssignedNormal);
                depletedAndShrunk |= nodes[0].Remaining == 0 &&
                    nodes[0].ActiveNormal == 0 &&
                    nodes[0].AssignedNormal == 0 &&
                    nodes[0].WaitingNormal == 0 &&
                    nodes.Skip(1).Sum(value => value.AssignedNormal) == 16;
            });
        }
        return session;
    }

    private static VisualTestSession CreateMiningIncomeCurve()
    {
        const int laneCount = 5;
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(2300f, 2100f), 40);
        var allWorkers = new List<TestUnitId>();
        var laneWorkers = new TestUnitId[laneCount][];
        var laneNodes = new TestResourceNodeId[laneCount];
        var accepted = 0;

        for (var lane = 0; lane < 4; lane++)
        {
            var playerId = lane + 1;
            var workerCount = lane + 1;
            var y = 240f + lane * 500f;
            rig.RegisterPlayer(playerId, 0, 0, 16, workerCount);
            rig.AddResourceDropOff(playerId, new Vector2(150f, y));
            laneNodes[lane] = rig.AddResourceNode(
                TestEconomyResourceKind.Minerals,
                new Vector2(500f, y),
                amount: 10_000,
                harvestBatch: 5,
                harvestSeconds: 1.99f,
                harvesterCapacity: 2);
            laneWorkers[lane] = Enumerable.Range(0, workerCount)
                .Select(index => rig.SpawnWorker(
                    new Vector2(205f, y + (index - workerCount / 2f) * 20f),
                    playerId,
                    maximumSpeed: 180f,
                    acceleration: 900f))
                .ToArray();
            allWorkers.AddRange(laneWorkers[lane]);
        }

        const int farPlayer = 5;
        const float farY = 300f;
        rig.RegisterPlayer(farPlayer, 0, 0, 16, 2);
        rig.AddResourceDropOff(farPlayer, new Vector2(1250f, farY));
        laneNodes[4] = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(2050f, farY),
            amount: 10_000,
            harvestBatch: 5,
            harvestSeconds: 1.99f,
            harvesterCapacity: 2);
        laneWorkers[4] = Enumerable.Range(0, 2)
            .Select(index => rig.SpawnWorker(
                new Vector2(1300f, farY + (index * 2 - 1) * 15f),
                farPlayer,
                maximumSpeed: 180f,
                acceleration: 900f))
            .ToArray();
        allWorkers.AddRange(laneWorkers[4]);

        var activeSlotInvariant = true;
        var waitingObserved = false;
        var previousWorkerStates = new Dictionary<int, TestWorkerEconomyState>();
        var interruptedGathering = new int[laneCount];
        var maximumStationaryDistance = new float[laneCount];
        var session = new VisualTestSession(
                "economy-mining-income-curve",
                "SC2-style 5 cargo / 1.99 second mining shows bounded worker marginal income",
                1800,
                rig,
                allWorkers.ToArray(),
                runtime =>
                {
                    var income = Enumerable.Range(1, laneCount)
                        .Select(player =>
                            runtime.ObservePlayerEconomy(player).Minerals)
                        .ToArray();
                    var secondMarginal = income[1] - income[0];
                    var thirdMarginal = income[2] - income[1];
                    var fourthMarginal = income[3] - income[2];
                    var monotonic = income[1] > income[0] &&
                                    income[2] >= income[1] &&
                                    income[3] >= income[2];
                    var diminishing = secondMarginal > thirdMarginal &&
                                      fourthMarginal <= thirdMarginal;
                    var distancePenalty = income[4] < income[1];
                    var laneDiagnostics = string.Join(
                        ";",
                        Enumerable.Range(0, laneCount).Select(lane =>
                        {
                            var node = runtime.ObserveResourceNode(
                                laneNodes[lane]);
                            var workers = string.Join(
                                ",",
                                laneWorkers[lane].Select(worker =>
                                {
                                    var economy = runtime.ObserveWorkerEconomy(
                                        worker);
                                    var movement = runtime.ObserveMovement(worker);
                                    return $"{economy.State}/{movement.Result}";
                                }));
                            return $"{lane + 1}:{node.AssignedNormal}/" +
                                   $"{node.ActiveNormal}/{node.WaitingNormal}[{workers}]";
                        }));
                    var passed = accepted == allWorkers.Count &&
                                 income[0] >= 15 && monotonic && diminishing &&
                                 distancePenalty && activeSlotInvariant &&
                                 waitingObserved;
                    return new ScenarioResult(
                        passed,
                        $"near1/2/3/4={string.Join('/', income.Take(4))}, " +
                        $"marginal={secondMarginal}/{thirdMarginal}/" +
                        $"{fourthMarginal}, far2={income[4]}, " +
                        $"monotonic={monotonic}, diminishing={diminishing}, " +
                        $"active={activeSlotInvariant}, waiting={waitingObserved}, " +
                        $"interruptions={string.Join('/', interruptedGathering)}, " +
                        $"stationary-distance=" +
                        $"{string.Join('/', maximumStationaryDistance.Select(value => value.ToString("0.0")))}, " +
                        $"lanes={laneDiagnostics}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(1100f, 1000f), 0.42f)
            .CameraKeyframe(360, new Vector2(330f, 740f), 0.72f)
            .CameraKeyframe(900, new Vector2(1650f, 300f), 0.68f)
            .CameraKeyframe(1440, new Vector2(330f, 1240f), 0.72f)
            .CameraKeyframe(1770, new Vector2(1100f, 1000f), 0.42f)
            .Highlight(
                new SimRect(new Vector2(80f, 130f), new Vector2(570f, 1920f)),
                "NEAR LANES: 1 / 2 / 3 / 4 SCVs",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(1160f, 180f), new Vector2(2140f, 420f)),
                "FAR LANE: same 2 SCVs, longer round trip",
                TestDiagnosticKind.Info);

        session.At(1, "Start every income lane through formal Gather commands", runtime =>
        {
            for (var lane = 0; lane < laneCount; lane++)
            {
                foreach (var worker in laneWorkers[lane])
                {
                    if (runtime.Gather(lane + 1, worker, laneNodes[lane]) ==
                        TestGatherCommandCode.Success)
                    {
                        accepted++;
                    }
                }
            }
        });
        for (var tick = 2; tick < session.DurationTicks; tick += 4)
        {
            session.At(tick, "Observe slot and waiting invariants", runtime =>
            {
                var nodes = laneNodes.Select(runtime.ObserveResourceNode).ToArray();
                activeSlotInvariant &= nodes.All(value =>
                    value.ActiveNormal <= 1);
                waitingObserved |= nodes.Take(4).Any(value =>
                    value.WaitingNormal > 0);
                for (var lane = 0; lane < laneCount; lane++)
                {
                    foreach (var worker in laneWorkers[lane])
                    {
                        var economy = runtime.ObserveWorkerEconomy(worker);
                        if (previousWorkerStates.TryGetValue(
                                worker.Value, out var previousState) &&
                            previousState == TestWorkerEconomyState.Gathering &&
                            economy.State ==
                                TestWorkerEconomyState.GoingToResource &&
                            economy.CargoAmount == 0)
                        {
                            interruptedGathering[lane]++;
                        }
                        previousWorkerStates[worker.Value] = economy.State;
                        if (economy.State is
                            TestWorkerEconomyState.Gathering or
                            TestWorkerEconomyState.WaitingForResource)
                        {
                            maximumStationaryDistance[lane] = MathF.Max(
                                maximumStationaryDistance[lane],
                                Vector2.Distance(
                                    runtime.Observe(worker).Position,
                                    nodes[lane].Position));
                        }
                    }
                }
            });
        }
        return session;
    }

    private static VisualTestSession CreateMassMiningEconomy()
    {
        const int baseCount = 4;
        const int workersPerBase = 24;
        const int nodesPerBase = 8;
        const int startingNodeAmount = 10_000;
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(1500f, 900f), 160);
        rig.RegisterPlayer(1, 12_000, 0, 200, baseCount * workersPerBase);
        Vector2[] baseCenters =
        [
            new(250f, 250f), new(1250f, 250f),
            new(250f, 650f), new(1250f, 650f)
        ];
        var resources = new List<TestResourceNodeId>(baseCount * nodesPerBase);
        var workers = new List<TestUnitId>(baseCount * workersPerBase);
        var buildings = new List<TestConstructionResult>(baseCount);
        var clusterWorkers = new TestUnitId[baseCount][];
        var clusterResources = new TestResourceNodeId[baseCount][];
        var townHall = DemoBuildingTypes.CommandCenter with
        {
            BuildSeconds = 0.25f,
            MaximumHealth = 12_000f,
            SupplyProvided = 0
        };

        for (var cluster = 0; cluster < baseCount; cluster++)
        {
            var center = baseCenters[cluster];
            var nodes = new TestResourceNodeId[nodesPerBase];
            for (var node = 0; node < nodesPerBase; node++)
            {
                var angle = node * MathF.Tau / nodesPerBase;
                var position = center + new Vector2(
                    MathF.Cos(angle), MathF.Sin(angle)) * 155f;
                nodes[node] = rig.AddResourceNode(
                    TestEconomyResourceKind.Minerals,
                    position,
                    startingNodeAmount,
                    harvestBatch: 6,
                    harvestSeconds: 0.35f,
                    harvesterCapacity: 2);
                resources.Add(nodes[node]);
            }
            clusterResources[cluster] = nodes;

            var members = new TestUnitId[workersPerBase];
            members[0] = rig.SpawnWorker(center + new Vector2(0f, 125f), 1);
            buildings.Add(rig.Build(1, members[0], townHall, center));
            clusterWorkers[cluster] = members;
            workers.Add(members[0]);
        }
        var previous = new Dictionary<int, TestWorkerCycleSnapshot>();
        var sawGoing = new HashSet<int>();
        var sawGathering = new HashSet<int>();
        var sawReturning = new HashSet<int>();
        var cycles = 0;
        var approachSamples = 0;
        var invalidApproaches = 0;
        var acceptedGatherCommands = 0;

        var session = new VisualTestSession(
            "economy-mass-mining",
            "96 workers continuously mine 32 mineral fields into four bases",
            600,
            rig,
            workers.ToArray(),
            runtime =>
            {
                var totalMined = resources.Sum(node =>
                    startingNodeAmount - runtime.ObserveResourceNode(node).Remaining);
                var recovery = runtime.ObserveRecovery(workers);
                var unreachableWorkers = workers
                    .Where(worker => runtime.ObserveRecovery([worker])
                        .UnreachableUnits > 0)
                    .Select(worker =>
                    {
                        var movement = runtime.Observe(worker);
                        var movementGoal = runtime.ObserveMovement(worker);
                        var economy = runtime.ObserveWorkerEconomy(worker);
                        return $"u{worker.Value}:{economy.State}/n" +
                               $"{economy.TargetNode.Value}/" +
                               $"{movementGoal.GoalKind}:{movementGoal.Result}@" +
                               $"{movement.Position.X:0},{movement.Position.Y:0}>" +
                               $"{movement.AssignedTarget.X:0}," +
                               $"{movement.AssignedTarget.Y:0}";
                    })
                    .ToArray();
                var basesCompleted = buildings.All(building =>
                    building.Succeeded &&
                    runtime.ObserveGameplayBuilding(building.BuildingId).State ==
                    TestBuildingLifecycleState.Completed);
                var passed = basesCompleted &&
                             acceptedGatherCommands == workers.Count &&
                             sawGoing.Count == workers.Count &&
                             sawGathering.Count == workers.Count &&
                             sawReturning.Count == workers.Count &&
                             cycles >= 120 && approachSamples >= 200 &&
                             invalidApproaches == 0 &&
                             totalMined >= cycles * 6 &&
                             recovery.UnreachableUnits == 0;
                return new ScenarioResult(
                    passed,
                    $"workers={workers.Count}, nodes={resources.Count}, " +
                    $"bases={buildings.Count}/{basesCompleted}, " +
                    $"cycles={cycles}, mined={totalMined}, " +
                    $"phases={sawGoing.Count}/{sawGathering.Count}/" +
                    $"{sawReturning.Count}, edges={approachSamples}:" +
                    $"{invalidApproaches}, unreachable=" +
                    $"{recovery.UnreachableUnits}[" +
                    $"{string.Join(',', unreachableWorkers)}]");
            })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(750f, 450f), 0.68f)
            .CameraKeyframe(90, new Vector2(750f, 450f), 0.68f)
            .CameraKeyframe(180, baseCenters[0], 1.05f)
            .CameraKeyframe(270, baseCenters[1], 1.05f)
            .CameraKeyframe(360, baseCenters[2], 1.05f)
            .CameraKeyframe(450, baseCenters[3], 1.05f)
            .CameraKeyframe(570, new Vector2(750f, 450f), 0.68f);

        for (var cluster = 0; cluster < baseCount; cluster++)
        {
            var captureCluster = cluster;
            session.Highlight(
                new SimRect(
                    baseCenters[cluster] - new Vector2(220f, 210f),
                    baseCenters[cluster] + new Vector2(220f, 210f)),
                $"MINING BASE {cluster + 1}: 24 workers / 8 patches",
                cluster % 2 == 0
                    ? TestDiagnosticKind.Info
                    : TestDiagnosticKind.Accepted);
            session.At(75, $"Spawn and start mining cluster {cluster + 1}", runtime =>
            {
                var center = baseCenters[captureCluster];
                for (var worker = 1; worker < workersPerBase; worker++)
                {
                    var node = worker % nodesPerBase;
                    var nodePosition = center + new Vector2(
                        MathF.Cos(node * MathF.Tau / nodesPerBase),
                        MathF.Sin(node * MathF.Tau / nodesPerBase)) * 125f;
                    var lane = worker / nodesPerBase;
                    clusterWorkers[captureCluster][worker] = runtime.SpawnWorker(
                        nodePosition + new Vector2(
                            (lane - 1) * 13f, lane * 7f), 1);
                    workers.Add(clusterWorkers[captureCluster][worker]);
                }
                for (var worker = 0; worker < workersPerBase; worker++)
                {
                    var result = runtime.Gather(
                        1,
                        clusterWorkers[captureCluster][worker],
                        clusterResources[captureCluster][worker % nodesPerBase]);
                    if (result == TestGatherCommandCode.Success)
                        acceptedGatherCommands++;
                }
            });
        }

        for (var tick = 75; tick < session.DurationTicks; tick += 2)
        {
            session.At(tick, "Observe mass-mining business telemetry", runtime =>
            {
                foreach (var worker in runtime.ObservePlayerWorkers(1))
                {
                    sawGoing.UnionWith(worker.State ==
                        TestWorkerEconomyState.GoingToResource
                            ? [worker.Unit.Value]
                            : []);
                    sawGathering.UnionWith(worker.State ==
                        TestWorkerEconomyState.Gathering
                            ? [worker.Unit.Value]
                            : []);
                    sawReturning.UnionWith(worker.State ==
                        TestWorkerEconomyState.ReturningCargo
                            ? [worker.Unit.Value]
                            : []);
                    var hadPrevious = previous.TryGetValue(
                        worker.Unit.Value, out var prior);
                    if (worker.State == TestWorkerEconomyState.ReturningCargo &&
                        (!hadPrevious || prior.State !=
                            TestWorkerEconomyState.ReturningCargo))
                    {
                        approachSamples++;
                        var expected = runtime.PreviewDropOffApproach(
                            1, worker.CargoKind, worker.Unit);
                        var assigned = runtime.ObserveMovement(
                            worker.Unit).NavigationTarget;
                        var unit = runtime.Observe(worker.Unit);
                        var gap = IndependentRectangleGap(
                            assigned,
                            unit.Radius,
                            expected.Center,
                            expected.HalfExtents * 2f);
                        var tolerance = IndependentNumericTolerance(
                            assigned,
                            expected.Center,
                            expected.HalfExtents * 2f);
                        if (!expected.Found ||
                            expected.HalfExtents.X <= 0f ||
                            expected.HalfExtents.Y <= 0f ||
                            gap > tolerance || gap < -tolerance)
                            invalidApproaches++;
                    }
                    if (hadPrevious &&
                        prior.State == TestWorkerEconomyState.ReturningCargo &&
                        prior.CargoAmount > 0 &&
                        worker.State == TestWorkerEconomyState.GoingToResource &&
                        worker.CargoAmount == 0)
                        cycles++;
                    previous[worker.Unit.Value] = worker;
                }
            });
        }
        return session;
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
        const float scoutMaximumSpeed = 170f;
        const long retreatTick = 540;
        var maximumMapTravelTicks = (long)Math.Ceiling(
            (navigationMap.WorldBounds.Width + navigationMap.WorldBounds.Height) /
            scoutMaximumSpeed * 60f);
        var retreatDeadline = retreatTick + maximumMapTravelTicks + 60;
        var durationTicks = checked((int)retreatDeadline + 1);
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
        var visibleEnemyUnitObserved = false;
        var visibleEnemyBuildingObserved = false;
        var scoutAtVisibilitySample = Vector2.Zero;
        var enemyBuildingStateAtSample = TestBuildingLifecycleState.ReservedApproach;
        var visibleAttackAccepted = false;
        var visibleAttackCode = TestPlayerOrderCommandCode.InvalidUnit;
        var hiddenAgain = false;
        var explorationRetained = false;
        var retreatScoutPosition = Vector2.Zero;
        var retreatResourceVisibility = TestMapVisibility.Hidden;
        var retreatKnownRemaining = int.MinValue;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 600;
        var visible = new[] { scout, enemy, enemyBuilder };
        var session = new VisualTestSession(
            "player-visibility-authority",
            "Player view, explored fog and authoritative command ownership",
            durationTicks,
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
                var versions = package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                               hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion;
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
                    $"{visibleAttackAccepted}({visibleAttackCode}), " +
                    $"entities={visibleEnemyUnitObserved}/" +
                    $"{visibleEnemyBuildingObserved}@scout" +
                    $"{scoutAtVisibilitySample.X:0}," +
                    $"{scoutAtVisibilitySample.Y:0}/" +
                    $"building-{enemyBuildingStateAtSample}, " +
                    $"hiddenAgain={hiddenAgain}, " +
                    $"explored={explorationRetained}, retreat-resource=" +
                    $"{retreatResourceVisibility}/{retreatKnownRemaining}@" +
                    $"{retreatScoutPosition.X:0},{retreatScoutPosition.Y:0}, " +
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
            visibleEnemyUnitObserved = view.Units.Contains(enemy);
            visibleEnemyBuildingObserved = view.Buildings.Contains(
                enemyBarracks.BuildingId);
            scoutAtVisibilitySample = runtime.Observe(scout).Position;
            enemyBuildingStateAtSample = runtime.ObserveGameplayBuilding(
                enemyBarracks.BuildingId).State;
            visibleAttackCode = runtime.PlayerAttackUnit(1, [scout], enemy);
            visibleAttackAccepted = visibleAttackCode ==
                                    TestPlayerOrderCommandCode.Success;
            runtime.PlayerMove(1, [scout], new Vector2(1050f, 400f));
        });
        session.At(retreatTick, "Observe the completed enemy building from actual sight range",
            runtime =>
            {
                var view = runtime.ObservePlayerView(1);
                visibleEnemyBuildingObserved = view.Buildings.Contains(
                    enemyBarracks.BuildingId);
                visibleEntitiesObserved = visibleEnemyUnitObserved &&
                                          visibleEnemyBuildingObserved;
                scoutAtVisibilitySample = runtime.Observe(scout).Position;
                runtime.PlayerMove(1, [scout], new Vector2(120f, 350f));
            });
        session.At(hotTick, "Capture explored fog while the scout returns", runtime =>
            hotCapture = runtime.CaptureRuntimeState());
        session.When(
            "Wait for the real visible-to-explored retreat transition",
            retreatDeadline,
            runtime =>
            {
                var view = runtime.ObservePlayerView(1);
                if (runtime.Tick < retreatTick || !visibleEntitiesObserved)
                    return false;
                var retreatResource = view.Resources.FirstOrDefault(value =>
                    value.NodeId == minerals);
                return !view.Units.Contains(enemy) &&
                       !view.Buildings.Contains(enemyBarracks.BuildingId) &&
                       retreatResource.NodeId == minerals &&
                       view.ExploredCells > 0 &&
                       view.VisibleCells > 0 &&
                       view.HiddenCells > 0 &&
                       retreatResource.Visibility == TestMapVisibility.Explored &&
                       retreatResource.KnownRemaining == -1;
            },
            runtime =>
            {
                var view = runtime.ObservePlayerView(1);
                hiddenAgain = !view.Units.Contains(enemy) &&
                              !view.Buildings.Contains(enemyBarracks.BuildingId);
                retreatScoutPosition = runtime.Observe(scout).Position;
                var retreatResource = view.Resources.Single(value =>
                    value.NodeId == minerals);
                retreatResourceVisibility = retreatResource.Visibility;
                retreatKnownRemaining = retreatResource.KnownRemaining;
                explorationRetained = view.ExploredCells > 0 &&
                                      view.VisibleCells > 0 &&
                                      view.HiddenCells > 0 &&
                                      retreatResource.Visibility ==
                                          TestMapVisibility.Explored &&
                                      retreatResource.KnownRemaining == -1;
            });
        session.FinishWhenConditionsCompleted();
        return session;
    }

    private static VisualTestSession CreateConstructionPlayerKnownPlacement()
    {
        var navigationMap = DemoMapDefinition.CreateSnapshot();
        var gameplayProfiles = DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            24, navigationMap, gameplayProfiles, null);
        rig.RegisterPlayer(1, 3000, 500, 30);
        rig.RegisterPlayer(2, 3000, 500, 30);
        var friendlyBuilder = rig.SpawnWorker(
            new Vector2(80f, 120f), 1, maximumSpeed: 400f, acceleration: 2000f);
        var hiddenBuilder = rig.SpawnWorker(
            new Vector2(80f, 300f), 1, maximumSpeed: 400f, acceleration: 2000f);
        var visibleBuilder = rig.SpawnWorker(
            new Vector2(80f, 500f), 1, maximumSpeed: 400f, acceleration: 2000f);
        var friendlyOccupant = rig.SpawnCombat(new Vector2(280f, 120f), 1);
        var hiddenEnemy = rig.SpawnCombat(new Vector2(800f, 300f), 2);
        var visibleEnemy = rig.SpawnCombat(new Vector2(320f, 500f), 2);
        var scout = rig.SpawnCombat(new Vector2(300f, 440f), 1);
        var profile = DemoBuildingTypes.SupplyDepot with { BuildSeconds = 1f };
        var friendlyTarget = new Vector2(280f, 120f);
        var hiddenTarget = new Vector2(800f, 300f);
        var visibleTarget = new Vector2(320f, 500f);
        rig.StartReplayPackageRecording();

        var hiddenAtIssue = false;
        var friendlyPreviewAccepted = false;
        var hiddenPreviewAccepted = false;
        var visiblePreviewRejected = false;
        var visibleRetryAccepted = false;
        var knownFriendlyWait = false;
        var friendlyStateAtSample = TestBuildingLifecycleState.ReservedApproach;
        var friendlyStatusAtSample = TestPublicConstructionStatus.None;
        var friendlyBuilderAtSample = Vector2.Zero;
        var friendlyOccupantAtSample = Vector2.Zero;
        var clearingObserved = false;
        var knownEnemyWait = false;
        TestConstructionResult friendlyBuild = default;
        TestConstructionResult hiddenBuild = default;
        TestConstructionResult visibleBuild = default;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 160;
        var visibleUnits = new[]
        {
            friendlyBuilder, hiddenBuilder, visibleBuilder, friendlyOccupant,
            hiddenEnemy, visibleEnemy, scout
        };
        var session = new VisualTestSession(
            "construction-player-known-placement",
            "Player-known construction placement and authority revalidation",
            360,
            rig,
            visibleUnits,
            runtime =>
            {
                var friendly = runtime.ObserveGameplayBuilding(
                    friendlyBuild.BuildingId);
                var hidden = runtime.ObserveGameplayBuilding(hiddenBuild.BuildingId);
                var visible = runtime.ObserveGameplayBuilding(
                    visibleBuild.BuildingId);
                var package = runtime.CaptureReplayPackage();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var hot = runtime.BindHotSnapshot(package, hotCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out _);
                var completed = friendly.State == TestBuildingLifecycleState.Completed &&
                                hidden.State == TestBuildingLifecycleState.Completed &&
                                visible.State == TestBuildingLifecycleState.Completed;
                var passed = hiddenAtIssue && friendlyPreviewAccepted &&
                             hiddenPreviewAccepted && visiblePreviewRejected &&
                             visibleRetryAccepted && friendlyBuild.Succeeded &&
                             hiddenBuild.Succeeded && visibleBuild.Succeeded &&
                             knownFriendlyWait && clearingObserved &&
                             knownEnemyWait && completed && packageRoundTrip &&
                             hotRoundTrip;
                return new ScenarioResult(
                    passed,
                    $"known={friendlyPreviewAccepted}/{visiblePreviewRejected}/" +
                    $"{hiddenPreviewAccepted}/{hiddenAtIssue}, " +
                    $"status={knownFriendlyWait}/{clearingObserved}/" +
                    $"{knownEnemyWait}, retry={visibleRetryAccepted}, " +
                    $"sample={friendlyStateAtSample}/" +
                    $"{friendlyStatusAtSample}@builder" +
                    $"{friendlyBuilderAtSample.X:0},{friendlyBuilderAtSample.Y:0}/" +
                    $"occupant{friendlyOccupantAtSample.X:0}," +
                    $"{friendlyOccupantAtSample.Y:0}, " +
                    $"states={friendly.State}/{hidden.State}/{visible.State}, " +
                    $"package={packageRoundTrip}, hot={hotRoundTrip}");
            })
            .RenderSpawnedUnits()
            .CameraKeyframe(0, new Vector2(300f, 300f), 0.9f)
            .CameraKeyframe(120, new Vector2(680f, 300f), 0.9f)
            .CameraKeyframe(260, new Vector2(800f, 300f), 1.05f)
            .Highlight(
                new SimRect(new Vector2(210f, 60f), new Vector2(360f, 180f)),
                "FRIENDLY: reservation accepted, policy decides evacuation",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(710f, 220f), new Vector2(890f, 380f)),
                "FOG: hidden occupant cannot change preview",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(240f, 430f), new Vector2(400f, 570f)),
                "VISIBLE ENEMY: placement rejected",
                TestDiagnosticKind.Rejected);
        session.At(5, "Hold friendly occupant and establish visibility", runtime =>
            runtime.Hold([friendlyOccupant]));
        session.At(10, "Compare friendly, hidden enemy and visible enemy previews",
            runtime =>
            {
                hiddenAtIssue = !runtime.ObservePlayerView(1).Units.Contains(
                    hiddenEnemy);
                var friendlyPreview = runtime.PreviewBuild(
                    1, friendlyBuilder, profile, friendlyTarget);
                var hiddenPreview = runtime.PreviewBuild(
                    1, hiddenBuilder, profile, hiddenTarget);
                var visiblePreview = runtime.PreviewBuild(
                    1, visibleBuilder, profile, visibleTarget);
                friendlyPreviewAccepted = friendlyPreview.Succeeded;
                hiddenPreviewAccepted = hiddenPreview.Succeeded;
                visiblePreviewRejected =
                    visiblePreview.Code ==
                        TestConstructionCommandCode.PlacementRejected &&
                    visiblePreview.PlacementCode ==
                        TestBuildingPlacementCode.UnitOverlap;
                friendlyBuild = runtime.Build(
                    1, friendlyBuilder, profile, friendlyTarget);
                hiddenBuild = runtime.Build(
                    1, hiddenBuilder, profile, hiddenTarget);
            });
        session.At(20, "Move the visible enemy away", runtime =>
            runtime.PlayerMove(2, [visibleEnemy], new Vector2(540f, 500f)));
        session.At(45, "Retry placement after visible blocker leaves", runtime =>
        {
            var preview = runtime.PreviewBuild(
                1, visibleBuilder, profile, visibleTarget);
            visibleRetryAccepted = preview.Succeeded;
            visibleBuild = runtime.Build(
                1, visibleBuilder, profile, visibleTarget);
        });
        for (var tick = 50; tick < 60; tick++)
        {
            session.At(tick, "Observe known-occupant feedback before releasing Hold",
                runtime =>
            {
                var view = runtime.ObservePlayerView(1);
                var friendlyView = view.BuildingViews.Single(value =>
                    value.BuildingId == friendlyBuild.BuildingId);
                friendlyStateAtSample = runtime.ObserveGameplayBuilding(
                    friendlyBuild.BuildingId).State;
                friendlyStatusAtSample = friendlyView.ConstructionStatus;
                friendlyBuilderAtSample = runtime.Observe(
                    friendlyBuilder).Position;
                friendlyOccupantAtSample = runtime.Observe(
                    friendlyOccupant).Position;
                knownFriendlyWait |= friendlyView.ConstructionStatus ==
                    TestPublicConstructionStatus.KnownOccupant;
            });
        }
        session.At(60, "Release Hold without replacing the construction order",
            runtime => runtime.PlayerMove(
                1, [friendlyOccupant], new Vector2(500f, 120f)));
        foreach (var tick in new long[] { 62, 66, 70, 74 })
        {
            session.At(tick, "Observe the public friendly-clearing state", runtime =>
            {
                clearingObserved |= runtime.ObservePlayerView(1).BuildingViews.Any(
                    value => value.BuildingId == friendlyBuild.BuildingId &&
                             value.ConstructionStatus ==
                                 TestPublicConstructionStatus.ClearingFriendlyUnits);
            });
        }
        for (var tick = 130; tick < 180; tick++)
        {
            session.At(tick, "Observe the authority wait before the blocker moves",
                runtime =>
                {
                    var view = runtime.ObservePlayerView(1);
                    knownEnemyWait |= view.Units.Contains(hiddenEnemy) &&
                        view.BuildingViews.Any(value =>
                            value.BuildingId == hiddenBuild.BuildingId &&
                            value.ConstructionStatus ==
                                TestPublicConstructionStatus.KnownOccupant);
                });
        }
        session.At(hotTick, "Capture an authority-blocked construction future",
            runtime => hotCapture = runtime.CaptureRuntimeState());
        session.At(180, "Move the now-visible enemy and allow hard commit", runtime =>
            runtime.PlayerMove(2, [hiddenEnemy], new Vector2(950f, 300f)));
        return session;
    }

    private static VisualTestSession CreateConcealmentDetectionConstruction()
    {
        var navigationMap = DemoMapDefinition.CreateSnapshot();
        var gameplayProfiles = DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            20, navigationMap, gameplayProfiles, null);
        rig.RegisterPlayer(1, 3000, 500, 30);
        rig.RegisterPlayer(2, 3000, 500, 30);
        var builder = rig.SpawnWorker(
            new Vector2(766f, 300f), 1,
            maximumSpeed: 400f, acceleration: 2000f);
        var harmlessWeapon = TestCombatProfile.Standard with
        {
            AttackDamage = 0f
        };
        var scout = rig.SpawnCombat(
            new Vector2(700f, 390f), 1, harmlessWeapon);
        var detector = rig.SpawnCombat(
            new Vector2(450f, 520f), 1,
            maximumSpeed: 300f,
            acceleration: 1400f,
            perception: new TestPerceptionProfile(
                TestUnitConcealmentKind.None, 110f));
        var buriedBlocker = rig.SpawnCombat(
            new Vector2(800f, 300f), 2,
            perception: new TestPerceptionProfile(
                TestUnitConcealmentKind.Burrowed, 0f));
        var buriedContact = rig.SpawnCombat(
            new Vector2(300f, 120f), 2,
            perception: new TestPerceptionProfile(
                TestUnitConcealmentKind.Burrowed, 0f));
        var passer = rig.SpawnCombat(
            new Vector2(180f, 120f), 1,
            maximumSpeed: 300f, acceleration: 1400f);
        var target = new Vector2(800f, 300f);
        var profile = DemoBuildingTypes.SupplyDepot with { BuildSeconds = 1f };
        rig.StartReplayPackageRecording();
        rig.Hold([scout, detector, buriedBlocker, buriedContact, passer]);

        var hiddenOnVisibleGround = false;
        var previewAccepted = false;
        var hiddenAttackRejected = false;
        var authorityWaitHidden = false;
        var burrowContactSuppressed = false;
        var detectedStateExposed = false;
        var detectedAttackAccepted = false;
        var waitState = TestBuildingLifecycleState.ReservedApproach;
        var blockerAtWait = Vector2.Zero;
        var builderAtWait = Vector2.Zero;
        TestConstructionResult build = default;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 169;
        var session = new VisualTestSession(
            "concealment-detection-construction",
            "Separate sight, detection and authority construction blocking",
            420,
            rig,
            [builder, scout, detector, buriedBlocker, buriedContact, passer],
            runtime =>
            {
                var building = runtime.ObserveGameplayBuilding(build.BuildingId);
                var package = runtime.CaptureReplayPackage();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var hot = runtime.BindHotSnapshot(package, hotCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var replay = runtime.ReplayPackage(decoded!, runtime.Tick);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var replayExact = replay.FinalHash == runtime.StateHash;
                var hotExact = resumed.FinalHash == runtime.StateHash;
                var exact = replayExact && hotExact;
                var passed = hiddenOnVisibleGround && previewAccepted &&
                             hiddenAttackRejected && authorityWaitHidden &&
                             burrowContactSuppressed && detectedStateExposed &&
                             detectedAttackAccepted &&
                             building.State == TestBuildingLifecycleState.Completed &&
                             packageRoundTrip && hotRoundTrip && exact &&
                             package.FormatVersion ==
                                 SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                             hot.FormatVersion ==
                                 SimulationHotSnapshot.CurrentFormatVersion;
                return new ScenarioResult(
                    passed,
                    $"hidden={hiddenOnVisibleGround}/{hiddenAttackRejected}, " +
                    $"preview={previewAccepted}, wait={authorityWaitHidden}, " +
                    $"burrowPass={burrowContactSuppressed}, " +
                    $"detected={detectedStateExposed}/{detectedAttackAccepted}, " +
                    $"state={building.State}, waitState={waitState}, " +
                    $"waitPos={builderAtWait}/{blockerAtWait}, " +
                    $"versions=package{package.FormatVersion}/" +
                    $"hot{hot.FormatVersion}, exact={exact}");
            })
            .RenderSpawnedUnits()
            .CameraKeyframe(0, new Vector2(620f, 300f), 0.85f)
            .CameraKeyframe(150, new Vector2(780f, 300f), 1.05f)
            .CameraKeyframe(300, new Vector2(900f, 300f), 0.95f)
            .Highlight(
                new SimRect(new Vector2(740f, 240f), new Vector2(860f, 360f)),
                "VISIBLE GROUND: undetected burrow blocks authority only",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(160f, 80f), new Vector2(460f, 160f)),
                "BURROW CONTACT: normal unit passes, buildings still block",
                TestDiagnosticKind.Accepted);
        session.At(10, "Issue on visible ground without detection", runtime =>
        {
            var view = runtime.ObservePlayerView(1);
            hiddenOnVisibleGround = view.VisibleCells > 0 &&
                                    !view.Units.Contains(buriedBlocker);
            var preview = runtime.PreviewBuild(1, builder, profile, target);
            previewAccepted = preview.Succeeded;
            hiddenAttackRejected = runtime.PlayerAttackUnit(
                1, [scout], buriedBlocker) ==
                TestPlayerOrderCommandCode.TargetNotVisible;
            build = runtime.Build(1, builder, profile, target);
            if (!build.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Concealment construction issue failed: " +
                    $"{build.Code}/{build.PlacementCode}; " +
                    $"preview={preview.Code}/{preview.PlacementCode}.");
            }
            runtime.PlayerMove(1, [passer], new Vector2(440f, 120f));
        });
        session.At(115, "Burrowed unit suppresses ordinary unit contact", runtime =>
        {
            var moving = runtime.Observe(passer);
            var buried = runtime.Observe(buriedContact);
            burrowContactSuppressed = moving.Position.X > 380f &&
                                      Vector2.Distance(
                                          buried.Position,
                                          new Vector2(300f, 120f)) < 1f;
        });
        session.At(hotTick, "Capture the undetected authority wait", runtime =>
        {
            var building = runtime.ObserveGameplayBuilding(build.BuildingId);
            waitState = building.State;
            blockerAtWait = runtime.Observe(buriedBlocker).Position;
            builderAtWait = runtime.Observe(builder).Position;
            var view = runtime.ObservePlayerView(1);
            authorityWaitHidden =
                building.State == TestBuildingLifecycleState.BlockedAtStart &&
                !view.Units.Contains(buriedBlocker) &&
                view.BuildingViews.Any(value =>
                    value.BuildingId == build.BuildingId &&
                    value.ConstructionStatus ==
                        TestPublicConstructionStatus.WaitingForClearance);
            hotCapture = runtime.CaptureRuntimeState();
        });
        session.At(180, "Move an explicit detector into range", runtime =>
            runtime.PlayerMove(1, [detector], new Vector2(730f, 370f)));
        session.At(270, "Detection reveals state and enables targeting", runtime =>
        {
            var view = runtime.ObservePlayerView(1);
            detectedStateExposed = view.UnitViews.Any(value =>
                value.Unit == buriedBlocker &&
                value.ConcealmentState ==
                    TestPlayerConcealmentState.ConcealedDetected) &&
                view.BuildingViews.Any(value =>
                    value.BuildingId == build.BuildingId &&
                    value.ConstructionStatus ==
                        TestPublicConstructionStatus.KnownOccupant);
            detectedAttackAccepted = runtime.PlayerAttackUnit(
                1, [scout], buriedBlocker) ==
                TestPlayerOrderCommandCode.Success;
        });
        session.At(280, "Remove the authority blocker and commit", runtime =>
            runtime.PlayerMove(2, [buriedBlocker], new Vector2(1040f, 300f)));
        return session;
    }

    private static VisualTestSession CreateActiveBurrowDetectionLifecycle()
    {
        var navigationMap = DemoMapDefinition.CreateSnapshot();
        var gameplayProfiles = DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            16, navigationMap, gameplayProfiles, null);
        rig.RegisterPlayer(1, 3000, 500, 30);
        rig.RegisterPlayer(2, 3000, 500, 30);

        var harmless = TestCombatProfile.Standard with
        {
            AttackDamage = 0f
        };
        var burrower = rig.SpawnCombat(
            new Vector2(800f, 300f), 2, harmless,
            maximumSpeed: 260f,
            acceleration: 1200f,
            concealmentCapability: TestConcealmentCapability.StandardBurrow);
        var queuedBurrower = rig.SpawnCombat(
            new Vector2(980f, 520f), 2, harmless,
            concealmentCapability: TestConcealmentCapability.StandardBurrow);
        var contactBurrower = rig.SpawnCombat(
            new Vector2(300f, 120f), 2, harmless,
            concealmentCapability: TestConcealmentCapability.StandardBurrow);
        var passer = rig.SpawnCombat(
            new Vector2(180f, 120f), 1, harmless,
            maximumSpeed: 300f,
            acceleration: 1500f);
        var sightProbe = rig.SpawnCombat(
            new Vector2(700f, 390f), 1, harmless);
        var detector = rig.SpawnCombat(
            new Vector2(450f, 520f), 1, harmless,
            maximumSpeed: 320f,
            acceleration: 1600f,
            perception: new TestPerceptionProfile(
                TestUnitConcealmentKind.None, 110f));
        rig.StartReplayPackageRecording();
        rig.Hold(
            [burrower, queuedBurrower, contactBurrower, passer, sightProbe,
                detector]);

        var visibleBefore = false;
        var activatingExposed = false;
        var invalidToggleRejected = false;
        var hotCapturedDuringTransition = false;
        var hiddenWithoutDetection = false;
        var reducedBurrowSight = false;
        var restrictedCommands = false;
        var mixedDepthPass = false;
        var passPosition = Vector2.Zero;
        var queuedRoundTrip = false;
        var detectedAndTargetable = false;
        var deactivatingStillConcealed = false;
        var restoredMovement = false;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 50;

        var session = new VisualTestSession(
            "active-burrow-detection-lifecycle",
            "Active Burrow lifecycle, detection and queued orders",
            480,
            rig,
            [burrower, queuedBurrower, contactBurrower, passer, sightProbe, detector],
            runtime =>
            {
                var final = runtime.ObserveConcealment(burrower);
                var queuedFinal = runtime.ObserveConcealment(queuedBurrower);
                var finalUnit = runtime.Observe(burrower);
                var moved = finalUnit.Position;
                var finalOrder = runtime.ObserveOrders(burrower);
                var package = runtime.CaptureReplayPackage();
                var packageRoundTrip = package.TryCanonicalRoundTrip(
                    out var decoded);
                var hot = runtime.BindHotSnapshot(package, hotCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var replay = runtime.ReplayPackage(decoded!, runtime.Tick);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var exact = replay.FinalHash == runtime.StateHash &&
                            resumed.FinalHash == runtime.StateHash;
                var passed = visibleBefore && activatingExposed &&
                             invalidToggleRejected &&
                             hotCapturedDuringTransition &&
                             hiddenWithoutDetection && reducedBurrowSight &&
                             restrictedCommands && mixedDepthPass &&
                             queuedRoundTrip && detectedAndTargetable &&
                             deactivatingStillConcealed && restoredMovement &&
                             final.Phase == TestUnitConcealmentPhase.Visible &&
                             final.ActiveKind == TestUnitConcealmentKind.None &&
                             queuedFinal.Phase ==
                                 TestUnitConcealmentPhase.Visible &&
                             Vector2.Distance(
                                 moved, new Vector2(800f, 300f)) > 20f &&
                             packageRoundTrip && hotRoundTrip &&
                             exact && package.FormatVersion ==
                                 SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                             hot.FormatVersion ==
                                 SimulationHotSnapshot.CurrentFormatVersion;
                return new ScenarioResult(
                    passed,
                    $"visible={visibleBefore}, activate={activatingExposed}, " +
                    $"invalid={invalidToggleRejected}, " +
                    $"hidden/sight={hiddenWithoutDetection}/{reducedBurrowSight}, " +
                    $"restricted/pass={restrictedCommands}/{mixedDepthPass}, " +
                    $"passPos={passPosition}, " +
                    $"queue={queuedRoundTrip}, detect={detectedAndTargetable}, " +
                    $"deactivate={deactivatingStillConcealed}/{restoredMovement}, " +
                    $"phases={final.Phase}/{queuedFinal.Phase}, moved={moved}, " +
                    $"unit={finalUnit.State}/{finalUnit.AssignedTarget}, " +
                    $"order={(byte)finalOrder.ActiveOrder}/" +
                    $"pending{finalOrder.PendingOrders}, " +
                    $"roundTrip={packageRoundTrip}/{hotRoundTrip}, " +
                    $"versions=package{package.FormatVersion}/hot{hot.FormatVersion}, " +
                    $"exact={exact}");
            })
            .RenderSpawnedUnits()
            .CameraKeyframe(0, new Vector2(650f, 320f), 0.78f)
            .CameraKeyframe(170, new Vector2(430f, 220f), 0.92f)
            .CameraKeyframe(285, new Vector2(610f, 350f), 1.05f)
            .CameraKeyframe(430, new Vector2(720f, 390f), 0.95f)
            .Highlight(
                new SimRect(new Vector2(540f, 220f), new Vector2(700f, 380f)),
                "BURROW: reduced sight + mixed-depth pass",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(900f, 450f), new Vector2(1060f, 590f)),
                "QUEUE: Burrow then Unburrow",
                TestDiagnosticKind.Accepted);

        session.At(5, "Observe the ordinary visible unit", runtime =>
        {
            var enemyView = runtime.ObservePlayerView(1);
            var ownView = runtime.ObservePlayerView(2);
            visibleBefore = enemyView.Units.Contains(burrower) &&
                            ownView.Units.Contains(sightProbe);
            invalidToggleRejected = runtime.PlayerConcealment(
                2, [burrower], activate: false, queued: true) ==
                TestPlayerOrderCommandCode.ContextActionUnavailable;
        });
        session.At(20, "Begin active Burrow and a queued toggle pair", runtime =>
        {
            if (runtime.PlayerConcealment(2, [burrower], activate: true) !=
                    TestPlayerOrderCommandCode.Success ||
                runtime.PlayerConcealment(
                    2, [queuedBurrower], activate: true) !=
                    TestPlayerOrderCommandCode.Success ||
                runtime.PlayerConcealment(
                    2, [contactBurrower], activate: true) !=
                    TestPlayerOrderCommandCode.Success ||
                runtime.PlayerConcealment(
                    2, [queuedBurrower], activate: false, queued: true) !=
                    TestPlayerOrderCommandCode.Success)
            {
                throw new InvalidOperationException(
                    "Active Burrow commands were not accepted.");
            }
            invalidToggleRejected &= runtime.PlayerConcealment(
                2, [queuedBurrower], activate: false, queued: true) ==
                TestPlayerOrderCommandCode.ContextActionUnavailable;
        });
        session.At(40, "Activation is visible and owned progress is exposed", runtime =>
        {
            var state = runtime.ObserveConcealment(burrower);
            var enemyView = runtime.ObservePlayerView(1);
            var own = runtime.ObservePlayerView(2).UnitViews.Single(
                value => value.Unit == burrower);
            activatingExposed =
                state.Phase == TestUnitConcealmentPhase.Activating &&
                state.TransitionProgress is > 0f and < 1f &&
                enemyView.Units.Contains(burrower) &&
                own.ConcealmentPhase ==
                    TestUnitConcealmentPhase.Activating &&
                own.CanToggleConcealment;
        });
        session.At(hotTick, "Capture a mid-activation hot state", runtime =>
        {
            hotCapture = runtime.CaptureRuntimeState();
            hotCapturedDuringTransition =
                runtime.ObserveConcealment(burrower).Phase ==
                TestUnitConcealmentPhase.Activating;
        });
        session.At(90, "Burrow hides, reduces sight and locks actions", runtime =>
        {
            var state = runtime.ObserveConcealment(burrower);
            hiddenWithoutDetection =
                state.Phase == TestUnitConcealmentPhase.Concealed &&
                state.ActiveKind == TestUnitConcealmentKind.Burrowed &&
                !runtime.ObservePlayerView(1).Units.Contains(burrower);
            reducedBurrowSight =
                !runtime.ObservePlayerView(2).Units.Contains(sightProbe);
            restrictedCommands =
                runtime.PlayerMove(
                    2, [burrower], new Vector2(700f, 420f)) ==
                    TestPlayerOrderCommandCode.ContextActionUnavailable &&
                runtime.PlayerAttackMove(
                    2, [burrower], new Vector2(770f, 300f)) ==
                    TestPlayerOrderCommandCode.ContextActionUnavailable;
            runtime.PlayerMove(1, [passer], new Vector2(440f, 120f));
        });
        session.At(91, "Clear the sight probe from the contact lane", runtime =>
            runtime.PlayerMove(1, [sightProbe], new Vector2(780f, 460f)));
        session.At(145, "Queued Burrow and Unburrow return to visible", runtime =>
        {
            var state = runtime.ObserveConcealment(queuedBurrower);
            queuedRoundTrip =
                state.Phase == TestUnitConcealmentPhase.Visible &&
                state.ActiveKind == TestUnitConcealmentKind.None;
        });
        session.At(210, "Ground unit passes through the buried unit", runtime =>
        {
            passPosition = runtime.Observe(passer).Position;
            mixedDepthPass = passPosition.X > 380f &&
                Vector2.Distance(
                    runtime.Observe(contactBurrower).Position,
                    new Vector2(300f, 120f)) < 1f;
        });
        session.At(190, "Move a detector into the concealed contact", runtime =>
            runtime.PlayerMove(1, [detector], new Vector2(730f, 370f)));
        session.At(285, "Detection reveals and authorizes a target command", runtime =>
        {
            var view = runtime.ObservePlayerView(1);
            detectedAndTargetable = view.UnitViews.Any(value =>
                    value.Unit == burrower &&
                    value.ConcealmentState ==
                        TestPlayerConcealmentState.ConcealedDetected) &&
                runtime.PlayerAttackUnit(1, [passer], burrower) ==
                    TestPlayerOrderCommandCode.Success;
        });
        session.At(300, "Stop the harmless detector-side attack", runtime =>
            runtime.Stop([passer]));
        session.At(310, "Begin Unburrow", runtime =>
        {
            if (runtime.PlayerConcealment(2, [burrower], activate: false) !=
                TestPlayerOrderCommandCode.Success)
            {
                throw new InvalidOperationException("Unburrow was not accepted.");
            }
        });
        session.At(330, "Unburrow remains concealed until completion", runtime =>
        {
            var state = runtime.ObserveConcealment(burrower);
            var view = runtime.ObservePlayerView(1);
            deactivatingStillConcealed =
                state.Phase == TestUnitConcealmentPhase.Deactivating &&
                state.ActiveKind == TestUnitConcealmentKind.Burrowed &&
                view.UnitViews.Any(value =>
                    value.Unit == burrower &&
                    value.ConcealmentState ==
                        TestPlayerConcealmentState.ConcealedDetected) &&
                runtime.PlayerMove(
                    2, [burrower], new Vector2(700f, 420f)) ==
                    TestPlayerOrderCommandCode.ContextActionUnavailable;
        });
        session.At(370, "Movement returns only after Unburrow completes", runtime =>
        {
            var state = runtime.ObserveConcealment(burrower);
            restoredMovement =
                state.Phase == TestUnitConcealmentPhase.Visible &&
                state.ActiveKind == TestUnitConcealmentKind.None &&
                runtime.PlayerMove(
                    2, [burrower], new Vector2(1040f, 300f)) ==
                    TestPlayerOrderCommandCode.Success;
        });
        return session;
    }

    private static VisualTestSession CreateAllianceSharedVisionTeamVictory()
    {
        var navigationMap = DemoMapDefinition.CreateSnapshot();
        var gameplayProfiles = DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, null);
        for (var player = 1; player <= 4; player++)
            rig.RegisterPlayer(player, 3000, 500, 30);
        rig.ConfigureAlliance(100, true, 1, 2);
        rig.ConfigureAlliance(200, true, 3, 4);

        var builders = new[]
        {
            rig.SpawnWorker(new Vector2(120f, 500f), 1),
            rig.SpawnWorker(new Vector2(930f, 570f), 2),
            rig.SpawnWorker(new Vector2(800f, 210f), 3),
            rig.SpawnWorker(new Vector2(400f, 360f), 4)
        };
        var placementBuilder = rig.SpawnWorker(new Vector2(340f, 580f), 1);
        var attacker = rig.SpawnCombat(new Vector2(700f, 350f), 1);
        var alliedDetector = rig.SpawnCombat(
            new Vector2(790f, 350f), 2,
            perception: new TestPerceptionProfile(
                TestUnitConcealmentKind.None, 120f));
        var concealedAlly = rig.SpawnCombat(
            new Vector2(820f, 410f), 2,
            perception: new TestPerceptionProfile(
                TestUnitConcealmentKind.Cloaked, 0f));
        var concealedEnemy = rig.SpawnCombat(
            new Vector2(860f, 350f), 3,
            perception: new TestPerceptionProfile(
                TestUnitConcealmentKind.Burrowed, 0f));
        var alliedPlacementBlocker = rig.SpawnCombat(
            new Vector2(500f, 500f), 2,
            profile: TestCombatProfile.Standard with { AttackDamage = 0f });

        var baseProfile = DemoBuildingTypes.CommandCenter with
        {
            BuildSeconds = 0.5f,
            MaximumHealth = 260f
        };
        rig.StartMatch(1, 2, 3, 4);
        rig.StartReplayPackageRecording();
        var bases = new[]
        {
            rig.Build(1, builders[0], baseProfile, new Vector2(250f, 500f)),
            rig.Build(2, builders[1], baseProfile, new Vector2(1050f, 570f)),
            rig.Build(3, builders[2], DemoBuildingTypes.Barracks with
            {
                BuildSeconds = 0.5f,
                MaximumHealth = 240f
            }, new Vector2(800f, 140f)),
            rig.Build(4, builders[3], DemoBuildingTypes.Barracks with
            {
                BuildSeconds = 0.5f,
                MaximumHealth = 240f
            }, new Vector2(400f, 260f))
        };

        var sharedVision = false;
        var relations = false;
        var friendlyFireRejected = false;
        var detectedEnemyTargetable = false;
        var alliedPlacementWait = false;
        var alliedPlacementCode = TestBuildingPlacementCode.Success;
        var teamVictory = false;
        var preVictoryStateHash = 0UL;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 300;
        var session = new VisualTestSession(
            "alliance-shared-vision-team-victory",
            "2v2 alliance relations, shared detection and team victory",
            420,
            rig,
            builders.Append(placementBuilder).Concat([
                attacker, alliedDetector, concealedAlly, concealedEnemy,
                alliedPlacementBlocker]).ToArray(),
            runtime =>
            {
                var match = runtime.ObserveMatch();
                var package = runtime.CaptureReplayPackage();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var hot = runtime.BindHotSnapshot(package, hotCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var replay = runtime.ReplayPackage(decoded!, 100);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var replayExact = replay.FinalHash == preVictoryStateHash;
                var hotExact = resumed.FinalHash == runtime.StateHash;
                var exact = replayExact && hotExact;
                teamVictory = match.Phase == TestMatchPhase.Completed &&
                              match.WinnerPlayerId == -1 &&
                              match.WinnerAllianceId == 100 &&
                              match.Players.Where(value => value.PlayerId <= 2)
                                  .All(value => value.Status ==
                                      TestMatchPlayerStatus.Victorious) &&
                              match.Players.Where(value => value.PlayerId >= 3)
                                  .All(value => value.Status ==
                                      TestMatchPlayerStatus.Defeated);
                var passed = bases.All(value => value.Succeeded) &&
                             sharedVision && relations &&
                             friendlyFireRejected && detectedEnemyTargetable &&
                             alliedPlacementWait && teamVictory &&
                             packageRoundTrip && hotRoundTrip && exact &&
                             package.FormatVersion ==
                                 SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                             hot.FormatVersion ==
                                 SimulationHotSnapshot.CurrentFormatVersion;
                return new ScenarioResult(
                    passed,
                    $"shared={sharedVision}, relations={relations}, " +
                    $"friendlyFire={friendlyFireRejected}, " +
                    $"detection={detectedEnemyTargetable}, " +
                    $"placementWait={alliedPlacementWait}/{alliedPlacementCode}, " +
                    $"match={match.Phase}[{string.Join(',', match.Players.Select(value => $"P{value.PlayerId}:{value.Status}/{value.ActiveBuildings}"))}], " +
                    $"buildings=[{string.Join(',', bases.Select(value => runtime.ObserveGameplayBuilding(value.BuildingId).State))}], " +
                    $"winner=A{match.WinnerAllianceId}/P{match.WinnerPlayerId}, " +
                    $"versions=package{package.FormatVersion}/hot{hot.FormatVersion}, " +
                    $"exact={replayExact}/{hotExact} " +
                    $"hash={runtime.StateHash:X16}/R{replay.FinalHash:X16}/H{resumed.FinalHash:X16}");
            })
            .RenderSpawnedUnits()
            .CameraKeyframe(0, new Vector2(640f, 350f), 0.82f)
            .CameraKeyframe(90, new Vector2(840f, 350f), 1.15f)
            .CameraKeyframe(150, new Vector2(640f, 350f), 0.72f)
            .Highlight(
                new SimRect(new Vector2(730f, 285f), new Vector2(920f, 450f)),
                "ALLY VISION + DETECTION",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(430f, 430f), new Vector2(570f, 570f)),
                "ALLY OCCUPANT: conservative placement wait",
                TestDiagnosticKind.Info);
        session.At(15, "Validate alliance-relative player observation", runtime =>
        {
            var view = runtime.ObservePlayerView(1);
            var ally = view.UnitViews.Single(value => value.Unit == concealedAlly);
            var enemy = view.UnitViews.Single(value => value.Unit == concealedEnemy);
            sharedVision = view.Units.Contains(alliedDetector) &&
                           view.Units.Contains(concealedEnemy);
            relations = ally.Relation == TestPlayerEntityRelation.Ally &&
                        ally.ConcealmentState ==
                            TestPlayerConcealmentState.ConcealedAlly &&
                        enemy.Relation == TestPlayerEntityRelation.Enemy &&
                        enemy.ConcealmentState ==
                            TestPlayerConcealmentState.ConcealedDetected;
            friendlyFireRejected = runtime.PlayerAttackUnit(
                1, [attacker], concealedAlly) ==
                TestPlayerOrderCommandCode.FriendlyTarget;
            detectedEnemyTargetable = runtime.PlayerAttackUnit(
                1, [attacker], concealedEnemy) ==
                TestPlayerOrderCommandCode.Success;
            alliedPlacementCode = runtime.PreviewBuild(
                1, placementBuilder, DemoBuildingTypes.SupplyDepot,
                new Vector2(500f, 500f)).PlacementCode;
            alliedPlacementWait = alliedPlacementCode ==
                                  TestBuildingPlacementCode.UnitOverlap;
        });
        session.At(100, "Capture pre-victory replay state", runtime =>
            preVictoryStateHash = runtime.StateHash);
        session.At(220, "Eliminate both members of the opposing alliance", runtime =>
        {
            runtime.DamageBuilding(bases[2].BuildingId, 100000f);
            runtime.DamageBuilding(bases[3].BuildingId, 100000f);
        });
        session.At(hotTick, "Capture completed 2v2 alliance state", runtime =>
            hotCapture = runtime.CaptureRuntimeState());
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
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion && hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion;
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

    private static VisualTestSession CreateModularAiSkirmish(
        AiConfigurationCatalogSnapshot? aiConfigurations)
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
                var constructionStates = string.Join(',',
                    runtime.ObserveGameplayBuildings()
                        .Where(value => value.State is not
                            TestBuildingLifecycleState.Canceled and not
                            TestBuildingLifecycleState.Destroyed)
                        .GroupBy(value => value.State)
                        .OrderBy(value => value.Key)
                        .Select(value => $"{value.Key}:{value.Count()}"));
                var pendingApproaches = string.Join(';',
                    runtime.ObserveGameplayBuildings()
                        .Where(value => value.State is
                            TestBuildingLifecycleState.ReservedApproach or
                            TestBuildingLifecycleState.BlockedAtStart)
                        .Select(value =>
                        {
                            var builder = runtime.Observe(value.BuilderUnit);
                            return $"{value.Id.Value}/{value.Name}:" +
                                   $"b{value.BuilderUnit.Value}@{builder.Position}," +
                                   $"center={value.Center},access={value.AccessPoint}," +
                                   $"slot={builder.AssignedTarget},v={builder.Velocity}," +
                                   $"mode={builder.State}";
                        }));
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
                    $"states=[{constructionStates}], " +
                    $"pending=[{pendingApproaches}], " +
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
            var profile = (aiConfigurations ?? DemoAiConfigurations.CreateCatalog())
                .Profile(0) with { AttackArmySize = 4 };
            runtime.AttachDemoAi(1, profile, buildingSeconds: 1.2f);
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

    private static VisualTestSession CreateDualAiRuntimeReplay(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake,
        AiConfigurationCatalogSnapshot? aiConfigurations)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        aiConfigurations ??= DemoAiConfigurations.CreateCatalog();
        var rig = MovementTestRig.CreateReplayPackageMap(
            96, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 3600, 1000, 15, 6);
        rig.RegisterPlayer(2, 3600, 1000, 15, 6);
        var leftWorkers = Enumerable.Range(0, 6).Select(index =>
            rig.SpawnWorker(
                new Vector2(80f + index % 2 * 18f, 300f + index * 24f), 1))
            .ToArray();
        var rightWorkers = Enumerable.Range(0, 6).Select(index =>
            rig.SpawnWorker(
                new Vector2(1190f - index % 2 * 18f, 440f + index * 24f), 2))
            .ToArray();
        var leftMinerals = new[]
        {
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(370f, 250f), 4000, 8, 0.35f, 2),
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(410f, 350f), 4000, 8, 0.35f, 2),
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(370f, 460f), 4000, 8, 0.35f, 2)
        };
        var rightMinerals = new[]
        {
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(1090f, 190f), 4000, 8, 0.35f, 2),
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(1110f, 330f), 4000, 8, 0.35f, 2),
            rig.AddResourceNode(TestEconomyResourceKind.Minerals,
                new Vector2(1060f, 640f), 4000, 8, 0.35f, 2)
        };
        rig.AddResourceNode(TestEconomyResourceKind.VespeneGas,
            new Vector2(300f, 570f), 4000, 8, 0.4f, 3, true, false);
        rig.AddResourceNode(TestEconomyResourceKind.VespeneGas,
            new Vector2(1170f, 150f), 4000, 8, 0.4f, 3, true, false);
        var townHall = DemoBuildingTypes.CommandCenter with
        {
            BuildSeconds = 0.5f,
            MaximumHealth = 720f,
            SupplyProvided = 0
        };
        rig.StartMatch(1, 2);
        rig.StartReplayPackageRecording();
        var leftBase = rig.Build(
            1, leftWorkers[0], townHall, new Vector2(220f, 350f));
        var rightBase = rig.Build(
            2, rightWorkers[0], townHall, new Vector2(1080f, 500f));
        for (var index = 1; index < 6; index++)
        {
            rig.Gather(1, leftWorkers[index], leftMinerals[(index - 1) % 3]);
            rig.Gather(2, rightWorkers[index], rightMinerals[(index - 1) % 3]);
        }

        TestAiRuntimeCapture? pairedCapture = null;
        var bothActive = false;
        const long captureTick = 1200;
        var visible = leftWorkers.Concat(rightWorkers).ToArray();
        var session = new VisualTestSession(
            "ai-dual-runtime-replay",
            "Two offset AIs with paired hot restore and command-only replay",
            4200,
            rig,
            visible,
            runtime =>
            {
                var package = runtime.CaptureReplayPackage();
                var persistence = runtime.ValidateAiPersistence(
                    package, pairedCapture!, runtime.Tick);
                var match = runtime.ObserveMatch();
                var first = runtime.ObserveAi(1);
                var second = runtime.ObserveAi(2);
                var offset = first.LastDecisionTick != second.LastDecisionTick;
                var packageRoundTrip = package.TryCanonicalRoundTrip(out _);
                var commandCoverage = package.EconomyCommandCount >= 12 &&
                                      package.ConstructionCommandCount >= 8 &&
                                      package.ProductionCommandCount >= 12 &&
                                      package.UnitCommandCount >= 8;
                var configMatches = aiConfigurations.StableHash ==
                                    DemoAiConfigurations.CreateCatalog().StableHash;
                var passed = leftBase.Succeeded && rightBase.Succeeded &&
                             bothActive && pairedCapture?.AgentCount == 2 &&
                             persistence.LiveExact && persistence.ReplayExact &&
                             packageRoundTrip && offset && commandCoverage &&
                             configMatches;
                return new ScenarioResult(
                    passed,
                    $"build={leftBase.Code}/{rightBase.Code}, established={bothActive}, " +
                    $"winner={match.WinnerPlayerId}@{match.CompletedTick}, " +
                    $"capture={pairedCapture?.Tick}/agents{pairedCapture?.AgentCount}, " +
                    $"offset={first.LastDecisionTick}/{second.LastDecisionTick}, " +
                    $"live={persistence.LiveExact}, replayNoAi={persistence.ReplayExact}, " +
                    $"package={package.FormatVersion}/{packageRoundTrip}, " +
                    $"commands=e{package.EconomyCommandCount}/" +
                    $"b{package.ConstructionCommandCount}/" +
                    $"p{package.ProductionCommandCount}/u{package.UnitCommandCount}, " +
                    $"config={configMatches}/{aiConfigurations.StableHashText}");
            })
            .RenderSpawnedUnits()
            .Highlight(
                new SimRect(new Vector2(25f, 80f), new Vector2(520f, 650f)),
                "STANDARD AI: 12-tick decisions, economy before commitment",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(990f, 80f), new Vector2(1255f, 680f)),
                "AGGRESSIVE AI: 10-tick decisions with offset 5",
                TestDiagnosticKind.Rejected)
            .Highlight(
                new SimRect(new Vector2(500f, 290f), new Vector2(760f, 415f)),
                "CHOKE: both restored live AI and command-only replay must match",
                TestDiagnosticKind.Accepted);
        session.At(60, "Load Resource profiles and attach offset AI agents", runtime =>
        {
            bothActive = runtime.ObserveMatch().Players.All(value =>
                value.EstablishedPresence);
            runtime.AttachDemoAi(
                1, aiConfigurations.Profile(0), 1.2f, decisionOffsetTicks: 0);
            runtime.AttachDemoAi(
                2, aiConfigurations.Profile(1), 1.2f, decisionOffsetTicks: 5);
        });
        session.At(captureTick, "Capture Simulation and both policy futures together",
            runtime =>
            {
                bothActive = runtime.ObserveMatch().Players.All(value =>
                    value.EstablishedPresence);
                pairedCapture = runtime.CaptureAiRuntimeState();
            });
        return session;
    }

    private static VisualTestSession CreateContinuousAiEncounter(
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        AiConfigurationCatalogSnapshot? aiConfigurations)
    {
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        aiConfigurations ??= DemoAiConfigurations.CreateCatalog();
        var level = AiEncounterLevelDefinition.CreateContinuousBattle(
            aiConfigurations);
        var rig = MovementTestRig.CreateReplayPackageMap(
            192, level.Navigation, gameplayProfiles, clearanceBake: null);
        var levelRuntime = new MovementTestRigAiEncounterRuntime(rig);
        var prepared = AiEncounterLevelOrchestrator.Prepare(level, levelRuntime);
        rig.StartReplayPackageRecording();
        var deployment = AiEncounterLevelOrchestrator.Begin(
            level, levelRuntime, prepared);
        var telemetry = new AiEncounterTelemetry();
        var visible = deployment.Workers
            .SelectMany(value => value)
            .Select(value => new TestUnitId(value))
            .ToArray();
        var aiAttached = false;

        var session = new VisualTestSession(
            level.Id,
            level.DisplayName,
            level.DurationTicks,
            rig,
            visible,
            runtime =>
            {
                var left = telemetry.Snapshot(1);
                var right = telemetry.Snapshot(2);
                var package = runtime.CaptureReplayPackage();
                var match = runtime.ObserveMatch();
                var infrastructure = left.InfrastructureTick >= 0 &&
                                     right.InfrastructureTick >= 0;
                var technology = left.TechnologyTick >= 0 &&
                                 right.TechnologyTick >= 0 &&
                                 left.MaximumTechnologyLevels >= 2 &&
                                 right.MaximumTechnologyLevels >= 2;
                var expansion = left.ExpansionTick >= 0 &&
                                right.ExpansionTick >= 0;
                var continuousAttack = left.AttackOrders >= 4 &&
                                       right.AttackOrders >= 4 &&
                                       left.Latest.LastAttackTick >= 3000 &&
                                       right.Latest.LastAttackTick >= 3000;
                var mutualCombat = left.Impacts >= 8 && right.Impacts >= 8 &&
                                   left.MaximumArmy >= 3 &&
                                   right.MaximumArmy >= 3;
                var attrition = left.MinimumArmyAfterFirstAttack <
                                    left.MaximumArmy &&
                                right.MinimumArmyAfterFirstAttack <
                                    right.MaximumArmy;
                var commandCoverage = package.EconomyCommandCount >= 12 &&
                                      package.ConstructionCommandCount >= 10 &&
                                      package.ProductionCommandCount >= 20 &&
                                      package.UnitCommandCount >= 20;
                var packageRoundTrip = package.TryCanonicalRoundTrip(out _);
                var gatheringLoop = left.CompletedGatherCycles >= 10 &&
                                    right.CompletedGatherCycles >= 10 &&
                                    left.SawGoingToResource && right.SawGoingToResource &&
                                    left.SawGathering && right.SawGathering &&
                                    left.SawReturningCargo && right.SawReturningCargo;
                var nearestDropOffEdges =
                    left.ReturningApproachSamples > 0 &&
                    right.ReturningApproachSamples > 0 &&
                    left.InvalidReturningApproaches == 0 &&
                    right.InvalidReturningApproaches == 0;
                var passed = aiAttached && infrastructure && technology &&
                             expansion && continuousAttack && mutualCombat &&
                             attrition && gatheringLoop && nearestDropOffEdges &&
                             commandCoverage &&
                             packageRoundTrip &&
                             match.Phase == TestMatchPhase.Running;
                return new ScenarioResult(
                    passed,
                    $"milestones=L[{left.InfrastructureTick}/" +
                    $"{left.TechnologyTick}/{left.ExpansionTick}/" +
                    $"{left.FirstAttackTick}] R[{right.InfrastructureTick}/" +
                    $"{right.TechnologyTick}/{right.ExpansionTick}/" +
                    $"{right.FirstAttackTick}], attacks={left.AttackOrders}/" +
                    $"{right.AttackOrders}, impacts={left.Impacts}/{right.Impacts}, " +
                    $"army={left.MaximumArmy}->{left.MinimumArmyAfterFirstAttack}/" +
                    $"{right.MaximumArmy}->{right.MinimumArmyAfterFirstAttack}, " +
                    $"tech={left.MaximumTechnologyLevels}/" +
                    $"{right.MaximumTechnologyLevels}, bases=" +
                    $"{left.Latest.TownHalls}/{right.Latest.TownHalls}, " +
                    $"facilities=L[{left.Latest.SupplyDepots}/" +
                    $"{left.Latest.Barracks}/{left.Latest.Refineries}/" +
                    $"{left.Latest.Academies}] R[{right.Latest.SupplyDepots}/" +
                    $"{right.Latest.Barracks}/{right.Latest.Refineries}/" +
                    $"{right.Latest.Academies}], " +
                    $"bank=L[{left.Latest.Minerals}/{left.Latest.VespeneGas}] " +
                    $"R[{right.Latest.Minerals}/{right.Latest.VespeneGas}], " +
                    $"gatherLoops={left.CompletedGatherCycles}/" +
                    $"{right.CompletedGatherCycles}, " +
                    $"dropOffEdges={left.ReturningApproachSamples}:" +
                    $"{left.InvalidReturningApproaches}/" +
                    $"{right.ReturningApproachSamples}:" +
                    $"{right.InvalidReturningApproaches}, " +
                    $"commands=e{package.EconomyCommandCount}/" +
                    $"b{package.ConstructionCommandCount}/" +
                    $"p{package.ProductionCommandCount}/u" +
                    $"{package.UnitCommandCount}, running=" +
                    $"{match.Phase == TestMatchPhase.Running}, " +
                    $"package={package.FormatVersion}/{packageRoundTrip}");
            })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(750f, 425f), 0.76f)
            .CameraKeyframe(180, new Vector2(750f, 425f), 0.76f)
            .CameraKeyframe(480, new Vector2(310f, 430f), 1.02f)
            .CameraKeyframe(900, new Vector2(1190f, 430f), 1.02f)
            .CameraKeyframe(1380, new Vector2(750f, 425f), 0.92f)
            .CameraKeyframe(1800, new Vector2(750f, 425f), 1.12f)
            .CameraKeyframe(2280, new Vector2(430f, 570f), 0.96f)
            .CameraKeyframe(2760, new Vector2(1070f, 570f), 0.96f)
            .CameraKeyframe(3300, new Vector2(750f, 425f), 0.80f)
            .Highlight(
                new SimRect(new Vector2(30f, 170f), new Vector2(640f, 720f)),
                "STANDARD AI: economy, production, academy and expansion",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(860f, 170f), new Vector2(1470f, 720f)),
                "AGGRESSIVE AI: faster scouting and attack cadence",
                TestDiagnosticKind.Rejected)
            .Highlight(
                new SimRect(new Vector2(620f, 315f), new Vector2(880f, 535f)),
                "ENCOUNTER LANE: reinforcements repeatedly contest the center",
                TestDiagnosticKind.Accepted);

        session.At(level.AiAttachTick, "OPENING: bases online, AI directors attach", _ =>
        {
            AiEncounterLevelOrchestrator.StartEconomy(
                level, levelRuntime, deployment);
            AiEncounterLevelOrchestrator.AttachAi(level, levelRuntime);
            aiAttached = true;
        });
        session.At(600, "DEVELOPMENT: supply, barracks and gas infrastructure", _ => { });
        session.At(1200, "TECHNOLOGY: academies climb combat upgrades", _ => { });
        session.At(1500, "FIRST CLASHES: both armies cross the center", _ => { });
        session.At(1900, "REINFORCEMENT: production replaces combat losses", _ => { });
        session.At(2200, "LATE SKIRMISH: expansions fund continuing attacks", _ => { });
        session.At(3000, "TECHED WAR: upgraded reinforcements sustain the front", _ => { });
        for (var tick = level.AiAttachTick;
             tick < level.DurationTicks;
             tick++)
        {
            session.At(tick, "Observe complete worker gathering cycles", _ =>
                telemetry.ObserveWorkerCycles(level, levelRuntime));
        }
        for (var tick = level.AiAttachTick;
             tick < level.DurationTicks;
             tick += 30)
        {
            session.At(tick, "Observe decoupled encounter telemetry", runtime =>
                telemetry.Observe(level, levelRuntime, runtime.Tick));
        }
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
        var resumeDiagnostic = string.Empty;
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
                    $"refinery={gasState.Operational}, hp={supplyState.Health:0}, " +
                    $"resume-detail={resumeDiagnostic}");
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
        {
            var before = runtime.ObserveGameplayBuilding(barracks.BuildingId);
            var worker = runtime.Observe(workers[1]);
            var movement = runtime.ObserveMovement(workers[1]);
            resumed = runtime.ResumeConstruction(
                1, barracks.BuildingId, workers[1]);
            resumeDiagnostic =
                $"{before.State}@{worker.Position.X:0},{worker.Position.Y:0}/" +
                $"{movement.Result}:{movement.GoalKind}";
        });
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

    private static VisualTestSession CreateConstructionQueuedBuilds(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            32, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 5000, 0, 40, 3);
        rig.AddResourceDropOff(1, new Vector2(80f, 110f));
        var minerals = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            new Vector2(500f, 110f),
            amount: 10_000,
            harvestBatch: 5,
            harvestSeconds: 0.6f,
            harvesterCapacity: 2);
        var mainBuilder = rig.SpawnWorker(
            new Vector2(70f, 180f), 1, maximumSpeed: 260f);
        var cancelBuilder = rig.SpawnWorker(
            new Vector2(70f, 350f), 1, maximumSpeed: 260f);
        var deadBuilder = rig.SpawnWorker(
            new Vector2(70f, 550f), 1, maximumSpeed: 260f,
            combatProfile: TestCombatProfile.Standard with
            {
                MaximumHealth = 10f
            });
        var dynamicBlocker = rig.SpawnCombat(
            new Vector2(410f, 290f), 1,
            radius: 14f, maximumSpeed: 240f, acceleration: 900f);
        var builderKiller = rig.SpawnCombat(
            new Vector2(70f, 580f), 2,
            TestCombatProfile.Standard with
            {
                AttackDamage = 1000f,
                AttackRange = 100f,
                AcquisitionRange = 100f,
                AttackWindupSeconds = 0.05f,
                Positioning = TestCombatPositioning.Ranged
            });

        var supply = DemoBuildingTypes.SupplyDepot with
        {
            BuildSeconds = 0.55f
        };
        var barracks = DemoBuildingTypes.Barracks with
        {
            BuildSeconds = 0.7f
        };
        var commandCenter = DemoBuildingTypes.CommandCenter with
        {
            BuildSeconds = 0.85f
        };
        rig.StartReplayPackageRecording();
        rig.Move([builderKiller], new Vector2(70f, 650f));
        var mainIds = new TestGameplayBuildingId[3];
        var cancelIds = new TestGameplayBuildingId[3];
        var deathIds = new TestGameplayBuildingId[2];
        var deathCleanup = false;
        var issued = false;
        var reserveCharge = false;
        var gatherQueued = false;
        var canceledMiddle = false;
        var cancelRefund = false;
        var noPrematureHardFootprints = false;
        var staticRejected = false;
        var staticRefund = false;
        var dynamicEntered = false;
        var dynamicEvacuated = false;
        var returnedToMining = false;
        var balanceAfterIssue = 0;
        var issueCodes = string.Empty;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 90;
        var mainCenters = new[]
        {
            new Vector2(120f, 180f),
            new Vector2(220f, 180f),
            new Vector2(410f, 180f)
        };
        var cancelCenters = new[]
        {
            new Vector2(120f, 350f),
            new Vector2(220f, 350f),
            new Vector2(400f, 350f)
        };

        var session = new VisualTestSession(
                "construction-queued-builds",
                "Shift Build queues soft reservations, revalidates each item and returns to mining",
                600,
                rig,
                [mainBuilder, cancelBuilder, deadBuilder, dynamicBlocker,
                    builderKiller],
                runtime =>
                {
                    var main = mainIds.Select(
                        runtime.ObserveGameplayBuilding).ToArray();
                    var canceled = cancelIds.Select(
                        runtime.ObserveGameplayBuilding).ToArray();
                    var mainComplete =
                        main[0].State == TestBuildingLifecycleState.Completed &&
                        main[1].State == TestBuildingLifecycleState.Canceled &&
                        main[2].State == TestBuildingLifecycleState.Completed;
                    var cancelComplete =
                        canceled[0].State == TestBuildingLifecycleState.Completed &&
                        canceled[1].State == TestBuildingLifecycleState.Canceled &&
                        canceled[2].State == TestBuildingLifecycleState.Completed;
                    var mainOrders = runtime.ObserveOrders(mainBuilder);
                    var cancelOrders = runtime.ObserveOrders(cancelBuilder);
                    var death = deathIds.Select(
                        runtime.ObserveGameplayBuilding).ToArray();
                    var worker = runtime.ObserveWorkerEconomy(mainBuilder);
                    returnedToMining |= worker.TargetNode == minerals &&
                        worker.State is not TestWorkerEconomyState.None and
                            not TestWorkerEconomyState.Idle;
                    var blocker = runtime.Observe(dynamicBlocker).Position;
                    dynamicEvacuated |=
                        MathF.Abs(blocker.X - mainCenters[2].X) >
                            commandCenter.Size.X * 0.5f + 14f ||
                        MathF.Abs(blocker.Y - mainCenters[2].Y) >
                            commandCenter.Size.Y * 0.5f + 14f;

                    var package = runtime.CaptureReplayPackage();
                    var packageRoundTrip = package.TryCanonicalRoundTrip(
                        out var decoded);
                    var replay = packageRoundTrip
                        ? runtime.ReplayPackage(decoded!, runtime.Tick)
                        : null;
                    var replayExact = replay is not null &&
                                      replay.FinalHash == runtime.StateHash;
                    var hotExact = false;
                    var hotVersion = 0;
                    if (hotCapture is not null)
                    {
                        var hot = runtime.BindHotSnapshot(package, hotCapture);
                        hotVersion = hot.FormatVersion;
                        if (hot.TryCanonicalRoundTrip(out var decodedHot))
                        {
                            var resumed = runtime.ResumeHotSnapshot(
                                package, decodedHot!, runtime.Tick);
                            hotExact = resumed.FinalHash == runtime.StateHash &&
                                       replay is not null &&
                                       replay.MatchesFrom(resumed, hotTick);
                        }
                    }
                    var versions = package.FormatVersion ==
                                       SimulationReplayPackageSnapshot
                                           .CurrentFormatVersion &&
                                   hotVersion ==
                                       SimulationHotSnapshot.CurrentFormatVersion;
                    var passed = deathCleanup && issued && reserveCharge &&
                                 gatherQueued && canceledMiddle && cancelRefund &&
                                 noPrematureHardFootprints && staticRejected &&
                                 staticRefund && dynamicEntered &&
                                 dynamicEvacuated &&
                                 returnedToMining && mainComplete &&
                                 cancelComplete && mainOrders.PendingOrders == 0 &&
                                 cancelOrders.PendingOrders == 0 &&
                                 mainOrders.CompletedQueuedOrders >= 3 &&
                                 cancelOrders.CompletedQueuedOrders >= 3 &&
                                 packageRoundTrip && replayExact && hotExact &&
                                 versions;
                    return new ScenarioResult(
                        passed,
                        $"issued/charge={issued}/{reserveCharge}" +
                        $"[{issueCodes}]@{balanceAfterIssue}, " +
                        $"hard-late={noPrematureHardFootprints}, " +
                        $"static-skip/refund={staticRejected}/{staticRefund}, " +
                        $"dynamic-enter/evict={dynamicEntered}/" +
                        $"{dynamicEvacuated}, cancel=" +
                        $"{canceledMiddle}/{cancelRefund}, death={deathCleanup}, " +
                        $"mine={returnedToMining}, complete=" +
                        $"{mainComplete}/{cancelComplete}, queue=" +
                        $"{mainOrders.CompletedQueuedOrders}/" +
                        $"{cancelOrders.CompletedQueuedOrders}, states=" +
                        $"{string.Join('/', main.Select(value => $"{value.State}:{value.Progress:0.00}"))}|" +
                        $"{string.Join('/', canceled.Select(value => $"{value.State}:{value.Progress:0.00}"))}|" +
                        $"deadAlive={runtime.IsUnitAlive(deadBuilder)}/" +
                        $"{string.Join('/', death.Select(value => value.State))}, " +
                        $"alive={runtime.IsUnitAlive(mainBuilder)}/" +
                        $"{runtime.IsUnitAlive(cancelBuilder)}, active=" +
                        $"{mainOrders.ActiveOrder}:{mainOrders.ActiveTargetBuilding}:" +
                        $"{mainOrders.PendingOrders}/" +
                        $"{cancelOrders.ActiveOrder}:{cancelOrders.ActiveTargetBuilding}:" +
                        $"{cancelOrders.PendingOrders}, replay=" +
                        $"{replayExact}, hot={hotExact}/v{hotVersion}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(280f, 340f), 0.92f)
            .CameraKeyframe(120, new Vector2(260f, 270f), 1.02f)
            .CameraKeyframe(230, new Vector2(400f, 260f), 1.1f)
            .CameraKeyframe(330, new Vector2(280f, 280f), 0.98f)
            .CameraKeyframe(410, new Vector2(210f, 180f), 1.05f)
            .Highlight(
                new SimRect(new Vector2(90f, 110f), new Vector2(505f, 250f)),
                "MAIN: BUILD -> STATIC SKIP -> DYNAMIC EVICT -> MINE",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(90f, 290f), new Vector2(455f, 410f)),
                "CANCEL: REMOVE ONE QUEUED ITEM, CONTINUE",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(90f, 490f), new Vector2(285f, 610f)),
                "DEATH: KEEP STARTED / REFUND PENDING",
                TestDiagnosticKind.Rejected);

        session.At(1, "Queue three sizes and a final mining order", runtime =>
        {
            var main = new[]
            {
                runtime.Build(1, mainBuilder, supply, mainCenters[0], queued: true),
                runtime.Build(1, mainBuilder, barracks, mainCenters[1], queued: true),
                runtime.Build(1, mainBuilder, commandCenter, mainCenters[2], queued: true)
            };
            var canceled = new[]
            {
                runtime.Build(1, cancelBuilder, supply, cancelCenters[0], queued: true),
                runtime.Build(1, cancelBuilder, barracks, cancelCenters[1], queued: true),
                runtime.Build(1, cancelBuilder, supply, cancelCenters[2], queued: true)
            };
            issued = main.Concat(canceled).All(value => value.Succeeded);
            for (var index = 0; index < 3; index++)
            {
                mainIds[index] = main[index].BuildingId;
                cancelIds[index] = canceled[index].BuildingId;
            }
            balanceAfterIssue = runtime.ObservePlayerEconomy(1).Minerals;
            reserveCharge = balanceAfterIssue == 3750;
            var death = new[]
            {
                runtime.Build(
                    1, deadBuilder, supply,
                    new Vector2(120f, 550f), queued: true),
                runtime.Build(
                    1, deadBuilder, barracks,
                    new Vector2(220f, 550f), queued: true)
            };
            issued &= death.All(value => value.Succeeded);
            issueCodes = string.Join('/', main.Concat(canceled).Concat(death)
                .Select(value => value.Code));
            deathIds[0] = death[0].BuildingId;
            deathIds[1] = death[1].BuildingId;
            balanceAfterIssue = runtime.ObservePlayerEconomy(1).Minerals;
            reserveCharge = balanceAfterIssue == 3750;
        });
        session.At(2, "Queue the final return-to-mining command", runtime =>
            gatherQueued = runtime.PlayerSmartResource(
                1, [mainBuilder], minerals, queued: true) ==
                TestPlayerOrderCommandCode.Success);
        session.At(8, "Move a friendly unit into the future large footprint", runtime =>
        {
            runtime.Move([dynamicBlocker], mainCenters[2]);
            runtime.AttackTarget([builderKiller], deadBuilder);
        });
        session.At(12, "Invalidate only the middle main reservation", runtime =>
            runtime.PlaceBuilding(mainCenters[1], new Vector2(44f, 44f)));
        session.At(14, "Cancel only the middle item in a second queue", runtime =>
        {
            var beforeCancel = runtime.ObservePlayerEconomy(1).Minerals;
            canceledMiddle = runtime.CancelConstruction(1, cancelIds[1]);
            cancelRefund = runtime.ObservePlayerEconomy(1).Minerals == beforeCancel + 112;
        });
        session.At(40, "Move the deterministic builder killer away", runtime =>
            runtime.Move([builderKiller], new Vector2(80f, 670f)));
        session.At(25, "Verify future reservations still have no hard footprint", runtime =>
        {
            var middle = runtime.ObserveGameplayBuilding(mainIds[1]);
            var last = runtime.ObserveGameplayBuilding(mainIds[2]);
            noPrematureHardFootprints = middle.ReservationId > 0 &&
                                          middle.FootprintId.Value == 0 &&
                                          last.ReservationId > 0 &&
                                          last.FootprintId.Value == 0;
        });
        session.At(65, "Settle the movable occupant inside the future footprint", runtime =>
        {
            runtime.Stop([dynamicBlocker]);
            var blocker = runtime.Observe(dynamicBlocker).Position;
            dynamicEntered = MathF.Abs(blocker.X - mainCenters[2].X) <=
                                 commandCenter.Size.X * 0.5f &&
                             MathF.Abs(blocker.Y - mainCenters[2].Y) <=
                                 commandCenter.Size.Y * 0.5f;
        });
        session.At(hotTick, "Capture queued reservations and active construction", runtime =>
            hotCapture = runtime.CaptureRuntimeState());
        for (var tick = 30; tick < session.DurationTicks; tick += 4)
        {
            session.At(tick, "Observe per-item queue transitions", runtime =>
            {
                if (deathIds[1].Value >= 0)
                {
                    var first = runtime.ObserveGameplayBuilding(deathIds[0]);
                    var pending = runtime.ObserveGameplayBuilding(deathIds[1]);
                    deathCleanup |= !runtime.IsUnitAlive(deadBuilder) &&
                        first.State ==
                            TestBuildingLifecycleState.WaitingForBuilder &&
                        pending.State == TestBuildingLifecycleState.Canceled;
                }
                if (mainIds[1].Value >= 0)
                {
                    var middle = runtime.ObserveGameplayBuilding(mainIds[1]);
                    if (middle.State == TestBuildingLifecycleState.Canceled)
                    {
                        staticRejected = true;
                        staticRefund |= runtime.ObservePlayerEconomy(1).Minerals ==
                                        4162;
                    }
                }
            });
        }
        return session;
    }

    private static VisualTestSession CreateConstructionMultiUnitEviction(
        GameplayProfileCatalogSnapshot? gameplayProfiles)
    {
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var created = NavigationMapSnapshot.TryCreate(
            NavigationMapSnapshot.CurrentFormatVersion,
            new SimRect(Vector2.Zero, new Vector2(1500f, 900f)),
            [], [], [], [],
            out var navigation,
            out var navigationValidation);
        if (!created || navigation is null)
        {
            throw new InvalidOperationException(
                $"Construction eviction map is invalid: " +
                $"{navigationValidation.FirstError}.");
        }

        var rig = MovementTestRig.CreateReplayPackageMap(
            128, navigation, gameplayProfiles, clearanceBake: null);
        rig.RegisterPlayer(1, 20000, 5000, 120, 0);
        var profiles = new[]
        {
            DemoBuildingTypes.SupplyDepot with { BuildSeconds = 0.5f },
            DemoBuildingTypes.SupplyDepot with
            {
                Id = 100,
                Name = "Medium Eviction Test",
                Size = new Vector2(72f, 64f),
                BuildSeconds = 0.5f
            },
            DemoBuildingTypes.Barracks with { BuildSeconds = 0.5f },
            DemoBuildingTypes.CommandCenter with { BuildSeconds = 0.5f }
        };
        Vector2[] centers =
        [
            new(150f, 220f),
            new(450f, 220f),
            new(800f, 220f),
            new(1240f, 240f)
        ];
        int[] blockerCounts = [1, 8, 8, 32];
        var builders = centers.Select(center => rig.SpawnWorker(
            center + new Vector2(0f, 390f),
            1,
            maximumSpeed: 105f)).ToArray();
        var blockerProfile = TestCombatProfile.Standard with
        {
            MaximumHealth = 10000f,
            AttackDamage = 0f,
            AttackRange = 0f,
            AcquisitionRange = 0f
        };
        var blockersByBuilding = new TestUnitId[centers.Length][];
        var targetsByBuilding = new Vector2[centers.Length][];
        for (var group = 0; group < centers.Length; group++)
        {
            var targets = CreateConstructionOccupantGrid(
                centers[group], profiles[group].Size, blockerCounts[group]);
            targetsByBuilding[group] = targets;
            blockersByBuilding[group] = targets.Select((target, index) =>
                rig.SpawnCombat(
                    target + new Vector2(0f, 180f),
                    1,
                    blockerProfile,
                    radius: 4f,
                    maximumSpeed: 260f,
                    acceleration: 1200f)).ToArray();
        }

        var holdCenter = new Vector2(500f, 700f);
        var holdBuilder = rig.SpawnWorker(
            holdCenter + new Vector2(230f, 0f),
            1,
            maximumSpeed: 100f);
        var holdBlocker = rig.SpawnCombat(
            holdCenter + new Vector2(0f, 120f),
            1,
            blockerProfile,
            radius: 5f,
            maximumSpeed: 220f,
            acceleration: 1000f);
        var holdProfile = profiles[1] with
        {
            Id = 101,
            Name = "Conservative Hold Test"
        };
        rig.StartReplayPackageRecording();

        var buildings = new TestConstructionResult[centers.Length];
        var holdBuilding = default(TestConstructionResult);
        var issued = false;
        var maximumEvacuations = new int[centers.Length];
        var insideAtResume = new int[centers.Length];
        var holdWaited = false;
        var moveOrderInjected = false;
        var moveOrderPreserved = false;
        var moveOrderResumed = false;
        var mover = blockersByBuilding[2][0];
        var moverFinal = new Vector2(1040f, 520f);
        TestRuntimeStateCapture? hotCapture = null;
        long hotTick = -1;

        var session = new VisualTestSession(
                "construction-multi-unit-eviction",
                "Stable 1/8/32 occupant evacuation with conservative Hold policy",
                900,
                rig,
                [.. builders,
                    .. blockersByBuilding.SelectMany(value => value),
                    holdBuilder, holdBlocker],
                runtime =>
                {
                    var package = runtime.CaptureReplayPackage();
                    var packageRoundTrip = package.TryCanonicalRoundTrip(
                        out var decoded);
                    var replay = packageRoundTrip
                        ? runtime.ReplayPackage(decoded!, runtime.Tick)
                        : null;
                    var replayExact = replay is not null &&
                                      replay.FinalHash == runtime.StateHash;
                    var hotExact = false;
                    var hotFinal = false;
                    var hotTrace = false;
                    var hotVersion = 0;
                    if (hotCapture is not null)
                    {
                        var hot = runtime.BindHotSnapshot(package, hotCapture);
                        hotVersion = hot.FormatVersion;
                        if (hot.TryCanonicalRoundTrip(out var decodedHot))
                        {
                            var resumed = runtime.ResumeHotSnapshot(
                                package, decodedHot!, runtime.Tick);
                            hotFinal = resumed.FinalHash == runtime.StateHash;
                            hotTrace = replay is not null &&
                                       replay.MatchesFrom(resumed, hotTick + 1);
                            hotExact = hotFinal && hotTrace;
                        }
                    }

                    var completed = buildings.All(value =>
                        value.Succeeded &&
                        runtime.ObserveGameplayBuilding(value.BuildingId).State ==
                            TestBuildingLifecycleState.Completed) &&
                        holdBuilding.Succeeded &&
                        runtime.ObserveGameplayBuilding(
                            holdBuilding.BuildingId).State ==
                            TestBuildingLifecycleState.Completed;
                    var exactCounts =
                        maximumEvacuations[0] == 1 &&
                        maximumEvacuations[1] == 8 &&
                        maximumEvacuations[2] == 8 &&
                        maximumEvacuations[3] == 32;
                    var moverOrder = runtime.ObserveOrders(mover);
                    moveOrderResumed |=
                        !moverOrder.ConstructionEvacuationActive &&
                        moverOrder.CompletedQueuedOrders >= 1 &&
                        Vector2.Distance(
                            runtime.Observe(mover).Position, moverFinal) < 28f;
                    var passed = issued && completed && exactCounts &&
                                 holdWaited && moveOrderInjected &&
                                 moveOrderPreserved && moveOrderResumed &&
                                 packageRoundTrip && replayExact && hotExact &&
                                 package.FormatVersion ==
                                     SimulationReplayPackageSnapshot
                                         .CurrentFormatVersion &&
                                 hotVersion ==
                                     SimulationHotSnapshot.CurrentFormatVersion;
                    return new ScenarioResult(
                        passed,
                        $"build={issued}/{completed}, " +
                        $"states={string.Join('/', buildings.Select(value => runtime.ObserveGameplayBuilding(value.BuildingId).State))}/" +
                        $"{runtime.ObserveGameplayBuilding(holdBuilding.BuildingId).State}, " +
                        $"inside={string.Join('/', insideAtResume)}, " +
                        $"evacuations={string.Join('/', maximumEvacuations)}, " +
                        $"hold={holdWaited}, " +
                        $"orders={moveOrderInjected}/{moveOrderPreserved}/" +
                        $"{moveOrderResumed}, replay={replayExact}, " +
                        $"hot={hotExact}/{hotFinal}/{hotTrace}/v{hotVersion}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(740f, 300f), 0.68f)
            .CameraKeyframe(250, new Vector2(730f, 240f), 0.78f)
            .CameraKeyframe(450, new Vector2(500f, 650f), 1.02f)
            .CameraKeyframe(600, new Vector2(1030f, 350f), 0.76f)
            .CameraKeyframe(755, new Vector2(740f, 420f), 0.7f)
            .Highlight(
                new SimRect(new Vector2(80f, 120f), new Vector2(1400f, 350f)),
                "1 / 8 / 8 / 32 FRIENDLY OCCUPANTS -> UNIQUE EXIT SLOTS",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(380f, 600f), new Vector2(620f, 800f)),
                "HOLD WAITS UNTIL PLAYER RELEASES IT",
                TestDiagnosticKind.Info);

        session.At(1, "Accept four footprint sizes before occupants enter", runtime =>
        {
            issued = true;
            for (var index = 0; index < buildings.Length; index++)
            {
                buildings[index] = runtime.Build(
                    1, builders[index], profiles[index], centers[index]);
                issued &= buildings[index].Succeeded;
            }
            holdBuilding = runtime.Build(
                1, holdBuilder, holdProfile, holdCenter);
            issued &= holdBuilding.Succeeded;
            runtime.Hold(builders);
            runtime.Hold([holdBuilder]);
        });
        session.At(20, "Move late friendly occupants into reserved footprints", runtime =>
        {
            for (var group = 0; group < blockersByBuilding.Length; group++)
            {
                for (var index = 0;
                     index < blockersByBuilding[group].Length;
                     index++)
                {
                    runtime.Move(
                        [blockersByBuilding[group][index]],
                        targetsByBuilding[group][index]);
                }
            }
            runtime.Move([holdBlocker], holdCenter);
        });
        session.At(150, "Settle ordinary occupants and preserve one Hold blocker", runtime =>
        {
            foreach (var group in blockersByBuilding)
                runtime.Stop(group);
            runtime.Hold([holdBlocker]);
        });
        session.At(180, "Resume builders only after late occupants have settled", runtime =>
        {
            for (var index = 0; index < buildings.Length; index++)
            {
                var bounds = new SimRect(
                    centers[index] - profiles[index].Size * 0.5f,
                    centers[index] + profiles[index].Size * 0.5f);
                insideAtResume[index] = blockersByBuilding[index].Count(unit =>
                    bounds.Contains(runtime.Observe(unit).Position));
                issued &= runtime.ResumeConstruction(
                    1, buildings[index].BuildingId, builders[index]);
            }
            issued &= runtime.ResumeConstruction(
                1, holdBuilding.BuildingId, holdBuilder);
        });
        for (var tick = 180; tick <= 560; tick++)
        {
            session.At(tick, "Observe policy-driven evacuation without internals", runtime =>
            {
                for (var group = 0; group < buildings.Length; group++)
                {
                    if (!buildings[group].Succeeded)
                        continue;
                    var active = blockersByBuilding[group].Count(unit =>
                    {
                        var order = runtime.ObserveOrders(unit);
                        return order.ConstructionEvacuationActive &&
                               order.ConstructionEvacuationBuilding ==
                                   buildings[group].BuildingId.Value;
                    });
                    maximumEvacuations[group] = Math.Max(
                        maximumEvacuations[group], active);
                }

                if (holdBuilding.Succeeded &&
                    runtime.ObserveGameplayBuilding(holdBuilding.BuildingId).State ==
                        TestBuildingLifecycleState.BlockedAtStart)
                {
                    holdWaited |=
                        !runtime.ObserveOrders(holdBlocker)
                            .ConstructionEvacuationActive;
                }

                if (!moveOrderInjected && buildings[2].Succeeded &&
                    runtime.ObserveGameplayBuilding(buildings[2].BuildingId).State ==
                        TestBuildingLifecycleState.BlockedAtStart)
                {
                    runtime.Move([mover], new Vector2(980f, 220f));
                    runtime.Move([mover], moverFinal, queued: true);
                    moveOrderInjected = true;
                }

                if (moveOrderInjected)
                {
                    var order = runtime.ObserveOrders(mover);
                    moveOrderPreserved |=
                        order.ConstructionEvacuationActive &&
                        order.ActiveOrder == TestOrderKind.Move &&
                        order.PendingOrders == 1;
                }

                var totalActive = blockersByBuilding
                    .SelectMany(value => value)
                    .Count(unit => runtime.ObserveOrders(unit)
                        .ConstructionEvacuationActive);
                if (hotCapture is null && totalActive >= 32)
                {
                    hotCapture = runtime.CaptureRuntimeState();
                    hotTick = runtime.Tick;
                }
            });
        }
        session.At(480, "Release the conservative Hold blocker explicitly", runtime =>
            runtime.Move([holdBlocker], holdCenter + new Vector2(0f, 150f)));
        return session;
    }

    private static Vector2[] CreateConstructionOccupantGrid(
        Vector2 center,
        Vector2 size,
        int count)
    {
        if (count <= 0)
            return [];
        var columns = count switch
        {
            1 => 1,
            <= 8 => 4,
            _ => 8
        };
        var rows = (count + columns - 1) / columns;
        var usable = size - new Vector2(36f, 36f);
        var stepX = columns == 1 ? 0f : usable.X / (columns - 1);
        var stepY = rows == 1 ? 0f : usable.Y / (rows - 1);
        var result = new Vector2[count];
        for (var index = 0; index < count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            result[index] = center + new Vector2(
                columns == 1 ? 0f : -usable.X * 0.5f + column * stepX,
                rows == 1 ? 0f : -usable.Y * 0.5f + row * stepY);
        }
        return result;
    }

    private static VisualTestSession CreateConstructionBlockerPolicyMatrix(
        GameplayProfileCatalogSnapshot? gameplayProfiles)
    {
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var created = NavigationMapSnapshot.TryCreate(
            NavigationMapSnapshot.CurrentFormatVersion,
            new SimRect(Vector2.Zero, new Vector2(1500f, 900f)),
            [], [], [], [],
            out var navigation,
            out var navigationValidation);
        if (!created || navigation is null)
        {
            throw new InvalidOperationException(
                $"Construction blocker policy map is invalid: " +
                $"{navigationValidation.FirstError}.");
        }

        var rig = MovementTestRig.CreateReplayPackageMap(
            64, navigation, gameplayProfiles, clearanceBake: null);
        rig.RegisterPlayer(1, 20000, 5000, 120, 0);
        rig.RegisterPlayer(2, 20000, 5000, 120, 0);
        rig.RegisterPlayer(3, 20000, 5000, 120, 0);
        rig.ConfigureAlliance(10, sharedVision: true, 1, 2);

        var laneCenters = new[]
        {
            new Vector2(160f, 210f),
            new Vector2(455f, 210f),
            new Vector2(750f, 210f),
            new Vector2(1045f, 210f),
            new Vector2(1340f, 210f)
        };
        var profiles = laneCenters.Select((_, index) =>
            DemoBuildingTypes.Barracks with
            {
                Id = 120 + index,
                Name = $"Blocker Policy Lane {index + 1}",
                BuildSeconds = 0.5f
            }).ToArray();
        var builders = laneCenters.Select(center => rig.SpawnWorker(
            center + new Vector2(0f, 300f),
            1,
            maximumSpeed: 82f)).ToArray();
        var blockerProfile = TestCombatProfile.Standard with
        {
            MaximumHealth = 10000f,
            AttackDamage = 0f,
            AttackRange = 0f,
            AcquisitionRange = 0f
        };
        var movable = rig.SpawnCombat(
            laneCenters[0], 1, blockerProfile,
            radius: 7f, maximumSpeed: 230f, acceleration: 1000f);
        var hold = rig.SpawnCombat(
            laneCenters[1], 1, blockerProfile,
            radius: 7f, maximumSpeed: 230f, acceleration: 1000f);
        var economy = rig.SpawnWorker(
            laneCenters[2], 1,
            radius: 7f, maximumSpeed: 150f, acceleration: 720f);
        var mineral = rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            laneCenters[2],
            amount: 5000,
            harvestBatch: 5,
            harvestSeconds: 60f,
            harvesterCapacity: 2);
        rig.AddResourceDropOff(1, laneCenters[2] + new Vector2(0f, 150f));
        var ally = rig.SpawnCombat(
            laneCenters[3] + new Vector2(-180f, 0f),
            2, blockerProfile,
            radius: 7f, maximumSpeed: 260f, acceleration: 1100f);
        var enemy = rig.SpawnCombat(
            laneCenters[4] + new Vector2(-180f, 0f),
            3, blockerProfile,
            radius: 7f, maximumSpeed: 260f, acceleration: 1100f);

        var allyPreviewCenter = new Vector2(520f, 700f);
        var enemyPreviewCenter = new Vector2(980f, 700f);
        var allyPreviewBlocker = rig.SpawnCombat(
            allyPreviewCenter, 2, blockerProfile,
            radius: 8f, maximumSpeed: 150f, acceleration: 720f);
        var enemyPreviewBlocker = rig.SpawnCombat(
            enemyPreviewCenter, 3, blockerProfile,
            radius: 8f, maximumSpeed: 150f, acceleration: 720f);
        var allyPreviewWorker = rig.SpawnWorker(
            allyPreviewCenter + new Vector2(0f, 105f), 1);
        var enemyPreviewWorker = rig.SpawnWorker(
            enemyPreviewCenter + new Vector2(0f, 105f), 1);
        rig.StartMatch(1, 2, 3);
        rig.StartReplayPackageRecording();
        var initialReplayPackage = rig.CaptureReplayPackage();
        var initialStateHash = rig.StateHash;

        var buildings = new TestConstructionResult[laneCenters.Length];
        var buildIssued = false;
        var gatherIssued = false;
        var lateMovesIssued = false;
        var previewRejected = false;
        var movableEvacuated = false;
        var conservativeWaitObserved = false;
        var economyOrderPreserved = false;
        var authorityOrdersPreserved = false;
        var released = false;
        var harvestCanceled = false;
        TestRuntimeStateCapture? hotCapture = null;
        long hotTick = -1;
        var replayCheckpoints = new Dictionary<
            long, (TestReplayPackage Package, ulong Hash)>();

        var session = new VisualTestSession(
                "construction-blocker-policy-matrix",
                "Construction blocker policy: evict owned idle, wait on protected orders and foreign authority",
                720,
                rig,
                [.. builders, movable, hold, economy, ally, enemy,
                    allyPreviewBlocker, enemyPreviewBlocker,
                    allyPreviewWorker, enemyPreviewWorker],
                runtime =>
                {
                    var package = runtime.CaptureReplayPackage();
                    var packageRoundTrip = package.TryCanonicalRoundTrip(
                        out var decoded);
                    var replay = packageRoundTrip
                        ? runtime.ReplayPackage(decoded!, runtime.Tick)
                        : null;
                    var replayExact = replay is not null &&
                                      replay.FinalHash == runtime.StateHash;
                    var checkpointChecks = replayCheckpoints
                        .OrderBy(value => value.Key).Select(value =>
                        {
                            var checkpointReplay = runtime.ReplayPackage(
                                value.Value.Package, value.Key);
                            return (
                                Tick: value.Key,
                                Exact: checkpointReplay.FinalHash ==
                                       value.Value.Hash);
                        }).ToArray();
                    var checkpointExact = checkpointChecks.All(value =>
                        value.Exact);
                    var checkpointResults = checkpointChecks.Select(value =>
                        $"{value.Tick}:{value.Exact}").ToArray();
                    var initialReplay = runtime.ReplayPackage(
                        initialReplayPackage, 0);
                    var initialExact = initialReplay.FinalHash ==
                                       initialStateHash;
                    var hotExact = false;
                    var hotVersion = 0;
                    if (hotCapture is not null)
                    {
                        var hot = runtime.BindHotSnapshot(package, hotCapture);
                        hotVersion = hot.FormatVersion;
                        if (hot.TryCanonicalRoundTrip(out var decodedHot))
                        {
                            var resumed = runtime.ResumeHotSnapshot(
                                package, decodedHot!, runtime.Tick);
                            hotExact = resumed.FinalHash == runtime.StateHash &&
                                       replay is not null &&
                                       replay.MatchesFrom(resumed, hotTick + 1);
                        }
                    }

                    var expectedFinal =
                        runtime.ObserveGameplayBuilding(buildings[0].BuildingId)
                            .State == TestBuildingLifecycleState.Completed &&
                        runtime.ObserveGameplayBuilding(buildings[1].BuildingId)
                            .State == TestBuildingLifecycleState.Completed &&
                        runtime.ObserveGameplayBuilding(buildings[2].BuildingId)
                            .State == TestBuildingLifecycleState.Canceled &&
                        runtime.ObserveGameplayBuilding(buildings[3].BuildingId)
                            .State == TestBuildingLifecycleState.Completed &&
                        runtime.ObserveGameplayBuilding(buildings[4].BuildingId)
                            .State == TestBuildingLifecycleState.Completed;
                    var economyFinal = runtime.ObserveWorkerEconomy(economy);
                    var economyStillGathering =
                        economyFinal.State == TestWorkerEconomyState.Gathering &&
                        economyFinal.TargetNode == mineral;
                    var foreignReleased =
                        Vector2.Distance(runtime.Observe(ally).Position,
                            laneCenters[3] + new Vector2(0f, 150f)) < 28f &&
                        Vector2.Distance(runtime.Observe(enemy).Position,
                            laneCenters[4] + new Vector2(0f, 150f)) < 28f;
                    var passed = buildIssued && gatherIssued && lateMovesIssued &&
                                 previewRejected && movableEvacuated &&
                                 conservativeWaitObserved &&
                                 economyOrderPreserved &&
                                 authorityOrdersPreserved && released &&
                                 harvestCanceled && expectedFinal &&
                                 economyStillGathering && foreignReleased &&
                                 packageRoundTrip && replayExact && hotExact &&
                                 initialExact && checkpointExact &&
                                 package.FormatVersion ==
                                     SimulationReplayPackageSnapshot
                                         .CurrentFormatVersion &&
                                 hotVersion ==
                                     SimulationHotSnapshot.CurrentFormatVersion;
                    return new ScenarioResult(
                        passed,
                        $"build={buildIssued}, preview={previewRejected}, " +
                        $"evict={movableEvacuated}, waits={conservativeWaitObserved}, " +
                        $"orders={economyOrderPreserved}/{authorityOrdersPreserved}, " +
                        $"release={released}/{foreignReleased}, " +
                        $"states={string.Join('/', buildings.Select(value => runtime.ObserveGameplayBuilding(value.BuildingId).State))}, " +
                        $"economy={economyFinal.State}/{economyStillGathering}, " +
                        $"replay={replayExact}/initial{initialExact}" +
                        $"[{string.Join(',', checkpointResults)}], " +
                        $"hot={hotExact}/v{hotVersion}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(750f, 250f), 0.7f)
            .CameraKeyframe(260, new Vector2(750f, 230f), 0.82f)
            .CameraKeyframe(380, new Vector2(750f, 230f), 0.82f)
            .CameraKeyframe(560, new Vector2(750f, 430f), 0.7f)
            .CameraKeyframe(715, new Vector2(750f, 420f), 0.7f)
            .Highlight(
                new SimRect(new Vector2(70f, 120f), new Vector2(280f, 300f)),
                "OWN IDLE -> SYSTEM EVACUATION",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(340f, 120f), new Vector2(860f, 300f)),
                "HOLD / HARVEST -> WAIT; PLAYER ORDER IS UNTOUCHED",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(930f, 120f), new Vector2(1440f, 300f)),
                "ALLY / ENEMY -> OWNER AUTHORITY RELEASE",
                TestDiagnosticKind.Rejected)
            .Highlight(
                new SimRect(new Vector2(400f, 610f), new Vector2(1100f, 790f)),
                "KNOWN ALLY + VISIBLE ENEMY -> PREVIEW REJECTED",
                TestDiagnosticKind.Info);

        session.At(1, "Protect the explicit Hold order", runtime =>
        {
            runtime.Hold([hold]);
        });
        session.At(3, "Start the long-running harvesting task", runtime =>
        {
            gatherIssued = runtime.Gather(1, economy, mineral) ==
                           TestGatherCommandCode.Success;
        });
        session.At(5, "Issue the five construction lanes", runtime =>
        {
            buildIssued = true;
            for (var index = 0; index < buildings.Length; index++)
            {
                buildings[index] = runtime.Build(
                    1, builders[index], profiles[index], laneCenters[index]);
                buildIssued &= buildings[index].Succeeded;
            }
        });
        session.At(8, "Move ally and enemy into already accepted reservations", runtime =>
        {
            lateMovesIssued =
                runtime.PlayerMove(2, [ally], laneCenters[3]) ==
                    TestPlayerOrderCommandCode.Success &&
                runtime.PlayerMove(3, [enemy], laneCenters[4]) ==
                    TestPlayerOrderCommandCode.Success;
        });
        session.At(10, "Reject known ally and visible enemy during preview", runtime =>
        {
            var allyPreview = runtime.PreviewBuild(
                1, allyPreviewWorker, profiles[0], allyPreviewCenter);
            var enemyPreview = runtime.PreviewBuild(
                1, enemyPreviewWorker, profiles[0], enemyPreviewCenter);
            previewRejected =
                allyPreview.Code == TestConstructionCommandCode.PlacementRejected &&
                allyPreview.PlacementCode == TestBuildingPlacementCode.UnitOverlap &&
                enemyPreview.Code == TestConstructionCommandCode.PlacementRejected &&
                enemyPreview.PlacementCode == TestBuildingPlacementCode.UnitOverlap;
        });
        foreach (var checkpoint in new long[] { 2, 4, 6, 9, 11, 180, 379, 381, 600 })
        {
            session.At(checkpoint, "Capture replay diagnostic checkpoint", runtime =>
                replayCheckpoints[checkpoint] = (
                    runtime.CaptureReplayPackage(), runtime.StateHash));
        }
        for (var tick = 180; tick <= 370; tick++)
        {
            session.At(tick, "Observe the policy without reaching into simulation internals", runtime =>
            {
                if (buildings.Any(value => !value.Succeeded))
                    return;
                movableEvacuated |= runtime.ObserveOrders(movable)
                    .ConstructionEvacuationActive;
                var holdBuilding = runtime.ObserveGameplayBuilding(
                    buildings[1].BuildingId);
                var economyBuilding = runtime.ObserveGameplayBuilding(
                    buildings[2].BuildingId);
                var allyBuilding = runtime.ObserveGameplayBuilding(
                    buildings[3].BuildingId);
                var enemyBuilding = runtime.ObserveGameplayBuilding(
                    buildings[4].BuildingId);
                var protectedBlocked =
                    holdBuilding.State == TestBuildingLifecycleState.BlockedAtStart &&
                    economyBuilding.State == TestBuildingLifecycleState.BlockedAtStart &&
                    allyBuilding.State == TestBuildingLifecycleState.BlockedAtStart &&
                    enemyBuilding.State == TestBuildingLifecycleState.BlockedAtStart;
                conservativeWaitObserved |= protectedBlocked &&
                    !runtime.ObserveOrders(hold).ConstructionEvacuationActive;
                var worker = runtime.ObserveWorkerEconomy(economy);
                economyOrderPreserved |= protectedBlocked &&
                    worker.State == TestWorkerEconomyState.Gathering &&
                    worker.TargetNode == mineral &&
                    !runtime.ObserveOrders(economy)
                        .ConstructionEvacuationActive;
                authorityOrdersPreserved |= protectedBlocked &&
                    !runtime.ObserveOrders(ally).ConstructionEvacuationActive &&
                    !runtime.ObserveOrders(enemy).ConstructionEvacuationActive;
                if (hotCapture is null && protectedBlocked && movableEvacuated)
                {
                    hotCapture = runtime.CaptureRuntimeState();
                    hotTick = runtime.Tick;
                }
            });
        }
        session.At(380, "Release only the orders owned by their proper authority", runtime =>
        {
            runtime.Move([hold], laneCenters[1] + new Vector2(0f, 150f));
            var allyMove = runtime.PlayerMove(
                2, [ally], laneCenters[3] + new Vector2(0f, 150f));
            var enemyMove = runtime.PlayerMove(
                3, [enemy], laneCenters[4] + new Vector2(0f, 150f));
            released = allyMove == TestPlayerOrderCommandCode.Success &&
                       enemyMove == TestPlayerOrderCommandCode.Success;
            harvestCanceled = runtime.CancelConstruction(
                1, buildings[2].BuildingId);
        });
        return session;
    }

    private static VisualTestSession CreateConstructionReservationHardCommit(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            24, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 1000, 0, 15, 2);
        var builder = rig.SpawnWorker(
            new Vector2(100f, 353f), 1, maximumSpeed: 60f);
        var duplicateBuilder = rig.SpawnWorker(new Vector2(100f, 430f), 1);
        var canceledBuilder = rig.SpawnWorker(new Vector2(100f, 600f), 1);
        var transit = new[]
        {
            rig.Spawn(new Vector2(200f, 325f), 8f, 280f, 900f),
            rig.Spawn(new Vector2(200f, 353f), 8f, 280f, 900f),
            rig.Spawn(new Vector2(200f, 381f), 8f, 280f, 900f)
        };
        var target = new Vector2(420f, 353f);
        var canceledTarget = new Vector2(400f, 600f);
        rig.StartReplayPackageRecording();
        var accepted = rig.Build(
            1, builder, DemoBuildingTypes.SupplyDepot, target);
        var initial = rig.ObserveGameplayBuilding(accepted.BuildingId);
        var duplicate = rig.Build(
            1, duplicateBuilder, DemoBuildingTypes.SupplyDepot, target);
        var rawPlacement = rig.AssessBuildingPlacement(
            target, DemoBuildingTypes.SupplyDepot.Size, TestMovementClass.Small);
        var canceled = rig.Build(
            1, canceledBuilder, DemoBuildingTypes.SupplyDepot, canceledTarget);
        rig.Move(transit, new Vector2(800f, 353f));

        var acceptedAsGhost = accepted.Succeeded && canceled.Succeeded &&
                              initial.ReservationId > 0 &&
                              initial.FootprintId.Value == 0 &&
                              initial.State == TestBuildingLifecycleState.ReservedApproach &&
                              rig.BuildingCount == 0;
        var overlapRejected = duplicate.Code ==
                                  TestConstructionCommandCode.PlacementRejected &&
                              duplicate.PlacementCode ==
                                  TestBuildingPlacementCode.DynamicFootprintOverlap &&
                              rawPlacement.CombinedCode ==
                                  TestBuildingPlacementCode.DynamicFootprintOverlap;
        var canceledCleanly = false;
        var crossedWhileGhost = false;
        var hardCommitted = false;
        var friendlyOccupantEvacuated = false;
        TestRuntimeStateCapture? ghostCapture = null;
        var session = new VisualTestSession(
            "construction-reservation-hard-commit",
            "Reserved ghost allows transit, rejects overlap, then commits hard footprint",
            720,
            rig,
            [builder, duplicateBuilder, canceledBuilder, .. transit],
            runtime =>
            {
                var building = runtime.ObserveGameplayBuilding(accepted.BuildingId);
                var canceledState = runtime.ObserveGameplayBuilding(canceled.BuildingId);
                var economy = runtime.ObservePlayerEconomy(1);
                var package = runtime.CaptureReplayPackage();
                var hot = runtime.BindHotSnapshot(package, ghostCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var hotReplay = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var hotExact = hotRoundTrip &&
                               hotReplay.FinalHash == runtime.StateHash;
                var passed = acceptedAsGhost && overlapRejected &&
                             canceledCleanly && crossedWhileGhost && hardCommitted &&
                             friendlyOccupantEvacuated && hotExact &&
                             building.ReservationId == 0 &&
                             building.FootprintId.Value > 0 &&
                             building.State is TestBuildingLifecycleState.Constructing or
                                 TestBuildingLifecycleState.Completed &&
                             canceledState.State == TestBuildingLifecycleState.Canceled &&
                             canceledState.ReservationId == 0 &&
                             canceledState.FootprintId.Value == 0 &&
                             runtime.BuildingCount == 1 &&
                             economy.Minerals == 875;
                return new ScenarioResult(
                    passed,
                    $"ghost={acceptedAsGhost}, overlap={overlapRejected}, " +
                    $"transit={crossedWhileGhost}, hard={hardCommitted}, " +
                    $"evacuated={friendlyOccupantEvacuated}, " +
                    $"cancel={canceledCleanly}, hot={hotExact}/{hot.CanonicalByteCount}B, " +
                    $"state={building.State}, " +
                    $"reservation={building.ReservationId}, " +
                    $"footprints={runtime.BuildingCount}, minerals={economy.Minerals}, " +
                    $"builder={runtime.Observe(builder).State}@" +
                    $"{runtime.Observe(builder).Position.X:0.0}," +
                    $"{runtime.Observe(builder).Position.Y:0.0}->" +
                    $"{building.AccessPoint.X:0.0},{building.AccessPoint.Y:0.0}, " +
                    $"occupant={runtime.Observe(duplicateBuilder).Position.X:0.0}," +
                    $"{runtime.Observe(duplicateBuilder).Position.Y:0.0}");
            });
        session.At(30, "Cancel a reservation without creating or removing a footprint", runtime =>
            canceledCleanly = runtime.CancelConstruction(1, canceled.BuildingId));
        session.At(90, "A friendly worker walks into the reserved footprint", runtime =>
            runtime.Move([duplicateBuilder], target));
        session.At(120, "Traffic crosses the reserved ghost before the builder arrives", runtime =>
        {
            var building = runtime.ObserveGameplayBuilding(accepted.BuildingId);
            crossedWhileGhost = transit.All(unit =>
                                    runtime.Observe(unit).Position.X > target.X + 70f) &&
                                building.ReservationId > 0 &&
                                building.FootprintId.Value == 0 &&
                                runtime.BuildingCount == 0;
            ghostCapture = runtime.CaptureRuntimeState();
        });
        session.At(420, "Builder arrival atomically converts reservation to hard footprint", runtime =>
        {
            var building = runtime.ObserveGameplayBuilding(accepted.BuildingId);
            hardCommitted = building.ReservationId == 0 &&
                            building.FootprintId.Value > 0 &&
                            runtime.BuildingCount == 1;
            var occupant = runtime.Observe(duplicateBuilder).Position;
            friendlyOccupantEvacuated =
                MathF.Abs(occupant.X - target.X) > 28f ||
                MathF.Abs(occupant.Y - target.Y) > 28f;
        });
        return session
            .Highlight(
                new SimRect(target - new Vector2(80f), target + new Vector2(80f)),
                "RESERVATION GHOST: no pathing collision before builder arrival",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(
                    canceledTarget - new Vector2(80f),
                    canceledTarget + new Vector2(80f)),
                "CANCEL: reservation-only cleanup and 75% refund",
                TestDiagnosticKind.Info);
    }

    private static VisualTestSession CreateConstructionUnderBuildDefense(
        NavigationMapSnapshot? navigationMap,
        GameplayProfileCatalogSnapshot? gameplayProfiles,
        ClearanceBakeSnapshot? clearanceBake)
    {
        navigationMap ??= DemoMapDefinition.CreateSnapshot();
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var rig = MovementTestRig.CreateReplayPackageMap(
            16, navigationMap, gameplayProfiles, clearanceBake);
        rig.RegisterPlayer(1, 1000, 1000, 20, 1);
        rig.RegisterPlayer(2, 5000, 5000, 30, 2);
        var targetBuilder = rig.SpawnWorker(new Vector2(350f, 353f), 2);
        var academyBuilder = rig.SpawnWorker(new Vector2(350f, 520f), 2);
        var attackProfile = new TestCombatProfile(
            MaximumHealth: 100f,
            AttackDamage: 20f,
            AttackRange: 220f,
            AcquisitionRange: 260f,
            AttackCooldownSeconds: 20f,
            AttackWindupSeconds: 0.05f,
            LeashDistance: 400f);
        var attackers = new[]
        {
            rig.SpawnCombat(new Vector2(40f, 308f), 1, attackProfile,
                maximumSpeed: 1200f, acceleration: 12000f),
            rig.SpawnCombat(new Vector2(40f, 338f), 1, attackProfile,
                maximumSpeed: 1200f, acceleration: 12000f),
            rig.SpawnCombat(new Vector2(40f, 368f), 1, attackProfile,
                maximumSpeed: 1200f, acceleration: 12000f),
            rig.SpawnCombat(new Vector2(40f, 398f), 1, attackProfile,
                maximumSpeed: 1200f, acceleration: 12000f)
        };
        var targetType = DemoBuildingTypes.SupplyDepot with
        {
            Name = "Armor Boundary Target",
            BuildSeconds = 10f,
            MaximumHealth = 1000f,
            Armor = 4f,
            ArmorUpgradePerLevel = 2f
        };
        var academyType = DemoBuildingTypes.Academy with { BuildSeconds = 0.1f };
        var weapon = DemoTechnologies.InfantryWeapons with
        {
            Cost = new EconomyCost(0, 0),
            ResearchSeconds = 0.1f
        };
        var fortification = DemoTechnologies.FortificationDoctrine with
        {
            Cost = new EconomyCost(0, 0),
            ResearchSeconds = 0.1f
        };

        rig.StartReplayPackageRecording();
        var target = rig.Build(
            2, targetBuilder, targetType, new Vector2(420f, 353f));
        var academy = rig.Build(
            2, academyBuilder, academyType, new Vector2(420f, 520f));
        var samples = new TestGameplayBuildingSnapshot[4];
        var previewDamage = new float[4];
        var impactDamage = Enumerable.Repeat(float.NaN, 4).ToArray();
        TestResearchResult weaponResearch = default;
        TestResearchResult armorResearch = default;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 315;

        void SampleAndAttack(MovementTestRig runtime, int index)
        {
            samples[index] = runtime.ObserveGameplayBuilding(target.BuildingId);
            previewDamage[index] = runtime.PreviewCombatDamage(
                attackers[index], target.BuildingId).TotalDamage;
            runtime.AttackBuilding([attackers[index]], target.BuildingId);
        }

        void CaptureImpactAndStop(MovementTestRig runtime, int index)
        {
            impactDamage[index] = runtime.ObserveCombatEvents().Events
                .LastOrDefault(value => value.Kind == CombatEventKind.Impact &&
                                        value.TargetKind == CombatTargetKind.Building)
                .Damage;
        }

        var session = new VisualTestSession(
            "construction-under-build-defense",
            "Construction armor stays zero until the exact completion boundary",
            690,
            rig,
            [targetBuilder, academyBuilder, .. attackers],
            runtime =>
            {
                var final = runtime.ObserveGameplayBuilding(target.BuildingId);
                var package = runtime.CaptureReplayPackage();
                var packageRoundTrip = package.TryCanonicalRoundTrip(out var decoded);
                var replay = runtime.ReplayPackage(decoded!, runtime.Tick);
                var hot = runtime.BindHotSnapshot(package, hotCapture!);
                var hotRoundTrip = hot.TryCanonicalRoundTrip(out var decodedHot);
                var resumed = runtime.ResumeHotSnapshot(
                    package, decodedHot!, runtime.Tick);
                var replayExact = replay.FinalHash == runtime.StateHash;
                var resumedExact = resumed.FinalHash == runtime.StateHash;
                var traceExact = replay.MatchesFrom(resumed, hotTick + 30);
                var exact = replayExact && resumedExact && traceExact;
                var constructionStates = samples.Take(3).All(value =>
                    value.State == TestBuildingLifecycleState.Constructing &&
                    value.Armor == 0f);
                var progressBands = samples[0].Progress is >= 0.06f and <= 0.18f &&
                                    samples[1].Progress is >= 0.42f and <= 0.58f &&
                                    samples[2].Progress is >= 0.90f and < 1f;
                var completedBoundary = samples[3].State ==
                                            TestBuildingLifecycleState.Completed &&
                                        samples[3].Armor == 6f &&
                                        final.Armor == 6f;
                var damageBoundary =
                    previewDamage.SequenceEqual([20f, 20f, 20f, 14f]) &&
                    impactDamage.SequenceEqual([20f, 20f, 20f, 14f]);
                var passed = target.Succeeded && academy.Succeeded &&
                             runtime.TechnologyLevel(2, fortification.Id) == 1 &&
                             constructionStates && progressBands && completedBoundary &&
                             damageBoundary && packageRoundTrip && hotRoundTrip && exact;
                return new ScenarioResult(
                    passed,
                    $"progress={string.Join(',', samples.Select(value => value.Progress.ToString("0.000")))}, " +
                    $"armor={string.Join(',', samples.Select(value => value.Armor))}, " +
                    $"preview={string.Join(',', previewDamage)}, " +
                    $"impact={string.Join(',', impactDamage)}, fort=" +
                    $"{runtime.TechnologyLevel(2, fortification.Id)}, " +
                    $"research={weaponResearch.Code}/{armorResearch.Code}, " +
                    $"exact={replayExact}/{resumedExact}/{traceExact}, " +
                    $"hash={runtime.StateHash:X16}/{replay.FinalHash:X16}/" +
                    $"{resumed.FinalHash:X16}, bytes=" +
                    $"{package.CanonicalByteCount}/{hot.CanonicalByteCount}");
            })
            .SelectBuildings(target.BuildingId)
            .Highlight(
                new SimRect(new Vector2(350f, 285f), new Vector2(490f, 420f)),
                "UNDER CONSTRUCTION: Armor 0 -> completed Armor 6",
                TestDiagnosticKind.Accepted);
        session.At(30, "Research weapon prerequisite", runtime =>
            weaponResearch = runtime.Research(2, academy.BuildingId, weapon));
        session.At(45, "Research Fortification before defense samples", runtime =>
            armorResearch = runtime.Research(2, academy.BuildingId, fortification));
        session.At(75, "Attack near 10% progress with zero effective armor", runtime =>
            SampleAndAttack(runtime, 0));
        session.At(90, "Capture first construction impact", runtime =>
            CaptureImpactAndStop(runtime, 0));
        session.At(hotTick, "Attack near 50% and capture hot construction state", runtime =>
        {
            hotCapture = runtime.CaptureRuntimeState();
            SampleAndAttack(runtime, 1);
        });
        session.At(330, "Capture midpoint construction impact", runtime =>
            CaptureImpactAndStop(runtime, 1));
        session.At(590, "Attack on the last incomplete construction interval", runtime =>
            SampleAndAttack(runtime, 2));
        session.At(605, "Capture pre-completion impact", runtime =>
            CaptureImpactAndStop(runtime, 2));
        session.When(
            "Wait for exact completion before the armored attack",
            675,
            runtime => runtime.ObserveGameplayBuilding(target.BuildingId).State ==
                       TestBuildingLifecycleState.Completed,
            runtime => SampleAndAttack(runtime, 3));
        session.When(
            "Wait for completed-building impact",
            689,
            runtime => runtime.ObserveCombatEvents().Events.Any(value =>
                value.Kind == CombatEventKind.Impact &&
                value.Attacker == attackers[3] &&
                value.TargetKind == CombatTargetKind.Building &&
                value.TargetId == target.BuildingId.Value),
            runtime => CaptureImpactAndStop(runtime, 3));
        return session;
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
                                 distinctSizes == 4 && catalog.FormatVersion == 2 &&
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

    private static VisualTestSession CreateProductionExitRallyBoundaries(
        GameplayProfileCatalogSnapshot? gameplayProfiles)
    {
        gameplayProfiles ??= DemoGameplayProfiles.CreateSnapshot();
        var created = NavigationMapSnapshot.TryCreate(
            NavigationMapSnapshot.CurrentFormatVersion,
            new SimRect(Vector2.Zero, new Vector2(1000f, 600f)),
            [], [], [], [],
            out var navigation,
            out var navigationValidation);
        if (!created || navigation is null)
        {
            throw new InvalidOperationException(
                $"Production boundary map is invalid: " +
                $"{navigationValidation.FirstError}.");
        }

        var rig = MovementTestRig.CreateReplayPackageMap(
            128, navigation, gameplayProfiles, clearanceBake: null);
        rig.RegisterPlayer(1, 10000, 2000, 80, 0);
        rig.RegisterPlayer(2, 10000, 2000, 80, 0);
        var catalog = DemoProductionCatalog.CreateSnapshot();
        var marine = catalog.Recipe(0) with { ProductionSeconds = 0.25f };
        var rallyMarine = marine with { ProductionSeconds = 0.5f };
        var barracks = DemoBuildingTypes.Barracks with { BuildSeconds = 0.2f };
        Vector2[] centers =
        [
            new(120f, 100f), new(370f, 100f),
            new(620f, 100f), new(870f, 100f),
            new(120f, 350f), new(370f, 350f),
            new(620f, 350f), new(870f, 350f)
        ];
        var builders = centers.Select(center => rig.SpawnWorker(
            center - new Vector2(100f, 0f), 1,
            maximumSpeed: 260f)).ToArray();
        var softSealPositions = CreateProductionSealPositions(
            centers[4], barracks.Size,
            marine.UnitType.Movement.PhysicalRadius);
        var hardSealPositions = CreateProductionSealPositions(
            centers[5], barracks.Size,
            marine.UnitType.Movement.PhysicalRadius);
        var blockerProfile = TestCombatProfile.Standard with
        {
            MaximumHealth = 10000f,
            AttackDamage = 0f,
            AttackRange = 0f,
            AcquisitionRange = 0f
        };
        var friendlyBlockers = softSealPositions.Select((position, index) =>
            rig.SpawnCombat(
                position, 1,
                blockerProfile,
                radius: 7.5f, maximumSpeed: 260f,
                acceleration: 1000f)).ToArray();
        var enemyBlockers = hardSealPositions.Select((position, index) =>
            rig.SpawnCombat(
                position, 2,
                blockerProfile,
                radius: 7.5f, maximumSpeed: 260f,
                acceleration: 1000f)).ToArray();
        var beforeTarget = rig.SpawnCombat(new Vector2(600f, 540f), 1);
        var afterTarget = rig.SpawnCombat(
            new Vector2(900f, 540f), 1,
            maximumSpeed: 220f, acceleration: 900f);
        var killerProfile = TestCombatProfile.Standard with
        {
            AttackDamage = 1000f,
            AttackRange = 40f,
            AcquisitionRange = 40f,
            AttackWindupSeconds = 0.05f,
            LeashDistance = 1400f,
            ProjectileSpeed = 0f
        };
        var beforeKiller = rig.SpawnCombat(
            new Vector2(550f, 540f), 2, killerProfile,
            maximumSpeed: 500f, acceleration: 1600f);
        var afterKiller = rig.SpawnCombat(
            new Vector2(710f, 540f), 2, killerProfile,
            maximumSpeed: 500f, acceleration: 1600f);
        var initialUnitCount = rig.UnitCount;
        rig.StartReplayPackageRecording();

        var buildings = new TestConstructionResult[centers.Length];
        var issued = false;
        var buildingsCompleted = false;
        var directionCaptured = false;
        var directionAligned = false;
        var softEvacuated = false;
        var softSpawned = false;
        var hardWaited = false;
        var hardRecovered = false;
        var beforeTargetDied = false;
        var beforeNoRally = false;
        var afterFollowed = false;
        var afterContinued = false;
        var beforeSpawnPosition = Vector2.Zero;
        var afterDeathPosition = Vector2.Zero;
        TestRuntimeStateCapture? hotCapture = null;
        const long hotTick = 300;

        var session = new VisualTestSession(
                "production-exit-rally-boundaries",
                "Four-way exits, soft friendly evacuation and Rally death boundaries",
                520,
                rig,
                [.. builders, .. friendlyBlockers, .. enemyBlockers,
                    beforeTarget, afterTarget, beforeKiller, afterKiller],
                runtime =>
                {
                    var package = runtime.CaptureReplayPackage();
                    var packageRoundTrip = package.TryCanonicalRoundTrip(
                        out var decoded);
                    var replay = packageRoundTrip
                        ? runtime.ReplayPackage(decoded!, runtime.Tick)
                        : null;
                    var replayExact = replay is not null &&
                                      replay.FinalHash == runtime.StateHash;
                    var hotExact = false;
                    var hotVersion = 0;
                    if (hotCapture is not null)
                    {
                        var hot = runtime.BindHotSnapshot(package, hotCapture);
                        hotVersion = hot.FormatVersion;
                        if (hot.TryCanonicalRoundTrip(out var decodedHot))
                        {
                            var resumed = runtime.ResumeHotSnapshot(
                                package, decodedHot!, runtime.Tick);
                            hotExact = resumed.FinalHash == runtime.StateHash &&
                                       replay is not null &&
                                       replay.MatchesFrom(resumed, hotTick);
                        }
                    }

                    buildingsCompleted = buildings.All(value =>
                        value.Succeeded &&
                        runtime.ObserveGameplayBuilding(value.BuildingId).State ==
                            TestBuildingLifecycleState.Completed);
                    if (runtime.UnitCount >= initialUnitCount + 8)
                        hardRecovered = true;
                    if (runtime.UnitCount >= initialUnitCount + 7)
                    {
                        var beforeUnit = new TestUnitId(initialUnitCount + 5);
                        var afterUnit = new TestUnitId(initialUnitCount + 6);
                        var before = runtime.Observe(beforeUnit);
                        var after = runtime.Observe(afterUnit);
                        beforeNoRally |=
                            runtime.ObserveOrders(beforeUnit).ActiveOrder ==
                                TestOrderKind.None &&
                            Vector2.Distance(
                                before.Position, beforeSpawnPosition) < 24f;
                        afterContinued |= !runtime.IsUnitAlive(afterTarget) &&
                            runtime.ObserveOrders(afterUnit).ActiveOrder !=
                                TestOrderKind.FollowFriendly &&
                            Vector2.Distance(
                                after.AssignedTarget, afterDeathPosition) < 12f &&
                            Vector2.Distance(
                                after.Position, afterDeathPosition) < 28f;
                    }
                    var versions = package.FormatVersion ==
                                       SimulationReplayPackageSnapshot
                                           .CurrentFormatVersion &&
                                   hotVersion ==
                                       SimulationHotSnapshot.CurrentFormatVersion;
                    var passed = issued && buildingsCompleted &&
                                 directionCaptured && directionAligned &&
                                 softEvacuated && softSpawned &&
                                 hardWaited && hardRecovered &&
                                 beforeTargetDied && beforeNoRally &&
                                 afterFollowed && afterContinued &&
                                 packageRoundTrip && replayExact && hotExact &&
                                 versions;
                    return new ScenarioResult(
                        passed,
                        $"build/train={buildingsCompleted}/{issued}, " +
                        $"directions={directionCaptured}/{directionAligned}, " +
                        $"soft={softEvacuated}/{softSpawned}, " +
                        $"hard={hardWaited}/{hardRecovered}, " +
                        $"death-before={beforeTargetDied}/{beforeNoRally}, " +
                        $"death-after={afterFollowed}/{afterContinued}, " +
                        $"afterAlive={runtime.IsUnitAlive(afterTarget)}, " +
                        $"afterOrder={(runtime.UnitCount >= initialUnitCount + 7 ? runtime.ObserveOrders(new TestUnitId(initialUnitCount + 6)).ActiveOrder : TestOrderKind.None)}, " +
                        $"afterPos={(runtime.UnitCount >= initialUnitCount + 7 ? runtime.Observe(new TestUnitId(initialUnitCount + 6)).Position : Vector2.Zero)}, " +
                        $"afterGoal={(runtime.UnitCount >= initialUnitCount + 7 ? runtime.Observe(new TestUnitId(initialUnitCount + 6)).AssignedTarget : Vector2.Zero)}, deathPos={afterDeathPosition}, " +
                        $"units={runtime.UnitCount - initialUnitCount}, " +
                        $"replay={replayExact}, hot={hotExact}/v{hotVersion}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(500f, 290f), 0.9f)
            .CameraKeyframe(100, new Vector2(495f, 145f), 1.02f)
            .CameraKeyframe(200, new Vector2(245f, 350f), 1.08f)
            .CameraKeyframe(320, new Vector2(745f, 420f), 1.04f)
            .CameraKeyframe(430, new Vector2(370f, 350f), 1.08f)
            .CameraKeyframe(515, new Vector2(500f, 290f), 0.92f)
            .Highlight(
                new SimRect(new Vector2(35f, 35f), new Vector2(950f, 170f)),
                "RALLY CHOOSES EAST / WEST / SOUTH / NORTH EXIT",
                TestDiagnosticKind.Accepted)
            .Highlight(
                new SimRect(new Vector2(35f, 270f), new Vector2(460f, 440f)),
                "FRIENDLY SOFT SEAL / ENEMY HARD SEAL",
                TestDiagnosticKind.Info)
            .Highlight(
                new SimRect(new Vector2(535f, 270f), new Vector2(960f, 570f)),
                "RALLY TARGET DIES BEFORE / AFTER SPAWN",
                TestDiagnosticKind.Rejected);

        session.At(1, "Build eight production facilities through the gameplay API",
            runtime =>
            {
                for (var index = 0; index < buildings.Length; index++)
                    buildings[index] = runtime.Build(
                        1, builders[index], barracks, centers[index]);
            });
        session.At(100, "Train through four Rally directions",
            runtime =>
            {
                Vector2[] rallies =
                [
                    new(280f, 100f), new(210f, 100f),
                    new(620f, 270f), new(870f, 30f)
                ];
                issued = true;
                for (var index = 0; index < rallies.Length; index++)
                {
                    issued &= runtime.SetRallyPoint(
                        1, buildings[index].BuildingId, rallies[index]);
                    issued &= runtime.Train(
                        1, buildings[index].BuildingId, marine).Succeeded;
                }
            });
        for (var tick = 110; tick <= 140; tick++)
        {
            session.At(tick, "Capture first-frame directional exits", runtime =>
            {
                if (directionCaptured ||
                    runtime.UnitCount < initialUnitCount + 4)
                {
                    return;
                }
                var east = runtime.Observe(
                    new TestUnitId(initialUnitCount)).Position;
                var west = runtime.Observe(
                    new TestUnitId(initialUnitCount + 1)).Position;
                var south = runtime.Observe(
                    new TestUnitId(initialUnitCount + 2)).Position;
                var north = runtime.Observe(
                    new TestUnitId(initialUnitCount + 3)).Position;
                var half = barracks.Size * 0.5f;
                directionAligned = east.X > centers[0].X + half.X &&
                                   west.X < centers[1].X - half.X &&
                                   south.Y > centers[2].Y + half.Y &&
                                   north.Y < centers[3].Y - half.Y;
                directionCaptured = true;
            });
        }
        session.At(150, "Train after friendly and enemy seals have settled",
            runtime =>
            {
                runtime.Hold(friendlyBlockers);
                runtime.Hold(enemyBlockers);
                issued &= runtime.SetRallyPoint(
                    1, buildings[4].BuildingId, new Vector2(300f, 350f));
                issued &= runtime.SetRallyPoint(
                    1, buildings[5].BuildingId, new Vector2(590f, 350f));
                issued &= runtime.Train(
                    1, buildings[4].BuildingId, marine).Succeeded;
                issued &= runtime.Train(
                    1, buildings[5].BuildingId, marine).Succeeded;
            });
        session.At(200, "Verify soft evacuation and persistent enemy hard block",
            runtime =>
            {
                softEvacuated = friendlyBlockers
                    .Select((unit, index) => Vector2.Distance(
                        runtime.Observe(unit).Position,
                        softSealPositions[index]))
                    .Any(distance => distance > 10f);
                softSpawned = runtime.UnitCount >= initialUnitCount + 5;
                var hardQueue = runtime.ObserveProduction(
                    buildings[5].BuildingId);
                hardWaited = hardQueue.ActiveState ==
                                 TestProductionOrderState.WaitingForExit &&
                             hardQueue.ActiveProgress == 1f &&
                             runtime.UnitCount == initialUnitCount + 5;
            });
        session.At(210, "Bind two unit Rally targets and start production",
            runtime =>
            {
                issued &= runtime.SetRallyFriendlyUnit(
                    1, buildings[6].BuildingId, beforeTarget);
                issued &= runtime.SetRallyFriendlyUnit(
                    1, buildings[7].BuildingId, afterTarget);
                issued &= runtime.Train(
                    1, buildings[6].BuildingId, rallyMarine).Succeeded;
                issued &= runtime.Train(
                    1, buildings[7].BuildingId, rallyMarine).Succeeded;
            });
        session.At(215, "Kill the first Rally target before its unit exits",
            runtime => runtime.AttackTarget([beforeKiller], beforeTarget));
        session.At(250, "Observe no Rally versus active friendly following",
            runtime =>
            {
                beforeTargetDied = !runtime.IsUnitAlive(beforeTarget);
                if (runtime.UnitCount < initialUnitCount + 7)
                    return;
                var beforeUnit = new TestUnitId(initialUnitCount + 5);
                var afterUnit = new TestUnitId(initialUnitCount + 6);
                beforeSpawnPosition = runtime.Observe(beforeUnit).Position;
                beforeNoRally = runtime.ObserveOrders(beforeUnit).ActiveOrder ==
                                TestOrderKind.None;
                var afterOrder = runtime.ObserveOrders(afterUnit);
                afterFollowed = afterOrder.ActiveOrder ==
                                    TestOrderKind.FollowFriendly &&
                                afterOrder.ActiveTargetUnit == afterTarget.Value;
            });
        session.At(260, "Move the live Rally target after its follower exits",
            runtime => runtime.Move(
                [afterTarget], new Vector2(780f, 560f)));
        session.At(hotTick, "Capture live FollowFriendly and blocked-exit future state",
            runtime =>
            {
                hotCapture = runtime.CaptureRuntimeState();
                if (runtime.UnitCount < initialUnitCount + 7)
                    return;
                var afterUnit = runtime.Observe(
                    new TestUnitId(initialUnitCount + 6));
                afterFollowed &= Vector2.Distance(
                    afterUnit.AssignedTarget,
                    runtime.Observe(afterTarget).Position) < 12f;
            });
        session.At(340, "Kill the Rally target after the follower has tracked it",
            runtime => runtime.AttackTarget([afterKiller], afterTarget));
        session.At(370, "Release the enemy seal after capturing the death position",
            runtime =>
            {
                afterDeathPosition = runtime.Observe(afterTarget).Position;
                runtime.Move(enemyBlockers, new Vector2(370f, 550f));
            });
        return session;
    }

    private static Vector2[] CreateProductionSealPositions(
        Vector2 center,
        Vector2 buildingSize,
        float producedUnitRadius)
    {
        var half = buildingSize * 0.5f;
        var offset = producedUnitRadius + 6f;
        return
        [
            center + new Vector2(half.X + offset, 0f),
            center + new Vector2(-half.X - offset, 0f),
            center + new Vector2(0f, half.Y + offset),
            center + new Vector2(0f, -half.Y - offset),
            center + new Vector2(half.X + offset, half.Y + offset),
            center + new Vector2(half.X + offset, -half.Y - offset),
            center + new Vector2(-half.X - offset, half.Y + offset),
            center + new Vector2(-half.X - offset, -half.Y - offset),
            center + new Vector2(half.X + offset, -half.Y * 0.5f),
            center + new Vector2(half.X + offset, half.Y * 0.5f),
            center + new Vector2(-half.X - offset, -half.Y * 0.5f),
            center + new Vector2(-half.X - offset, half.Y * 0.5f)
        ];
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
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion &&
                             producingHot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion &&
                             waitingHot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion &&
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
                var leaderObservation = runtime.Observe(leader);
                var marineOrders = produced
                    ? runtime.ObserveOrders(new TestUnitId(4))
                    : default;
                var marineMovement = produced
                    ? runtime.ObserveMovement(new TestUnitId(4))
                    : default;
                var bodyGap = produced
                    ? Vector2.Distance(
                          marine.Position, leaderObservation.Position) -
                      marine.Radius - leaderObservation.Radius
                    : float.PositiveInfinity;
                var gatherStarted = worker.State is not
                    TestWorkerEconomyState.None and not TestWorkerEconomyState.Idle &&
                    worker.TargetNode == minerals;
                var contactTolerance = produced
                    ? IndependentNumericTolerance(
                        marine.Position,
                        leaderObservation.Position,
                        Vector2.Zero)
                    : 0f;
                var resolvedFriendlyTarget =
                    marineOrders.ActiveOrder == TestOrderKind.FollowFriendly &&
                    marineOrders.ActiveTargetUnit == leader.Value &&
                    marineMovement.GoalKind ==
                        UnitMovementGoalKind.FollowRange &&
                    marineMovement.TargetId == leader.Value &&
                    marineMovement.Result == UnitMovementLegResult.Reached &&
                    bodyGap >= -contactTolerance &&
                    bodyGap <= contactTolerance;
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
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion && hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion &&
                             package.ProductionCommandCount == 5 &&
                             exact && rejected;
                return new ScenarioResult(
                    passed,
                    $"targets={resourceRally.Kind}/{friendlyRally.Kind}, " +
                    $"gather={worker.State}@node{worker.TargetNode.Value}, " +
                    $"remaining={runtime.ObserveResourceNode(minerals).Remaining}, " +
                    $"marine={marine.Position}->{marine.AssignedTarget}, " +
                    $"leader={leaderObservation.Position}, order=" +
                    $"{marineOrders.ActiveOrder}#{marineOrders.ActiveTargetUnit}, " +
                    $"movement={marineMovement.GoalKind}#" +
                    $"{marineMovement.TargetId}/{marineMovement.Result}, " +
                    $"gap={bodyGap:0.######}, " +
                    $"protocol={protocol}, " +
                    $"persistence={packageRoundTrip}/{hotRoundTrip}/{exact}, " +
                    $"versions=log8/package{package.FormatVersion}/hot{hot.FormatVersion}, " +
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
                "RESOURCE -> Gather / FRIENDLY -> live follow target",
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
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion && hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion &&
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
                    $"versions=log8/package{package.FormatVersion}/hot{hot.FormatVersion}");
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
        var upgradedAttacker = rig.SpawnCombat(
            new Vector2(700f, 500f), 1,
            new TestCombatProfile(
                80f, 10f, 100f, 220f, 10f, 0.1f, 400f,
                BaseUpgradeDamage: 2f));
        var upgradedTarget = rig.SpawnCombat(
            new Vector2(805f, 500f), 2,
            new TestCombatProfile(
                100f, 0f, 10f, 30f, 1f, 0f, 60f,
                Armor: 1f,
                Attributes: CombatAttribute.Armored | CombatAttribute.Mechanical));
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
            [builder, upgradedAttacker, upgradedTarget],
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
                var upgradedDamage = runtime.PreviewCombatDamage(
                    upgradedAttacker, upgradedTarget);
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
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion && hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion &&
                             package.ConstructionCommandCount == 1 &&
                             package.ProductionCommandCount == 5 &&
                             technologyCatalog.StableHash ==
                                 DemoTechnologies.CreateCatalog().StableHash &&
                             upgradedDamage.DamagePerAttack == 13f &&
                             upgradedDamage.TotalDamage == 13f &&
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
                    $"upgradedDamage={upgradedDamage.TotalDamage}, " +
                    $"persistence={packageRoundTrip}/{hotRoundTrip}/{exact}, " +
                    $"versions=log8/package{package.FormatVersion}/hot{hot.FormatVersion}");
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
                             package.FormatVersion == SimulationReplayPackageSnapshot.CurrentFormatVersion && hot.FormatVersion == SimulationHotSnapshot.CurrentFormatVersion &&
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
                var combatEvents = runtime.ObserveCombatEvents().Events;
                var gameplayEvents = runtime.ObserveGameplayEvents().Events;
                var buildingRetargets = gameplayEvents.Where(value =>
                    value.Kind == GameplayEventKind.BuildingAttackChaseRetargeted &&
                    value.Building == target.BuildingId).ToArray();
                var buildingAttackStarts = combatEvents.Where(value =>
                    value.Kind == CombatEventKind.AttackStarted &&
                    value.TargetKind == CombatTargetKind.Building &&
                    value.TargetId == target.BuildingId.Value).ToArray();
                var participatingAttackers = attackers.Count(attacker =>
                    buildingAttackStarts.Any(value =>
                        value.Attacker == attacker));
                var nonParticipatingAttackers = attackers
                    .Where(attacker => !buildingAttackStarts.Any(value =>
                        value.Attacker == attacker))
                    .Select(value => value.Value)
                    .ToArray();
                var outOfRangeStarts = buildingAttackStarts.Count(value =>
                {
                    var gap = IndependentRectangleGap(
                        value.WorldPosition,
                        runtime.Observe(value.Attacker).Radius,
                        building.Center,
                        building.Size);
                    var tolerance = IndependentNumericTolerance(
                        value.WorldPosition,
                        building.Center,
                        building.Size);
                    return gap > attackerProfile.AttackRange + tolerance;
                });
                var attackerDetails = attackers.Select(value =>
                {
                    var unit = runtime.Observe(value);
                    var combat = runtime.ObserveCombat(value);
                    var movement = runtime.ObserveMovement(value);
                    return $"u{value.Value}@{unit.Position.X:0},{unit.Position.Y:0}/" +
                           $"{combat.State}/{movement.GoalKind}:{movement.Result}";
                });
                var passed = target.Succeeded && attackIssued &&
                             targetCompletedBeforeAttack &&
                             building.State == TestBuildingLifecycleState.Destroyed &&
                             building.Health == 0f && runtime.GameplayBuildingCount == 0 &&
                             runtime.BuildingCount == 0 &&
                             economy.SupplyCapacity == 10 &&
                             package.ConstructionCommandCount == 1 &&
                             package.UnitCommandCount == 1 &&
                             hotRoundTrip &&
                             hot.FormatVersion ==
                                 SimulationHotSnapshot.CurrentFormatVersion &&
                             exact && participatingAttackers == attackers.Length &&
                             outOfRangeStarts == 0;
                return new ScenarioResult(
                    passed,
                    $"state={building.State}, hp={building.Health:0}, " +
                    $"footprints={runtime.BuildingCount}, supply={economy.SupplyCapacity}, " +
                    $"orders={package.UnitCommandCount}, hot={hot.CanonicalByteCount}B, " +
                    $"exact={exact}, starts={buildingAttackStarts.Length}, " +
                    $"retargets={buildingRetargets.Length}[" +
                    $"{string.Join(',', buildingRetargets.Select(value =>
                        $"u{value.Unit.Value}@{value.Tick}:{value.Value:0}"))}], " +
                    $"participants={participatingAttackers}/{attackers.Length}, " +
                    $"missing=[{string.Join(',', nonParticipatingAttackers)}], " +
                    $"out-of-range={outOfRangeStarts}, " +
                    $"units=[{string.Join(',', attackerDetails)}]");
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
                var stragglers = string.Join(
                    ";",
                    all.Where(unit =>
                        Vector2.Distance(
                            runtime.Observe(unit).Position,
                            runtime.Observe(unit).AssignedTarget) > 20f)
                        .Select(unit =>
                        {
                            var snapshot = runtime.Observe(unit);
                            var movement = runtime.ObserveMovement(unit);
                            var direction = positive.Contains(unit) ? "+" : "-";
                            return $"{direction}u{unit.Value}:" +
                                   $"{snapshot.State}/" +
                                   $"{movement.GoalKind}:{movement.Result}@" +
                                   $"{snapshot.Position.X:0},{snapshot.Position.Y:0}>" +
                                   $"{snapshot.AssignedTarget.X:0}," +
                                   $"{snapshot.AssignedTarget.Y:0}/v" +
                                   $"{snapshot.Velocity.X:0.0}," +
                                   $"{snapshot.Velocity.Y:0.0}";
                        }));
                return new ScenarioResult(
                    shared.Passed && bothDirectionsPassed && trafficSafe,
                    $"positive={positiveArrivals}/{positive.Length}, " +
                    $"negative={negativeArrivals}/{negative.Length}, " +
                    $"both={bothDirectionsPassed}, conflicts={traffic.ConflictTicks}, " +
                    $"maxWait={maximumWait}, trafficSafe={trafficSafe}, " +
                    $"recovery={recovery.TotalEvents}/{recovery.UnreachableUnits}, " +
                    $"{shared.Summary}, stragglers=[{stragglers}]");
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
