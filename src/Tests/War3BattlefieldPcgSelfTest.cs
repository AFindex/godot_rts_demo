using System.Numerics;
using War3Rts;
using War3Rts.Pcg;

namespace RtsDemo.Tests;

public static class War3BattlefieldPcgSelfTest
{
    public static SelfTestResult Run()
    {
        try
        {
            var first = War3BattlefieldPcg.Generate(
                War3HumanScenario.WorldBounds,
                War3HumanScenario.PlayerHome,
                War3HumanScenario.EnemyHome);
            var repeated = War3BattlefieldPcg.Generate(
                War3HumanScenario.WorldBounds,
                War3HumanScenario.PlayerHome,
                War3HumanScenario.EnemyHome);
            var deterministic = first.StableHash == repeated.StableHash &&
                                first.ForestTreePositions.SequenceEqual(
                                    repeated.ForestTreePositions) &&
                                first.NeutralGoldPositions.SequenceEqual(
                                    repeated.NeutralGoldPositions);
            var trees = first.ForestTreePositions.ToArray();
            var centerX = (War3HumanScenario.WorldBounds.Min.X +
                           War3HumanScenario.WorldBounds.Max.X) * 0.5f;
            var mirrored = trees.All(point =>
            {
                var expected = new Vector2(centerX * 2f - point.X, point.Y);
                return trees.Any(candidate =>
                    Vector2.DistanceSquared(candidate, expected) < 0.001f);
            });
            var densityLayered = first.DenseTreeCount >= trees.Length / 3 &&
                                 first.SparseTreeCount >= trees.Length / 4;
            var terrain = War3HumanScenario.CreateTerrain();
            var navigation = War3HumanScenario.CreateNavigation();
            var terrainReady = terrain.Columns == 200 && terrain.Rows == 120 &&
                               terrain.Cells.ToArray().Count(
                                   static cell => cell.IsRamp) == 20 &&
                               terrain.HeightAt(War3HumanScenario.PlayerHome) >
                               terrain.HeightAt(
                                   (War3HumanScenario.PlayerHome +
                                    War3HumanScenario.EnemyHome) * 0.5f) + 32f;
            var sharedLayout = navigation.Obstacles.Length ==
                               War3HumanScenario.ExpectedResourceNodeCount;
            var passed = deterministic && mirrored && densityLayered &&
                         terrainReady && sharedLayout &&
                         trees.Length == War3BattlefieldPcg.ForestTreeCount;
            return new SelfTestResult(
                passed,
                $"hash={first.StableHashText}, trees={trees.Length}, " +
                $"dense={first.DenseTreeCount}, sparse={first.SparseTreeCount}, " +
                $"mirrored={mirrored}, terrain={terrain.Columns}x{terrain.Rows}, " +
                $"obstacles={navigation.Obstacles.Length}");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }
}
