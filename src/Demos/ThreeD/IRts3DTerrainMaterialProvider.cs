using Godot;
using RtsDemo.Simulation;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Presentation-only material boundary for terrain themes. Implementations
/// may use project-owned textures, exported game assets, or debug colors;
/// gameplay continues to consume TerrainMapSnapshot only.
/// </summary>
public interface IRts3DTerrainMaterialProvider
{
    Material SurfaceMaterial(TerrainSurfaceDefinition surface);
    Material CliffMaterial(TerrainSurfaceDefinition upperSurface);
}

/// <summary>
/// Optional presentation contract for themes that can render several ground
/// surfaces in one splat-style material. The authoritative terrain still owns
/// one semantic SurfaceId per cell; blend weights are generated only while the
/// presentation mesh is built.
/// </summary>
public interface IRts3DTerrainBlendMaterialProvider :
    IRts3DTerrainMaterialProvider
{
    Material BlendedSurfaceMaterial();
    bool TryGetBlendChannel(
        TerrainSurfaceDefinition surface,
        out int channel);
}

/// <summary>
/// Optional War3-style dual-grid contract. A separate visual tilepoint grid
/// supplies four material identities to every rendered cell; the material
/// selects authored transition tiles from those four corner identities.
/// </summary>
public interface IRts3DTerrainDualGridMaterialProvider :
    IRts3DTerrainMaterialProvider
{
    bool DualGridEnabled { get; }
    Material DualGridSurfaceMaterial();
    bool TryGetDualGridLayer(
        TerrainSurfaceDefinition surface,
        out int layer);
}

/// <summary>
/// Optional Warcraft-style cliff model boundary. Implementations resolve a
/// normalized TL/TR/BR/BL height signature such as BAAA to one static mesh.
/// The presenter owns placement, scale, batching and fallback cliff faces.
/// </summary>
public interface IRts3DTerrainClassicCliffProvider :
    IRts3DTerrainMaterialProvider
{
    bool ClassicCliffMeshesEnabled { get; }
    int ClassicCliffVariationCount(string signature);
    bool TryGetClassicCliffMesh(
        string signature,
        int variation,
        out Rts3DClassicCliffMesh definition);
    Material ClassicCliffMaterial(TerrainSurfaceDefinition upperSurface);
    bool TryGetClassicCliffGroundLayer(
        TerrainSurfaceDefinition upperSurface,
        out int layer);
}

public readonly record struct Rts3DClassicCliffMesh(
    string AssetKey,
    Mesh Mesh,
    Transform3D ModelTransform);
