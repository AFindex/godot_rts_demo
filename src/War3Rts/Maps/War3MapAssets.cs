using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using RtsDemo.Simulation;
using War3Rts.Pcg;
using Vector2 = System.Numerics.Vector2;

namespace War3Rts.Maps;

public enum War3MapObjectKind : byte
{
    SpawnPoint,
    GoldMine,
    Tree,
    Decoration,
    PathingBlocker
}

public sealed class War3MapMetadata
{
    public string Id { get; set; } = "untitled";
    public string DisplayName { get; set; } = "Untitled Battlefield";
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public int RecommendedPlayers { get; set; } = 2;
    public string PreviewPath { get; set; } = string.Empty;
}

public sealed class War3MapObject
{
    public string Id { get; set; } = string.Empty;
    public War3MapObjectKind Kind { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float RadiusX { get; set; } = 16f;
    public float RadiusY { get; set; } = 16f;
    public int OwnerSlot { get; set; }
    public int Amount { get; set; }
    public string Prototype { get; set; } = string.Empty;

    [JsonIgnore]
    public Vector2 Position => new(X, Y);

    [JsonIgnore]
    public SimRect Bounds => new(
        Position - new Vector2(RadiusX, RadiusY),
        Position + new Vector2(RadiusX, RadiusY));
}

public sealed class War3MapPcgLayer
{
    public string Id { get; set; } = string.Empty;
    public string Generator { get; set; } = string.Empty;
    public int Seed { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class War3MapAsset
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public War3MapMetadata Metadata { get; set; } = new();
    public string TerrainRecipe { get; set; } = string.Empty;
    public string TerrainPayloadBase64 { get; set; } = string.Empty;
    public List<War3MapObject> Objects { get; set; } = [];
    public List<War3MapPcgLayer> PcgLayers { get; set; } = [];
    public string RuntimeHash { get; set; } = string.Empty;
}

public sealed class War3MapManifest
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public int RecommendedPlayers { get; set; } = 2;
    public string Asset { get; set; } = "map.w3map.json";
    public string Preview { get; set; } = string.Empty;
    public bool BuiltIn { get; set; }
}

public sealed record War3MapCatalogEntry(
    War3MapManifest Manifest,
    string ManifestPath,
    string AssetPath);

public sealed class War3MapRuntime
{
    internal War3MapRuntime(
        War3MapMetadata metadata,
        TerrainMapSnapshot terrain,
        War3MapObject[] objects,
        string stableHashText)
    {
        Metadata = metadata;
        Terrain = terrain;
        Objects = objects;
        StableHashText = stableHashText;
        PlayerSpawn = objects.Single(value =>
            value.Kind == War3MapObjectKind.SpawnPoint && value.OwnerSlot == 1).Position;
        EnemySpawn = objects.Single(value =>
            value.Kind == War3MapObjectKind.SpawnPoint && value.OwnerSlot == 2).Position;
    }

    public War3MapMetadata Metadata { get; }
    public TerrainMapSnapshot Terrain { get; }
    public War3MapObject[] Objects { get; }
    public string StableHashText { get; }
    public Vector2 PlayerSpawn { get; }
    public Vector2 EnemySpawn { get; }
    public IEnumerable<War3MapObject> Resources => Objects.Where(value =>
        value.Kind is War3MapObjectKind.GoldMine or War3MapObjectKind.Tree);

