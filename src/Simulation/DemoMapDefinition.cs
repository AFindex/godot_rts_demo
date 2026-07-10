using System.Numerics;

namespace RtsDemo.Simulation;

public static class DemoMapDefinition
{
    public static StaticWorld CreateWorld() => new(
        new SimRect(new Vector2(24f, 70f), new Vector2(1256f, 696f)),
        new SimRect(new Vector2(545f, 100f), new Vector2(715f, 292f)),
        new SimRect(new Vector2(545f, 414f), new Vector2(715f, 642f)),
        new SimRect(new Vector2(870f, 250f), new Vector2(970f, 450f)));

    public static PortalGraphRoutePlanner CreateRoutePlanner(StaticWorld world)
    {
        PortalNode[] nodes =
        [
            new(0, new Vector2(520f, 353f), "West choke mouth"),
            new(1, new Vector2(740f, 353f), "East choke mouth"),
            new(2, new Vector2(825f, 225f), "Upper west bypass"),
            new(3, new Vector2(1015f, 225f), "Upper east bypass"),
            new(4, new Vector2(825f, 475f), "Lower west bypass"),
            new(5, new Vector2(1015f, 475f), "Lower east bypass")
        ];

        PortalEdge[] edges =
        [
            new(0, 1, 112f, ChokeId: 0),
            new(1, 2, 190f),
            new(2, 3, 180f),
            new(1, 4, 190f),
            new(4, 5, 180f)
        ];

        return new PortalGraphRoutePlanner(world, nodes, edges);
    }

    public static ChokeController CreateChokeController() => new(
        new ChokeDefinition(
            0,
            new Vector2(520f, 353f),
            new Vector2(740f, 353f),
            Width: 112f,
            ApproachDistance: 155f));
}
