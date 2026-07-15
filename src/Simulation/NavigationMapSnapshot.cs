using System.Numerics;
using System.Text;

namespace RtsDemo.Simulation;

public enum NavigationMapErrorCode
{
    None = 0,
    UnsupportedFormatVersion = 1001,
    InvalidWorldBounds = 1002,
    InvalidObstacle = 1101,
    ObstacleOutsideWorld = 1102,
    NonDensePortalId = 1201,
    InvalidPortalPosition = 1202,
    PortalOutsideWorld = 1203,
    PortalInsideObstacle = 1204,
    InvalidPortalEdge = 1301,
    DuplicatePortalEdge = 1302,
    InvalidEdgeChokeReference = 1303,
    NonDenseChokeId = 1401,
    InvalidChokeGeometry = 1402,
    ChokeOutsideWorld = 1403,
    ChokeEdgeGeometryMismatch = 1404,
    MissingResourceAsset = 1501,
    NullResourceElement = 1502
}

public readonly record struct NavigationMapValidationIssue(
    NavigationMapErrorCode Code,
    int ElementIndex,
    string Message);

public sealed class NavigationMapValidationResult
{
    public NavigationMapValidationResult(NavigationMapValidationIssue[] issues)
    {
        Issues = issues;
    }

    public bool IsValid => Issues.Length == 0;
    public NavigationMapValidationIssue[] Issues { get; }
    public NavigationMapErrorCode FirstError =>
        IsValid ? NavigationMapErrorCode.None : Issues[0].Code;
}

/// <summary>
/// Engine-independent, validated and immutable-by-contract navigation data.
/// Godot resources are converted to this snapshot before simulation startup.
/// </summary>
public sealed class NavigationMapSnapshot
{
    public const int CurrentFormatVersion = 1;

    private readonly SimRect[] _obstacles;
    private readonly PortalNode[] _portalNodes;
    private readonly PortalEdge[] _portalEdges;
    private readonly ChokeDefinition[] _chokes;
    private readonly byte[] _canonicalBytes;

    private NavigationMapSnapshot(
        int formatVersion,
        SimRect worldBounds,
        SimRect[] obstacles,
        PortalNode[] portalNodes,
        PortalEdge[] portalEdges,
        ChokeDefinition[] chokes)
    {
        FormatVersion = formatVersion;
        WorldBounds = worldBounds;
        _obstacles = obstacles;
        _portalNodes = portalNodes;
        _portalEdges = portalEdges;
        _chokes = chokes;
        _canonicalBytes = BuildCanonicalBytes();
        StableHash = ComputeStableHash(_canonicalBytes);
    }

    public int FormatVersion { get; }
    public SimRect WorldBounds { get; }
    public ReadOnlySpan<SimRect> Obstacles => _obstacles;
    public ReadOnlySpan<PortalNode> PortalNodes => _portalNodes;
    public ReadOnlySpan<PortalEdge> PortalEdges => _portalEdges;
    public ReadOnlySpan<ChokeDefinition> Chokes => _chokes;
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;

    public static bool TryCreate(
        int formatVersion,
        SimRect worldBounds,
        ReadOnlySpan<SimRect> obstacles,
        ReadOnlySpan<PortalNode> portalNodes,
        ReadOnlySpan<PortalEdge> portalEdges,
        ReadOnlySpan<ChokeDefinition> chokes,
        out NavigationMapSnapshot? snapshot,
        out NavigationMapValidationResult validation)
    {
        var obstacleCopy = obstacles.ToArray();
        var portalNodeCopy = portalNodes.ToArray();
        var portalEdgeCopy = portalEdges.ToArray();
        var chokeCopy = chokes.ToArray();
        validation = Validate(
            formatVersion,
            worldBounds,
            obstacleCopy,
            portalNodeCopy,
            portalEdgeCopy,
            chokeCopy);
        if (!validation.IsValid)
        {
            snapshot = null;
            return false;
        }

        snapshot = new NavigationMapSnapshot(
            formatVersion,
            worldBounds,
            obstacleCopy,
            portalNodeCopy,
            portalEdgeCopy,
            chokeCopy);
        return true;
    }

    public StaticWorld CreateWorld() =>
        new(WorldBounds, _obstacles.ToArray());

    public StaticWorld CreateWorld(ITerrainMapQuery terrain) =>
        new(WorldBounds, terrain, _obstacles.ToArray());