    public NavigationMapSnapshot CreateNavigation()
    {
        var obstacles = Objects
            .Where(value => value.Kind is War3MapObjectKind.GoldMine or
                                           War3MapObjectKind.Tree or
                                           War3MapObjectKind.PathingBlocker)
            .Select(value => value.Bounds)
            .ToArray();
        if (!NavigationMapSnapshot.TryCreate(
                NavigationMapSnapshot.CurrentFormatVersion,
                Terrain.Bounds,
                obstacles,
                [], [], [],
                out var snapshot,
                out var validation) || snapshot is null)
        {
            throw new InvalidDataException(
                $"Map navigation is invalid: {validation.FirstError}.");
        }
        return snapshot;
    }
}

public sealed record War3MapValidationIssue(
    string Code,
    string Message,
    int Column = -1,
    int Row = -1,
    string ObjectId = "");

public sealed class War3MapValidationResult(War3MapValidationIssue[] issues)
{
    public War3MapValidationIssue[] Issues { get; } = issues;
    public bool IsValid => Issues.Length == 0;
    public string Summary => IsValid
        ? "Map is valid."
        : string.Join("\n", Issues.Take(12).Select(value =>
            $"{value.Code}: {value.Message}"));
}

public static class War3MapCodec
{
    public const string BuiltInTerrainRecipe = "war3_human_battlefield_v1";
    public const string BuiltInLayoutGenerator = "war3_human_layout_v1";
    public const string DefaultMapId = "lordaeron_crossroads";
    public const string DefaultCatalogRoot = "res://war3_rts/maps";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static War3MapAsset CreateBuiltInDefaultAsset() => new()
    {
        Metadata = new War3MapMetadata
        {
            Id = DefaultMapId,
            DisplayName = "Lordaeron Crossroads",
            Description = "6400×3840 classic two-player battlefield with mirrored forests.",
            Author = "RTS Demo",
            RecommendedPlayers = 2
        },
        TerrainRecipe = BuiltInTerrainRecipe,
        PcgLayers =
        [
            new War3MapPcgLayer
            {
                Id = "default-layout",
                Generator = BuiltInLayoutGenerator,
                Seed = War3BattlefieldPcg.Seed
            }
        ]
    };

    public static War3MapAsset CreateNew(
        string id,
        string displayName,
        int columns = 64,
        int rows = 64,
        float cellSize = 32f)
    {
        if (columns is < 8 or > 512 || rows is < 8 or > 512)
            throw new ArgumentOutOfRangeException(nameof(columns));
        TerrainSurfaceDefinition[] surfaces =
        [
            new(0, "sand", "Lordaeron Dirt"),
            new(1, "metal", "Lordaeron Dirt Rough"),
            new(2, "rock", "Lordaeron Rock"),
            new(3, "badlands", "Lordaeron Grass")
        ];
        var cells = Enumerable.Repeat(
            new TerrainCell(0, 3, TerrainPathing.Ground,
                TerrainCellFlags.Buildable), columns * rows).ToArray();
        if (!TerrainMapSnapshot.TryCreate(
                new SimRect(Vector2.Zero,
                    new Vector2(columns * cellSize, rows * cellSize)),
                cellSize,
                48f,
                surfaces,
                cells,
                out var terrain,
                out var validation) || terrain is null)
        {
            throw new InvalidOperationException(validation.FirstError.ToString());
        }
        var centerY = rows * cellSize * 0.5f;
        var firstHome = new Vector2(cellSize * 6f, centerY);
        var secondHome = new Vector2((columns - 6f) * cellSize, centerY);
        return FromRuntime(
            new War3MapMetadata
            {
                Id = SanitizeId(id),
                DisplayName = displayName,
                Author = "Map Author",
                RecommendedPlayers = 2
            },
            terrain,
            [
                Spawn("spawn-1", 1, firstHome.X, firstHome.Y),
                Spawn("spawn-2", 2, secondHome.X, secondHome.Y),
                Gold("base-gold-1", firstHome + new Vector2(200f, 0f), 1, 32_000),
                Gold("base-gold-2", secondHome - new Vector2(200f, 0f), 2, 32_000),
                Tree("base-tree-1-0", firstHome + new Vector2(0f, -180f), 1),
                Tree("base-tree-1-1", firstHome + new Vector2(0f, 180f), 1),
                Tree("base-tree-2-0", secondHome + new Vector2(0f, -180f), 2),
                Tree("base-tree-2-1", secondHome + new Vector2(0f, 180f), 2)
            ]);
    }

    public static War3MapAsset FromRuntime(
        War3MapMetadata metadata,
        TerrainMapSnapshot terrain,
        IEnumerable<War3MapObject> objects)
    {
        var asset = new War3MapAsset
        {
            Metadata = metadata,
            TerrainPayloadBase64 = Convert.ToBase64String(
                terrain.CanonicalBytes.Span),
            Objects = objects.Select(CloneObject).ToList()
        };
        if (TryExpand(asset, out var runtime, out _))
            asset.RuntimeHash = runtime!.StableHashText;
        return asset;
    }

    public static string Serialize(War3MapAsset asset) =>
        JsonSerializer.Serialize(asset, JsonOptions) + System.Environment.NewLine;

