using System.Numerics;
using RtsDemo.Simulation;
using War3Rts;

namespace RtsDemo.Tests;

public static class BuildingUpgradeSelfTest
{
    private const int PlayerId = 1;

    public static SelfTestResult Run()
    {
        try
        {
            var buildings = War3HumanContent.CreateBuildingCatalog();
            var upgrades = War3HumanContent.CreateBuildingUpgradeCatalog();
            var production = War3HumanContent.CreateProductionCatalog();
            var technologies = War3HumanContent.CreateTechnologyCatalog();
            var profiles = upgrades.Profiles.ToArray();
            var towerUpgrades = upgrades.ForSource(
                War3HumanContent.ScoutTower).ToArray();
            var towerBranchesValid = towerUpgrades.Length == 3 &&
                towerUpgrades.Select(value => value.TargetType.Id)
                    .Order().SequenceEqual(new[]
                    {
                        War3HumanContent.GuardTower,
                        War3HumanContent.CannonTower,
                        War3HumanContent.ArcaneTower
                    }.Order()) &&
                towerUpgrades.Single(value => value.TargetType.Id ==
                    War3HumanContent.GuardTower).Requirements.Any(value =>
                    value.Kind == TechnologyRequirementKind.CompletedBuilding &&
                    value.TargetId == War3HumanContent.LumberMill) &&
                towerUpgrades.Single(value => value.TargetType.Id ==
                    War3HumanContent.CannonTower).Requirements.Any(value =>
                    value.Kind == TechnologyRequirementKind.CompletedBuilding &&
                    value.TargetId == War3HumanContent.Workshop) &&
                towerUpgrades.Single(value => value.TargetType.Id ==
                    War3HumanContent.ArcaneTower).Requirements.Length == 0;
            var catalogValid = profiles.Length == 5 && towerBranchesValid &&
                profiles[0].SourceBuildingTypeId == War3HumanContent.TownHall &&
                profiles[0].TargetType.Id == War3HumanContent.Keep &&
                profiles[0].Cost == new EconomyCost(320, 210) &&
                profiles[0].UpgradeSeconds == 140f &&
                profiles[1].SourceBuildingTypeId == War3HumanContent.Keep &&
                profiles[1].TargetType.Id == War3HumanContent.Castle &&
                profiles[1].Cost == new EconomyCost(360, 210) &&
                profiles[1].UpgradeSeconds == 140f &&
                profiles[1].Requirements.SequenceEqual(
                [
                    new TechnologyRequirementProfile(
                        TechnologyRequirementKind.CompletedBuilding,
                        War3HumanContent.AltarOfKings,
                        1)
                ]) &&
                upgrades.SatisfiesBuildingType(
                    War3HumanContent.Castle,
                    War3HumanContent.TownHall);

            var simulation = CreateSimulation(buildings, upgrades);
            simulation.StartCommandRecording();
            var townHall = new GameplayBuildingId(0);
            var first = simulation.IssueBuildingUpgrade(
                PlayerId, townHall, profiles[0]);
            var blockedProduction = simulation.IssueProduction(
                PlayerId,
                townHall,
                production.Recipes.ToArray().Single(value =>
                    value.UnitType.Id == War3HumanContent.Peasant));
            var blockedBuildingAbility = simulation.IssueBuildingAbility(
                PlayerId, townHall, abilityId: 0);
            for (var tick = 0; tick < 70; tick++) simulation.Tick(1f);

            var hotState = simulation.CaptureRuntimeState();
            var hotPayload = RuntimeHotSnapshotCodec.Serialize(
                SimulationHotSnapshot.CurrentFormatVersion,
                0UL,
                hotState);
            var hotRoundTrip = RuntimeHotSnapshotCodec.TryDeserialize(
                hotPayload,
                SimulationHotSnapshot.CurrentFormatVersion,
                out var packageHash,
                out var restoredHot,
                out var hotValidation) &&
                restoredHot is not null && packageHash == 0UL &&
                hotValidation == HotSnapshotValidationCode.Success &&
                restoredHot.BuildingUpgrades.Orders.Length == 1 &&
                restoredHot.BuildingUpgrades.CatalogProfiles.Length == 5 &&
                MathF.Abs(
                    restoredHot.BuildingUpgrades.Orders[0].Progress - 0.5f) <
                    0.0001f;

            var canceled = simulation.CancelBuildingUpgrade(
                PlayerId, first.OrderId);
            var afterCancel = simulation.Economy.Players.Snapshot(PlayerId);
            var refundValid = afterCancel.Minerals == 1_920 &&
                              afterCancel.VespeneGas == 1_947 &&
                              simulation.Construction.Observe(townHall).Type.Id ==
                              War3HumanContent.TownHall;

            var restarted = simulation.IssueBuildingUpgrade(
                PlayerId, townHall, profiles[0]);
            for (var tick = 0; tick < 141; tick++) simulation.Tick(1f);
            var keep = simulation.Construction.Observe(townHall);
            var keepValid = restarted.Succeeded &&
                            keep.Type.Id == War3HumanContent.Keep &&
                            keep.MaximumHealth == 2_000f &&
                            keep.Health == 2_000f;

            var peasantRecipe = production.Recipes.ToArray().Single(value =>
                value.UnitType.Id == War3HumanContent.Peasant);
            var inheritedProduction = simulation.IssueProduction(
                PlayerId, townHall, peasantRecipe);
            var inheritedCanceled = inheritedProduction.Succeeded &&
                simulation.CancelProduction(
                    PlayerId, inheritedProduction.OrderId);
            var inheritedResearch = technologies.Technologies.ToArray()
                .Where(value =>
                    value.ResearcherBuildingTypeId == War3HumanContent.TownHall)
                .All(value => simulation.Technology.ValidateEnqueue(
                        PlayerId,
                        townHall,
                        value,
                        simulation.Construction,
                        simulation.Economy.Players,
                        simulation.BuildingUpgrades.IsUpgrading,
                        simulation.BuildingUpgrades.SatisfiesBuildingType)
                    .Code != ResearchCommandCode.WrongResearcherType);

            var castleStarted = simulation.IssueBuildingUpgrade(
                PlayerId, townHall, profiles[1]);
            for (var tick = 0; tick < 141; tick++) simulation.Tick(1f);
            var castle = simulation.Construction.Observe(townHall);
            var castleValid = castleStarted.Succeeded &&
                              castle.Type.Id == War3HumanContent.Castle &&
                              castle.MaximumHealth == 2_500f &&
                              castle.Health == 2_500f &&
                              technologies.Technologies.ToArray()
                                  .Where(value => value.Requirements.Any(requirement =>
                                      requirement.Kind ==
                                          TechnologyRequirementKind.CompletedBuilding &&
                                      requirement.TargetId ==
                                          War3HumanContent.Castle))
                                  .All(value => value.Requirements.Any());

            var log = simulation.CaptureProductionCommandLog();
            var commandRoundTrip =
                ProductionCommandLogSnapshot.TryDeserialize(
                    log.CanonicalBytes,
                    out var repeated,
                    out var commandValidation) &&
                repeated is not null &&
                commandValidation ==
                    ProductionCommandLogValidationCode.Success &&
                repeated.StableHash == log.StableHash &&
                repeated.Entries.Count(value => value.Kind ==
                    ProductionReplayCommandKind.UpgradeBuilding) == 3 &&
                repeated.Entries.Count(value => value.Kind ==
                    ProductionReplayCommandKind.CancelBuildingUpgrade) == 1;

            var replaySimulation = CreateSimulation(buildings, upgrades);
            var replay = new ProductionCommandReplay(log);
            while (replaySimulation.Metrics.Tick < simulation.Metrics.Tick)
            {
                replay.ApplyForCurrentTick(replaySimulation);
                replaySimulation.Tick(1f);
            }
            var replayValid = replay.Completed &&
                              replaySimulation.ComputeStateHash() ==
                              simulation.ComputeStateHash();

            var malformed = CreateSimulation(buildings, upgrades);
            malformed.IssueBuildingUpgrade(
                PlayerId, townHall, profiles[0]);
            malformed.Production.Enqueue(
                PlayerId,
                townHall,
                peasantRecipe,
                malformed.Construction,
                malformed.Economy.Players);
            var exclusivityRejected = false;
            try
            {
                malformed.BuildingUpgrades.ValidateQueueExclusivity(
                    malformed.Production, malformed.Technology);
            }
            catch (InvalidOperationException)
            {
                exclusivityRejected = true;
            }

            var passed = catalogValid && first.Succeeded &&
                         blockedProduction.Code ==
                             ProductionCommandCode.ProducerUpgrading &&
                         blockedBuildingAbility.Code ==
                             AbilityCommandCode.CasterDisabled &&
                         hotRoundTrip && canceled && refundValid && keepValid &&
                         inheritedCanceled && inheritedResearch && castleValid &&
                         commandRoundTrip && replayValid && exclusivityRejected;
            return new SelfTestResult(
                passed,
                $"catalog={profiles.Length}/{catalogValid}, towers=" +
                $"{towerUpgrades.Length}/{towerBranchesValid}[" +
                string.Join(';', towerUpgrades.Select(value =>
                    $"{value.Id}:{value.SourceBuildingTypeId}>" +
                    $"{value.TargetType.Id}:" +
                    string.Join(',', value.Requirements.Select(requirement =>
                        $"{requirement.Kind}/{requirement.TargetId}")))) + "], " +
                $"cost={profiles[0].Cost.Minerals}/{profiles[0].Cost.VespeneGas}-" +
                $"{profiles[1].Cost.Minerals}/{profiles[1].Cost.VespeneGas}, " +
                $"blocked={blockedProduction.Code}/" +
                $"{blockedBuildingAbility.Code}, hot={hotRoundTrip}/" +
                $"{hotValidation}/{restoredHot?.BuildingUpgrades.CatalogProfiles.Length}, " +
                $"refund={refundValid}, forms={keepValid}/{castleValid}, " +
                $"inherit={inheritedCanceled}/{inheritedResearch}, " +
                $"commands={commandRoundTrip}, replay={replayValid}, " +
                $"invalid={exclusivityRejected}");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }

    private static RtsSimulation CreateSimulation(
        BuildingTypeCatalogSnapshot buildings,
        BuildingUpgradeCatalogSnapshot upgrades)
    {
        var world = new StaticWorld(new SimRect(
            Vector2.Zero, new Vector2(1_024f, 768f)));
        var simulation = new RtsSimulation(
            world, new StraightLinePathProvider(), capacity: 32);
        simulation.Economy.Players.RegisterPlayer(
            PlayerId,
            minerals: 2_000,
            vespeneGas: 2_000,
            supplyCapacity: 12,
            supplyUsed: 0);
        simulation.BuildingUpgrades.ConfigureCatalog(upgrades);

        var townHallType = buildings.Type(War3HumanContent.TownHall);
        var altarType = buildings.Type(War3HumanContent.AltarOfKings);
        var townHallBounds = Bounds(
            new Vector2(250f, 300f), townHallType.Size);
        var altarBounds = Bounds(
            new Vector2(540f, 300f), altarType.Size);
        var townHallFootprint = simulation.PlaceBuilding(townHallBounds);
        var altarFootprint = simulation.PlaceBuilding(altarBounds);
        simulation.Construction.RestoreRuntimeState(
            new ConstructionRuntimeSnapshot(
            [
                Completed(
                    0, townHallType, townHallBounds, townHallFootprint),
                Completed(
                    1, altarType, altarBounds, altarFootprint)
            ],
            new ConstructionReservationRuntimeSnapshot(1, [])));
        simulation.Economy.RegisterTownHall(
            PlayerId,
            new GameplayBuildingId(0),
            townHallBounds);
        return simulation;
    }

    private static ConstructionRuntimeEntry Completed(
        int id,
        BuildingTypeProfile type,
        SimRect bounds,
        DynamicFootprintId footprint) => new(
        new GameplayBuildingId(id),
        PlayerId,
        type,
        bounds,
        default,
        footprint,
        BuildingLifecycleState.Completed,
        -1,
        (bounds.Min + bounds.Max) * 0.5f,
        1f,
        type.MaximumHealth,
        new EconomyResourceNodeId(-1));

    private static SimRect Bounds(Vector2 center, Vector2 size) =>
        new(center - size * 0.5f, center + size * 0.5f);
}
