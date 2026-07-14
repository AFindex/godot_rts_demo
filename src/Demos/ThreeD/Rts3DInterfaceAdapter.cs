using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Anti-corruption layer between the simulation and the 3D presentation. It
/// reads authoritative state and emits immutable operation-layer snapshots;
/// it never owns selection and never issues a gameplay command.
/// </summary>
public sealed class Rts3DInterfaceAdapter
{
    private readonly int _playerId;
    private readonly BuildingTypeCatalogSnapshot _buildings;
    private readonly ProductionCatalogSnapshot _production;
    private readonly TechnologyCatalogSnapshot _technologies;

    public Rts3DInterfaceAdapter(
        int playerId,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies)
    {
        if (playerId < 0) throw new ArgumentOutOfRangeException(nameof(playerId));
        _playerId = playerId;
        _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
        _production = production ?? throw new ArgumentNullException(nameof(production));
        _technologies = technologies ?? throw new ArgumentNullException(nameof(technologies));
    }

    public GameplaySelectionSnapshot CreateSelection(
        RtsSimulation simulation,
        IEnumerable<int> unitIds,
        IEnumerable<int> buildingIds,
        SelectionSubgroupKey? preferred = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(unitIds);
        ArgumentNullException.ThrowIfNull(buildingIds);
        var entities = new List<GameplaySelectionEntity>();
        foreach (var unit in unitIds.Distinct().Order())
        {
            if (!IsOwnedUnitAvailable(simulation, unit)) continue;
            var type = UnitType(simulation, unit);
            entities.Add(new GameplaySelectionEntity(
                type.IsWorker
                    ? GameplaySelectionKind.Worker
                    : GameplaySelectionKind.CombatUnit,
                unit, type.Id, type.Name, simulation.Units.Positions[unit]));
        }
        foreach (var value in buildingIds.Distinct().Order())
        {
            var id = new GameplayBuildingId(value);
            if (!simulation.Construction.IsAlive(id)) continue;
            var building = simulation.Construction.Observe(id);
            if (building.PlayerId != _playerId) continue;
            entities.Add(new GameplaySelectionEntity(
                GameplaySelectionKind.Building,
                value,
                building.Type.Id,
                building.Type.Name,
                (building.Bounds.Min + building.Bounds.Max) * 0.5f));
        }
        return GameplaySelectionSnapshot.Create(entities, preferred);
    }

