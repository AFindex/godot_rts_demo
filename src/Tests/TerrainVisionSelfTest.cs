using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class TerrainVisionSelfTest
{
    private const float Delta = 1f / 60f;

    public static SelfTestResult Run()
    {
        var elevated = VerifyElevatedSpotterAndProjectilePersistence();
        Console.WriteLine($"RTS_TERRAIN_VISION_CASE elevated {elevated}");
        var ramp = VerifyRampTransition();
        Console.WriteLine($"RTS_TERRAIN_VISION_CASE ramp {ramp}");
        var shared = VerifySharedVisionDetectionComposition();
        Console.WriteLine($"RTS_TERRAIN_VISION_CASE shared {shared}");
        var smoke = VerifyObstructingTerrain();
        Console.WriteLine($"RTS_TERRAIN_VISION_CASE smoke {smoke}");
        var retreat = VerifyRetreatToHighGroundBreaksCombat();
        Console.WriteLine($"RTS_TERRAIN_VISION_CASE retreat {retreat}");
        var passed = elevated.Passed && ramp.Passed && shared.Passed &&
                     smoke.Passed && retreat.Passed;
        return new SelfTestResult(
            passed,
            $"elevated={elevated.Summary}, ramp={ramp.Summary}, " +
            $"shared={shared.Summary}, smoke={smoke.Summary}, " +
            $"retreat={retreat.Summary}");
    }

    private static CaseResult VerifyElevatedSpotterAndProjectilePersistence()
    {
        var fixture = CreateFixture();
        RegisterPlayers(fixture.Simulation, 1, 2);
        var attacker = AddCombat(
            fixture.Simulation, new Vector2(300f, 120f), 1,
            RangedAttacker(projectileSpeed: 42f));
        var target = AddCombat(
            fixture.Simulation, new Vector2(480f, 120f), 2, PassiveTarget());
        var observer = fixture.Simulation.AddUnit(
            new Vector2(60f, 430f),
            team: 1,
            combatProfile: PassiveTarget(),
            radius: 7.5f,
            maxSpeed: 220f,
            acceleration: 1_200f,
            perception: UnitPerceptionProfileSnapshot.ElevatedObserver(245f));

        Tick(fixture.Simulation, 4);
        var initiallyHidden = !IsVisible(fixture.Simulation, 1, target);
        var rejected = fixture.Simulation.IssuePlayerSmartCommand(
            1,
            [attacker],
            new SmartCommandTarget(
                SmartCommandTargetKind.EnemyUnit,
                fixture.Simulation.Units.Positions[target],
                target),
            attackMoveModifier: false).Code ==
            PlayerOrderCommandCode.TargetNotVisible;

        fixture.Simulation.IssueMove([observer], new Vector2(300f, 185f));
        var spotted = TickUntil(fixture.Simulation, 360, () =>
            IsVisible(fixture.Simulation, 1, target));
        var accepted = fixture.Simulation.IssuePlayerSmartCommand(
            1,
            [attacker],
            new SmartCommandTarget(
                SmartCommandTargetKind.EnemyUnit,
                fixture.Simulation.Units.Positions[target],
                target),
            attackMoveModifier: false).Succeeded;
        var launched = TickUntil(fixture.Simulation, 180, () =>
            fixture.Simulation.CombatProjectiles.ActiveCount > 0);
        var healthBeforeFlight = fixture.Simulation.Combat.Health[target];
        fixture.Simulation.IssueMove([observer], new Vector2(60f, 430f));
        var lostWhileFlying = TickUntil(fixture.Simulation, 360, () =>
            fixture.Simulation.CombatProjectiles.ActiveCount > 0 &&
            !IsVisible(fixture.Simulation, 1, target));
        var impacted = TickUntil(fixture.Simulation, 360, () =>
            fixture.Simulation.CombatProjectiles.ActiveCount == 0 &&
            fixture.Simulation.Combat.Health[target] < healthBeforeFlight);

        var passed = initiallyHidden && rejected && spotted && accepted &&
                     launched && lostWhileFlying && impacted;
        return new CaseResult(
            passed,
            $"hidden={initiallyHidden}, reject={rejected}, spot={spotted}, " +
            $"launch={launched}, lostFlying={lostWhileFlying}, hit={impacted}");
    }

    private static CaseResult VerifyRampTransition()
    {
        var fixture = CreateFixture();
        RegisterPlayers(fixture.Simulation, 1, 2);
        var scout = fixture.Simulation.AddUnit(
            new Vector2(320f, 260f),
            team: 1,
            combatProfile: PassiveTarget(),
            radius: 7.5f,
            maxSpeed: 105f,
            acceleration: 800f,
            perception: new UnitPerceptionProfileSnapshot(
                UnitConcealmentKind.None,
                0f,
                180f,
                PlayerVisibilitySystem.DefaultGroundObservationHeight,
                TerrainVisionMode.Ground));
        var target = AddCombat(
            fixture.Simulation, new Vector2(520f, 260f), 2, PassiveTarget());
        Tick(fixture.Simulation, 4);
        var hiddenAtBottom = !IsVisible(fixture.Simulation, 1, target);
        fixture.Simulation.IssueMove([scout], new Vector2(480f, 260f));
        var firstVisiblePosition = Vector2.Zero;
        var becameVisible = TickUntil(fixture.Simulation, 300, () =>
        {
            if (!IsVisible(fixture.Simulation, 1, target)) return false;
            firstVisiblePosition = fixture.Simulation.Units.Positions[scout];
            return true;
        });
        var firstHeight = fixture.Terrain.HeightAt(firstVisiblePosition);
        var transitionedOnUpperRamp = becameVisible &&
            firstVisiblePosition.X >= 400f && firstVisiblePosition.X <= 445f &&
            firstHeight >= fixture.Terrain.CliffLevelHeight * 0.70f;
        return new CaseResult(
            hiddenAtBottom && transitionedOnUpperRamp,
            $"bottomHidden={hiddenAtBottom}, visible={becameVisible}, " +
            $"first={firstVisiblePosition.X:F1}/{firstHeight:F1}");
    }

    private static CaseResult VerifySharedVisionDetectionComposition()
    {
        var fixture = CreateFixture();
        RegisterPlayers(fixture.Simulation, 1, 2, 3);
        fixture.Simulation.ConfigureAlliance(73, sharedVision: true, 1, 3);
        var detector = fixture.Simulation.AddUnit(
            new Vector2(300f, 120f),
            team: 1,
            combatProfile: PassiveTarget(),
            radius: 7.5f,
            maxSpeed: 220f,
            acceleration: 1_200f,
            perception: new UnitPerceptionProfileSnapshot(
                UnitConcealmentKind.None,
                DetectionRange: 230f,
                VisionRange: 190f));
        var allyScout = fixture.Simulation.AddUnit(
            new Vector2(740f, 420f),
            team: 3,
            combatProfile: PassiveTarget(),
            radius: 7.5f,
            maxSpeed: 220f,
            acceleration: 1_200f,
            perception: UnitPerceptionProfileSnapshot.Standard);
        var concealed = fixture.Simulation.AddUnit(
            new Vector2(480f, 120f),
            team: 2,
            combatProfile: PassiveTarget(),
            radius: 7.5f,
            maxSpeed: 128f,
            acceleration: 720f,
            perception: new UnitPerceptionProfileSnapshot(
                UnitConcealmentKind.Cloaked,
                DetectionRange: 0f,
                VisionRange: 200f));
        Tick(fixture.Simulation, 4);
        var targetPosition = fixture.Simulation.Units.Positions[concealed];
        var detectionAloneNotSight =
            fixture.Simulation.Visibility.IsDetected(1, targetPosition) &&
            !IsVisible(fixture.Simulation, 1, concealed);
        fixture.Simulation.IssueMove([allyScout], new Vector2(500f, 180f));
        var sharedPlusDetection = TickUntil(fixture.Simulation, 360, () =>
            IsVisible(fixture.Simulation, 1, concealed));
        fixture.Simulation.IssueMove([detector], new Vector2(60f, 430f));
        var sharedWithoutDetectionHidden = TickUntil(fixture.Simulation, 360, () =>
            !fixture.Simulation.Visibility.IsDetected(1, targetPosition) &&
            !IsVisible(fixture.Simulation, 1, concealed));
        return new CaseResult(
            detectionAloneNotSight && sharedPlusDetection &&
            sharedWithoutDetectionHidden,
            $"detectOnly={detectionAloneNotSight}, composed={sharedPlusDetection}, " +
            $"noDetect={sharedWithoutDetectionHidden}");
    }

    private static CaseResult VerifyObstructingTerrain()
    {
        var outside = CreateSmokeVisibilityFixture(
            new Vector2(80f, 360f), TerrainVisionMode.Ground,
            new Vector2(180f, 360f));
        var inside = CreateSmokeVisibilityFixture(
            new Vector2(180f, 400f), TerrainVisionMode.Ground,
            new Vector2(180f, 360f));
        var elevated = CreateSmokeVisibilityFixture(
            new Vector2(80f, 360f), TerrainVisionMode.Elevated,
            new Vector2(180f, 360f));
        var behind = CreateSmokeVisibilityFixture(
            new Vector2(80f, 360f), TerrainVisionMode.Ground,
            new Vector2(300f, 360f));
        var passed = !outside && inside && elevated && !behind;
        return new CaseResult(
            passed,
            $"outside={outside}, inside={inside}, elevated={elevated}, " +
            $"behind={behind}");
    }

    private static CaseResult VerifyRetreatToHighGroundBreaksCombat()
    {
        var fixture = CreateFixture();
        RegisterPlayers(fixture.Simulation, 1, 2);
        var attacker = AddCombat(
            fixture.Simulation, new Vector2(300f, 260f), 1,
            RangedAttacker(projectileSpeed: 0f) with
            {
                AttackRange = 250f,
                AcquisitionRange = 250f,
                LeashDistance = 320f
            });
        var target = AddCombat(
            fixture.Simulation, new Vector2(340f, 260f), 2, PassiveTarget());
        Tick(fixture.Simulation, 4);
        fixture.Simulation.Hold([attacker]);
        var damagedLow = TickUntil(fixture.Simulation, 180, () =>
            fixture.Simulation.Combat.Health[target] < 1_000f);
        fixture.Simulation.IssueMove([target], new Vector2(500f, 260f));
        var lost = TickUntil(fixture.Simulation, 360, () =>
            !IsVisible(fixture.Simulation, 1, target));
        var healthAtLoss = fixture.Simulation.Combat.Health[target];
        Tick(fixture.Simulation, 150);
        var stopped = MathF.Abs(
            fixture.Simulation.Combat.Health[target] - healthAtLoss) < 0.001f;
        var disengaged = fixture.Simulation.Combat.TargetUnits[attacker] < 0;
        return new CaseResult(
            damagedLow && lost && stopped && disengaged,
            $"damaged={damagedLow}, lost={lost}, stopped={stopped}, " +
            $"disengaged={disengaged}, hp={healthAtLoss:F0}");
    }

    private static bool CreateSmokeVisibilityFixture(
        Vector2 observerPosition,
        TerrainVisionMode mode,
        Vector2 targetPosition)
    {
        var fixture = CreateFixture();
        RegisterPlayers(fixture.Simulation, 1, 2);
        fixture.Simulation.AddUnit(
            observerPosition,
            team: 1,
            combatProfile: PassiveTarget(),
            perception: new UnitPerceptionProfileSnapshot(
                UnitConcealmentKind.None,
                0f,
                260f,
                PlayerVisibilitySystem.DefaultGroundObservationHeight,
                mode));
        var target = AddCombat(
            fixture.Simulation, targetPosition, 2, PassiveTarget());
        Tick(fixture.Simulation, 4);
        return IsVisible(fixture.Simulation, 1, target);
    }

    private static Fixture CreateFixture()
    {
        var bounds = new SimRect(Vector2.Zero, new Vector2(800f, 480f));
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                bounds,
                [], [], [], [],
                out var navigation,
                out var validation) || navigation is null)
        {
            throw new InvalidOperationException(validation.FirstError.ToString());
        }
        TerrainSurfaceDefinition[] surfaces =
        [
            new(0, "badlands", "Low"),
            new(1, "rock", "High"),
            new(2, "metal", "Ramp"),
            new(3, "vision-smoke", "Smoke")
        ];
        var low = new TerrainCell(
            0, 0, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var high = new TerrainCell(
            1, 1, TerrainPathing.Ground, TerrainCellFlags.Buildable);
        var builder = new TerrainMapBuilder(
            bounds, 40f, 52f, surfaces, low);
        for (var row = 0; row < builder.Rows; row++)
        {
            for (var column = 10; column < builder.Columns; column++)
                builder.SetCell(column, row, high);
        }
        var ramp = new TerrainCell(
            0, 2, TerrainPathing.Ground, TerrainCellFlags.Ramp,
            TerrainRampDirection.PositiveX);
        for (var row = 5; row <= 7; row++)
            builder.SetCell(10, row, ramp);
        var smoke = new TerrainCell(
            0,
            3,
            TerrainPathing.Ground,
            TerrainCellFlags.Buildable | TerrainCellFlags.BlocksVision);
        builder.Paint(
            new SimRect(new Vector2(120f, 320f), new Vector2(240f, 440f)),
            smoke);
        var terrain = builder.Build();
        var world = navigation.CreateWorld(terrain);
        var simulation = new RtsSimulation(
            world,
            new GridPathProvider(world),
            capacity: 32);
        return new Fixture(terrain, simulation);
    }

    private static void RegisterPlayers(RtsSimulation simulation, params int[] players)
    {
        foreach (var player in players)
            simulation.Economy.Players.RegisterPlayer(player, 0, 0, 200);
    }

    private static int AddCombat(
        RtsSimulation simulation,
        Vector2 position,
        int team,
        CombatProfileSnapshot profile) => simulation.AddUnit(
        position,
        team,
        profile,
        radius: 7.5f,
        maxSpeed: 128f,
        acceleration: 720f,
        perception: UnitPerceptionProfileSnapshot.Standard);

    private static CombatProfileSnapshot PassiveTarget() => new(
        MaximumHealth: 1_000f,
        AttackDamage: 0f,
        AttackRange: 0f,
        AcquisitionRange: 0f,
        AttackCooldownSeconds: 1f,
        AttackWindupSeconds: 0f,
        LeashDistance: 0f);

    private static CombatProfileSnapshot RangedAttacker(float projectileSpeed) =>
        new(
            MaximumHealth: 200f,
            AttackDamage: 25f,
            AttackRange: 210f,
            AcquisitionRange: 220f,
            AttackCooldownSeconds: 0.7f,
            AttackWindupSeconds: 0f,
            LeashDistance: 320f,
            ProjectileSpeed: projectileSpeed);

    private static bool IsVisible(RtsSimulation simulation, int player, int unit) =>
        simulation.Visibility.IsUnitVisible(
            player, unit, simulation.Units, simulation.Combat);

    private static bool TickUntil(
        RtsSimulation simulation,
        int maximumTicks,
        Func<bool> condition)
    {
        for (var tick = 0; tick < maximumTicks; tick++)
        {
            simulation.Tick(Delta);
            if (condition()) return true;
        }
        return false;
    }

    private static void Tick(RtsSimulation simulation, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
            simulation.Tick(Delta);
    }

    private sealed record Fixture(
        TerrainMapSnapshot Terrain,
        RtsSimulation Simulation);

    private readonly record struct CaseResult(bool Passed, string Summary);
}
