using System.Numerics;

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
        "open-field",
        "dense-formation",
        "opposing-streams",
        "crossing-streams",
        "command-replace",
        "rapid-reissue",
        "shared-target-reservations",
        "stop-command",
        "hold-command",
        "mixed-radii",
        "boundary-target",
        "dynamic-local-invalidation",
        "dynamic-building-detour",
        "dynamic-building-remove",
        "dynamic-portal-reroute",
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

    public static VisualTestSession Create(string caseId) => caseId switch
    {
        "single-unit" => CreateSingleUnit(),
        "open-field" => CreateOpenField(),
        "dense-formation" => CreateDenseFormation(),
        "opposing-streams" => CreateOpposingStreams(),
        "crossing-streams" => CreateCrossingStreams(),
        "command-replace" => CreateCommandReplace(),
        "rapid-reissue" => CreateRapidReissue(),
        "shared-target-reservations" => CreateSharedTargetReservations(),
        "stop-command" => CreateStopCommand(),
        "hold-command" => CreateHoldCommand(),
        "mixed-radii" => CreateMixedRadii(),
        "boundary-target" => CreateBoundaryTarget(),
        "dynamic-local-invalidation" => CreateDynamicLocalInvalidation(),
        "dynamic-building-detour" => CreateDynamicBuildingDetour(),
        "dynamic-building-remove" => CreateDynamicBuildingRemove(),
        "dynamic-portal-reroute" => CreateDynamicPortalReroute(),
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
                var radius = (row + column) % 3 switch
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