    public static bool TryDeserialize(
        string json,
        out War3MapAsset? asset,
        out string error)
    {
        try
        {
            asset = JsonSerializer.Deserialize<War3MapAsset>(json, JsonOptions);
            if (asset is null)
            {
                error = "Map JSON produced no asset.";
                return false;
            }
            error = string.Empty;
            return true;
        }
        catch (JsonException exception)
        {
            asset = null;
            error = exception.Message;
            return false;
        }
    }

    public static bool TryLoad(
        string path,
        out War3MapAsset? asset,
        out string error)
    {
        if (!War3MapStorage.TryReadText(path, out var text, out error))
        {
            asset = null;
            return false;
        }
        return TryDeserialize(text, out asset, out error);
    }

    public static bool TryExpand(
        War3MapAsset asset,
        out War3MapRuntime? runtime,
        out War3MapValidationResult validation)
    {
        var issues = new List<War3MapValidationIssue>();
        if (asset.FormatVersion != War3MapAsset.CurrentFormatVersion)
            issues.Add(new("unsupported_format",
                $"Expected map format {War3MapAsset.CurrentFormatVersion}, got {asset.FormatVersion}."));
        if (string.IsNullOrWhiteSpace(asset.Metadata.Id) ||
            string.IsNullOrWhiteSpace(asset.Metadata.DisplayName))
            issues.Add(new("invalid_metadata", "Map id and display name are required."));

        TerrainMapSnapshot? terrain = null;
        if (asset.TerrainPayloadBase64.Length > 0)
        {
            try
            {
                var payload = Convert.FromBase64String(asset.TerrainPayloadBase64);
                if (!TerrainMapSnapshot.TryDeserialize(
                        payload, out terrain, out var terrainValidation) || terrain is null)
                    issues.Add(new("invalid_terrain",
                        $"Terrain payload failed: {terrainValidation.FirstError}."));
            }
            catch (FormatException exception)
            {
                issues.Add(new("invalid_terrain_payload", exception.Message));
            }
        }
        else if (asset.TerrainRecipe == BuiltInTerrainRecipe)
        {
            terrain = War3HumanBattlefield.Create(
                War3HumanScenario.WorldBounds,
                War3HumanScenario.TerrainCellSize,
                War3HumanScenario.TerrainCliffHeight);
        }
        else
        {
            issues.Add(new("missing_terrain", "Map has no terrain payload or known migration recipe."));
        }

        var objects = asset.Objects.Select(CloneObject).ToList();
        foreach (var layer in asset.PcgLayers.Where(value => value.Enabled))
        {
            if (layer.Generator == BuiltInLayoutGenerator)
            {
                if (terrain is not null)
                    objects.AddRange(CreateBuiltInLayout(terrain.Bounds));
            }
            else
            {
                issues.Add(new("unknown_pcg_layer",
                    $"PCG layer '{layer.Id}' uses unknown generator '{layer.Generator}'."));
            }
        }
        if (terrain is not null) ValidateTerrainTopology(terrain, issues);
        ValidateObjects(terrain, objects, issues);
        validation = new War3MapValidationResult([.. issues]);
        if (!validation.IsValid || terrain is null)
        {
            runtime = null;
            return false;
        }

        var hash = ComputeRuntimeHash(terrain, objects);
        runtime = new War3MapRuntime(
            CloneMetadata(asset.Metadata), terrain, [.. objects], hash);
        return true;
    }

    public static bool TrySavePackage(
        War3MapAsset asset,
        string assetPath,
        bool builtIn,
        out War3MapValidationResult validation,
        out string error)
    {
        if (!TryExpand(asset, out var runtime, out validation) || runtime is null)
        {
            error = validation.Summary;
            return false;
        }
        asset.RuntimeHash = runtime.StableHashText;
        var manifest = new War3MapManifest
        {
            Id = asset.Metadata.Id,
            DisplayName = asset.Metadata.DisplayName,
            Description = asset.Metadata.Description,
            Author = asset.Metadata.Author,
            RecommendedPlayers = asset.Metadata.RecommendedPlayers,
            Preview = asset.Metadata.PreviewPath,
            Asset = Path.GetFileName(assetPath),
            BuiltIn = builtIn
        };
        var manifestPath = War3MapStorage.Combine(
            War3MapStorage.DirectoryName(assetPath), "manifest.json");
        if (!War3MapStorage.TryWriteText(assetPath, Serialize(asset), out error))
            return false;
        if (!War3MapStorage.TryWriteText(
                manifestPath,
                JsonSerializer.Serialize(manifest, JsonOptions) + System.Environment.NewLine,
                out error))
            return false;
        return true;
    }