    public PortalGraphRoutePlanner CreateRoutePlanner(StaticWorld world) =>
        new(world, _portalNodes.ToArray(), _portalEdges.ToArray());

    public ChokeController CreateChokeController() =>
        new(_chokes.ToArray());

    private static NavigationMapValidationResult Validate(
        int formatVersion,
        SimRect worldBounds,
        SimRect[] obstacles,
        PortalNode[] portalNodes,
        PortalEdge[] portalEdges,
        ChokeDefinition[] chokes)
    {
        var issues = new List<NavigationMapValidationIssue>();
        if (formatVersion != CurrentFormatVersion)
        {
            AddIssue(
                issues,
                NavigationMapErrorCode.UnsupportedFormatVersion,
                -1,
                $"Expected navigation format {CurrentFormatVersion}, got {formatVersion}.");
        }

        if (!IsFiniteRect(worldBounds) || worldBounds.Width <= 0f || worldBounds.Height <= 0f)
        {
            AddIssue(
                issues,
                NavigationMapErrorCode.InvalidWorldBounds,
                -1,
                "World bounds must be finite and have positive size.");
        }

        for (var index = 0; index < obstacles.Length; index++)
        {
            var obstacle = obstacles[index];
            if (!IsFiniteRect(obstacle) || obstacle.Width <= 0f || obstacle.Height <= 0f)
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.InvalidObstacle,
                    index,
                    "Obstacle must be finite and have positive size.");
                continue;
            }

