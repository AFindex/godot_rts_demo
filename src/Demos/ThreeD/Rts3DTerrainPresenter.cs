using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Presentation-only mesh adapter for the engine-independent terrain snapshot.
/// It batches cells by material and never supplies collision or gameplay data.
/// </summary>
public partial class Rts3DTerrainPresenter : Node3D
{
    private readonly Dictionary<string, StandardMaterial3D> _materials = [];
    private MeshInstance3D? _visual;

    public void Initialize(TerrainMapSnapshot terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        _visual?.QueueFree();
        var mesh = new ArrayMesh();
        foreach (var surface in terrain.Surfaces)
            AppendSurface(mesh, terrain, surface);
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
        tool.SetMaterial(Material(surface.MaterialKey));
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

    private void AppendCliffs(ArrayMesh mesh, TerrainMapSnapshot terrain)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetMaterial(Material("cliff-face"));
        var added = false;
        for (var row = 0; row < terrain.Rows; row++)
        {
            for (var column = 0; column < terrain.Columns; column++)
            {
                if (column + 1 < terrain.Columns)
                {
                    added |= AppendVerticalEdge(
                        tool,
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
                    added |= AppendVerticalEdge(
                        tool,
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
        if (added)
            tool.Commit(mesh);
    }

    private static bool AppendVerticalEdge(
        SurfaceTool tool,
        Vector3 firstA,
        Vector3 firstB,
        Vector3 secondA,
        Vector3 secondB)
    {
        var firstAverage = (firstA.Y + firstB.Y) * 0.5f;
        var secondAverage = (secondA.Y + secondB.Y) * 0.5f;
        if (MathF.Abs(firstAverage - secondAverage) < 0.001f)
            return false;
        var highA = firstAverage > secondAverage ? firstA : secondA;
        var highB = firstAverage > secondAverage ? firstB : secondB;
        var lowA = firstAverage > secondAverage ? secondA : firstA;
        var lowB = firstAverage > secondAverage ? secondB : firstB;
        AddTriangle(tool, highA, lowB, lowA);
        AddTriangle(tool, highA, highB, lowB);
        return true;
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

    private static void AddTriangle(
        SurfaceTool tool,
        Vector3 a,
        Vector3 b,
        Vector3 c)
    {
        var normal = (b - a).Cross(c - a).Normalized();
        if (normal == Vector3.Zero)
            normal = Vector3.Up;
        tool.SetNormal(normal);
        tool.SetUV(new Vector2(a.X, a.Z) * 0.25f);
        tool.AddVertex(a);
        tool.SetNormal(normal);
        tool.SetUV(new Vector2(b.X, b.Z) * 0.25f);
        tool.AddVertex(b);
        tool.SetNormal(normal);
        tool.SetUV(new Vector2(c.X, c.Z) * 0.25f);
        tool.AddVertex(c);
    }
}
