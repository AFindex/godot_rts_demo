using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Presentation-only mesh adapter for the engine-independent terrain snapshot.
/// It batches cells by material and never supplies collision or gameplay data.
/// </summary>
public partial class Rts3DTerrainPresenter : Node3D
{
    private const int BlendSampleRadius = 2;
    private readonly Dictionary<string, StandardMaterial3D> _materials = [];
    private MeshInstance3D? _visual;
    private IRts3DTerrainMaterialProvider? _materialProvider;
    private float _cellWorldSize = 1f;
    private float _cliffWorldHeight = 1f;

    public void Initialize(
        TerrainMapSnapshot terrain,
        IRts3DTerrainMaterialProvider? materialProvider = null,
        TerrainVisualLayerMap? visualLayerMap = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        _visual?.QueueFree();
        _materialProvider = materialProvider;
        _cellWorldSize = MathF.Max(0.001f,
            SimPlane3DTransform.ToWorldLength(terrain.CellSize));
        _cliffWorldHeight = MathF.Max(0.001f,
            SimPlane3DTransform.ToWorldLength(terrain.CliffLevelHeight));
        var mesh = new ArrayMesh();
        var dualGridProvider =
            materialProvider as IRts3DTerrainDualGridMaterialProvider;
        var blendProvider =
            materialProvider as IRts3DTerrainBlendMaterialProvider;
        if (dualGridProvider?.DualGridEnabled == true)
            AppendDualGridSurface(
                mesh, terrain, dualGridProvider, visualLayerMap);
        else if (blendProvider is not null)
            AppendBlendedSurface(mesh, terrain, blendProvider);
        foreach (var surface in terrain.Surfaces)
        {
            if (dualGridProvider?.DualGridEnabled == true &&
                dualGridProvider.TryGetDualGridLayer(surface, out _))
            {
                continue;
            }
            if (blendProvider?.TryGetBlendChannel(surface, out _) == true)
                continue;
            AppendSurface(mesh, terrain, surface);
        }
        AppendCliffs(mesh, terrain);
        _visual = new MeshInstance3D
        {
            Name = "TerrainMesh",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On
        };
        AddChild(_visual);
    }

