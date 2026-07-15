namespace RtsDemo.Demos;

using RtsDemo.Presentation;

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
    public const string TerrainDynamicTopology3D =
        "res://demo/3d/TerrainDynamicTopology3D.tscn";
    public const string TerrainPresetGallery3D =
        "res://demo/3d/TerrainPresetGallery3D.tscn";
    public const string TerrainVisionCombat3D =
        "res://demo/3d/TerrainVisionCombat3D.tscn";
    public const string TerrainAuthoringWorkspace =
        "res://demo/terrain/TerrainAuthoringWorkspace.tscn";
    public const string War3AssetLab = "res://demo/war3/War3AssetLab.tscn";
    public const string War3Rts = "res://war3_rts/War3Rts.tscn";
    public const string CompatibilityEntry = "res://Main.tscn";

    public static string TerrainShowcaseScene(TerrainShowcaseTarget target) =>
        target switch
        {
            TerrainShowcaseTarget.PresetGallery => TerrainPresetGallery3D,
            TerrainShowcaseTarget.Traversal => TerrainTraversal3D,
            TerrainShowcaseTarget.DynamicTopology => TerrainDynamicTopology3D,
            TerrainShowcaseTarget.VisionCombat => TerrainVisionCombat3D,
            TerrainShowcaseTarget.AuthoringWorkspace =>
                TerrainAuthoringWorkspace,
            _ => throw new ArgumentOutOfRangeException(
                nameof(target), target, "Unknown terrain showcase target.")
        };
}
