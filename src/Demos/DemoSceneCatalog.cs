namespace RtsDemo.Demos;

/// <summary>
/// Single source of truth for user-facing demo scenes. Tooling continues to
/// enter through res://Main.tscn, while launchers switch to these packages.
/// </summary>
public static class DemoSceneCatalog
{
    public const string Classic2D = "res://demo/2d/RtsDemo2D.tscn";
    public const string Encounter3D = "res://demo/3d/RtsEncounter3D.tscn";
    public const string TerrainTraversal3D =
        "res://demo/3d/TerrainTraversal3D.tscn";
    public const string TerrainVisionCombat3D =
        "res://demo/3d/TerrainVisionCombat3D.tscn";
    public const string CompatibilityEntry = "res://Main.tscn";
}
