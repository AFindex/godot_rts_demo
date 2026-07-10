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
        ClearanceBakeSnapshot? clearanceBake = null) => caseId switch
    {
        "single-unit" => CreateSingleUnit(),
        "attack-move-engage-resume" => CreateAttackMoveEngageResume(),
        "attack-move-leash-resume" => CreateAttackMoveLeashResume(),
        "attack-move-command-isolation" => CreateAttackMoveCommandIsolation(),
        "attack-move-cancel" => CreateAttackMoveCancel(),
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
            720,
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
                var passed = stoppedUnit.State == TestUnitState.Idle &&
                             heldUnit.State == TestUnitState.Holding &&
                             stoppedUnit.Velocity.LengthSquared() < 0.25f &&
                             heldUnit.Velocity.LengthSquared() < 0.25f &&
                             stoppedCombat.Target is null && heldCombat.Target is null &&
                             runtime.ObserveCombat(upperEnemy).Alive &&
                             runtime.ObserveCombat(lowerEnemy).Alive;
                return new ScenarioResult(
                    passed,
                    $"stop={stoppedUnit.State}/{stoppedCombat.State}, " +
                    $"hold={heldUnit.State}/{heldCombat.State}, " +
                    $"targets={stoppedCombat.Target}/{heldCombat.Target}");
            })
            .At(90, "Stop cancels upper engagement", runtime =>
                runtime.Stop([stopped]))
            .At(90, "Hold cancels lower engagement", runtime =>
                runtime.Hold([held]));
    }

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