    public Rts3DHudSnapshot CreateHudSnapshot(
        RtsSimulation simulation,
        GameplaySelectionSnapshot selection,
        Rts3DControlGroupSnapshot[] controlGroups,
        double elapsedSeconds,
        string mode,
        string status)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(controlGroups);
        var economy = simulation.Economy.Players.Snapshot(_playerId);
        var card = CreateCommandCard(simulation, selection);
        return new Rts3DHudSnapshot(
            elapsedSeconds,
            economy.Minerals,
            economy.VespeneGas,
            economy.SupplyUsed,
            economy.SupplyCapacity,
            CountIdleWorkers(simulation),
            CreateSelectionPanel(simulation, selection),
            Rts3DCommandLayout.Compose(card),
            controlGroups,
            mode,
            status);
    }

    public Rts3DRallyMarkerSnapshot[] CreateRallyMarkers(
        RtsSimulation simulation,
        IEnumerable<int> selectedBuildings)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(selectedBuildings);
        var result = new List<Rts3DRallyMarkerSnapshot>();
        foreach (var value in selectedBuildings.Distinct().Order())
        {
            var id = new GameplayBuildingId(value);
            if (!simulation.Construction.IsAlive(id)) continue;
            var building = simulation.Construction.Observe(id);
            if (building.PlayerId != _playerId) continue;
            var rally = simulation.Production.Observe(id).Rally;
            if (!rally.IsSet) continue;
            var target = ResolveLiveRallyPosition(simulation, rally);
            result.Add(new Rts3DRallyMarkerSnapshot(
                value,
                Rts3DRallyGeometry.EdgeToward(building.Bounds, target),
                target,
                rally.Kind));
        }
        return result.ToArray();
    }

    public int UnitTypeId(RtsSimulation simulation, int unit) =>
        UnitType(simulation, unit).Id;

    public bool IsOwnedUnitAvailable(RtsSimulation simulation, int unit) =>
        (uint)unit < (uint)simulation.Units.Count &&
        simulation.Units.Alive[unit] &&
        simulation.Combat.Teams[unit] == _playerId;

    public bool IsOwnedBuildingAvailable(
        RtsSimulation simulation,
        int buildingId)
    {
        var id = new GameplayBuildingId(buildingId);
        return simulation.Construction.IsAlive(id) &&
               simulation.Construction.Observe(id).PlayerId == _playerId;
    }

    public bool SupportsRally(RtsSimulation simulation, int buildingId)
    {
        if (!IsOwnedBuildingAvailable(simulation, buildingId)) return false;
        var building = simulation.Construction.Observe(
            new GameplayBuildingId(buildingId));
        return building.State == BuildingLifecycleState.Completed &&
               building.Type.Function is BuildingFunctionKind.TownHall or
                   BuildingFunctionKind.Production;
    }

    private CommandCardSnapshot CreateCommandCard(
        RtsSimulation simulation,
        GameplaySelectionSnapshot selection)
    {
        var active = selection.ActiveSubgroup;
        if (active is null) return CommandCardSnapshot.Empty;
        var candidates = new List<CommandCardActionCandidate>();
        if (active.Key.Kind is GameplaySelectionKind.Worker or
            GameplaySelectionKind.CombatUnit)
        {
            AddUnitActions(simulation, active, candidates);
        }
        else
        {
            AddBuildingActions(simulation, active, candidates);
        }
        return CommandCardComposer.Compose(selection, candidates);
    }

    private void AddUnitActions(
        RtsSimulation simulation,
        SelectionSubgroupSnapshot active,
        List<CommandCardActionCandidate> result)
    {
        result.Add(new(active.Key, CommandCardActionKind.Move, -1, -1,
            "Move", true, "Move without engaging", 0));
        result.Add(new(active.Key, CommandCardActionKind.AttackMove, -1, -1,
            "Attack Move", true, "Engage enemies on the route", 1));
        result.Add(new(active.Key, CommandCardActionKind.Stop, -1, -1,
            "Stop", true, "Cancel current orders", 2));
        result.Add(new(active.Key, CommandCardActionKind.Hold, -1, -1,
            "Hold Position", true, "Attack in range without chasing", 3));
        if (active.Key.Kind != GameplaySelectionKind.Worker) return;
        var carriesCargo = active.Members.Any(value =>
            simulation.Economy.Worker(value.EntityId).CargoAmount > 0);
        result.Add(new(active.Key, CommandCardActionKind.ReturnCargo, -1, -1,
            "Return Cargo", carriesCargo,
            carriesCargo ? "Return carried resources" : "No carried resources", 4));
        foreach (var profile in _buildings.Types.ToArray())
        {
            var spend = simulation.Economy.Players.ValidateSpend(
                _playerId, profile.Cost);
            result.Add(new CommandCardActionCandidate(
                active.Key,
                CommandCardActionKind.Build,
                -1,
                profile.Id,
                profile.Name,
                spend.Succeeded,
                $"{profile.Cost.Minerals}M {profile.Cost.VespeneGas}G · " +
                (spend.Succeeded ? "Ready" : spend.Code.ToString()),
                20 + profile.Id));
        }
    }

    private void AddBuildingActions(
        RtsSimulation simulation,
        SelectionSubgroupSnapshot active,
        List<CommandCardActionCandidate> result)
    {
        var ids = active.Members
            .Select(value => new GameplayBuildingId(value.EntityId))
            .Where(simulation.Construction.IsAlive)
            .ToArray();
        if (ids.Length == 0) return;
        var profile = simulation.Construction.Observe(ids[0]).Type;
        var completed = ids.Where(id => simulation.Construction.Observe(id).State ==
            BuildingLifecycleState.Completed).ToArray();
        if (profile.Function is BuildingFunctionKind.TownHall or
            BuildingFunctionKind.Production)
        {
            result.Add(new(active.Key, CommandCardActionKind.Rally,
                ids[0].Value, -1, "Set Rally", completed.Length > 0,
                completed.Length > 0 ? "Ground, resource or friendly unit" :
                "Building is not complete", 0));
        }
        foreach (var recipe in _production.Recipes.ToArray()
                     .Where(value => value.ProducerBuildingTypeId == profile.Id))
        {
            var availability = completed.Select(id =>
                    simulation.Production.ObserveAvailability(
                        _playerId, id, recipe, simulation.Construction,
                        simulation.Economy.Players))
                .ToArray();
            var ready = availability.Any(value => value.Available);
            var code = availability.Length > 0
                ? availability[0].Code
                : ProductionCommandCode.ProducerNotCompleted;
            result.Add(new(active.Key, CommandCardActionKind.Train,
                ids[0].Value, recipe.Id, recipe.UnitType.Name, ready,
                $"{recipe.Cost.Minerals}M {recipe.Cost.VespeneGas}G " +
                $"{recipe.Cost.Supply}S · {(ready ? "Ready" : code)}",
                10 + recipe.Id));
        }
        foreach (var technology in _technologies.Technologies.ToArray()
                     .Where(value => value.ResearcherBuildingTypeId == profile.Id))
        {
            var availability = completed.Select(id =>
                    simulation.Technology.ObserveAvailability(
                        _playerId, id, technology, simulation.Construction,
                        simulation.Economy.Players))
                .ToArray();
            var ready = availability.Any(value => value.Available);
            var code = availability.Length > 0
                ? availability[0].Code
                : ResearchCommandCode.ResearcherNotCompleted;
            result.Add(new(active.Key, CommandCardActionKind.Research,
                ids[0].Value, technology.Id, technology.Name, ready,
                $"{technology.Cost.Minerals}M {technology.Cost.VespeneGas}G · " +
                (ready ? "Ready" : code.ToString()),
                20 + technology.Id));
        }
    }

    private Rts3DSelectionPanelSnapshot CreateSelectionPanel(
        RtsSimulation simulation,
        GameplaySelectionSnapshot selection)
    {
        var active = selection.ActiveSubgroup;
        if (active is null) return Rts3DSelectionPanelSnapshot.Empty;
        var health = 0f;
        var maximumHealth = 0f;
        if (active.Key.Kind == GameplaySelectionKind.Building)
        {
            foreach (var member in active.Members)
            {
                var id = new GameplayBuildingId(member.EntityId);
                if (!simulation.Construction.IsAlive(id)) continue;
                var building = simulation.Construction.Observe(id);
                health += building.Health;
                maximumHealth += building.MaximumHealth;
            }
        }
        else
        {
            foreach (var member in active.Members)
            {
                health += simulation.Combat.Health[member.EntityId];
                maximumHealth += simulation.Combat.MaximumHealth[member.EntityId];
            }
        }
        var subtitle = selection.Entities.Length == 1
            ? active.Key.Kind.ToString()
            : $"{selection.Entities.Length} selected · subgroup " +
              $"{selection.ActiveSubgroupIndex + 1}/{selection.Subgroups.Length}";
        return new Rts3DSelectionPanelSnapshot(
            active.Members.Length == 1
                ? active.Name
                : $"{active.Name} ×{active.Members.Length}",
            subtitle,
            health,
            maximumHealth,
            selection.Subgroups,
            selection.ActiveSubgroupIndex,
            active.Key.Kind == GameplaySelectionKind.Building
                ? RallyLabel(simulation, active.Members)
                : string.Empty,
            active.Key.Kind == GameplaySelectionKind.Building
                ? QueueItems(simulation, active.Members)
                : []);
    }

    private string RallyLabel(
        RtsSimulation simulation,
        IReadOnlyList<GameplaySelectionEntity> buildings)
    {
        var rallies = buildings.Select(value => simulation.Production.Observe(
                new GameplayBuildingId(value.EntityId)).Rally)
            .ToArray();
        if (rallies.Length == 0 || rallies.All(value => !value.IsSet))
            return "Rally: not set";
        var first = rallies[0];
        if (rallies.Any(value => value != first)) return "Rally: mixed targets";
        return first.Kind switch
        {
            RallyTargetKind.ResourceNode =>
                $"Rally: {simulation.Economy.ObserveResourceNode(first.ResourceNode).Kind}",
            RallyTargetKind.FriendlyUnit => $"Rally: follow unit #{first.Unit}",
            RallyTargetKind.Ground =>
                $"Rally: ground {first.Position.X:0}, {first.Position.Y:0}",
            _ => "Rally: not set"
        };
    }

    private static Rts3DQueueItemSnapshot[] QueueItems(
        RtsSimulation simulation,
        IReadOnlyList<GameplaySelectionEntity> buildings)
    {
        var entries = new List<Rts3DQueueItemSnapshot>();
        foreach (var member in buildings)
        {
            var id = new GameplayBuildingId(member.EntityId);
            foreach (var order in simulation.Production.Observe(id).Orders)
            {
                entries.Add(new Rts3DQueueItemSnapshot(
                    order.Recipe.UnitType.Name,
                    order.Progress,
                    1,
                    order.State.ToString()));
            }
            foreach (var order in simulation.Technology.Observe(id).Orders)
            {
                entries.Add(new Rts3DQueueItemSnapshot(
                    order.Technology.Name,
                    order.Progress,
                    1,
                    "Research"));
            }
        }
        return entries
            .GroupBy(value => (value.Label, value.State))
            .Select(group => new Rts3DQueueItemSnapshot(
                group.Key.Label,
                group.Average(value => value.Progress),
                group.Count(),
                group.Key.State))
            .Take(6)
            .ToArray();
    }

    private int CountIdleWorkers(RtsSimulation simulation)
    {
        var count = 0;
        for (var unit = 0; unit < simulation.Units.Count; unit++)
        {
            if (simulation.Units.Alive[unit] &&
                simulation.Economy.IsWorkerOwnedBy(unit, _playerId) &&
                simulation.Economy.Worker(unit).State == WorkerEconomyState.Idle)
                count++;
        }
        return count;
    }

    private UnitTypeProfile UnitType(RtsSimulation simulation, int unit)
    {
        if ((uint)unit >= (uint)simulation.Units.Count)
            throw new ArgumentOutOfRangeException(nameof(unit));
        var worker = simulation.Economy.IsWorker(unit);
        var radius = simulation.Units.Radii[unit];
        var health = simulation.Combat.MaximumHealth[unit];
        return _production.UnitTypes.ToArray()
            .Where(value => value.IsWorker == worker)
            .OrderBy(value => MathF.Abs(value.Movement.PhysicalRadius - radius) * 10f +
                              MathF.Abs(value.Combat.MaximumHealth - health))
            .ThenBy(value => value.Id)
            .First();
    }

    private static NVector2 ResolveLiveRallyPosition(
        RtsSimulation simulation,
        RallyTarget rally)
    {
        if (rally.Kind == RallyTargetKind.FriendlyUnit &&
            (uint)rally.Unit < (uint)simulation.Units.Count &&
            simulation.Units.Alive[rally.Unit])
            return simulation.Units.Positions[rally.Unit];
        return rally.Position;
    }

}
