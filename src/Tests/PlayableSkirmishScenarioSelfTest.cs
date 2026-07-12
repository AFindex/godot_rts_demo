using RtsDemo.Scenarios;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class PlayableSkirmishScenarioSelfTest
{
    public static SelfTestResult Run(
        BuildingTypeCatalogSnapshot? buildings = null,
        ProductionCatalogSnapshot? production = null,
        TechnologyCatalogSnapshot? technologies = null)
    {
        buildings ??= DemoBuildingTypes.CreateCatalog();
        production ??= DemoProductionCatalog.CreateSnapshot();
        technologies ??= DemoTechnologies.CreateCatalog();
        var navigation = PlayableSkirmishScenario.CreateNavigationSnapshot();
        var world = navigation.CreateWorld();
        var bake = ClearanceBakeSnapshot.Build(navigation);
        var simulation = new RtsSimulation(
            world,
            new GridPathProvider(world, staticBake: bake),
            PlayableSkirmishScenario.SimulationCapacity,
            navigation.CreateRoutePlanner(world),
            navigation.CreateChokeController(),
            bake);
        var runtime = PlayableSkirmishScenario.Prepare(
            simulation, buildings, production, technologies);
        for (var tick = 0; tick < 1_800; tick++)
        {
            runtime.AiDirector.Update(simulation.Metrics.Tick);
            simulation.Tick(1f / 60f);
        }

        var match = simulation.Match.CreateSnapshot(
            simulation.Construction, simulation.Economy,
            simulation.Units, simulation.Combat);
        var player = match.Players.Single(value =>
            value.PlayerId == PlayableSkirmishScenario.PlayerId);
        var enemy = match.Players.Single(value =>
            value.PlayerId == PlayableSkirmishScenario.EnemyId);
        var playerBank = simulation.Economy.Players.Snapshot(
            PlayableSkirmishScenario.PlayerId);
        var enemyFacilities = simulation.Construction.CreateOverview()
            .Count(value => !value.IsTerminal &&
                value.PlayerId == PlayableSkirmishScenario.EnemyId);
        var resourceCount = runtime.ResourceNodes.Length;
        var passed = navigation.WorldBounds.Width >= 3_000f &&
                     navigation.WorldBounds.Height >= 1_600f &&
                     resourceCount >= 60 &&
                     runtime.PlayerWorkers.Length == 12 &&
                     runtime.EnemyWorkers.Length == 12 &&
                     player.TownHalls >= 1 && enemy.TownHalls >= 1 &&
                     playerBank.Minerals > 1_800 &&
                     enemyFacilities >= 3 &&
                     match.Phase == MatchPhase.Running;
        return new SelfTestResult(
            passed,
            $"map={navigation.WorldBounds.Width:0}x" +
            $"{navigation.WorldBounds.Height:0}, resources={resourceCount}, " +
            $"workers={runtime.PlayerWorkers.Length}/" +
            $"{runtime.EnemyWorkers.Length}, bank={playerBank.Minerals}/" +
            $"{playerBank.VespeneGas}, enemyFacilities={enemyFacilities}, " +
            $"match={match.Phase}");
    }
}