    public static bool TryParseManifest(
        string json,
        out War3MapManifest? manifest,
        out string error)
    {
        try
        {
            manifest = JsonSerializer.Deserialize<War3MapManifest>(json, JsonOptions);
            if (manifest is null ||
                manifest.FormatVersion != War3MapManifest.CurrentFormatVersion ||
                string.IsNullOrWhiteSpace(manifest.Id) ||
                string.IsNullOrWhiteSpace(manifest.Asset))
            {
                error = "Manifest is incomplete or uses an unsupported version.";
                manifest = null;
                return false;
            }
            error = string.Empty;
            return true;
        }
        catch (JsonException exception)
        {
            manifest = null;
            error = exception.Message;
            return false;
        }
    }

    private static void ValidateObjects(
        TerrainMapSnapshot? terrain,
        List<War3MapObject> objects,
        List<War3MapValidationIssue> issues)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in objects)
        {
            if (string.IsNullOrWhiteSpace(value.Id) || !ids.Add(value.Id))
                issues.Add(new("invalid_object_id", "Object ids must be non-empty and unique.",
                    ObjectId: value.Id));
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
                !float.IsFinite(value.RadiusX) || !float.IsFinite(value.RadiusY) ||
                value.RadiusX < 0f || value.RadiusY < 0f)
                issues.Add(new("invalid_object", $"Object '{value.Id}' has invalid geometry.",
                    ObjectId: value.Id));
            if (terrain is not null && !terrain.Bounds.Contains(value.Position))
                issues.Add(new("object_out_of_bounds", $"Object '{value.Id}' is outside the map.",
                    ObjectId: value.Id));
        }
        var spawns = objects.Where(value => value.Kind == War3MapObjectKind.SpawnPoint)
            .ToArray();
        foreach (var slot in new[] { 1, 2 })
        {
            if (spawns.Count(value => value.OwnerSlot == slot) != 1)
                issues.Add(new("missing_spawn", $"Exactly one spawn point is required for slot {slot}."));
            if (objects.Count(value => value.Kind == War3MapObjectKind.GoldMine &&
                                       value.OwnerSlot == slot) != 1)
                issues.Add(new("missing_owned_gold",
                    $"Exactly one starting gold mine is required for slot {slot}."));
            if (objects.Count(value => value.Kind == War3MapObjectKind.Tree &&
                                       value.OwnerSlot == slot) < 2)
                issues.Add(new("missing_owned_trees",
                    $"At least two starting trees are required for slot {slot}."));
        }
        if (terrain is null) return;
        foreach (var spawn in spawns)
        {
            if (!terrain.IsDiscTraversable(spawn.Position, MathF.Max(16f, spawn.RadiusX)))
                AddLocated("spawn_not_traversable",
                    $"Spawn '{spawn.Id}' is not on traversable ground.", spawn);
        }
        foreach (var resource in objects.Where(value =>
                     value.Kind is War3MapObjectKind.GoldMine or War3MapObjectKind.Tree))
        {
            foreach (var spawn in spawns)
            {
                var exclusion = MathF.Max(96f, spawn.RadiusX + resource.RadiusX + 32f);
                if (Vector2.DistanceSquared(resource.Position, spawn.Position) <
                    exclusion * exclusion)
                    AddLocated("resource_spawn_exclusion",
                        $"Resource '{resource.Id}' overlaps spawn '{spawn.Id}' exclusion zone.",
                        resource);
            }
        }
        if (spawns.Length >= 2 &&
            !GridConnected(terrain, spawns[0].Position, spawns[1].Position))
            issues.Add(new("spawn_disconnected", "Player spawn points are not path-connected."));
        return;

        void AddLocated(string code, string message, War3MapObject value)
        {
            terrain.TryCellAt(value.Position, out var column, out var row);
            issues.Add(new(code, message, column, row, value.Id));
        }
    }

    private static void ValidateTerrainTopology(
        TerrainMapSnapshot terrain,
        List<War3MapValidationIssue> issues)
    {
        for (var row = 0; row < terrain.Rows; row++)
        for (var column = 0; column < terrain.Columns; column++)
        {
            var cell = terrain.Cell(column, row);
            if (column + 1 < terrain.Columns)
                ValidateCliffEdge(column + 1, row);
            if (row + 1 < terrain.Rows)
                ValidateCliffEdge(column, row + 1);
            if (!cell.IsRamp) continue;
            var (dx, dy) = cell.RampDirection switch
            {
                TerrainRampDirection.PositiveX => (1, 0),
                TerrainRampDirection.NegativeX => (-1, 0),
                TerrainRampDirection.PositiveY => (0, 1),
                TerrainRampDirection.NegativeY => (0, -1),
                _ => (0, 0)
            };
            var lowColumn = column - dx;
            var lowRow = row - dy;
            var highColumn = column + dx;
            var highRow = row + dy;
            if ((uint)lowColumn >= (uint)terrain.Columns ||
                (uint)lowRow >= (uint)terrain.Rows ||
                (uint)highColumn >= (uint)terrain.Columns ||
                (uint)highRow >= (uint)terrain.Rows)
            {
                issues.Add(new("invalid_ramp_edge",
                    "Ramp requires in-bounds low and high neighbours.",
                    column, row));
                continue;
            }
            var low = terrain.Cell(lowColumn, lowRow);
            var high = terrain.Cell(highColumn, highRow);
            if (low.IsRamp || low.CliffLevel != cell.CliffLevel)
                issues.Add(new("invalid_ramp_low",
                    $"Ramp low neighbour must be flat level {cell.CliffLevel}.",
                    column, row));
            if (high.IsRamp || high.CliffLevel != cell.CliffLevel + 1)
                issues.Add(new("invalid_ramp_high",
                    $"Ramp high neighbour must be flat level {cell.CliffLevel + 1}.",
                    column, row));

            void ValidateCliffEdge(int nextColumn, int nextRow)
            {
                var next = terrain.Cell(nextColumn, nextRow);
                if (cell.IsRamp || next.IsRamp) return;
                if (Math.Abs(cell.CliffLevel - next.CliffLevel) <= 1) return;
                issues.Add(new("unsupported_cliff_step",
                    "Classic War3 cliff topology supports one level per edge.",
                    column, row));
            }
        }
    }

    private static bool GridConnected(
        TerrainMapSnapshot terrain,
        Vector2 start,
        Vector2 target)
    {
        if (!terrain.TryCellAt(start, out var startColumn, out var startRow) ||
            !terrain.TryCellAt(target, out var targetColumn, out var targetRow))
            return false;
        var targetIndex = targetRow * terrain.Columns + targetColumn;
        var seen = new bool[terrain.CellCount];
        var queue = new Queue<int>();
        var first = startRow * terrain.Columns + startColumn;
        seen[first] = true;
        queue.Enqueue(first);
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            if (index == targetIndex) return true;
            var column = index % terrain.Columns;
            var row = index / terrain.Columns;
            Visit(column - 1, row);
            Visit(column + 1, row);
            Visit(column, row - 1);
            Visit(column, row + 1);

            void Visit(int nextColumn, int nextRow)
            {
                if ((uint)nextColumn >= (uint)terrain.Columns ||
                    (uint)nextRow >= (uint)terrain.Rows)
                    return;
                var next = nextRow * terrain.Columns + nextColumn;
                if (seen[next]) return;
                var center = terrain.Bounds.Min + new Vector2(
                    (column + 0.5f) * terrain.CellSize,
                    (row + 0.5f) * terrain.CellSize);
                var nextCenter = terrain.Bounds.Min + new Vector2(
                    (nextColumn + 0.5f) * terrain.CellSize,
                    (nextRow + 0.5f) * terrain.CellSize);
                center = Vector2.Min(center, terrain.Bounds.Max - new Vector2(0.001f));
                nextCenter = Vector2.Min(nextCenter,
                    terrain.Bounds.Max - new Vector2(0.001f));
                if (!terrain.IsSegmentTraversable(center, nextCenter, 0f)) return;
                seen[next] = true;
                queue.Enqueue(next);
            }
        }
        return false;
    }

    private static IEnumerable<War3MapObject> CreateBuiltInLayout(SimRect bounds)
    {
        var player = new Vector2(1_280f, 1_920f);
        var enemy = new Vector2(5_120f, 1_920f);
        var layout = War3BattlefieldPcg.Generate(bounds, player, enemy);
        var neutralGolds = layout.NeutralGoldPositions.ToArray();
        var forestTrees = layout.ForestTreePositions.ToArray();
        yield return Spawn("spawn-1", 1, player.X, player.Y);
        yield return Spawn("spawn-2", 2, enemy.X, enemy.Y);
        foreach (var item in BaseResources(player, 1f, 1)) yield return item;
        foreach (var item in BaseResources(enemy, -1f, 2)) yield return item;
        var goldIndex = 0;
        foreach (var position in neutralGolds)
        {
            yield return Gold($"neutral-gold-{goldIndex++}", position, 0, 25_000);
        }
        var treeIndex = 0;
        foreach (var position in forestTrees)
        {
            yield return Tree($"pcg-tree-{treeIndex++}", position, 0);
        }
    }

    private static IEnumerable<War3MapObject> BaseResources(
        Vector2 home,
        float direction,
        int owner)
    {
        yield return Gold($"base-gold-{owner}",
            home + new Vector2(direction * 235f, 0f), owner, 32_000);
        for (var index = 0; index < 14; index++)
        {
            var row = index / 7;
            var column = index % 7;
            var jitter =
                (PcgHashNoise.Value01(index * 1.37f, row * 3.11f, 0xBA53_7EEDu) -
                 0.5f) * 12f;
            var distance = 274f + PcgHashNoise.Value01(
                index * 0.91f + 7f, row * 2.43f, 0x71EE_2026u) * 70f;
            var position = home + new Vector2(
                direction * ((column - 3f) * 46f + jitter),
                (row == 0 ? -1f : 1f) * distance);
            yield return Tree($"base-tree-{owner}-{index}", position, owner);
        }
    }

    private static War3MapObject Spawn(
        string id,
        int owner,
        float x,
        float y) => new()
    {
        Id = id,
        Kind = War3MapObjectKind.SpawnPoint,
        X = x,
        Y = y,
        RadiusX = 32f,
        RadiusY = 32f,
        OwnerSlot = owner,
        Prototype = "human_start"
    };

    private static War3MapObject Gold(
        string id,
        Vector2 position,
        int owner,
        int amount) => new()
    {
        Id = id,
        Kind = War3MapObjectKind.GoldMine,
        X = position.X,
        Y = position.Y,
        RadiusX = 52f,
        RadiusY = 42f,
        OwnerSlot = owner,
        Amount = amount,
        Prototype = "war3_gold_mine"
    };

    private static War3MapObject Tree(
        string id,
        Vector2 position,
        int owner) => new()
    {
        Id = id,
        Kind = War3MapObjectKind.Tree,
        X = position.X,
        Y = position.Y,
        RadiusX = 13f,
        RadiusY = 13f,
        OwnerSlot = owner,
        Amount = War3HumanScenario.TreeHealth,
        Prototype = "lordaeron_tree"
    };

    private static string ComputeRuntimeHash(
        TerrainMapSnapshot terrain,
        IEnumerable<War3MapObject> objects)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = terrain.StableHash ^ offset;
        foreach (var value in objects.OrderBy(value => value.Id,
                     StringComparer.Ordinal))
        {
            foreach (var item in Encoding.UTF8.GetBytes(value.Id))
                hash = unchecked((hash ^ item) * prime);
            hash = unchecked((hash ^ (byte)value.Kind) * prime);
            hash = unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(value.X)) * prime);
            hash = unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(value.Y)) * prime);
            hash = unchecked((hash ^ (uint)value.OwnerSlot) * prime);
            hash = unchecked((hash ^ (uint)value.Amount) * prime);
        }
        return hash.ToString("X16");
    }

    private static War3MapObject CloneObject(War3MapObject value) => new()
    {
        Id = value.Id,
        Kind = value.Kind,
        X = value.X,
        Y = value.Y,
        RadiusX = value.RadiusX,
        RadiusY = value.RadiusY,
        OwnerSlot = value.OwnerSlot,
        Amount = value.Amount,
        Prototype = value.Prototype
    };

    private static War3MapMetadata CloneMetadata(War3MapMetadata value) => new()
    {
        Id = value.Id,
        DisplayName = value.DisplayName,
        Description = value.Description,
        Author = value.Author,
        RecommendedPlayers = value.RecommendedPlayers,
        PreviewPath = value.PreviewPath
    };

    private static string SanitizeId(string value)
    {
        var chars = value.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '_').ToArray();
        return new string(chars).Trim('_');
    }
}

