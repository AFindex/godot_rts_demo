using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static partial class VisualTestCatalog
{
    private const int SimulationTicksPerSecond = 60;

    private static VisualTestSession CreateSemanticConstructionContactMatrix()
    {
        var profiles = DemoBuildingTypes.All;
        var movement = DemoGameplayProfiles.CreateSnapshot().Unit(1);
        const int directions = 24;
        var caseCount = profiles.Length * directions;
        var columns = 12;
        var spacing = 360f;
        var rows = (int)MathF.Ceiling(caseCount / (float)columns);
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(
                2f * spacing + columns * spacing,
                2f * spacing + rows * spacing),
            caseCount + 8);
        rig.RegisterPlayer(1, 100_000, 100_000, 512, caseCount);

        var workers = new TestUnitId[caseCount];
        var buildings = new TestConstructionResult[caseCount];
        var centers = new Vector2[caseCount];
        var origins = new Vector2[caseCount];
        var commandsAccepted = true;
        for (var index = 0; index < caseCount; index++)
        {
            var profile = profiles[index / directions];
            var directionIndex = index % directions;
            var angle = directionIndex * MathF.Tau / directions;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var column = index % columns;
            var row = index / columns;
            var center = new Vector2(
                spacing + column * spacing,
                spacing + row * spacing);
            var travel = profile.Size.Length() * 0.5f + 120f;
            var origin = center + direction * travel;
            centers[index] = center;
            origins[index] = origin;
            workers[index] = rig.SpawnWorker(
                origin,
                1,
                movement.PhysicalRadius,
                movement.MaximumSpeed,
                movement.Acceleration);
            TestResourceNodeId? refineryNode = null;
            if (profile.RequiresVespeneNode)
            {
                var gas = DemoEconomyProfiles.VespeneGeyser;
                refineryNode = rig.AddResourceNode(
                    TestEconomyResourceKind.VespeneGas,
                    center,
                    gas.Amount,
                    gas.HarvestBatch,
                    gas.HarvestSeconds,
                    gas.HarvesterCapacity,
                    requiresRefinery: true,
                    operational: false);
            }
            buildings[index] = rig.Build(
                1, workers[index], profile, center, refineryNode);
            commandsAccepted &= buildings[index].Succeeded;
        }
        var caseByBuildingId = buildings
            .Select((value, index) => (value, index))
            .Where(value => value.value.Succeeded)
            .ToDictionary(
                value => value.value.BuildingId.Value,
                value => value.index);

        var maximumTravel = profiles.Max(value => value.Size.Length() * 0.5f + 120f);
        var movementSeconds = maximumTravel / movement.MaximumSpeed +
                              movement.MaximumSpeed / movement.Acceleration;
        var pathQueueTicks = (int)MathF.Ceiling(caseCount / 24f);
        var deadline = pathQueueTicks + (int)MathF.Ceiling(
            (movementSeconds + profiles.Max(value => value.BuildSeconds)) *
            SimulationTicksPerSecond) + 2;
        var duration = deadline + SimulationTicksPerSecond;

        var started = new bool[caseCount];
        var completed = new bool[caseCount];
        var eventCursor = 0UL;
        var earlyFootprints = 0;
        var remoteProgressFrames = 0;
        var penetrations = 0;
        var radialFailures = 0;
        var radialFailureDetails = new List<string>();
        var startContactFailures = 0;
        var completionEvents = 0;
        VisualTestSession? session = null;
        session = new VisualTestSession(
                "semantic-construction-contact-matrix",
                "Formal buildings from 24 bearings require true circle-to-rectangle contact",
                duration,
                rig,
                workers,
                runtime =>
                {
                    var allCompleted = buildings.All(value =>
                        value.Succeeded &&
                        runtime.ObserveGameplayBuilding(value.BuildingId).State ==
                        TestBuildingLifecycleState.Completed);
                    var incompleteDetails = Enumerable.Range(0, caseCount)
                        .Where(index => buildings[index].Succeeded)
                        .Select(index =>
                        {
                            var building = runtime.ObserveGameplayBuilding(
                                buildings[index].BuildingId);
                            var worker = runtime.Observe(workers[index]);
                            var movementSnapshot = runtime.ObserveMovement(
                                workers[index]);
                            var gap = IndependentRectangleGap(
                                worker.Position,
                                worker.Radius,
                                building.Center,
                                building.Size);
                            return (index, building, worker, movementSnapshot, gap);
                        })
                        .Where(value => value.building.State !=
                                        TestBuildingLifecycleState.Completed)
                        .Select(value =>
                            $"c{value.index}/p{value.index / directions}/" +
                            $"a{value.index % directions * 15}:" +
                            $"state={value.building.State}/" +
                            $"progress={value.building.Progress:0.###}/" +
                            $"gap={value.gap:0.######}/" +
                            $"pos={value.worker.Position.X:0.###}," +
                            $"{value.worker.Position.Y:0.###}/" +
                            $"move={value.movementSnapshot.GoalKind}:" +
                            $"{value.movementSnapshot.Result}")
                        .ToArray();
                    var passed = commandsAccepted && session!.ConditionsCompleted &&
                                 allCompleted && started.All(value => value) &&
                                 completed.All(value => value) &&
                                 earlyFootprints == 0 &&
                                 remoteProgressFrames == 0 &&
                                 penetrations == 0 && radialFailures == 0 &&
                                 startContactFailures == 0 &&
                                 completionEvents == caseCount;
                    return new ScenarioResult(
                        passed,
                        $"cases={caseCount}, accepted={commandsAccepted}, " +
                        $"completed={completed.Count(value => value)}/{caseCount}, " +
                        $"early-footprints={earlyFootprints}, " +
                        $"remote-progress={remoteProgressFrames}, " +
                        $"penetrations={penetrations}, radial={radialFailures}, " +
                        $"radial-detail=[{string.Join(';', radialFailureDetails)}], " +
                        $"start-contact={startContactFailures}, " +
                        $"completion-events={completionEvents}, " +
                        $"incomplete=[{string.Join(';', incompleteDetails)}], " +
                        $"condition={session!.ConditionFailure}");
                })
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(
                rig.WorldMaximum.X * 0.5f,
                rig.WorldMaximum.Y * 0.5f), 0.18f)
            .CameraKeyframe(duration, new Vector2(
                rig.WorldMaximum.X * 0.5f,
                rig.WorldMaximum.Y * 0.5f), 0.18f);

        for (var tick = 0; tick < duration; tick++)
        {
            session.At(tick, "Record independent construction geometry", runtime =>
            {
                var events = runtime.ObserveGameplayEvents(eventCursor);
                eventCursor = events.LatestSequence;
                foreach (var gameplayEvent in events.Events)
                {
                    if (!caseByBuildingId.TryGetValue(
                            gameplayEvent.Building.Value,
                            out var buildingIndex))
                        continue;
                    if (gameplayEvent.Kind is GameplayEventKind.ConstructionStarted or
                        GameplayEventKind.ConstructionResumed)
                    {
                        started[buildingIndex] = true;
                        var snapshot = runtime.ObserveGameplayBuilding(
                            buildings[buildingIndex].BuildingId);
                        var radius = runtime.Observe(workers[buildingIndex]).Radius;
                        var gap = IndependentRectangleGap(
                            gameplayEvent.WorldPosition,
                            radius,
                            snapshot.Center,
                            snapshot.Size);
                        var tolerance = IndependentNumericTolerance(
                            gameplayEvent.WorldPosition,
                            snapshot.Center,
                            snapshot.Size);
                        if (gap > tolerance || gap < -tolerance)
                            startContactFailures++;
                    }
                    else if (gameplayEvent.Kind ==
                             GameplayEventKind.ConstructionCompleted)
                    {
                        if (!completed[buildingIndex]) completionEvents++;
                        completed[buildingIndex] = true;
                    }
                    else if (gameplayEvent.Kind ==
                             GameplayEventKind.ConstructionProgressed)
                    {
                        var snapshot = runtime.ObserveGameplayBuilding(
                            buildings[buildingIndex].BuildingId);
                        var radius = runtime.Observe(workers[buildingIndex]).Radius;
                        var gap = IndependentRectangleGap(
                            gameplayEvent.WorldPosition,
                            radius,
                            snapshot.Center,
                            snapshot.Size);
                        var tolerance = IndependentNumericTolerance(
                            gameplayEvent.WorldPosition,
                            snapshot.Center,
                            snapshot.Size);
                        if (gameplayEvent.Value > 0f && gap > tolerance)
                            remoteProgressFrames++;
                    }
                }

                for (var index = 0; index < caseCount; index++)
                {
                    if (!buildings[index].Succeeded) continue;
                    var snapshot = runtime.ObserveGameplayBuilding(
                        buildings[index].BuildingId);
                    var worker = runtime.Observe(workers[index]);
                    var gap = IndependentRectangleGap(
                        worker.Position,
                        worker.Radius,
                        snapshot.Center,
                        snapshot.Size);
                    var tolerance = IndependentNumericTolerance(
                        worker.Position,
                        snapshot.Center,
                        snapshot.Size);
                    if (!started[index] && snapshot.FootprintId.Value > 0)
                        earlyFootprints++;
                    if (snapshot.FootprintId.Value > 0 && gap < -tolerance)
                        penetrations++;

                    if (runtime.Tick != 0) continue;
                    var fromCenter = origins[index] - centers[index];
                    var toAccess = snapshot.AccessPoint - centers[index];
                    var cross = MathF.Abs(
                        fromCenter.X * toAccess.Y -
                        fromCenter.Y * toAccess.X);
                    var crossTolerance = IndependentNumericTolerance(
                        origins[index], centers[index], snapshot.Size) *
                        MathF.Max(1f, fromCenter.Length() * toAccess.Length());
                    if (cross > crossTolerance ||
                        Vector2.Dot(fromCenter, toAccess) <= 0f)
                    {
                        radialFailures++;
                        radialFailureDetails.Add(
                            $"c{index}/p{index / directions}/" +
                            $"a{index % directions * 15}:" +
                            $"cross={cross:0.######}/tol={crossTolerance:0.######}/" +
                            $"access={snapshot.AccessPoint.X:0.###}," +
                            $"{snapshot.AccessPoint.Y:0.###}");
                    }
                }
            });
        }

        session.When(
            "Wait for every formal building to complete",
            deadline,
            runtime => buildings.All(value =>
                value.Succeeded &&
                runtime.ObserveGameplayBuilding(value.BuildingId).State ==
                TestBuildingLifecycleState.Completed),
            _ => { });
        return session;
    }

    private static VisualTestSession CreateSemanticConstructionResumeMatrix()
    {
        var profiles = DemoBuildingTypes.All;
        var movement = DemoGameplayProfiles.CreateSnapshot().Unit(1);
        var gasProfile = DemoEconomyProfiles.VespeneGeyser;
        Vector2[] centers =
        [
            new(330f, 240f),
            new(760f, 240f),
            new(1250f, 280f),
            new(430f, 700f),
            new(980f, 700f)
        ];
        float[] originalAngles =
        [
            MathF.PI,
            -MathF.PI * 0.5f,
            0f,
            MathF.PI * 0.5f,
            MathF.PI * 1.25f
        ];
        float[] replacementAngles =
        [
            MathF.PI * 0.2f,
            MathF.PI * 0.8f,
            MathF.PI * 1.35f,
            MathF.PI * 1.8f,
            MathF.PI * 0.35f
        ];
        var worldSize = new Vector2(1650f, 1000f);
        var rig = MovementTestRig.CreateOpenField(
            worldSize, profiles.Length * 2 + 8);
        rig.RegisterPlayer(1, 20_000, 20_000, 80, profiles.Length * 2);
        var originalWorkers = new TestUnitId[profiles.Length];
        var replacementWorkers = new TestUnitId[profiles.Length];
        var originalRetreatTargets = new Vector2[profiles.Length];
        var buildings = new TestConstructionResult[profiles.Length];
        var commandsAccepted = true;

        for (var index = 0; index < profiles.Length; index++)
        {
            var distance = profiles[index].Size.Length() * 0.5f + 145f;
            var originalDirection = new Vector2(
                MathF.Cos(originalAngles[index]),
                MathF.Sin(originalAngles[index]));
            var replacementDirection = new Vector2(
                MathF.Cos(replacementAngles[index]),
                MathF.Sin(replacementAngles[index]));
            var originalPosition = centers[index] +
                                   originalDirection * distance;
            var replacementPosition = centers[index] +
                                      replacementDirection * distance;
            originalWorkers[index] = rig.SpawnWorker(
                originalPosition,
                1,
                movement.PhysicalRadius,
                movement.MaximumSpeed,
                movement.Acceleration);
            replacementWorkers[index] = rig.SpawnWorker(
                replacementPosition,
                1,
                movement.PhysicalRadius,
                movement.MaximumSpeed,
                movement.Acceleration);
            originalRetreatTargets[index] = Vector2.Clamp(
                originalPosition + originalDirection * 70f,
                new Vector2(30f),
                worldSize - new Vector2(30f));

            TestResourceNodeId? refineryNode = null;
            if (profiles[index].RequiresVespeneNode)
            {
                refineryNode = rig.AddResourceNode(
                    TestEconomyResourceKind.VespeneGas,
                    centers[index],
                    gasProfile.Amount,
                    gasProfile.HarvestBatch,
                    gasProfile.HarvestSeconds,
                    gasProfile.HarvesterCapacity,
                    gasProfile.RequiresRefinery,
                    operational: false);
            }
            buildings[index] = rig.Build(
                1,
                originalWorkers[index],
                profiles[index],
                centers[index],
                refineryNode);
            commandsAccepted &= buildings[index].Succeeded;
        }

        var pausedProgress = new float[profiles.Length];
        var lastProgress = new float[profiles.Length];
        var interrupted = false;
        var resumed = false;
        var resumeAccepted = true;
        var accessTargetsValid = true;
        var pauseTick = -1L;
        var remoteProgress = 0;
        var penetrations = 0;
        var pauseProgressViolations = 0;
        var pauseTicks = (int)MathF.Ceiling(
            profiles.Min(value => value.BuildSeconds) * 0.1f *
            SimulationTicksPerSecond);
        var maximumTravel = profiles.Max(value =>
            value.Size.Length() * 0.5f + 145f);
        var deadline = (int)MathF.Ceiling((
            maximumTravel / movement.MaximumSpeed * 2f +
            profiles.Max(value => value.BuildSeconds) +
            movement.MaximumSpeed / movement.Acceleration * 5f +
            pauseTicks / (float)SimulationTicksPerSecond + 8f) *
            SimulationTicksPerSecond);
        var duration = deadline + SimulationTicksPerSecond;
        VisualTestSession? session = null;
        session = new VisualTestSession(
                "semantic-construction-resume-matrix",
                "Every formal building pauses and resumes with a different worker",
                duration,
                rig,
                [.. originalWorkers, .. replacementWorkers],
                runtime =>
                {
                    var events = runtime.ObserveGameplayEvents().Events;
                    var resumedEvents = events.Count(value =>
                        value.Kind == GameplayEventKind.ConstructionResumed &&
                        buildings.Any(building =>
                            building.BuildingId == value.Building));
                    var completedEvents = events.Count(value =>
                        value.Kind == GameplayEventKind.ConstructionCompleted &&
                        buildings.Any(building =>
                            building.BuildingId == value.Building));
                    var originalOutcomes = originalWorkers
                        .Select((unit, index) => (
                            Index: index,
                            Unit: unit,
                            Movement: runtime.ObserveMovement(unit),
                            Distance: Vector2.Distance(
                                runtime.Observe(unit).Position,
                                originalRetreatTargets[index])))
                        .ToArray();
                    var originalCommandsPreserved = originalOutcomes.All(value =>
                            value.Movement.GoalKind ==
                                UnitMovementGoalKind.GroundPoint &&
                            value.Movement.Result is
                                UnitMovementLegResult.Reached or
                                UnitMovementLegResult.SettledShort &&
                            value.Distance <=
                            runtime.Observe(value.Unit).Radius +
                            IndependentNumericTolerance(
                                runtime.Observe(value.Unit).Position,
                                originalRetreatTargets[value.Index],
                                Vector2.Zero));
                    var originalDetails = string.Join(';', originalOutcomes
                        .Select(value =>
                            $"u{value.Unit.Value}:{value.Movement.GoalKind}/" +
                            $"{value.Movement.Result}/d{value.Distance:0.###}"));
                    var completed = buildings.All(value =>
                        value.Succeeded &&
                        runtime.ObserveGameplayBuilding(value.BuildingId).State ==
                            TestBuildingLifecycleState.Completed);
                    var passed = commandsAccepted && interrupted && resumed &&
                                 resumeAccepted && accessTargetsValid &&
                                 session!.ConditionsCompleted && completed &&
                                 resumedEvents == profiles.Length &&
                                 completedEvents == profiles.Length &&
                                 originalCommandsPreserved &&
                                 remoteProgress == 0 && penetrations == 0 &&
                                 pauseProgressViolations == 0;
                    return new ScenarioResult(
                        passed,
                        $"buildings={profiles.Length}/{completed}, " +
                        $"resume={resumedEvents}/{resumeAccepted}, " +
                        $"completed-events={completedEvents}, " +
                        $"access={accessTargetsValid}, " +
                        $"original-orders={originalCommandsPreserved}" +
                        $"[{originalDetails}], " +
                        $"pause-progress={pauseProgressViolations}, " +
                        $"remote-progress={remoteProgress}, " +
                        $"penetrations={penetrations}, " +
                        $"condition={session!.ConditionFailure}");
                })
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(825f, 500f), 0.56f)
            .CameraKeyframe(duration, new Vector2(825f, 500f), 0.56f);

        session.When(
                "Wait for every original worker to establish real progress",
                deadline,
                runtime => commandsAccepted && buildings.All(value =>
                    runtime.ObserveGameplayBuilding(value.BuildingId).Progress >=
                    0.2f),
                runtime =>
                {
                    for (var index = 0; index < profiles.Length; index++)
                    {
                        pausedProgress[index] = runtime.ObserveGameplayBuilding(
                            buildings[index].BuildingId).Progress;
                        lastProgress[index] = pausedProgress[index];
                        commandsAccepted &= runtime.PlayerMove(
                            1,
                            [originalWorkers[index]],
                            originalRetreatTargets[index]) ==
                            TestPlayerOrderCommandCode.Success;
                    }
                    pauseTick = runtime.Tick;
                    interrupted = true;
                })
            .When(
                "Verify progress remains frozen without an assigned worker",
                deadline,
                runtime => interrupted &&
                    runtime.Tick >= pauseTick + pauseTicks &&
                    buildings.Select((value, index) => (
                            Building: runtime.ObserveGameplayBuilding(
                                value.BuildingId),
                            Paused: pausedProgress[index]))
                        .All(value =>
                            value.Building.State ==
                                TestBuildingLifecycleState.WaitingForBuilder &&
                            value.Building.Progress == value.Paused),
                runtime =>
                {
                    for (var index = 0; index < profiles.Length; index++)
                    {
                        resumeAccepted &= runtime.ResumeConstruction(
                            1,
                            buildings[index].BuildingId,
                            replacementWorkers[index]);
                        var movementSnapshot = runtime.ObserveMovement(
                            replacementWorkers[index]);
                        var buildingSnapshot = runtime.ObserveGameplayBuilding(
                            buildings[index].BuildingId);
                        var accessGap = IndependentRectangleGap(
                            movementSnapshot.NavigationTarget,
                            runtime.Observe(replacementWorkers[index]).Radius,
                            buildingSnapshot.Center,
                            buildingSnapshot.Size);
                        var tolerance = IndependentNumericTolerance(
                            movementSnapshot.NavigationTarget,
                            buildingSnapshot.Center,
                            buildingSnapshot.Size);
                        accessTargetsValid &=
                            movementSnapshot.GoalKind ==
                                UnitMovementGoalKind.BuildingBoundary &&
                            movementSnapshot.TargetId ==
                                buildings[index].BuildingId.Value &&
                            MathF.Abs(accessGap) <= tolerance;
                    }
                    resumed = true;
                })
            .When(
                "Wait for every replacement worker to complete construction",
                deadline,
                runtime => buildings.All(value =>
                    runtime.ObserveGameplayBuilding(value.BuildingId).State ==
                    TestBuildingLifecycleState.Completed),
                _ => { });

        for (var tick = 1; tick < duration; tick++)
        {
            session.At(tick, "Audit paused and resumed construction contact", runtime =>
            {
                for (var index = 0; index < profiles.Length; index++)
                {
                    var buildingSnapshot = runtime.ObserveGameplayBuilding(
                        buildings[index].BuildingId);
                    if (interrupted && !resumed &&
                        buildingSnapshot.Progress != pausedProgress[index])
                    {
                        pauseProgressViolations++;
                    }
                    if (!resumed ||
                        buildingSnapshot.Progress <= lastProgress[index])
                    {
                        lastProgress[index] = buildingSnapshot.Progress;
                        continue;
                    }

                    var workerSnapshot = runtime.Observe(
                        replacementWorkers[index]);
                    var gap = IndependentRectangleGap(
                        workerSnapshot.Position,
                        workerSnapshot.Radius,
                        buildingSnapshot.Center,
                        buildingSnapshot.Size);
                    var tolerance = IndependentNumericTolerance(
                        workerSnapshot.Position,
                        buildingSnapshot.Center,
                        buildingSnapshot.Size);
                    if (gap > tolerance) remoteProgress++;
                    if (gap < -tolerance) penetrations++;
                    lastProgress[index] = buildingSnapshot.Progress;
                }
            });
        }
        return session;
    }

    private static VisualTestSession CreateSemanticRealRefineryCycle()
    {
        var movement = DemoGameplayProfiles.CreateSnapshot().Unit(1);
        var mineral = DemoEconomyProfiles.MineralField;
        var gasProfile = DemoEconomyProfiles.VespeneGeyser;
        var rig = MovementTestRig.CreateEconomyMap(
            new Vector2(1500f, 900f), 48);
        rig.RegisterPlayer(1, 400, 0, 40, 12);
        var workers = Enumerable.Range(0, 12)
            .Select(index => rig.SpawnWorker(
                new Vector2(120f + index % 4 * 22f, 300f + index / 4 * 24f),
                1,
                movement.PhysicalRadius,
                movement.MaximumSpeed,
                movement.Acceleration))
            .ToArray();
        Vector2[] mineralPositions =
        [
            new(560f, 210f), new(640f, 210f),
            new(720f, 250f), new(750f, 330f),
            new(720f, 410f), new(640f, 450f),
            new(560f, 450f), new(520f, 330f)
        ];
        var minerals = mineralPositions.Select(position => rig.AddResourceNode(
            TestEconomyResourceKind.Minerals,
            position,
            mineral.Amount,
            mineral.HarvestBatch,
            mineral.HarvestSeconds,
            mineral.HarvesterCapacity)).ToArray();
        var gasPosition = new Vector2(430f, 650f);
        var gas = rig.AddResourceNode(
            TestEconomyResourceKind.VespeneGas,
            gasPosition,
            gasProfile.Amount,
            gasProfile.HarvestBatch,
            gasProfile.HarvestSeconds,
            gasProfile.HarvesterCapacity,
            requiresRefinery: gasProfile.RequiresRefinery,
            operational: false);
        var commandCenter = rig.Build(
            1,
            workers[0],
            DemoBuildingTypes.CommandCenter,
            new Vector2(300f, 350f));
        var refinery = default(TestConstructionResult);
        var miningIssued = false;
        var refineryIssued = false;
        var gasIssued = false;

        var furthestMineralDistance = mineralPositions.Max(value =>
            Vector2.Distance(value, new Vector2(300f, 350f)));
        var mineralWaves = (int)MathF.Ceiling(
            DemoBuildingTypes.Refinery.Cost.Minerals /
            (float)(mineral.HarvestBatch * mineralPositions.Length));
        var gasWaves = (int)MathF.Ceiling(10f / 3f);
        var seconds = DemoBuildingTypes.CommandCenter.BuildSeconds +
            Vector2.Distance(workers.Select(rig.Observe).First().Position,
                new Vector2(300f, 350f)) / movement.MaximumSpeed +
            mineralWaves * (2f * furthestMineralDistance /
                movement.MaximumSpeed + mineral.HarvestSeconds) +
            DemoBuildingTypes.Refinery.BuildSeconds +
            2f * Vector2.Distance(gasPosition, new Vector2(300f, 350f)) /
                movement.MaximumSpeed * gasWaves +
            gasProfile.HarvestSeconds * gasWaves +
            movement.MaximumSpeed / movement.Acceleration * 8f;
        var deadline = (int)MathF.Ceiling(seconds * SimulationTicksPerSecond) + 12;
        var duration = deadline + SimulationTicksPerSecond;

        VisualTestSession? session = null;
        session = new VisualTestSession(
                "semantic-real-refinery-cycle",
                "Build a real town hall and refinery, then complete repeated gas deliveries",
                duration,
                rig,
                workers,
                runtime =>
                {
                    var gameplayEvents = runtime.ObserveGameplayEvents().Events;
                    var gasHarvests = gameplayEvents.Count(value =>
                        value.Kind == GameplayEventKind.HarvestCompleted &&
                        value.ResourceKind == TestEconomyResourceKind.VespeneGas);
                    var gasDeliveries = gameplayEvents.Count(value =>
                        value.Kind == GameplayEventKind.CargoDelivered &&
                        value.ResourceKind == TestEconomyResourceKind.VespeneGas);
                    var refinerySnapshot = refinery.Succeeded
                        ? runtime.ObserveGameplayBuilding(refinery.BuildingId)
                        : default;
                    var commandSnapshot = runtime.ObserveGameplayBuilding(
                        commandCenter.BuildingId);
                    var invalidHarvestGeometry = gameplayEvents.Count(value =>
                        value.Kind == GameplayEventKind.HarvestCompleted &&
                        value.ResourceKind == TestEconomyResourceKind.VespeneGas &&
                        IndependentRectangleGap(
                            value.WorldPosition,
                            runtime.Observe(value.Unit).Radius,
                            refinerySnapshot.Center,
                            refinerySnapshot.Size) >
                        IndependentNumericTolerance(
                            value.WorldPosition,
                            refinerySnapshot.Center,
                            refinerySnapshot.Size));
                    var invalidDeliveryGeometry = gameplayEvents.Count(value =>
                        value.Kind == GameplayEventKind.CargoDelivered &&
                        value.ResourceKind == TestEconomyResourceKind.VespeneGas &&
                        IndependentRectangleGap(
                            value.WorldPosition,
                            runtime.Observe(value.Unit).Radius,
                            commandSnapshot.Center,
                            commandSnapshot.Size) >
                        IndependentNumericTolerance(
                            value.WorldPosition,
                            commandSnapshot.Center,
                            commandSnapshot.Size));
                    var gasNode = runtime.ObserveResourceNode(gas);
                    var passed = commandCenter.Succeeded && refinery.Succeeded &&
                                 miningIssued && refineryIssued && gasIssued &&
                                 session!.ConditionsCompleted && gasHarvests >= 10 &&
                                 gasDeliveries >= 10 &&
                                 invalidHarvestGeometry == 0 &&
                                 invalidDeliveryGeometry == 0 &&
                                 gasNode.ActiveNormal <= 1 &&
                                 runtime.ObservePlayerEconomy(1).VespeneGas ==
                                 gasDeliveries * gasProfile.HarvestBatch;
                    return new ScenarioResult(
                        passed,
                        $"town-hall={commandSnapshot.State}, " +
                        $"refinery={refinerySnapshot.State}, " +
                        $"harvests={gasHarvests}, deliveries={gasDeliveries}, " +
                        $"gas={runtime.ObservePlayerEconomy(1).VespeneGas}, " +
                        $"active={gasNode.ActiveNormal}, assigned={gasNode.AssignedNormal}, " +
                        $"invalid-geometry={invalidHarvestGeometry}/" +
                        $"{invalidDeliveryGeometry}, condition={session!.ConditionFailure}");
                })
            .RenderOmniscient()
            .CameraKeyframe(0, new Vector2(470f, 410f), 0.9f)
            .CameraKeyframe(duration, new Vector2(470f, 410f), 0.9f);

        session.When(
                "Wait for the real town hall",
                deadline,
                runtime => commandCenter.Succeeded &&
                    runtime.ObserveGameplayBuilding(commandCenter.BuildingId).State ==
                    TestBuildingLifecycleState.Completed,
                runtime =>
                {
                    miningIssued = runtime.PlayerSmartResource(
                        1, workers.Skip(1).ToArray(), minerals[0]) ==
                        TestPlayerOrderCommandCode.Success;
                })
            .When(
                "Mine the refinery cost through real delivery",
                deadline,
                runtime => runtime.ObservePlayerEconomy(1).Minerals >=
                    DemoBuildingTypes.Refinery.Cost.Minerals,
                runtime =>
                {
                    refinery = runtime.Build(
                        1,
                        workers[0],
                        DemoBuildingTypes.Refinery,
                        gasPosition,
                        gas);
                    refineryIssued = refinery.Succeeded;
                })
            .When(
                "Wait for the real refinery",
                deadline,
                runtime => refinery.Succeeded &&
                    runtime.ObserveGameplayBuilding(refinery.BuildingId).State ==
                    TestBuildingLifecycleState.Completed,
                runtime =>
                {
                    gasIssued = runtime.PlayerSmartResource(
                        1, workers.Skip(1).Take(4).ToArray(), gas) ==
                        TestPlayerOrderCommandCode.Success;
                })
            .When(
                "Complete ten gas delivery cycles",
                deadline,
                runtime => runtime.ObserveGameplayEvents().Events.Count(value =>
                    value.Kind == GameplayEventKind.CargoDelivered &&
                    value.ResourceKind == TestEconomyResourceKind.VespeneGas) >= 10,
                _ => { });
        return session;
    }

    private static VisualTestSession CreateSemanticUnreachableQueueRelease()
    {
        var movement = DemoGameplayProfiles.CreateSnapshot().Unit(1);
        var rig = MovementTestRig.CreateObstacleField(
            new Vector2(900f, 520f),
            8,
            new SimRect(new Vector2(420f, 0f), new Vector2(480f, 520f)));
        var unit = rig.Spawn(new Vector2(140f, 250f), movement);
        var unreachable = new Vector2(700f, 250f);
        var firstReachable = new Vector2(250f, 130f);
        var secondReachable = new Vector2(330f, 390f);
        var sawUnreachable = false;
        var unreachableTick = -1L;
        var eventCursor = 0UL;
        const int duration = 480;
        var session = new VisualTestSession(
                "semantic-unreachable-queue-release",
                "An unreachable movement leg releases two following Shift moves in order",
                duration,
                rig,
                [unit],
                runtime =>
                {
                    var order = runtime.ObserveOrders(unit);
                    var position = runtime.Observe(unit).Position;
                    var movementResult = runtime.ObserveMovement(unit);
                    var tolerance = IndependentNumericTolerance(
                        position, secondReachable, Vector2.Zero);
                    var passed = sawUnreachable && unreachableTick >= 0 &&
                                 order.PendingOrders == 0 &&
                                 order.CompletedQueuedOrders == 2 &&
                                 movementResult.Result ==
                                     UnitMovementLegResult.Reached &&
                                 Vector2.Distance(position, secondReachable) <=
                                     runtime.Observe(unit).Radius + tolerance;
                    return new ScenarioResult(
                        passed,
                        $"unreachable={sawUnreachable}@{unreachableTick}, " +
                        $"completed={order.CompletedQueuedOrders}, " +
                        $"pending={order.PendingOrders}, result={movementResult.Result}, " +
                        $"position={position.X:0.###},{position.Y:0.###}");
                })
            .Highlight(
                new SimRect(new Vector2(420f, 0f), new Vector2(480f, 520f)),
                "Full-height wall: first command cannot reach",
                TestDiagnosticKind.Rejected);
        session.At(0, "Issue unreachable plus two Shift moves", runtime =>
        {
            runtime.Move([unit], unreachable);
            runtime.Move([unit], firstReachable, queued: true);
            runtime.Move([unit], secondReachable, queued: true);
        });
        for (var tick = 1; tick < duration; tick++)
        {
            session.At(tick, "Observe public movement result", runtime =>
            {
                var events = runtime.ObserveGameplayEvents(eventCursor);
                eventCursor = events.LatestSequence;
                var unreachableEvent = events.Events.FirstOrDefault(value =>
                    value.Kind == GameplayEventKind.MovementLegFinished &&
                    value.Unit == unit &&
                    value.MovementResult ==
                        UnitMovementLegResult.Unreachable);
                if (sawUnreachable || unreachableEvent.Sequence == 0) return;
                sawUnreachable = true;
                unreachableTick = unreachableEvent.Tick;
            });
        }
        return session;
    }

    private static VisualTestSession CreateSemanticFollowBodyRange()
    {
        var movement = DemoGameplayProfiles.CreateSnapshot().Unit(1);
        var production = DemoProductionCatalog.CreateSnapshot();
        var marine = production.Recipe(0);
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(1300f, 760f), 32);
        rig.RegisterPlayer(1, 10_000, 2_000, 80, 1);
        var builder = rig.SpawnWorker(
            new Vector2(150f, 360f),
            1,
            movement.PhysicalRadius,
            movement.MaximumSpeed,
            movement.Acceleration);
        var target = rig.SpawnCombat(
            new Vector2(760f, 360f),
            1,
            radius: movement.PhysicalRadius,
            maximumSpeed: movement.MaximumSpeed,
            acceleration: movement.Acceleration);
        var barracks = rig.Build(
            1, builder, DemoBuildingTypes.Barracks, new Vector2(350f, 360f));
        var initialUnits = rig.UnitCount;
        var follower = new TestUnitId(-1);
        var targetMoved = false;
        var targetKilled = false;
        var lastTargetPosition = Vector2.Zero;
        var nonOverlapViolation = 0;
        var followedAfterMove = false;
        var stoppedWithoutDrift = false;
        var previousFollowerPosition = Vector2.Zero;
        var stationaryTicks = 0;
        var minimumGapAfterTargetMove = float.PositiveInfinity;
        var followReachedEvents = 0;
        var lastFollowReachedTick = -1L;
        var eventCursor = 0UL;
        var deadline = (int)MathF.Ceiling((
            DemoBuildingTypes.Barracks.BuildSeconds +
            marine.ProductionSeconds + 18f) * SimulationTicksPerSecond);
        var duration = deadline + SimulationTicksPerSecond;
        VisualTestSession? session = null;
        session = new VisualTestSession(
                "semantic-follow-body-range",
                "A produced unit follows another unit's body boundary and continues to its last position",
                duration,
                rig,
                [builder, target],
                runtime =>
                {
                    var order = follower.Value >= 0
                        ? runtime.ObserveOrders(follower)
                        : default;
                    var finalPosition = follower.Value >= 0
                        ? runtime.Observe(follower).Position
                        : Vector2.Zero;
                    var targetPosition = runtime.IsUnitAlive(target)
                        ? runtime.Observe(target).Position
                        : lastTargetPosition;
                    var movementSnapshot = follower.Value >= 0
                        ? runtime.ObserveMovement(follower)
                        : default;
                    var tolerance = IndependentNumericTolerance(
                        finalPosition, lastTargetPosition, Vector2.Zero);
                    var passed = barracks.Succeeded && session!.ConditionsCompleted &&
                                 follower.Value >= 0 && targetMoved && targetKilled &&
                                 followedAfterMove && stoppedWithoutDrift &&
                                 nonOverlapViolation == 0 &&
                                 order.ActiveOrder == TestOrderKind.Move &&
                                 Vector2.Distance(finalPosition, lastTargetPosition) <=
                                     movement.PhysicalRadius + tolerance;
                    return new ScenarioResult(
                        passed,
                        $"follower={follower.Value}, moved={followedAfterMove}, " +
                        $"killed={targetKilled}, drift-stop={stoppedWithoutDrift}, " +
                        $"overlap={nonOverlapViolation}, order={order.ActiveOrder}, " +
                        $"movement={movementSnapshot.GoalKind}:" +
                        $"{movementSnapshot.Result}, " +
                        $"position={finalPosition.X:0.###}," +
                        $"{finalPosition.Y:0.###}, target=" +
                        $"{targetPosition.X:0.###},{targetPosition.Y:0.###}, " +
                        $"min-gap={minimumGapAfterTargetMove:0.######}, " +
                        $"follow-reached={followReachedEvents}@" +
                        $"{lastFollowReachedTick}, target-movement=" +
                        $"{runtime.ObserveMovement(target).GoalKind}:" +
                        $"{runtime.ObserveMovement(target).Result}, " +
                        $"condition={session!.ConditionFailure}");
                })
            .RenderSpawnedUnits()
            .RenderOmniscient();

        for (var tick = 0; tick < duration; tick++)
        {
            session.At(tick, "Observe follower body separation", runtime =>
            {
                if (follower.Value < 0 || !runtime.IsUnitAlive(follower)) return;
                var followerState = runtime.Observe(follower);
                var targetPosition = runtime.IsUnitAlive(target)
                    ? runtime.Observe(target).Position
                    : lastTargetPosition;
                if (runtime.IsUnitAlive(target))
                {
                    var targetState = runtime.Observe(target);
                    var separation = Vector2.Distance(
                        followerState.Position, targetState.Position) -
                        followerState.Radius - targetState.Radius;
                    var tolerance = IndependentNumericTolerance(
                        followerState.Position,
                        targetState.Position,
                        Vector2.Zero);
                    if (separation < -tolerance) nonOverlapViolation++;
                }
                if (targetMoved && runtime.IsUnitAlive(target) &&
                    follower.Value >= 0)
                {
                    var targetState = runtime.Observe(target);
                    var gap = Vector2.Distance(
                                  followerState.Position,
                                  targetState.Position) -
                              followerState.Radius - targetState.Radius;
                    minimumGapAfterTargetMove = MathF.Min(
                        minimumGapAfterTargetMove, gap);
                    var tolerance = IndependentNumericTolerance(
                        followerState.Position,
                        targetState.Position,
                        Vector2.Zero);
                    if (gap <= tolerance) followedAfterMove = true;
                }
                if (targetKilled)
                {
                    var delta = Vector2.Distance(
                        followerState.Position, previousFollowerPosition);
                    var tolerance = IndependentNumericTolerance(
                        followerState.Position,
                        previousFollowerPosition,
                        Vector2.Zero);
                    stationaryTicks = delta <= tolerance
                        ? stationaryTicks + 1
                        : 0;
                    stoppedWithoutDrift |= stationaryTicks >=
                        SimulationTicksPerSecond;
                }
                previousFollowerPosition = followerState.Position;

                var events = runtime.ObserveGameplayEvents(eventCursor);
                eventCursor = events.LatestSequence;
                foreach (var gameplayEvent in events.Events)
                {
                    if (!targetMoved || gameplayEvent.Unit != follower ||
                        gameplayEvent.Kind !=
                            GameplayEventKind.MovementLegFinished ||
                        gameplayEvent.MovementGoalKind !=
                            UnitMovementGoalKind.FollowRange ||
                        gameplayEvent.MovementResult !=
                            UnitMovementLegResult.Reached)
                    {
                        continue;
                    }
                    followReachedEvents++;
                    lastFollowReachedTick = gameplayEvent.Tick;
                }
            });
        }

        session.When(
                "Wait for production building",
                deadline,
                runtime => barracks.Succeeded &&
                    runtime.ObserveGameplayBuilding(barracks.BuildingId).State ==
                    TestBuildingLifecycleState.Completed,
                runtime =>
                {
                    runtime.SetRallyFriendlyUnit(1, barracks.BuildingId, target);
                    runtime.Train(1, barracks.BuildingId, marine);
                })
            .When(
                "Wait for follower to spawn",
                deadline,
                runtime => runtime.UnitCount > initialUnits,
                runtime => follower = new TestUnitId(initialUnits))
            .When(
                "Wait for follower to reach the stationary target",
                deadline,
                runtime => follower.Value >= 0 &&
                    runtime.ObserveMovement(follower).GoalKind ==
                        UnitMovementGoalKind.FollowRange &&
                    runtime.ObserveMovement(follower).Result ==
                        UnitMovementLegResult.Reached,
                runtime =>
                {
                    runtime.Move([target], new Vector2(1040f, 520f));
                    targetMoved = true;
                })
            .When(
                "Wait for follower to track moved target",
                deadline,
                _ => followedAfterMove,
                runtime =>
                {
                    lastTargetPosition = runtime.Observe(target).Position;
                    runtime.DamageUnit(target, 100_000f);
                    targetKilled = true;
                })
            .When(
                "Wait for follower to stop at target's last position",
                deadline,
                _ => stoppedWithoutDrift,
                _ => { });
        return session;
    }

    private static VisualTestSession CreateSemanticProductionExitRestoration()
    {
        var movement = DemoGameplayProfiles.CreateSnapshot().Unit(1);
        var production = DemoProductionCatalog.CreateSnapshot();
        var marine = production.Recipe(0);
        var building = DemoBuildingTypes.Barracks;
        var softCenter = new Vector2(420f, 350f);
        var hardCenter = new Vector2(1180f, 350f);
        const float blockerRadius = 2.5f;
        var softTargets = IndependentRoundedBoundary(
            softCenter,
            building.Size,
            marine.UnitType.Movement.PhysicalRadius,
            7f);
        var hardTargets = IndependentRoundedBoundary(
            hardCenter,
            building.Size,
            marine.UnitType.Movement.PhysicalRadius,
            7f);
        var rig = MovementTestRig.CreateOpenField(
            new Vector2(1600f, 900f),
            softTargets.Length + hardTargets.Length + 16);
        rig.RegisterPlayer(1, 10_000, 2_000, 240, 2);
        rig.RegisterPlayer(2, 10_000, 2_000, 240, 0);
        var softBuilder = rig.SpawnWorker(
            softCenter - new Vector2(190f, 0f),
            1,
            movement.PhysicalRadius,
            movement.MaximumSpeed,
            movement.Acceleration);
        var hardBuilder = rig.SpawnWorker(
            hardCenter - new Vector2(190f, 0f),
            1,
            movement.PhysicalRadius,
            movement.MaximumSpeed,
            movement.Acceleration);
        var blockerProfile = TestCombatProfile.Standard with
        {
            MaximumHealth = 10_000f,
            AttackDamage = 0f,
            AttackRange = 0f,
            AcquisitionRange = 0f
        };
        var softBlockers = new TestUnitId[softTargets.Length];
        var hardBlockers = new TestUnitId[hardTargets.Length];
        var softBuilding = rig.Build(
            1, softBuilder, building, softCenter);
        var hardBuilding = rig.Build(
            1, hardBuilder, building, hardCenter);
        var blockersSpawned = false;
        var sealsVerified = false;
        var trainingAccepted = false;
        var hardWaited = false;
        var hardReleased = false;
        var hardProducedBeforeRelease = false;

        var maximumBuilderTravel = 190f + building.Size.Length() * 0.5f;
        var deadline = (int)MathF.Ceiling((
            maximumBuilderTravel / movement.MaximumSpeed +
            building.BuildSeconds +
            marine.ProductionSeconds * 2f +
            movement.MaximumSpeed / movement.Acceleration * 6f + 10f) *
            SimulationTicksPerSecond);
        var duration = deadline + SimulationTicksPerSecond;
        VisualTestSession? session = null;
        session = new VisualTestSession(
                "semantic-production-exit-restoration",
                "Continuous friendly and enemy perimeter seals verify displacement and hard waiting",
                duration,
                rig,
                [softBuilder, hardBuilder],
                runtime =>
                {
                    var events = runtime.ObserveGameplayEvents().Events;
                    var produced = events.Where(value =>
                        value.Kind == GameplayEventKind.UnitProduced).ToArray();
                    var softProduced = produced.Where(value =>
                        value.Building == softBuilding.BuildingId).ToArray();
                    var hardProduced = produced.Where(value =>
                        value.Building == hardBuilding.BuildingId).ToArray();
                    var displacementStarts = events.Where(value =>
                        value.Kind ==
                            GameplayEventKind.ProductionDisplacementStarted)
                        .ToArray();
                    var displacementFinishes = events.Where(value =>
                        value.Kind ==
                            GameplayEventKind.ProductionDisplacementFinished)
                        .ToArray();
                    var invalidSpawnGeometry = produced.Count(value =>
                    {
                        var producer = runtime.ObserveGameplayBuilding(
                            value.Building);
                        var radius = runtime.Observe(value.Unit).Radius;
                        var gap = IndependentRectangleGap(
                            value.WorldPosition,
                            radius,
                            producer.Center,
                            producer.Size);
                        var tolerance = IndependentNumericTolerance(
                            value.WorldPosition,
                            producer.Center,
                            producer.Size);
                        return gap > tolerance || gap < -tolerance;
                    });
                    var displacedUnits = displacementStarts
                        .Select(value => value.Unit)
                        .Distinct()
                        .ToArray();
                    var restoredHold = displacedUnits.Length > 0 &&
                        displacedUnits.All(unit =>
                            runtime.ObserveOrders(unit).ActiveOrder ==
                            TestOrderKind.Hold);
                    var allFinished = displacedUnits.Length > 0 &&
                        displacedUnits.All(unit =>
                            displacementFinishes.Any(value =>
                                value.Unit == unit));
                    var livingHardBlockers = blockersSpawned
                        ? hardBlockers.Count(value =>
                            runtime.ObserveCombat(value).Alive)
                        : 0;
                    var passed = softBuilding.Succeeded &&
                                 hardBuilding.Succeeded && blockersSpawned &&
                                 sealsVerified && trainingAccepted &&
                                 session!.ConditionsCompleted && hardWaited &&
                                 hardReleased && !hardProducedBeforeRelease &&
                                 softProduced.Length == 1 &&
                                 hardProduced.Length == 1 &&
                                 invalidSpawnGeometry == 0 &&
                                 restoredHold && allFinished;
                    return new ScenarioResult(
                        passed,
                        $"seals={softTargets.Length}/{hardTargets.Length}/" +
                        $"{sealsVerified}, " +
                        $"produced={softProduced.Length}/{hardProduced.Length}, " +
                        $"hard-wait={hardWaited}, " +
                        $"early={hardProducedBeforeRelease}, " +
                        $"displacement={displacementStarts.Length}/" +
                        $"{displacementFinishes.Length}, hold={restoredHold}, " +
                        $"spawn-geometry={invalidSpawnGeometry}, " +
                        $"hard-alive={livingHardBlockers}, " +
                        $"condition={session!.ConditionFailure}");
                })
            .RenderOmniscient()
            .RenderSpawnedUnits()
            .CameraKeyframe(0, new Vector2(800f, 360f), 0.62f)
            .CameraKeyframe(duration, new Vector2(800f, 360f), 0.62f);

        session.When(
                "Wait for both formal production buildings",
                deadline,
                runtime => softBuilding.Succeeded && hardBuilding.Succeeded &&
                    runtime.ObserveGameplayBuilding(softBuilding.BuildingId).State ==
                        TestBuildingLifecycleState.Completed &&
                    runtime.ObserveGameplayBuilding(hardBuilding.BuildingId).State ==
                        TestBuildingLifecycleState.Completed,
                runtime =>
                {
                    for (var index = 0; index < softBlockers.Length; index++)
                    {
                        softBlockers[index] = runtime.SpawnCombat(
                            softTargets[index], 1, blockerProfile,
                            blockerRadius, 180f, 900f);
                    }
                    for (var index = 0; index < hardBlockers.Length; index++)
                    {
                        hardBlockers[index] = runtime.SpawnCombat(
                            hardTargets[index], 2, blockerProfile,
                            blockerRadius, 180f, 900f);
                    }
                    runtime.Hold(softBlockers);
                    runtime.Hold(hardBlockers);
                    blockersSpawned = true;
                })
            .When(
                "Wait for both continuous perimeter seals",
                deadline,
                runtime => blockersSpawned &&
                    IndependentProductionSealClosed(
                        runtime, softBlockers, softCenter, building.Size,
                        marine.UnitType.Movement.PhysicalRadius) &&
                    IndependentProductionSealClosed(
                        runtime, hardBlockers, hardCenter, building.Size,
                        marine.UnitType.Movement.PhysicalRadius),
                runtime =>
                {
                    sealsVerified = true;
                    trainingAccepted = runtime.SetRallyPoint(
                        1,
                        softBuilding.BuildingId,
                        softCenter + new Vector2(0f, -240f));
                    trainingAccepted &= runtime.SetRallyPoint(
                        1,
                        hardBuilding.BuildingId,
                        hardCenter + new Vector2(0f, -240f));
                    trainingAccepted &= runtime.Train(
                        1, softBuilding.BuildingId, marine).Succeeded;
                    trainingAccepted &= runtime.Train(
                        1, hardBuilding.BuildingId, marine).Succeeded;
                })
            .When(
                "Wait for friendly displacement and enemy hard block",
                deadline,
                runtime => runtime.ObserveGameplayEvents().Events.Any(value =>
                               value.Kind == GameplayEventKind.UnitProduced &&
                               value.Building == softBuilding.BuildingId) &&
                           runtime.ObserveProduction(hardBuilding.BuildingId)
                               .ActiveState ==
                           TestProductionOrderState.WaitingForExit,
                runtime =>
                {
                    hardWaited = true;
                    hardProducedBeforeRelease = runtime.ObserveGameplayEvents()
                        .Events.Any(value =>
                            value.Kind == GameplayEventKind.UnitProduced &&
                            value.Building == hardBuilding.BuildingId);
                    foreach (var blocker in hardBlockers)
                        runtime.DamageUnit(blocker, 100_000f);
                    hardReleased = true;
                })
            .When(
                "Wait for hard exit release and restored soft orders",
                deadline,
                runtime =>
                {
                    var events = runtime.ObserveGameplayEvents().Events;
                    var displaced = events.Where(value =>
                            value.Kind ==
                                GameplayEventKind.ProductionDisplacementStarted)
                        .Select(value => value.Unit)
                        .Distinct()
                        .ToArray();
                    return events.Any(value =>
                               value.Kind == GameplayEventKind.UnitProduced &&
                               value.Building == hardBuilding.BuildingId) &&
                           displaced.Length > 0 &&
                           displaced.All(unit => events.Any(value =>
                               value.Kind ==
                                   GameplayEventKind.ProductionDisplacementFinished &&
                               value.Unit == unit)) &&
                           displaced.All(unit =>
                               runtime.ObserveOrders(unit).ActiveOrder ==
                               TestOrderKind.Hold);
                },
                _ => { });
        return session;
    }

    private static bool IndependentProductionSealClosed(
        MovementTestRig runtime,
        IReadOnlyList<TestUnitId> blockers,
        Vector2 buildingCenter,
        Vector2 buildingSize,
        float producedRadius)
    {
        var tolerance = IndependentNumericTolerance(
            buildingCenter, buildingCenter, buildingSize);
        var candidates = IndependentRoundedBoundary(
            buildingCenter,
            buildingSize,
            producedRadius + tolerance,
            MathF.Max(producedRadius * 2f, 8f));
        foreach (var candidate in candidates)
        {
            var covered = false;
            foreach (var blocker in blockers)
            {
                if (!runtime.ObserveCombat(blocker).Alive)
                    continue;
                var unit = runtime.Observe(blocker);
                var overlap = producedRadius + unit.Radius + tolerance;
                if (Vector2.DistanceSquared(candidate, unit.Position) <
                    overlap * overlap)
                {
                    covered = true;
                    break;
                }
            }
            if (!covered)
                return false;
        }
        return true;
    }

    private static Vector2[] IndependentRoundedBoundary(
        Vector2 center,
        Vector2 size,
        float clearance,
        float maximumSpacing)
    {
        var half = size * 0.5f;
        var minimum = center - half;
        var maximum = center + half;
        var result = new List<Vector2>();
        AddIndependentSide(result,
            new Vector2(minimum.X, minimum.Y - clearance),
            new Vector2(maximum.X, minimum.Y - clearance), maximumSpacing);
        AddIndependentArc(result,
            new Vector2(maximum.X, minimum.Y), clearance,
            -MathF.PI * 0.5f, 0f, maximumSpacing);
        AddIndependentSide(result,
            new Vector2(maximum.X + clearance, minimum.Y),
            new Vector2(maximum.X + clearance, maximum.Y), maximumSpacing);
        AddIndependentArc(result,
            maximum, clearance, 0f, MathF.PI * 0.5f, maximumSpacing);
        AddIndependentSide(result,
            new Vector2(maximum.X, maximum.Y + clearance),
            new Vector2(minimum.X, maximum.Y + clearance), maximumSpacing);
        AddIndependentArc(result,
            new Vector2(minimum.X, maximum.Y), clearance,
            MathF.PI * 0.5f, MathF.PI, maximumSpacing);
        AddIndependentSide(result,
            new Vector2(minimum.X - clearance, maximum.Y),
            new Vector2(minimum.X - clearance, minimum.Y), maximumSpacing);
        AddIndependentArc(result,
            minimum, clearance, MathF.PI, MathF.PI * 1.5f, maximumSpacing);
        return result.ToArray();
    }

    private static void AddIndependentSide(
        List<Vector2> result,
        Vector2 start,
        Vector2 end,
        float maximumSpacing)
    {
        var count = Math.Max(
            1,
            (int)MathF.Ceiling(Vector2.Distance(start, end) / maximumSpacing));
        for (var index = 0; index <= count; index++)
        {
            var point = start + (end - start) * (index / (float)count);
            if (result.Count == 0 || result[^1] != point) result.Add(point);
        }
    }

    private static void AddIndependentArc(
        List<Vector2> result,
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle,
        float maximumSpacing)
    {
        var count = Math.Max(
            1,
            (int)MathF.Ceiling(
                MathF.PI * 0.5f * radius / maximumSpacing));
        for (var index = 0; index <= count; index++)
        {
            var angle = startAngle +
                        (endAngle - startAngle) * index / count;
            var point = center +
                new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            if (result.Count == 0 || result[^1] != point) result.Add(point);
        }
    }

    private static float IndependentRectangleGap(
        Vector2 unitCenter,
        float unitRadius,
        Vector2 rectangleCenter,
        Vector2 rectangleSize)
    {
        var half = rectangleSize * 0.5f;
        var minimum = rectangleCenter - half;
        var maximum = rectangleCenter + half;
        var nearest = Vector2.Clamp(unitCenter, minimum, maximum);
        return Vector2.Distance(unitCenter, nearest) - unitRadius;
    }

    private static float IndependentNumericTolerance(
        Vector2 left,
        Vector2 right,
        Vector2 size)
    {
        var largest = MathF.Max(
            1f,
            MathF.Max(
                MathF.Max(MathF.Abs(left.X), MathF.Abs(left.Y)),
                MathF.Max(
                    MathF.Max(MathF.Abs(right.X), MathF.Abs(right.Y)),
                    MathF.Max(MathF.Abs(size.X), MathF.Abs(size.Y)))));
        return (MathF.BitIncrement(largest) - largest) * 32f;
    }
}
