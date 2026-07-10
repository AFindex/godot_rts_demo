using Godot;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.GodotRuntime.Resources;

public static class NavigationMapResourceConverter
{
    public static bool TryLoadSnapshot(
        string resourcePath,
        out NavigationMapSnapshot? snapshot,
        out NavigationMapValidationResult validation)
    {
        if (!ResourceLoader.Exists(resourcePath))
        {
            snapshot = null;
            validation = SingleIssue(
                NavigationMapErrorCode.MissingResourceAsset,
                $"Navigation resource does not exist: {resourcePath}");
            return false;
        }

        var resource = GD.Load<RtsNavigationMapResource>(resourcePath);
        if (resource is null)
        {
            snapshot = null;
            validation = SingleIssue(
                NavigationMapErrorCode.MissingResourceAsset,
                $"Navigation resource could not be loaded: {resourcePath}");
            return false;
        }

        return TryConvert(resource, out snapshot, out validation);
    }

    public static bool TryConvert(
        RtsNavigationMapResource resource,
        out NavigationMapSnapshot? snapshot,
        out NavigationMapValidationResult validation)
    {
        var obstacles = new SimRect[resource.Obstacles.Count];
        for (var index = 0; index < obstacles.Length; index++)
        {
            var source = resource.Obstacles[index];
            if (source is null)
            {
                snapshot = null;
                validation = NullElement("obstacle", index);
                return false;
            }

            obstacles[index] = ToSimRect(source.Bounds);
        }

        var portals = new PortalNode[resource.Portals.Count];
        for (var index = 0; index < portals.Length; index++)
        {
            var source = resource.Portals[index];
            if (source is null)
            {
                snapshot = null;
                validation = NullElement("portal", index);
                return false;
            }

            portals[index] = new PortalNode(
                source.Id,
                ToNumerics(source.Position),
                source.DisplayName ?? string.Empty);
        }

        var edges = new PortalEdge[resource.Edges.Count];
        for (var index = 0; index < edges.Length; index++)
        {
            var source = resource.Edges[index];
            if (source is null)
            {
                snapshot = null;
                validation = NullElement("edge", index);
                return false;
            }

            edges[index] = new PortalEdge(
                source.FromPortal,
                source.ToPortal,
                source.Width,
                source.ChokeId);
        }

        var chokes = new ChokeDefinition[resource.Chokes.Count];
        for (var index = 0; index < chokes.Length; index++)
        {
            var source = resource.Chokes[index];
            if (source is null)
            {
                snapshot = null;
                validation = NullElement("choke", index);
                return false;
            }

            chokes[index] = new ChokeDefinition(
                source.Id,
                ToNumerics(source.A),
                ToNumerics(source.B),
                source.Width,
                source.ApproachDistance);
        }

        return NavigationMapSnapshot.TryCreate(
            resource.FormatVersion,
            ToSimRect(resource.WorldBounds),
            obstacles,
            portals,
            edges,
            chokes,
            out snapshot,
            out validation);
    }

    public static RtsNavigationMapResource FromSnapshot(NavigationMapSnapshot snapshot)
    {
        var resource = new RtsNavigationMapResource
        {
            FormatVersion = snapshot.FormatVersion,
            WorldBounds = ToGodotRect(snapshot.WorldBounds)
        };

        var obstacles = snapshot.Obstacles;
        for (var index = 0; index < obstacles.Length; index++)
        {
            resource.Obstacles.Add(new NavigationObstacleResource
            {
                Bounds = ToGodotRect(obstacles[index])
            });
        }

        var portals = snapshot.PortalNodes;
        for (var index = 0; index < portals.Length; index++)
        {
            var source = portals[index];
            resource.Portals.Add(new NavigationPortalResource
            {
                Id = source.Id,
                Position = ToGodot(source.Position),
                DisplayName = source.Name ?? string.Empty
            });
        }

        var edges = snapshot.PortalEdges;
        for (var index = 0; index < edges.Length; index++)
        {
            var source = edges[index];
            resource.Edges.Add(new NavigationPortalEdgeResource
            {
                FromPortal = source.FromNode,
                ToPortal = source.ToNode,
                Width = source.Width,
                ChokeId = source.ChokeId
            });
        }

        var chokes = snapshot.Chokes;
        for (var index = 0; index < chokes.Length; index++)
        {
            var source = chokes[index];
            resource.Chokes.Add(new NavigationChokeResource
            {
                Id = source.Id,
                A = ToGodot(source.A),
                B = ToGodot(source.B),
                Width = source.Width,
                ApproachDistance = source.ApproachDistance
            });
        }

        return resource;
    }

    private static NavigationMapValidationResult NullElement(string type, int index) =>
        SingleIssue(
            NavigationMapErrorCode.NullResourceElement,
            $"Navigation {type} resource at index {index} is null.",
            index);

    private static NavigationMapValidationResult SingleIssue(
        NavigationMapErrorCode code,
        string message,
        int index = -1) =>
        new([new NavigationMapValidationIssue(code, index, message)]);

    private static SimRect ToSimRect(Rect2 rect) =>
        new(ToNumerics(rect.Position), ToNumerics(rect.End));

    private static Rect2 ToGodotRect(SimRect rect) =>
        new(ToGodot(rect.Min), ToGodot(rect.Max - rect.Min));

    private static NVector2 ToNumerics(Vector2 vector) =>
        new(vector.X, vector.Y);

    private static Vector2 ToGodot(NVector2 vector) =>
        new(vector.X, vector.Y);
}
