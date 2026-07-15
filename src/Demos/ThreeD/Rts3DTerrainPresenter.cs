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
    private const float ClassicCliffApronMaximumLocalHeight = 65f;
    private readonly Dictionary<string, StandardMaterial3D> _materials = [];
    private MeshInstance3D? _visual;
    private Node3D? _classicCliffRoot;
    private IRts3DTerrainMaterialProvider? _materialProvider;
    private float _cellWorldSize = 1f;
    private float _cliffWorldHeight = 1f;

    public TerrainCliffMeshDiagnostics? LastClassicCliffDiagnostics
    {
        get;
        private set;
    }

    public void Initialize(
        TerrainMapSnapshot terrain,
        IRts3DTerrainMaterialProvider? materialProvider = null,
        TerrainVisualLayerMap? visualLayerMap = null,
        TerrainClassicCliffStyleMap? cliffStyleMap = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        _visual?.QueueFree();
        _classicCliffRoot?.QueueFree();
        _classicCliffRoot = null;
        LastClassicCliffDiagnostics = null;
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
        var classicCliffProvider =
            materialProvider as IRts3DTerrainClassicCliffProvider;
        var classicCliffSeams =
            classicCliffProvider?.ClassicCliffMeshesEnabled == true
                ? AppendClassicCliffs(
                    terrain, classicCliffProvider, cliffStyleMap)
                : null;
        if (dualGridProvider?.DualGridEnabled == true)
            AppendDualGridSurface(
                mesh, terrain, dualGridProvider, visualLayerMap,
                classicCliffSeams, classicCliffProvider);
        else if (blendProvider is not null)
            AppendBlendedSurface(
                mesh, terrain, blendProvider, classicCliffSeams);
        foreach (var surface in terrain.Surfaces)
        {
            if (dualGridProvider?.DualGridEnabled == true &&
                dualGridProvider.TryGetDualGridLayer(surface, out _))
            {
                continue;
            }
            if (blendProvider?.TryGetBlendChannel(surface, out _) == true)
                continue;
            AppendSurface(mesh, terrain, surface, classicCliffSeams);
        }
        AppendCliffs(mesh, terrain, classicCliffSeams);
        _visual = new MeshInstance3D
        {
            Name = "TerrainMesh",
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On
        };
        AddChild(_visual);
    }

    private TerrainClassicCliffSeamMap AppendClassicCliffs(
        TerrainMapSnapshot terrain,
        IRts3DTerrainClassicCliffProvider provider,
        TerrainClassicCliffStyleMap? styleMap)
    {
        if (styleMap is not null && !styleMap.Matches(terrain))
        {
            throw new ArgumentException(
                "Classic cliff style map does not match the terrain.",
                nameof(styleMap));
        }
        var layout = TerrainCliffMeshLayout.Build(
            terrain, provider.ClassicCliffVariationCount);
        LastClassicCliffDiagnostics = layout.Diagnostics;
        var resolvedTiles = new List<TerrainClassicCliffTile>();
        var groups = new Dictionary<ClassicCliffGroupKey, ClassicCliffGroup>();
        var occupiedTiles = layout.Tiles
            .ToArray()
            .Select(static tile => (tile.Column, tile.Row))
            .ToHashSet();
        foreach (var sourceTile in layout.Tiles)
        {
            var tile = sourceTile with
            {
                CliffStyleId = styleMap?.StyleAt(
                    sourceTile.Column, sourceTile.Row) ??
                    provider.DefaultClassicCliffStyle
            };
            if (!provider.TryGetClassicCliffMesh(
                    tile.Signature, tile.Variation, out var definition))
            {
                continue;
            }
            var key = new ClassicCliffGroupKey(
                definition.AssetKey, tile.CliffStyleId);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new ClassicCliffGroup(definition);
                groups.Add(key, group);
            }
            group.Transforms.Add(ClassicCliffTransform(terrain, tile) *
                                 definition.ModelTransform);
            group.TerrainRevealEdges.Add(
                ClassicCliffTerrainRevealEdges(
                    terrain, tile, occupiedTiles));
            resolvedTiles.Add(tile);
        }

        var root = new Node3D { Name = "ClassicCliffMeshes" };
        foreach (var (key, group) in groups)
        {
            var parts = SplitClassicCliffMesh(group.Definition.Mesh);
            var baseName =
                $"Cliff_{Path.GetFileNameWithoutExtension(key.AssetKey)}" +
                $"_C{key.CliffStyleId}";
            AddClassicCliffBatch(
                root,
                baseName + "_Opaque",
                parts.Opaque,
                provider.ClassicCliffMaterial(key.CliffStyleId),
                group,
                useCustomData: false,
                GeometryInstance3D.ShadowCastingSetting.On);
            AddClassicCliffBatch(
                root,
                baseName + "_Reveal",
                parts.Reveal,
                provider.ClassicCliffRevealMaterial(key.CliffStyleId),
                group,
                useCustomData: true,
                GeometryInstance3D.ShadowCastingSetting.Off);
        }
        if (root.GetChildCount() > 0)
        {
            _classicCliffRoot = root;
            AddChild(root);
        }
        else
        {
            root.Free();
        }
        var seamMap = TerrainClassicCliffSeamMap.Build(
            terrain, resolvedTiles);
        GD.Print(
            $"TERRAIN_CLASSIC_CLIFF_LAYOUT hash={layout.StableHashText} " +
            $"candidates={layout.Diagnostics.CandidateTiles} " +
            $"selected={layout.Diagnostics.SelectedTiles} " +
            $"resolved={seamMap.CoveredClassicTiles} batches={groups.Count} " +
            $"raisedGroundCutQuads={seamMap.CoveredGroundQuadrants} " +
            $"biasedFootprintQuads={seamMap.CoveredFootprintQuadrants} " +
            $"rampFallback={layout.Diagnostics.RampFallbackTiles} " +
            $"heightFallback={layout.Diagnostics.UnsupportedHeightTiles} " +
            $"missing={layout.Diagnostics.MissingAssetTiles}");
        return seamMap;
    }

    private static ClassicCliffMeshParts SplitClassicCliffMesh(Mesh source)
    {
        if (source is not ArrayMesh sourceMesh)
        {
            return new ClassicCliffMeshParts(
                (Mesh)source.Duplicate(), new ArrayMesh());
        }
        var opaque = new ArrayMesh();
        var reveal = new ArrayMesh();
        for (var surface = 0;
             surface < sourceMesh.GetSurfaceCount();
             surface++)
        {
            var primitive = sourceMesh.SurfaceGetPrimitiveType(surface);
            var sourceArrays = sourceMesh.SurfaceGetArrays(surface);
            if (primitive != Mesh.PrimitiveType.Triangles)
            {
                opaque.AddSurfaceFromArrays(primitive, sourceArrays);
                continue;
            }
            var vertices = sourceArrays[(int)Mesh.ArrayType.Vertex]
                .AsVector3Array();
            var indices = sourceArrays[(int)Mesh.ArrayType.Index]
                .AsInt32Array();
            if (indices.Length == 0)
                indices = Enumerable.Range(0, vertices.Length).ToArray();
            var opaqueIndices = new List<int>(indices.Length);
            var revealIndices = new List<int>(indices.Length / 3);
            for (var index = 0; index + 2 < indices.Length; index += 3)
            {
                var a = indices[index];
                var b = indices[index + 1];
                var c = indices[index + 2];
                var maximumHeight = MathF.Max(
                    vertices[a].Z,
                    MathF.Max(vertices[b].Z, vertices[c].Z));
                var target = maximumHeight <=
                             ClassicCliffApronMaximumLocalHeight
                    ? revealIndices
                    : opaqueIndices;
                target.Add(a);
                target.Add(b);
                target.Add(c);
            }
            AddFilteredSurface(opaque, sourceMesh, surface, opaqueIndices);
            AddFilteredSurface(reveal, sourceMesh, surface, revealIndices);
        }
        return new ClassicCliffMeshParts(opaque, reveal);
    }

    private static void AddFilteredSurface(
        ArrayMesh target,
        ArrayMesh source,
        int surface,
        List<int> indices)
    {
        if (indices.Count == 0) return;
        var arrays = source.SurfaceGetArrays(surface);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var index in indices)
        {
            if (normals.Length == vertices.Length)
                tool.SetNormal(normals[index]);
            if (uvs.Length == vertices.Length)
                tool.SetUV(uvs[index]);
            tool.AddVertex(vertices[index]);
        }
        tool.Index();
        tool.Commit(target);
    }

    private static void AddClassicCliffBatch(
        Node3D root,
        string name,
        Mesh mesh,
        Material material,
        ClassicCliffGroup group,
        bool useCustomData,
        GeometryInstance3D.ShadowCastingSetting shadowCasting)
    {
        if (mesh.GetSurfaceCount() == 0) return;
        if (mesh is ArrayMesh arrayMesh)
        {
            for (var surface = 0;
                 surface < arrayMesh.GetSurfaceCount();
                 surface++)
            {
                arrayMesh.SurfaceSetMaterial(surface, material);
            }
        }
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            UseCustomData = useCustomData,
            InstanceCount = group.Transforms.Count
        };
        for (var index = 0; index < group.Transforms.Count; index++)
        {
            multiMesh.SetInstanceTransform(index, group.Transforms[index]);
            if (useCustomData)
            {
                multiMesh.SetInstanceCustomData(
                    index, group.TerrainRevealEdges[index]);
            }
        }
        root.AddChild(new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multiMesh,
            MaterialOverride = material,
            CastShadow = shadowCasting
        });
    }

    private Transform3D ClassicCliffTransform(
        TerrainMapSnapshot terrain,
        TerrainClassicCliffTile tile)
    {
        const float exportedWar3TileWorldSize = 1.28f;
        var origin = ClassicCliffOrigin(terrain, tile);
        var fit = new Vector3(
            _cellWorldSize / exportedWar3TileWorldSize,
            _cliffWorldHeight / exportedWar3TileWorldSize,
            _cellWorldSize / exportedWar3TileWorldSize);
        // The exporter converts Warcraft XY ground coordinates to a Godot
        // root whose local X/Z are original X/-Y. HiveWE's cliff convention
        // is X=original Y and Y=-original X, so this axis remap preserves the
        // model filename's TL/TR/BR/BL ordering instead of transposing it.
        var basis = new Basis(
            new Vector3(0f, 0f, -fit.X),
            new Vector3(0f, fit.Y, 0f),
            new Vector3(-fit.Z, 0f, 0f));
        return new Transform3D(basis, origin);
    }

    internal static Vector3 ClassicCliffOrigin(
        TerrainMapSnapshot terrain,
        TerrainClassicCliffTile tile)
    {
        var bounds = terrain.CellBounds(tile.Column, tile.Row);
        var centre = new System.Numerics.Vector2(
            bounds.Min.X + terrain.CellSize * 0.5f,
            bounds.Min.Y + terrain.CellSize * 0.5f);
        return SimPlane3DTransform.ToWorld(
            centre,
            SimPlane3DTransform.ToWorldLength(
                tile.BaseLevel * terrain.CliffLevelHeight));
    }

    internal static Color ClassicCliffTerrainRevealEdges(
        TerrainMapSnapshot terrain,
        TerrainClassicCliffTile tile,
        IReadOnlySet<(int Column, int Row)> occupiedTiles)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(occupiedTiles);
        // Each channel owns one model border. Reveal is safe only where both
        // edge endpoints are at the base level and no neighbouring cliff tile
        // is welded to that border: red=top, green=right, blue=bottom,
        // alpha=left. Shared cliff-to-cliff borders remain fully opaque.
        return new Color(
            CanReveal(
                tile.Column, tile.Row + 1,
                tile.Column + 1, tile.Row + 1,
                tile.Column, tile.Row + 1),
            CanReveal(
                tile.Column + 1, tile.Row + 1,
                tile.Column + 1, tile.Row,
                tile.Column + 1, tile.Row),
            CanReveal(
                tile.Column + 1, tile.Row,
                tile.Column, tile.Row,
                tile.Column, tile.Row - 1),
            CanReveal(
                tile.Column, tile.Row,
                tile.Column, tile.Row + 1,
                tile.Column - 1, tile.Row));

        float CanReveal(
            int firstColumn,
            int firstRow,
            int secondColumn,
            int secondRow,
            int neighbourColumn,
            int neighbourRow) =>
            !occupiedTiles.Contains((neighbourColumn, neighbourRow)) &&
            terrain.Cell(firstColumn, firstRow).CliffLevel == tile.BaseLevel &&
            terrain.Cell(secondColumn, secondRow).CliffLevel == tile.BaseLevel
                ? 1f
                : 0f;
    }

    private void AppendSurface(
        ArrayMesh mesh,
        TerrainMapSnapshot terrain,
        TerrainSurfaceDefinition surface,
        TerrainClassicCliffSeamMap? cliffSeams)
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
                var geometry = GroundCellGeometry(terrain, column, row);
                var mask = cliffSeams?.GroundQuadrantMask(column, row) ?? 0;
                var footprint =
                    cliffSeams?.ClassicFootprintMask(column, row) ?? 0;
                added |= AppendGroundCell(
                    tool, geometry, mask, footprint);
            }
        }
        if (added)
            tool.Commit(mesh);
    }

    private void AppendDualGridSurface(
        ArrayMesh mesh,
        TerrainMapSnapshot terrain,
        IRts3DTerrainDualGridMaterialProvider dualGridProvider,
        TerrainVisualLayerMap? authoredVisualMap,
        TerrainClassicCliffSeamMap? cliffSeams,
        IRts3DTerrainClassicCliffProvider? classicCliffProvider)
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
        var seamPointOverrides = 0;
        if (cliffSeams is not null && classicCliffProvider is not null)
        {
            visualMap = cliffSeams.BuildGroundTransitionMap(
                terrain,
                visualMap,
                cliffStyle =>
                    classicCliffProvider.TryGetClassicCliffGroundLayer(
                    cliffStyle, out var layer)
                    ? layer
                    : null,
                out seamPointOverrides);
        }
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
                var visualCell = visualMap.Cell(column, row);
                var encoded = new Vector2(
                    visualCell.PackedLayerMasks,
                    visualCell.BaseVariation);
                var geometry = GroundCellGeometry(terrain, column, row);
                added |= AppendDualGridGroundCell(
                    tool, geometry,
                    cliffSeams?.GroundQuadrantMask(column, row) ?? 0,
                    cliffSeams?.ClassicFootprintMask(column, row) ?? 0,
                    encoded);
            }
        }
        if (cliffSeams is not null)
        {
            foreach (var tile in cliffSeams.Tiles)
            {
                AppendClassicCliffUnderlay(
                    tool, terrain, visualMap, tile);
                added = true;
            }
        }
        if (added)
            tool.Commit(mesh);
        GD.Print(
            $"TERRAIN_VISUAL_LAYER_MAP source={visualMap.SourceTerrainHash} " +
            $"visual={visualMap.StableHashText} " +
            $"points={visualMap.PointColumns}x{visualMap.PointRows} " +
            $"cliffGroundOverrides={seamPointOverrides}");
    }

    private void AppendClassicCliffUnderlay(
        SurfaceTool tool,
        TerrainMapSnapshot terrain,
        TerrainVisualLayerMap visualMap,
        TerrainClassicCliffTile tile)
    {
        // SC2 reveal assumes terrain exists directly below every masked cliff
        // border. Our gameplay-cell mesh is clipped on raised quadrants, so a
        // presentation-only floor closes sub-triangle gaps under the classic
        // centre-to-centre footprint. It reuses each surrounding visual cell's
        // exact dual-grid encoding and world UVs, making exterior borders join
        // the existing terrain without another material transition.
        var origin = ClassicCliffOrigin(terrain, tile) +
                     Vector3.Down * ClassicCliffUnderlayDepthBias(
                         _cliffWorldHeight);
        var half = _cellWorldSize * 0.5f;
        var centre = origin + new Vector3(half, 0f, half);
        var right = origin + new Vector3(_cellWorldSize, 0f, 0f);
        var top = origin + new Vector3(0f, 0f, _cellWorldSize);
        var topRight = origin +
                       new Vector3(_cellWorldSize, 0f, _cellWorldSize);
        AddDualGridQuad(
            tool,
            origin,
            origin + new Vector3(half, 0f, 0f),
            centre,
            origin + new Vector3(0f, 0f, half),
            Encoded(tile.Column, tile.Row));
        AddDualGridQuad(
            tool,
            origin + new Vector3(half, 0f, 0f),
            right,
            right + new Vector3(0f, 0f, half),
            centre,
            Encoded(tile.Column + 1, tile.Row));
        AddDualGridQuad(
            tool,
            origin + new Vector3(0f, 0f, half),
            centre,
            centre + new Vector3(0f, 0f, half),
            top,
            Encoded(tile.Column, tile.Row + 1));
        AddDualGridQuad(
            tool,
            centre,
            right + new Vector3(0f, 0f, half),
            topRight,
            centre + new Vector3(0f, 0f, half),
            Encoded(tile.Column + 1, tile.Row + 1));

        Vector2 Encoded(int column, int row)
        {
            var cell = visualMap.Cell(column, row);
            return new Vector2(cell.PackedLayerMasks, cell.BaseVariation);
        }
    }

    private void AppendBlendedSurface(
        ArrayMesh mesh,
        TerrainMapSnapshot terrain,
        IRts3DTerrainBlendMaterialProvider blendProvider,
        TerrainClassicCliffSeamMap? cliffSeams)
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

                var weightA = BlendWeights(
                    terrain, blendProvider, column, row, false, false);
                var weightB = BlendWeights(
                    terrain, blendProvider, column, row, true, false);
                var weightC = BlendWeights(
                    terrain, blendProvider, column, row, true, true);
                var weightD = BlendWeights(
                    terrain, blendProvider, column, row, false, true);
                var geometry = GroundCellGeometry(terrain, column, row);
                var weights = GroundCellWeights(
                    weightA, weightB, weightC, weightD);
                added |= AppendBlendGroundCell(
                    tool, geometry, weights,
                    cliffSeams?.GroundQuadrantMask(column, row) ?? 0,
                    cliffSeams?.ClassicFootprintMask(column, row) ?? 0);
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

    private void AppendCliffs(
        ArrayMesh mesh,
        TerrainMapSnapshot terrain,
        TerrainClassicCliffSeamMap? classicCliffSeams)
    {
        var tools = new Dictionary<ushort, SurfaceTool>();
        for (var row = 0; row < terrain.Rows; row++)
        {
            for (var column = 0; column < terrain.Columns; column++)
            {
                if (column + 1 < terrain.Columns)
                {
                    AppendCliffEdgeWithCoverage(
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
                            terrain.CellCornerHeight(column + 1, row, false, true)),
                        IsClassicTileCovered(
                            classicCliffSeams, column, row - 1),
                        IsClassicTileCovered(
                            classicCliffSeams, column, row));
                }
                if (row + 1 < terrain.Rows)
                {
                    AppendCliffEdgeWithCoverage(
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
                            terrain.CellCornerHeight(column, row + 1, true, false)),
                        IsClassicTileCovered(
                            classicCliffSeams, column - 1, row),
                        IsClassicTileCovered(
                            classicCliffSeams, column, row));
                }
            }
        }
        foreach (var tool in tools.Values)
            tool.Commit(mesh);
    }

    private static bool IsClassicTileCovered(
        TerrainClassicCliffSeamMap? classicCliffSeams,
        int column,
        int row) =>
        classicCliffSeams?.CoversClassicTile(column, row) == true;

    private void AppendCliffEdgeWithCoverage(
        Dictionary<ushort, SurfaceTool> tools,
        TerrainSurfaceDefinition firstSurface,
        TerrainSurfaceDefinition secondSurface,
        Vector3 firstA,
        Vector3 firstB,
        Vector3 secondA,
        Vector3 secondB,
        bool firstHalfCovered,
        bool secondHalfCovered)
    {
        if (firstHalfCovered && secondHalfCovered)
            return;
        if (!firstHalfCovered && !secondHalfCovered)
        {
            AppendCliffEdge(
                tools, firstSurface, secondSurface,
                firstA, firstB, secondA, secondB);
            return;
        }
        var firstMiddle = firstA.Lerp(firstB, 0.5f);
        var secondMiddle = secondA.Lerp(secondB, 0.5f);
        if (!firstHalfCovered)
        {
            AppendCliffEdge(
                tools, firstSurface, secondSurface,
                firstA, firstMiddle, secondA, secondMiddle);
        }
        if (!secondHalfCovered)
        {
            AppendCliffEdge(
                tools, firstSurface, secondSurface,
                firstMiddle, firstB, secondMiddle, secondB);
        }
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

    private GroundCellGeometryData GroundCellGeometry(
        TerrainMapSnapshot terrain,
        int column,
        int row)
    {
        var bounds = terrain.CellBounds(column, row);
        var bottomLeft = Point(terrain, bounds.Min.X, bounds.Min.Y,
            terrain.CellCornerHeight(column, row, false, false));
        var bottomRight = Point(terrain, bounds.Max.X, bounds.Min.Y,
            terrain.CellCornerHeight(column, row, true, false));
        var topRight = Point(terrain, bounds.Max.X, bounds.Max.Y,
            terrain.CellCornerHeight(column, row, true, true));
        var topLeft = Point(terrain, bounds.Min.X, bounds.Max.Y,
            terrain.CellCornerHeight(column, row, false, true));
        var bottomMiddle = bottomLeft.Lerp(bottomRight, 0.5f);
        var rightMiddle = bottomRight.Lerp(topRight, 0.5f);
        var topMiddle = topLeft.Lerp(topRight, 0.5f);
        var leftMiddle = bottomLeft.Lerp(topLeft, 0.5f);
        var centre = bottomMiddle.Lerp(topMiddle, 0.5f);
        return new GroundCellGeometryData(
            bottomLeft,
            bottomRight,
            topRight,
            topLeft,
            bottomMiddle,
            rightMiddle,
            topMiddle,
            leftMiddle,
            centre);
    }

    private bool AppendGroundCell(
        SurfaceTool tool,
        GroundCellGeometryData geometry,
        byte covered,
        byte footprint)
    {
        if (covered == 0 && footprint == 0)
        {
            AddGroundQuad(tool,
                geometry.BottomLeft, geometry.BottomRight,
                geometry.TopRight, geometry.TopLeft);
            return true;
        }
        var added = false;
        if ((covered & TerrainClassicCliffSeamMap.BottomLeft) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.BottomLeft, geometry.BottomMiddle,
                geometry.Centre, geometry.LeftMiddle,
                footprint, TerrainClassicCliffSeamMap.BottomLeft);
            AddGroundQuad(tool, quad.A, quad.B, quad.C, quad.D);
            added = true;
        }
        if ((covered & TerrainClassicCliffSeamMap.BottomRight) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.BottomMiddle, geometry.BottomRight,
                geometry.RightMiddle, geometry.Centre,
                footprint, TerrainClassicCliffSeamMap.BottomRight);
            AddGroundQuad(tool, quad.A, quad.B, quad.C, quad.D);
            added = true;
        }
        if ((covered & TerrainClassicCliffSeamMap.TopRight) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.Centre, geometry.RightMiddle,
                geometry.TopRight, geometry.TopMiddle,
                footprint, TerrainClassicCliffSeamMap.TopRight);
            AddGroundQuad(tool, quad.A, quad.B, quad.C, quad.D);
            added = true;
        }
        if ((covered & TerrainClassicCliffSeamMap.TopLeft) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.LeftMiddle, geometry.Centre,
                geometry.TopMiddle, geometry.TopLeft,
                footprint, TerrainClassicCliffSeamMap.TopLeft);
            AddGroundQuad(tool, quad.A, quad.B, quad.C, quad.D);
            added = true;
        }
        return added;
    }

    private bool AppendDualGridGroundCell(
        SurfaceTool tool,
        GroundCellGeometryData geometry,
        byte covered,
        byte footprint,
        Vector2 encodedCell)
    {
        if (covered == 0 && footprint == 0)
        {
            AddDualGridQuad(tool,
                geometry.BottomLeft, geometry.BottomRight,
                geometry.TopRight, geometry.TopLeft, encodedCell);
            return true;
        }
        var added = false;
        if ((covered & TerrainClassicCliffSeamMap.BottomLeft) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.BottomLeft, geometry.BottomMiddle,
                geometry.Centre, geometry.LeftMiddle,
                footprint, TerrainClassicCliffSeamMap.BottomLeft);
            AddDualGridQuad(
                tool, quad.A, quad.B, quad.C, quad.D, encodedCell);
            added = true;
        }
        if ((covered & TerrainClassicCliffSeamMap.BottomRight) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.BottomMiddle, geometry.BottomRight,
                geometry.RightMiddle, geometry.Centre,
                footprint, TerrainClassicCliffSeamMap.BottomRight);
            AddDualGridQuad(
                tool, quad.A, quad.B, quad.C, quad.D, encodedCell);
            added = true;
        }
        if ((covered & TerrainClassicCliffSeamMap.TopRight) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.Centre, geometry.RightMiddle,
                geometry.TopRight, geometry.TopMiddle,
                footprint, TerrainClassicCliffSeamMap.TopRight);
            AddDualGridQuad(
                tool, quad.A, quad.B, quad.C, quad.D, encodedCell);
            added = true;
        }
        if ((covered & TerrainClassicCliffSeamMap.TopLeft) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.LeftMiddle, geometry.Centre,
                geometry.TopMiddle, geometry.TopLeft,
                footprint, TerrainClassicCliffSeamMap.TopLeft);
            AddDualGridQuad(
                tool, quad.A, quad.B, quad.C, quad.D, encodedCell);
            added = true;
        }
        return added;
    }

    private bool AppendBlendGroundCell(
        SurfaceTool tool,
        GroundCellGeometryData geometry,
        GroundCellWeightData weights,
        byte covered,
        byte footprint)
    {
        if (covered == 0 && footprint == 0)
        {
            AddBlendQuad(tool,
                geometry.BottomLeft, geometry.BottomRight,
                geometry.TopRight, geometry.TopLeft,
                weights.BottomLeft, weights.BottomRight,
                weights.TopRight, weights.TopLeft);
            return true;
        }
        var added = false;
        if ((covered & TerrainClassicCliffSeamMap.BottomLeft) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.BottomLeft, geometry.BottomMiddle,
                geometry.Centre, geometry.LeftMiddle,
                footprint, TerrainClassicCliffSeamMap.BottomLeft);
            AddBlendQuad(tool,
                quad.A, quad.B, quad.C, quad.D,
                weights.BottomLeft, weights.BottomMiddle,
                weights.Centre, weights.LeftMiddle);
            added = true;
        }
        if ((covered & TerrainClassicCliffSeamMap.BottomRight) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.BottomMiddle, geometry.BottomRight,
                geometry.RightMiddle, geometry.Centre,
                footprint, TerrainClassicCliffSeamMap.BottomRight);
            AddBlendQuad(tool,
                quad.A, quad.B, quad.C, quad.D,
                weights.BottomMiddle, weights.BottomRight,
                weights.RightMiddle, weights.Centre);
            added = true;
        }
        if ((covered & TerrainClassicCliffSeamMap.TopRight) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.Centre, geometry.RightMiddle,
                geometry.TopRight, geometry.TopMiddle,
                footprint, TerrainClassicCliffSeamMap.TopRight);
            AddBlendQuad(tool,
                quad.A, quad.B, quad.C, quad.D,
                weights.Centre, weights.RightMiddle,
                weights.TopRight, weights.TopMiddle);
            added = true;
        }
        if ((covered & TerrainClassicCliffSeamMap.TopLeft) == 0)
        {
            var quad = BiasedGroundQuad(
                geometry.LeftMiddle, geometry.Centre,
                geometry.TopMiddle, geometry.TopLeft,
                footprint, TerrainClassicCliffSeamMap.TopLeft);
            AddBlendQuad(tool,
                quad.A, quad.B, quad.C, quad.D,
                weights.LeftMiddle, weights.Centre,
                weights.TopMiddle, weights.TopLeft);
            added = true;
        }
        return added;
    }

    private GroundQuadData BiasedGroundQuad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        byte footprint,
        byte quadrant)
    {
        if ((footprint & quadrant) == 0)
            return new GroundQuadData(a, b, c, d);
        var offset = Vector3.Down * ClassicCliffGroundDepthBias(
            _cliffWorldHeight);
        return new GroundQuadData(
            a + offset, b + offset, c + offset, d + offset);
    }

    internal static float ClassicCliffGroundDepthBias(
        float cliffWorldHeight) =>
        MathF.Max(0.0025f, cliffWorldHeight * 0.0025f);

    internal static float ClassicCliffUnderlayDepthBias(
        float cliffWorldHeight) =>
        ClassicCliffGroundDepthBias(cliffWorldHeight) * 2f;

    private static GroundCellWeightData GroundCellWeights(
        Color bottomLeft,
        Color bottomRight,
        Color topRight,
        Color topLeft)
    {
        var bottomMiddle = bottomLeft.Lerp(bottomRight, 0.5f);
        var rightMiddle = bottomRight.Lerp(topRight, 0.5f);
        var topMiddle = topLeft.Lerp(topRight, 0.5f);
        var leftMiddle = bottomLeft.Lerp(topLeft, 0.5f);
        var centre = bottomMiddle.Lerp(topMiddle, 0.5f);
        return new GroundCellWeightData(
            bottomLeft,
            bottomRight,
            topRight,
            topLeft,
            bottomMiddle,
            rightMiddle,
            topMiddle,
            leftMiddle,
            centre);
    }

    private void AddGroundQuad(
        SurfaceTool tool,
        Vector3 bottomLeft,
        Vector3 bottomRight,
        Vector3 topRight,
        Vector3 topLeft)
    {
        AddTriangle(tool, bottomLeft, topRight, bottomRight);
        AddTriangle(tool, bottomLeft, topLeft, topRight);
    }

    private void AddDualGridQuad(
        SurfaceTool tool,
        Vector3 bottomLeft,
        Vector3 bottomRight,
        Vector3 topRight,
        Vector3 topLeft,
        Vector2 encodedCell)
    {
        AddDualGridTriangle(
            tool, bottomLeft, topRight, bottomRight, encodedCell);
        AddDualGridTriangle(
            tool, bottomLeft, topLeft, topRight, encodedCell);
    }

    private void AddBlendQuad(
        SurfaceTool tool,
        Vector3 bottomLeft,
        Vector3 bottomRight,
        Vector3 topRight,
        Vector3 topLeft,
        Color weightBottomLeft,
        Color weightBottomRight,
        Color weightTopRight,
        Color weightTopLeft)
    {
        AddBlendTriangle(
            tool, bottomLeft, topRight, bottomRight,
            weightBottomLeft, weightTopRight, weightBottomRight);
        AddBlendTriangle(
            tool, bottomLeft, topLeft, topRight,
            weightBottomLeft, weightTopLeft, weightTopRight);
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

    private readonly record struct ClassicCliffGroupKey(
        string AssetKey,
        byte CliffStyleId);

    private readonly record struct ClassicCliffMeshParts(
        Mesh Opaque,
        Mesh Reveal);

    private sealed class ClassicCliffGroup(Rts3DClassicCliffMesh definition)
    {
        public Rts3DClassicCliffMesh Definition { get; } = definition;
        public List<Transform3D> Transforms { get; } = [];
        public List<Color> TerrainRevealEdges { get; } = [];
    }

    private readonly record struct GroundCellGeometryData(
        Vector3 BottomLeft,
        Vector3 BottomRight,
        Vector3 TopRight,
        Vector3 TopLeft,
        Vector3 BottomMiddle,
        Vector3 RightMiddle,
        Vector3 TopMiddle,
        Vector3 LeftMiddle,
        Vector3 Centre);

    private readonly record struct GroundCellWeightData(
        Color BottomLeft,
        Color BottomRight,
        Color TopRight,
        Color TopLeft,
        Color BottomMiddle,
        Color RightMiddle,
        Color TopMiddle,
        Color LeftMiddle,
        Color Centre);

    private readonly record struct GroundQuadData(
        Vector3 A,
        Vector3 B,
        Vector3 C,
        Vector3 D);
}
