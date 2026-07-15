using RtsDemo.Scenarios;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class TerrainTestPresetCatalogSelfTest
{
    public static SelfTestResult Run()
    {
        try
        {
            var presets = TerrainTestPresetCatalog.Presets.ToArray();
            var idsStable = presets.Length == 12 &&
                            presets.Select(value => value.Id).Distinct().Count() ==
                            presets.Length &&
                            presets.All(value => value.Id.All(character =>
                                char.IsLower(character) || char.IsDigit(character) ||
                                character == '-'));
            var hashesUnique = presets.Select(value => value.Terrain.StableHash)
                                   .Distinct().Count() == presets.Length;
            var roundTrips = 0;
            var anchors = 0;
            var topologyMatches = 0;
            var connectivityMatches = 0;
            var placementMatches = 0;
            var routeMatches = 0;
            var details = new List<string>(presets.Length);
            foreach (var preset in presets)
            {
                var bytes = preset.Terrain.CanonicalBytes.Span;
                if (TerrainMapSnapshot.TryDeserialize(
                        bytes,
                        out var roundTrip,
                        out _) &&
                    roundTrip is not null &&
                    roundTrip.StableHash == preset.Terrain.StableHash)
                {
                    roundTrips++;
                }

                if (preset.Terrain.IsDiscTraversable(preset.Start, 8f) &&
                    preset.Terrain.IsDiscTraversable(preset.Goal, 8f))
                {
                    anchors++;
                }
                if (preset.Terrain.IsAreaBuildable(preset.BuildingProbe) ==
                    preset.BuildingProbeBuildable)
                {
                    placementMatches++;
                }

                var navigation = EmptyNavigation(preset.Terrain.Bounds);
                var clearance = ClearanceBakeSnapshot.Build(
                    navigation, preset.Terrain, cellSize: 8f);
                var topology = TerrainNavigationTopologyBuilder.Build(
                    preset.Terrain, clearance);
                if (topology.Ramps.Length == preset.ExpectedRampCount)
                    topologyMatches++;
                var layer = clearance.CreateConnectivitySnapshot(
                    MovementClass.Medium);
                var connected = SameComponent(
                    layer, preset.Start, preset.Goal);
                if (connected == preset.StartGoalConnected)
                    connectivityMatches++;
                var route = topology.CreateRoutePlanner().Plan(
                    preset.Start,
                    preset.Goal,
                    MovementClearance.MediumNavigationRadius);
                if (route.ChokeIds.Length ==
                    preset.ExpectedMediumRouteChokes)
                {
                    routeMatches++;
                }
                details.Add(
                    $"{preset.Id}:{topology.Ramps.Length}/" +
                    $"{route.ChokeIds.Length}/" +
                    $"{(connected ? 'C' : 'X')}/" +
                    $"r{topology.Layer(MovementClass.Medium).RegionCount}/" +
                    $"m{string.Join('-', topology.Ramps.ToArray().Select(value => (byte)value.MovementMask))}");
            }

            var dynamicCases = VerifyDynamicBlockers();
            var sizedRoutes = VerifySizedRampChoice();
            var passed = idsStable && hashesUnique &&
                         roundTrips == presets.Length &&
                         anchors == presets.Length &&
                         topologyMatches == presets.Length &&
                         connectivityMatches == presets.Length &&
                         placementMatches == presets.Length &&
                         routeMatches == presets.Length && dynamicCases &&
                         sizedRoutes;
            return new SelfTestResult(
                passed,
                $"presets={presets.Length}, ids={idsStable}, " +
                $"hashes={hashesUnique}, roundTrip={roundTrips}, " +
                $"anchors={anchors}, topology={topologyMatches}, " +
                $"connectivity={connectivityMatches}, " +
                $"placement={placementMatches}, routes={routeMatches}, " +
                $"dynamic={dynamicCases}, sized={sizedRoutes}, " +
                $"maps=[{string.Join(',', details)}]");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(
                false,
                $"exception={exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool VerifyDynamicBlockers()
    {
        var bypass = TerrainTestPresetCatalog.Get("parallel-ramp-bypass");
        var causeway = TerrainTestPresetCatalog.Get("island-causeway");
        return ConnectivityAfterBlocker(bypass) &&
               !ConnectivityAfterBlocker(causeway);
    }

    private static bool VerifySizedRampChoice()
    {
        var preset = TerrainTestPresetCatalog.Get("narrow-wide-choice");
        var navigation = EmptyNavigation(preset.Terrain.Bounds);
        var clearance = ClearanceBakeSnapshot.Build(
            navigation, preset.Terrain, cellSize: 4f);
        var topology = TerrainNavigationTopologyBuilder.Build(
            preset.Terrain, clearance);
        var ramps = topology.Ramps;
        var largeRoute = topology.CreateRoutePlanner().Plan(
            preset.Start,
            preset.Goal,
            MovementClearance.LargeNavigationRadius);
        return ramps.Length == 2 &&
               ramps[0].Width == 20f &&
               ramps[0].Supports(MovementClass.Medium) &&
               !ramps[0].Supports(MovementClass.Large) &&
               ramps[1].Width == 120f &&
               ramps[1].Supports(MovementClass.Large) &&
               largeRoute.ChokeIds.SequenceEqual([1]);
    }

    private static bool ConnectivityAfterBlocker(TerrainTestPreset preset)
    {
        if (preset.DynamicBlocker is not { } blocker)
            throw new InvalidOperationException(
                $"Preset {preset.Id} has no dynamic blocker.");
        var navigation = EmptyNavigation(preset.Terrain.Bounds);
        var world = navigation.CreateWorld(preset.Terrain);
        world.DynamicOccupancy.Place(blocker);
        var connectivity = new NavigationConnectivityAnalyzer(
            world, 8f).Analyze(MovementClearance.MediumNavigationRadius);
        return SameComponent(connectivity, preset.Start, preset.Goal);
    }

    private static bool SameComponent(
        NavigationConnectivitySnapshot snapshot,
        System.Numerics.Vector2 first,
        System.Numerics.Vector2 second)
    {
        var firstNode = NodeAt(snapshot, first);
        var secondNode = NodeAt(snapshot, second);
        return firstNode >= 0 && secondNode >= 0 &&
               snapshot.IsWalkable(firstNode) &&
               snapshot.IsWalkable(secondNode) &&
               snapshot.ComponentAt(firstNode) == snapshot.ComponentAt(secondNode);
    }

    private static int NodeAt(
        NavigationConnectivitySnapshot snapshot,
        System.Numerics.Vector2 position)
    {
        if (!snapshot.WorldBounds.Contains(position)) return -1;
        var column = Math.Clamp(
            (int)MathF.Floor(
                (position.X - snapshot.WorldBounds.Min.X) /
                snapshot.CellSize),
            0,
            snapshot.Columns - 1);
        var row = Math.Clamp(
            (int)MathF.Floor(
                (position.Y - snapshot.WorldBounds.Min.Y) /
                snapshot.CellSize),
            0,
            snapshot.Rows - 1);
        return row * snapshot.Columns + column;
    }

    private static NavigationMapSnapshot EmptyNavigation(SimRect bounds)
    {
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                bounds,
                [], [], [], [],
                out var navigation,
                out var validation) || navigation is null)
        {
            throw new InvalidOperationException(
                $"Preset navigation failed: {validation.FirstError}.");
        }
        return navigation;
    }
}
