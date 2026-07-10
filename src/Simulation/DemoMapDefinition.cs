using System.Numerics;

namespace RtsDemo.Simulation;

public static class DemoMapDefinition
{
    private static readonly NavigationMapSnapshot Snapshot = BuildSnapshot();

    public static NavigationMapSnapshot CreateSnapshot() => Snapshot;

    private static NavigationMapSnapshot BuildSnapshot()
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

        ChokeDefinition[] chokes =
        [
            new(
                0,
                new Vector2(520f, 353f),
                new Vector2(740f, 353f),
                Width: 112f,
                ApproachDistance: 155f)
        ];

        var created = NavigationMapSnapshot.TryCreate(
            NavigationMapSnapshot.CurrentFormatVersion,
            new SimRect(new Vector2(24f, 70f), new Vector2(1256f, 696f)),
            [
                new SimRect(new Vector2(545f, 100f), new Vector2(715f, 292f)),
                new SimRect(new Vector2(545f, 414f), new Vector2(715f, 642f)),
                new SimRect(new Vector2(870f, 250f), new Vector2(970f, 450f))
            ],
            nodes,
            edges,
            chokes,
            out var snapshot,
            out var validation);
        if (!created || snapshot is null)
        {
            throw new InvalidOperationException(
                $"Built-in demo navigation data is invalid: {validation.FirstError}.");
        }

        return snapshot;
    }

    public static StaticWorld CreateWorld() => CreateSnapshot().CreateWorld();

    public static PortalGraphRoutePlanner CreateRoutePlanner(StaticWorld world) =>
        CreateSnapshot().CreateRoutePlanner(world);

    public static ChokeController CreateChokeController() =>
        CreateSnapshot().CreateChokeController();
}