public static class War3MapCatalog
{
    public static IReadOnlyList<War3MapCatalogEntry> Enumerate(
        string root = War3MapCodec.DefaultCatalogRoot)
    {
        var entries = new List<War3MapCatalogEntry>();
        foreach (var directory in War3MapStorage.EnumerateDirectories(root))
        {
            var manifestPath = War3MapStorage.Combine(directory, "manifest.json");
            if (!War3MapStorage.TryReadText(manifestPath, out var json, out _) ||
                !War3MapCodec.TryParseManifest(json, out var manifest, out _) ||
                manifest is null)
                continue;
            entries.Add(new War3MapCatalogEntry(
                manifest,
                manifestPath,
                War3MapStorage.Combine(directory, manifest.Asset)));
        }
        return entries
            .GroupBy(value => value.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(value => value.Manifest.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool TryLoadRuntime(
        War3MapCatalogEntry entry,
        out War3MapRuntime? runtime,
        out string error)
    {
        if (!War3MapCodec.TryLoad(entry.AssetPath, out var asset, out error) ||
            asset is null)
        {
            runtime = null;
            return false;
        }
        if (!War3MapCodec.TryExpand(asset, out runtime, out var validation) ||
            runtime is null)
        {
            error = validation.Summary;
            return false;
        }
        if (!entry.Manifest.Id.Equals(
                runtime.Metadata.Id, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Manifest id '{entry.Manifest.Id}' does not match map id " +
                    $"'{runtime.Metadata.Id}'.";
            runtime = null;
            return false;
        }
        if (!string.IsNullOrWhiteSpace(asset.RuntimeHash) &&
            !asset.RuntimeHash.Equals(runtime.StableHashText,
                StringComparison.OrdinalIgnoreCase))
        {
            error = $"Runtime hash mismatch: asset {asset.RuntimeHash}, loaded " +
                    $"{runtime.StableHashText}.";
            runtime = null;
            return false;
        }
        error = string.Empty;
        return true;
    }
}

public static class War3MapStorage
{
    public static bool TryReadText(string path, out string text, out string error)
    {
        try
        {
            if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Godot.FileAccess.FileExists(path))
                {
                    text = string.Empty;
                    error = $"File does not exist: {path}";
                    return false;
                }
                text = Godot.FileAccess.GetFileAsString(path);
            }
            else
            {
                text = File.ReadAllText(path);
            }
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            text = string.Empty;
            error = exception.Message;
            return false;
        }
    }

    public static bool TryWriteText(string path, string text, out string error)
    {
        try
        {
            var native = Globalize(path);
            var directory = Path.GetDirectoryName(native);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(native, text, new UTF8Encoding(false));
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    public static IEnumerable<string> EnumerateDirectories(string root)
    {
        if (root.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            using var directory = DirAccess.Open(root);
            if (directory is null) yield break;
            directory.ListDirBegin();
            while (true)
            {
                var name = directory.GetNext();
                if (name.Length == 0) break;
                if (directory.CurrentIsDir() && name is not "." and not "..")
                    yield return Combine(root, name);
            }
            directory.ListDirEnd();
            yield break;
        }
        if (!Directory.Exists(root)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(root))
            yield return directory;
    }

    public static string Combine(string first, string second) =>
        first.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
        first.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
            ? first.TrimEnd('/') + "/" + second.TrimStart('/', '\\')
            : Path.Combine(first, second);

    public static string DirectoryName(string path)
    {
        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            var index = path.LastIndexOf('/');
            return index > 5 ? path[..index] : path;
        }
        return Path.GetDirectoryName(path) ?? string.Empty;
    }

    private static string Globalize(string path) =>
        path.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
            ? ProjectSettings.GlobalizePath(path)
            : path;
}
