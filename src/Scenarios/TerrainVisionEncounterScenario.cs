using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Scenarios;

public static class TerrainVisionEncounterScenario
{
    public const int PlayerId = 1;
    public const int EnemyId = 2;
    public const int AllyId = 3;
    public static readonly SimRect Bounds = new(
        Vector2.Zero, new Vector2(960f, 560f));
    public static readonly SimRect SmokeBounds = new(
        new Vector2(100f, 360f), new Vector2(260f, 520f));

    public static TerrainVisionEncounterRuntime Prepare()
    {
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                Bounds,
                [], [], [], [],
                out var navigation,
                out var validation) || navigation is null)
        {
            throw new InvalidOperationException(validation.FirstError.ToString());
        }
        var terrain = CreateTerrain();
        var clearance = ClearanceBakeSnapshot.Build(navigation, terrain);
        var world = navigation.CreateWorld(terrain);
        var simulation = new RtsSimulation(
            world,
            new GridPathProvider(world, staticBake: clearance),
            capacity: 64,
            clearanceBake: clearance);
        foreach (var player in new[] { PlayerId, EnemyId, AllyId })
            simulation.Economy.Players.RegisterPlayer(player, 0, 0, 200);
        simulation.ConfigureAlliance(81, sharedVision: true, PlayerId, AllyId);

        var attackers = new int[6];
        var defenders = new int[6];
        for (var index = 0; index < 6; index++)
        {
            attackers[index] = simulation.AddUnit(
                new Vector2(250f + index % 3 * 30f, 125f + index / 3 * 34f),
                PlayerId,
                AttackerProfile(),
                radius: 8f,
                maxSpeed: 125f,
                acceleration: 800f,
                perception: UnitPerceptionProfileSnapshot.Standard);
            defenders[index] = simulation.AddUnit(
                new Vector2(520f + index % 3 * 30f, 125f + index / 3 * 34f),
                EnemyId,
                PassiveProfile(),
                radius: 8f,
                maxSpeed: 110f,
                acceleration: 700f,
                perception: UnitPerceptionProfileSnapshot.Standard);
        }
        simulation.Hold(attackers);

        var elevatedScout = simulation.AddUnit(
            new Vector2(900f, 510f),
            PlayerId,
            PassiveProfile(),
            radius: 10f,
            maxSpeed: 240f,
            acceleration: 1_400f,
            perception: UnitPerceptionProfileSnapshot.ElevatedObserver(285f));
        var alliedScout = simulation.AddUnit(
            new Vector2(900f, 420f),
            AllyId,
            PassiveProfile(),
            radius: 9f,
            maxSpeed: 210f,
            acceleration: 1_200f,
            perception: UnitPerceptionProfileSnapshot.Standard);
        simulation.Hold([alliedScout]);

        simulation.AddUnit(
            new Vector2(70f, 440f),
            PlayerId,
            PassiveProfile(),
            perception: UnitPerceptionProfileSnapshot.Standard);
        var smokeTarget = simulation.AddUnit(
            new Vector2(180f, 440f),
            EnemyId,
            PassiveProfile(),
            perception: UnitPerceptionProfileSnapshot.Standard);

        return new TerrainVisionEncounterRuntime(
            terrain,
            clearance,
            simulation,
            attackers,
            defenders,
            elevatedScout,
            alliedScout,
            smokeTarget);
    }

    private static TerrainMapSnapshot CreateTerrain()
    {
        TerrainSurfaceDefinition[] surfaces =
        [
            new(0, "badlands", "Low Ground"),
            new(1, "rock", "High Ground"),
            new(2, "metal", "Ramp"),
            new(3, "vision-smoke", "Obstructing Terrain")
        ];
        var low = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var high = new TerrainCell(
            1, 1, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var builder = new TerrainMapBuilder(
            Bounds, 40f, 52f, surfaces, low);
        for (var row = 0; row < builder.Rows; row++)
        {
            for (var column = 11; column < builder.Columns; column++)
                builder.SetCell(column, row, high);
        }
        var ramp = new TerrainCell(
            0, 2, TerrainPathing.Ground, TerrainCellFlags.Ramp,
            TerrainRampDirection.PositiveX);
        for (var row = 6; row <= 9; row++)
            builder.SetCell(11, row, ramp);
        builder.Paint(
            SmokeBounds,
            new TerrainCell(
                0,
                3,
                TerrainPathing.Ground,
                TerrainCellFlags.Buildable | TerrainCellFlags.BlocksVision));
        return builder.Build();
    }

    private static CombatProfileSnapshot PassiveProfile() => new(
        MaximumHealth: 1_000f,
        AttackDamage: 0f,
        AttackRange: 0f,
        AcquisitionRange: 0f,
        AttackCooldownSeconds: 1f,
        AttackWindupSeconds: 0f,
        LeashDistance: 0f);

    private static CombatProfileSnapshot AttackerProfile() => new(
        MaximumHealth: 180f,
        AttackDamage: 18f,
        AttackRange: 275f,
        AcquisitionRange: 285f,
        AttackCooldownSeconds: 0.82f,
        AttackWindupSeconds: 0.08f,
        LeashDistance: 360f,
        ProjectileSpeed: 75f);
}

public sealed record TerrainVisionEncounterRuntime(
    TerrainMapSnapshot Terrain,
    ClearanceBakeSnapshot Clearance,
    RtsSimulation Simulation,
    int[] Attackers,
    int[] Defenders,
    int ElevatedScout,
    int AlliedScout,
    int SmokeTarget);