    private void AppendSurface(
        ArrayMesh mesh,
        TerrainMapSnapshot terrain,
        TerrainSurfaceDefinition surface)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetMaterial(_materialProvider?.SurfaceMaterial(surface) ??
                         Material(surface.MaterialKey));
        var added = false;
        for (var row = 0; row < terrain.Rows; row++)
        {
            for (var column = 0; column < terrain.Columns; column++)
            {
                if (terrain.Cell(column, row).SurfaceId != surface.Id)
                    continue;
                var bounds = terrain.CellBounds(column, row);
                var a = Point(terrain, bounds.Min.X, bounds.Min.Y,
                    terrain.CellCornerHeight(column, row, false, false));
                var b = Point(terrain, bounds.Max.X, bounds.Min.Y,
                    terrain.CellCornerHeight(column, row, true, false));
                var c = Point(terrain, bounds.Max.X, bounds.Max.Y,
                    terrain.CellCornerHeight(column, row, true, true));
                var d = Point(terrain, bounds.Min.X, bounds.Max.Y,
                    terrain.CellCornerHeight(column, row, false, true));
                AddTriangle(tool, a, c, b);
                AddTriangle(tool, a, d, c);
                added = true;
            }
        }
        if (added)
            tool.Commit(mesh);
    }

    private void AppendDualGridSurface(
        ArrayMesh mesh,
        TerrainMapSnapshot terrain,
        IRts3DTerrainDualGridMaterialProvider dualGridProvider,
        TerrainVisualLayerMap? authoredVisualMap)
    {
        if (authoredVisualMap is not null &&
            (!string.Equals(
                authoredVisualMap.SourceTerrainHash,
                terrain.StableHashText,
                StringComparison.OrdinalIgnoreCase) ||
             authoredVisualMap.CellColumns != terrain.Columns ||
             authoredVisualMap.CellRows != terrain.Rows))
        {
            throw new ArgumentException(
                "The authored visual layer map does not match the terrain.",
                nameof(authoredVisualMap));
        }
        var visualMap = authoredVisualMap ?? TerrainVisualLayerMap.FromTerrain(
            terrain, dualGridProvider);
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetMaterial(dualGridProvider.DualGridSurfaceMaterial());
        var added = false;
        for (var row = 0; row < terrain.Rows; row++)
        {
            for (var column = 0; column < terrain.Columns; column++)
            {
                var cell = terrain.Cell(column, row);
                if (!dualGridProvider.TryGetDualGridLayer(
                        terrain.Surface(cell.SurfaceId), out _))
                {
                    continue;
                }
                var bounds = terrain.CellBounds(column, row);
                var a = Point(terrain, bounds.Min.X, bounds.Min.Y,
                    terrain.CellCornerHeight(column, row, false, false));
                var b = Point(terrain, bounds.Max.X, bounds.Min.Y,
                    terrain.CellCornerHeight(column, row, true, false));
                var c = Point(terrain, bounds.Max.X, bounds.Max.Y,
                    terrain.CellCornerHeight(column, row, true, true));
                var d = Point(terrain, bounds.Min.X, bounds.Max.Y,
                    terrain.CellCornerHeight(column, row, false, true));
                var visualCell = visualMap.Cell(column, row);
                var encoded = new Vector2(
                    visualCell.PackedLayerMasks,
                    visualCell.BaseVariation);
                AddDualGridTriangle(tool, a, c, b, encoded);
                AddDualGridTriangle(tool, a, d, c, encoded);
                added = true;
            }
        }
        if (added)
            tool.Commit(mesh);
        GD.Print(
            $"TERRAIN_VISUAL_LAYER_MAP source={visualMap.SourceTerrainHash} " +
            $"visual={visualMap.StableHashText} " +
            $"points={visualMap.PointColumns}x{visualMap.PointRows}");
    }

    private void AppendBlendedSurface(
        ArrayMesh mesh,
        TerrainMapSnapshot terrain,
        IRts3DTerrainBlendMaterialProvider blendProvider)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetMaterial(blendProvider.BlendedSurfaceMaterial());
        var added = false;
        for (var row = 0; row < terrain.Rows; row++)
        {
            for (var column = 0; column < terrain.Columns; column++)
            {
                var cell = terrain.Cell(column, row);
                if (!blendProvider.TryGetBlendChannel(
                        terrain.Surface(cell.SurfaceId), out _))
                {
                    continue;
                }

                var bounds = terrain.CellBounds(column, row);
                var a = Point(terrain, bounds.Min.X, bounds.Min.Y,
                    terrain.CellCornerHeight(column, row, false, false));
                var b = Point(terrain, bounds.Max.X, bounds.Min.Y,
                    terrain.CellCornerHeight(column, row, true, false));
                var c = Point(terrain, bounds.Max.X, bounds.Max.Y,
                    terrain.CellCornerHeight(column, row, true, true));
                var d = Point(terrain, bounds.Min.X, bounds.Max.Y,
                    terrain.CellCornerHeight(column, row, false, true));
                var weightA = BlendWeights(
                    terrain, blendProvider, column, row, false, false);
                var weightB = BlendWeights(
                    terrain, blendProvider, column, row, true, false);
                var weightC = BlendWeights(
                    terrain, blendProvider, column, row, true, true);
                var weightD = BlendWeights(
                    terrain, blendProvider, column, row, false, true);
                AddBlendTriangle(tool, a, c, b, weightA, weightC, weightB);
                AddBlendTriangle(tool, a, d, c, weightA, weightD, weightC);
                added = true;
            }
        }
        if (added)
            tool.Commit(mesh);
    }

    private static Color BlendWeights(
        TerrainMapSnapshot terrain,
        IRts3DTerrainBlendMaterialProvider blendProvider,
        int column,
        int row,
        bool maximumX,
        bool maximumY)
    {
        Span<float> weights = stackalloc float[4];
        var vertexColumn = column + (maximumX ? 1 : 0);
        var vertexRow = row + (maximumY ? 1 : 0);
        var targetHeight = terrain.CellCornerHeight(
            column, row, maximumX, maximumY);
        var heightTolerance = MathF.Max(
            0.001f, terrain.CliffLevelHeight * 0.16f);

        // A small Gaussian footprint gives each logical cell a continuous
        // presentation weight field. Samples that do not meet at roughly the
        // same elevation are rejected so a ground texture cannot smear down a
        // cliff face or onto a disconnected plateau.
        for (var sampleRow = vertexRow - BlendSampleRadius;
             sampleRow < vertexRow + BlendSampleRadius;
             sampleRow++)
        {
            if ((uint)sampleRow >= (uint)terrain.Rows)
                continue;
            for (var sampleColumn = vertexColumn - BlendSampleRadius;
                 sampleColumn < vertexColumn + BlendSampleRadius;
                 sampleColumn++)
            {
                if ((uint)sampleColumn >= (uint)terrain.Columns)
                    continue;
                var sample = terrain.Cell(sampleColumn, sampleRow);
                if (!blendProvider.TryGetBlendChannel(
                        terrain.Surface(sample.SurfaceId), out var channel) ||
                    (uint)channel >= 4u)
                {
                    continue;
                }

                var nearestMaximumX = vertexColumn > sampleColumn;
                var nearestMaximumY = vertexRow > sampleRow;
                var sampleHeight = terrain.CellCornerHeight(
                    sampleColumn, sampleRow,
                    nearestMaximumX, nearestMaximumY);
                if (MathF.Abs(sampleHeight - targetHeight) > heightTolerance)
                    continue;

                var deltaX = sampleColumn + 0.5f - vertexColumn;
                var deltaY = sampleRow + 0.5f - vertexRow;
                var distanceSquared = deltaX * deltaX + deltaY * deltaY;
                weights[channel] += MathF.Exp(-distanceSquared / 1.8f);
            }
        }

        var total = weights[0] + weights[1] + weights[2] + weights[3];
        if (total <= 0.0001f)
        {
            var targetSurface = terrain.Surface(
                terrain.Cell(column, row).SurfaceId);
            if (blendProvider.TryGetBlendChannel(targetSurface, out var channel) &&
                (uint)channel < 4u)
            {
                weights[channel] = 1f;
                total = 1f;
            }
        }
        var inverse = total > 0.0001f ? 1f / total : 1f;
        return new Color(
            weights[0] * inverse,
            weights[1] * inverse,
            weights[2] * inverse,
            weights[3] * inverse);
    }

    private void AppendCliffs(ArrayMesh mesh, TerrainMapSnapshot terrain)
    {
        var tools = new Dictionary<ushort, SurfaceTool>();
        for (var row = 0; row < terrain.Rows; row++)
        {
            for (var column = 0; column < terrain.Columns; column++)
            {
                if (column + 1 < terrain.Columns)
                {
                    AppendCliffEdge(
                        tools,
                        terrain.Surface(terrain.Cell(column, row).SurfaceId),
                        terrain.Surface(terrain.Cell(column + 1, row).SurfaceId),
                        Point(terrain,
                            terrain.CellBounds(column, row).Max.X,
                            terrain.CellBounds(column, row).Min.Y,
                            terrain.CellCornerHeight(column, row, true, false)),
                        Point(terrain,
                            terrain.CellBounds(column, row).Max.X,
                            terrain.CellBounds(column, row).Max.Y,
                            terrain.CellCornerHeight(column, row, true, true)),
                        Point(terrain,
                            terrain.CellBounds(column + 1, row).Min.X,
                            terrain.CellBounds(column + 1, row).Min.Y,
                            terrain.CellCornerHeight(column + 1, row, false, false)),
                        Point(terrain,
                            terrain.CellBounds(column + 1, row).Min.X,
                            terrain.CellBounds(column + 1, row).Max.Y,
                            terrain.CellCornerHeight(column + 1, row, false, true)));
                }
                if (row + 1 < terrain.Rows)
                {
                    AppendCliffEdge(
                        tools,
                        terrain.Surface(terrain.Cell(column, row).SurfaceId),
                        terrain.Surface(terrain.Cell(column, row + 1).SurfaceId),
                        Point(terrain,
                            terrain.CellBounds(column, row).Min.X,
                            terrain.CellBounds(column, row).Max.Y,
                            terrain.CellCornerHeight(column, row, false, true)),
                        Point(terrain,
                            terrain.CellBounds(column, row).Max.X,
                            terrain.CellBounds(column, row).Max.Y,
                            terrain.CellCornerHeight(column, row, true, true)),
                        Point(terrain,
                            terrain.CellBounds(column, row + 1).Min.X,
                            terrain.CellBounds(column, row + 1).Min.Y,
                            terrain.CellCornerHeight(column, row + 1, false, false)),
                        Point(terrain,
                            terrain.CellBounds(column, row + 1).Max.X,
                            terrain.CellBounds(column, row + 1).Min.Y,
                            terrain.CellCornerHeight(column, row + 1, true, false)));
                }
            }
        }
        foreach (var tool in tools.Values)
            tool.Commit(mesh);
    }

    private void AppendCliffEdge(
        Dictionary<ushort, SurfaceTool> tools,
        TerrainSurfaceDefinition firstSurface,
        TerrainSurfaceDefinition secondSurface,
        Vector3 firstA,
        Vector3 firstB,
        Vector3 secondA,
        Vector3 secondB)
    {
        var firstAverage = (firstA.Y + firstB.Y) * 0.5f;
        var secondAverage = (secondA.Y + secondB.Y) * 0.5f;
        if (MathF.Abs(firstAverage - secondAverage) < 0.001f)
            return;
        var firstIsHigh = firstAverage > secondAverage;
        var highA = firstIsHigh ? firstA : secondA;
        var highB = firstIsHigh ? firstB : secondB;
        var lowA = firstIsHigh ? secondA : firstA;
        var lowB = firstIsHigh ? secondB : firstB;
        var upperSurface = firstIsHigh ? firstSurface : secondSurface;
        var batchKey = _materialProvider is null
            ? ushort.MaxValue
            : upperSurface.Id;
        if (!tools.TryGetValue(batchKey, out var tool))
        {
            tool = new SurfaceTool();
            tool.Begin(Mesh.PrimitiveType.Triangles);
            tool.SetMaterial(
                _materialProvider?.CliffMaterial(upperSurface) ??
                Material("cliff-face"));
            tools.Add(batchKey, tool);
        }
        var horizontalTiles = MathF.Max(0.001f,
            highA.DistanceTo(highB) / _cellWorldSize);
        var verticalTiles = MathF.Max(0.001f,
            MathF.Abs(firstAverage - secondAverage) / _cliffWorldHeight);
        var uvHighA = Vector2.Zero;
        var uvHighB = new Vector2(horizontalTiles, 0f);
        var uvLowA = new Vector2(0f, verticalTiles);
        var uvLowB = new Vector2(horizontalTiles, verticalTiles);
        AddTriangle(tool, highA, lowB, lowA, uvHighA, uvLowB, uvLowA);
        AddTriangle(tool, highA, highB, lowB, uvHighA, uvHighB, uvLowB);
    }

    private StandardMaterial3D Material(string key)
    {
        if (_materials.TryGetValue(key, out var existing))
            return existing;
        var color = key switch
        {
            "badlands" => new Color("405143"),
            "rock" => new Color("535d68"),
            "sand" => new Color("816c48"),
            "shallow-water" => new Color("27758a"),
            "deep-water" => new Color("173c63"),
            "mud" => new Color("625540"),
            "vision-smoke" => new Color("4f405f"),
            "metal" => new Color("374c57"),
            "cliff-face" => new Color("30383f"),
            _ => new Color("59665d")
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = key == "metal" ? 0.58f : 0.93f,
            Metallic = key == "metal" ? 0.42f : 0.02f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        _materials.Add(key, material);
        return material;
    }

    private static Vector3 Point(
        TerrainMapSnapshot terrain,
        float simulationX,
        float simulationY,
        float simulationHeight) =>
        SimPlane3DTransform.ToWorld(
            new System.Numerics.Vector2(simulationX, simulationY),
            SimPlane3DTransform.ToWorldLength(simulationHeight));

    private void AddTriangle(
        SurfaceTool tool,
        Vector3 a,
        Vector3 b,
        Vector3 c)
    {
        var scale = 1f / _cellWorldSize;
        AddTriangle(
            tool, a, b, c,
            new Vector2(a.X, a.Z) * scale,
            new Vector2(b.X, b.Z) * scale,
            new Vector2(c.X, c.Z) * scale);
    }

    private void AddBlendTriangle(
        SurfaceTool tool,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color weightA,
        Color weightB,
        Color weightC)
    {
        var scale = 1f / _cellWorldSize;
        var normal = (b - a).Cross(c - a).Normalized();
        if (normal == Vector3.Zero)
            normal = Vector3.Up;
        AddBlendVertex(tool, a, new Vector2(a.X, a.Z) * scale, normal, weightA);
        AddBlendVertex(tool, b, new Vector2(b.X, b.Z) * scale, normal, weightB);
        AddBlendVertex(tool, c, new Vector2(c.X, c.Z) * scale, normal, weightC);
    }

    private void AddDualGridTriangle(
        SurfaceTool tool,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector2 encodedCell)
    {
        var scale = 1f / _cellWorldSize;
        var normal = (b - a).Cross(c - a).Normalized();
        if (normal == Vector3.Zero)
            normal = Vector3.Up;
        AddDualGridVertex(
            tool, a, new Vector2(a.X, a.Z) * scale, normal, encodedCell);
        AddDualGridVertex(
            tool, b, new Vector2(b.X, b.Z) * scale, normal, encodedCell);
        AddDualGridVertex(
            tool, c, new Vector2(c.X, c.Z) * scale, normal, encodedCell);
    }

    private static void AddDualGridVertex(
        SurfaceTool tool,
        Vector3 position,
        Vector2 uv,
        Vector3 normal,
        Vector2 encodedCell)
    {
        tool.SetNormal(normal);
        tool.SetUV(uv);
        tool.SetUV2(encodedCell);
        tool.AddVertex(position);
    }

    private static void AddBlendVertex(
        SurfaceTool tool,
        Vector3 position,
        Vector2 uv,
        Vector3 normal,
        Color weights)
    {
        tool.SetNormal(normal);
        tool.SetUV(uv);
        tool.SetColor(weights);
        tool.AddVertex(position);
    }

    private static void AddTriangle(
        SurfaceTool tool,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC)
    {
        var normal = (b - a).Cross(c - a).Normalized();
        if (normal == Vector3.Zero)
            normal = Vector3.Up;
        tool.SetNormal(normal);
        tool.SetUV(uvA);
        tool.AddVertex(a);
        tool.SetNormal(normal);
        tool.SetUV(uvB);
        tool.AddVertex(b);
        tool.SetNormal(normal);
        tool.SetUV(uvC);
        tool.AddVertex(c);
    }
}
