using System.Numerics;
using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class PathProviderFallbackSelfTest
{
    public static SelfTestResult Run()
    {
        var world = new StaticWorld(
            new SimRect(Vector2.Zero, new Vector2(320f, 180f)),
            []);
        var primary = new UnavailablePathProvider();
        var fallback = new GridPathProvider(world);
        var provider = new ValidatingFallbackPathProvider(
            primary, fallback, world);
        var start = new Vector2(40f, 40f);
        var goal = new Vector2(280f, 140f);
        var path = provider.FindPath(start, goal, navigationRadius: 8f);
        var ready = provider.IsReady;
        var resolved = path.Length >= 2 && path[0] == start && path[^1] == goal;
        var passed = ready && resolved && primary.Requests == 1;
        return new SelfTestResult(
            passed,
            $"ready={ready}, resolved={resolved}, " +
            $"primaryReady={primary.IsReady}, requests={primary.Requests}");
    }

    private sealed class UnavailablePathProvider : IPathProvider
    {
        public bool IsReady => false;
        public int Requests { get; private set; }

        public Vector2[] FindPath(
            Vector2 start,
            Vector2 goal,
            float navigationRadius)
        {
            Requests++;
            return [];
        }
    }
}