            if (!worldBounds.Contains(obstacle.Min) || !worldBounds.Contains(obstacle.Max))
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.ObstacleOutsideWorld,
                    index,
                    "Obstacle must be fully contained by world bounds.");
            }
        }

        for (var index = 0; index < portalNodes.Length; index++)
        {
            var node = portalNodes[index];
            if (node.Id != index)
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.NonDensePortalId,
                    index,
                    $"Portal ID must equal its dense array index {index}.");
            }

            if (!IsFinite(node.Position))
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.InvalidPortalPosition,
                    index,
                    "Portal position must be finite.");
                continue;
            }

            if (!worldBounds.Contains(node.Position))
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.PortalOutsideWorld,
                    index,
                    "Portal position must be inside world bounds.");
            }

            for (var obstacleIndex = 0; obstacleIndex < obstacles.Length; obstacleIndex++)
            {
                if (obstacles[obstacleIndex].Contains(node.Position))
                {
                    AddIssue(
                        issues,
                        NavigationMapErrorCode.PortalInsideObstacle,
                        index,
                        $"Portal is inside obstacle {obstacleIndex}.");
                    break;
                }
            }
        }

        for (var index = 0; index < portalEdges.Length; index++)
        {
            var edge = portalEdges[index];
            if ((uint)edge.FromNode >= (uint)portalNodes.Length ||
                (uint)edge.ToNode >= (uint)portalNodes.Length ||
                edge.FromNode == edge.ToNode || !float.IsFinite(edge.Width) || edge.Width <= 0f)
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.InvalidPortalEdge,
                    index,
                    "Edge endpoints must reference different portals and width must be positive.");
                continue;
            }

            if (edge.ChokeId < -1 || edge.ChokeId >= chokes.Length)
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.InvalidEdgeChokeReference,
                    index,
                    "Edge choke reference must be -1 or a valid choke ID.");
            }

            for (var previous = 0; previous < index; previous++)
            {
                var other = portalEdges[previous];
                var duplicate = edge.FromNode == other.FromNode && edge.ToNode == other.ToNode ||
                                edge.FromNode == other.ToNode && edge.ToNode == other.FromNode;
                if (duplicate)
                {
                    AddIssue(
                        issues,
                        NavigationMapErrorCode.DuplicatePortalEdge,
                        index,
                        $"Edge duplicates undirected edge {previous}.");
                    break;
                }
            }
        }

        for (var index = 0; index < chokes.Length; index++)
        {
            var choke = chokes[index];
            if (choke.Id != index)
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.NonDenseChokeId,
                    index,
                    $"Choke ID must equal its dense array index {index}.");
            }

            if (!IsFinite(choke.A) || !IsFinite(choke.B) ||
                Vector2.DistanceSquared(choke.A, choke.B) <= 0.0001f ||
                !float.IsFinite(choke.Width) || choke.Width <= 0f ||
                !float.IsFinite(choke.ApproachDistance) || choke.ApproachDistance <= 0f)
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.InvalidChokeGeometry,
                    index,
                    "Choke endpoints, width and approach distance must be valid.");
                continue;
            }

            if (!worldBounds.Contains(choke.A) || !worldBounds.Contains(choke.B))
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.ChokeOutsideWorld,
                    index,
                    "Choke endpoints must be inside world bounds.");
            }
        }

        for (var edgeIndex = 0; edgeIndex < portalEdges.Length; edgeIndex++)
        {
            var edge = portalEdges[edgeIndex];
            if ((uint)edge.ChokeId >= (uint)chokes.Length ||
                (uint)edge.FromNode >= (uint)portalNodes.Length ||
                (uint)edge.ToNode >= (uint)portalNodes.Length)
            {
                continue;
            }

            var choke = chokes[edge.ChokeId];
            var from = portalNodes[edge.FromNode].Position;
            var to = portalNodes[edge.ToNode].Position;
            var matches = ApproximatelyEqual(from, choke.A) && ApproximatelyEqual(to, choke.B) ||
                          ApproximatelyEqual(from, choke.B) && ApproximatelyEqual(to, choke.A);
            if (!matches)
            {
                AddIssue(
                    issues,
                    NavigationMapErrorCode.ChokeEdgeGeometryMismatch,
                    edgeIndex,
                    "A choke edge must connect portal nodes at the choke endpoints.");
            }
        }

        return new NavigationMapValidationResult(issues.ToArray());
    }

    private byte[] BuildCanonicalBytes()
    {
        using var stream = new MemoryStream(512);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteInt(writer, FormatVersion);
        WriteRect(writer, WorldBounds);
        WriteInt(writer, _obstacles.Length);
        for (var index = 0; index < _obstacles.Length; index++)
        {
            WriteRect(writer, _obstacles[index]);
        }

        WriteInt(writer, _portalNodes.Length);
        for (var index = 0; index < _portalNodes.Length; index++)
        {
            var node = _portalNodes[index];
            WriteInt(writer, node.Id);
            WriteVector(writer, node.Position);
            WriteString(writer, node.Name ?? string.Empty);
        }

        WriteInt(writer, _portalEdges.Length);
        for (var index = 0; index < _portalEdges.Length; index++)
        {
            var edge = _portalEdges[index];
            WriteInt(writer, edge.FromNode);
            WriteInt(writer, edge.ToNode);
            WriteFloat(writer, edge.Width);
            WriteInt(writer, edge.ChokeId);
        }

        WriteInt(writer, _chokes.Length);
        for (var index = 0; index < _chokes.Length; index++)
        {
            var choke = _chokes[index];
            WriteInt(writer, choke.Id);
            WriteVector(writer, choke.A);
            WriteVector(writer, choke.B);
            WriteFloat(writer, choke.Width);
            WriteFloat(writer, choke.ApproachDistance);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static ulong ComputeStableHash(ReadOnlySpan<byte> data)
    {
        var hash = 14695981039346656037UL;
        for (var index = 0; index < data.Length; index++)
        {
            hash ^= data[index];
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static void AddIssue(
        List<NavigationMapValidationIssue> issues,
        NavigationMapErrorCode code,
        int elementIndex,
        string message) =>
        issues.Add(new NavigationMapValidationIssue(code, elementIndex, message));

    private static bool IsFiniteRect(SimRect rect) =>
        IsFinite(rect.Min) && IsFinite(rect.Max);

    private static bool IsFinite(Vector2 vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y);

    private static bool ApproximatelyEqual(Vector2 left, Vector2 right) =>
        Vector2.DistanceSquared(left, right) <= 0.25f * 0.25f;

    private static void WriteRect(BinaryWriter writer, SimRect rect)
    {
        WriteVector(writer, rect.Min);
        WriteVector(writer, rect.Max);
    }

    private static void WriteVector(BinaryWriter writer, Vector2 vector)
    {
        WriteFloat(writer, vector.X);
        WriteFloat(writer, vector.Y);
    }

    private static void WriteFloat(BinaryWriter writer, float value) =>
        WriteInt(writer, BitConverter.SingleToInt32Bits(value));

    private static void WriteInt(BinaryWriter writer, int value) =>
        writer.Write(value);

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt(writer, bytes.Length);
        writer.Write(bytes);
    }
}
