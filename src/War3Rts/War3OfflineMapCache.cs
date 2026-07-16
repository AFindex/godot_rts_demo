using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RtsDemo.Simulation;
using War3Rts.Maps;

namespace War3Rts;

internal sealed class War3OfflineMapCacheDocument
{
    public int FormatVersion { get; set; }
    public string MapId { get; set; } = string.Empty;
    public string SourceManifestHash { get; set; } = string.Empty;
    public string SourceAssetHash { get; set; } = string.Empty;
    public string MapRuntimeHash { get; set; } = string.Empty;
    public string TerrainHash { get; set; } = string.Empty;
    public string NavigationHash { get; set; } = string.Empty;
    public float PathCellSize { get; set; }
    public int ClearanceFormatVersion { get; set; }
    public string ClearanceHash { get; set; } = string.Empty;
    public string ClearancePayloadBase64 { get; set; } = string.Empty;
    public int HotSnapshotFormatVersion { get; set; }
    public string BootstrapIdentityHash { get; set; } = string.Empty;
    public string BootstrapStateHash { get; set; } = string.Empty;
    public string BootstrapPayloadBase64 { get; set; } = string.Empty;
    public int[] PlayerWorkers { get; set; } = [];
    public int[] EnemyWorkers { get; set; } = [];
    public int[] ResourceNodes { get; set; } = [];
    public War3MapAsset BakedMap { get; set; } = new();
}

/// <summary>
/// Versioned, derived cache for a War3 map. The authored map remains the source
/// of truth: exact source-file fingerprints, resource hashes and state hashes
/// must all match before any cached payload is used.
/// </summary>
internal sealed class War3OfflineMapCache
{
    public const int CurrentFormatVersion = 1;
    public const int ScenarioBootstrapVersion = 1;
    public const float BattlefieldPathCellSize = 24f;
    public const string FileName = "map.w3cache.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly War3OfflineMapCacheDocument _document;

    private War3OfflineMapCache(
        string path,
        War3OfflineMapCacheDocument document)
    {
        Path = path;
        _document = document;
    }

    public string Path { get; }

    public static string CachePath(War3MapCatalogEntry entry) =>
        War3MapStorage.Combine(
            War3MapStorage.DirectoryName(entry.AssetPath), FileName);

    public static bool TryLoadMap(
        War3MapCatalogEntry entry,
        out War3OfflineMapCache? cache,
        out War3MapRuntime? map,
        out string reason)
    {
        cache = null;
        map = null;
        if (!TrySourceFingerprints(
                entry,
                out var manifestHash,
                out var assetHash,
                out var sourceAsset,
                out reason))
        {
            return false;
        }

        var path = CachePath(entry);
        if (!War3MapStorage.TryReadText(path, out var json, out reason))
            return false;

        War3OfflineMapCacheDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<War3OfflineMapCacheDocument>(
                json, JsonOptions);
        }
        catch (JsonException exception)
        {
            reason = $"cache_json:{exception.Message}";
            return false;
        }

        if (document is null ||
            document.FormatVersion != CurrentFormatVersion)
        {
            reason = "cache_format";
            return false;
        }
        if (!document.MapId.Equals(
                entry.Manifest.Id, StringComparison.OrdinalIgnoreCase) ||
            !document.SourceManifestHash.Equals(
                manifestHash, StringComparison.OrdinalIgnoreCase) ||
            !document.SourceAssetHash.Equals(
                assetHash, StringComparison.OrdinalIgnoreCase))
        {
            reason = "source_fingerprint";
            return false;
        }
        if (document.BakedMap is null ||
            string.IsNullOrWhiteSpace(document.BakedMap.TerrainPayloadBase64))
        {
            reason = "baked_map_missing";
            return false;
        }
        if (!War3MapCodec.TryExpand(
                document.BakedMap, out map, out var validation) ||
            map is null)
        {
            reason = $"baked_map:{validation.Summary}";
            return false;
        }
        if (!map.Metadata.Id.Equals(
                entry.Manifest.Id, StringComparison.OrdinalIgnoreCase) ||
            !map.StableHashText.Equals(
                document.MapRuntimeHash, StringComparison.OrdinalIgnoreCase) ||
            !map.Terrain.StableHashText.Equals(
                document.TerrainHash, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(sourceAsset!.RuntimeHash) &&
            !map.StableHashText.Equals(
                sourceAsset.RuntimeHash, StringComparison.OrdinalIgnoreCase))
        {
            map = null;
            reason = "baked_map_hash";
            return false;
        }

        cache = new War3OfflineMapCache(path, document);
        reason = string.Empty;
        return true;
    }

    public bool TryLoadClearance(
        NavigationMapSnapshot navigation,
        TerrainMapSnapshot terrain,
        out ClearanceBakeSnapshot? bake,
        out string reason)
    {
        bake = null;
        if (MathF.Abs(
                _document.PathCellSize - BattlefieldPathCellSize) > 0.0001f ||
            _document.ClearanceFormatVersion !=
            ClearanceBakeSnapshot.CurrentFormatVersion ||
            !_document.NavigationHash.Equals(
                navigation.StableHashText, StringComparison.OrdinalIgnoreCase) ||
            !_document.TerrainHash.Equals(
                terrain.StableHashText, StringComparison.OrdinalIgnoreCase))
        {
            reason = "clearance_identity";
            return false;
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(_document.ClearancePayloadBase64);
        }
        catch (FormatException)
        {
            reason = "clearance_base64";
            return false;
        }
        if (!ClearanceBakeSnapshot.TryDeserialize(
                payload, out bake, out var validation) ||
            bake is null)
        {
            reason = $"clearance_payload:{validation.FirstError}";
            return false;
        }
        if (bake.SourceNavigationHash != navigation.StableHash ||
            bake.SourceTerrainHash != terrain.StableHash ||
            bake.WorldBounds != terrain.Bounds ||
            MathF.Abs(bake.CellSize - BattlefieldPathCellSize) > 0.0001f ||
            !bake.StableHashText.Equals(
                _document.ClearanceHash, StringComparison.OrdinalIgnoreCase))
        {
            bake = null;
            reason = "clearance_hash";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool TryRestoreBootstrap(
        RtsSimulation simulation,
        War3MapRuntime map,
        NavigationMapSnapshot navigation,
        ClearanceBakeSnapshot bake,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies,
        out War3HumanRuntime? runtime,
        out string reason)
    {
        runtime = null;
        var identity = ComputeBootstrapIdentity(
            map, navigation, bake, buildings, production, technologies);
        if (_document.HotSnapshotFormatVersion !=
            SimulationHotSnapshot.CurrentFormatVersion ||
            !_document.BootstrapIdentityHash.Equals(
                Hex(identity), StringComparison.OrdinalIgnoreCase))
        {
            reason = "bootstrap_identity";
            return false;
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(_document.BootstrapPayloadBase64);
        }
        catch (FormatException)
        {
            reason = "bootstrap_base64";
            return false;
        }
        if (!RuntimeHotSnapshotCodec.TryDeserialize(
                payload,
                SimulationHotSnapshot.CurrentFormatVersion,
                out var payloadIdentity,
                out var state,
                out var validation) ||
            state is null)
        {
            reason = $"bootstrap_payload:{validation}";
            return false;
        }
        if (payloadIdentity != identity ||
            state.Units.Capacity != War3HumanScenario.Capacity ||
            !_document.BootstrapStateHash.Equals(
                Hex(state.StateHash), StringComparison.OrdinalIgnoreCase))
        {
            reason = "bootstrap_header";
            return false;
        }

        try
        {
            simulation.RestoreRuntimeState(state);
            if (simulation.ComputeStateHash() != state.StateHash)
            {
                reason = "bootstrap_state_hash";
                return false;
            }
            runtime = War3HumanScenario.RestoreRuntime(
                simulation,
                buildings,
                production,
                technologies,
                map,
                _document.PlayerWorkers,
                _document.EnemyWorkers,
                _document.ResourceNodes);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or InvalidOperationException or
            ArgumentException or IndexOutOfRangeException)
        {
            runtime = null;
            reason = $"bootstrap_restore:{exception.Message}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool TryWrite(
        War3MapCatalogEntry entry,
        War3MapRuntime map,
        NavigationMapSnapshot navigation,
        ClearanceBakeSnapshot bake,
        RtsSimulation simulation,
        War3HumanRuntime runtime,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies,
        out string path,
        out int byteCount,
        out string reason)
    {
        path = CachePath(entry);
        byteCount = 0;
        if (!TrySourceFingerprints(
                entry,
                out var manifestHash,
                out var assetHash,
                out _,
                out reason))
        {
            return false;
        }
        if (bake.SourceNavigationHash != navigation.StableHash ||
            bake.SourceTerrainHash != map.Terrain.StableHash ||
            MathF.Abs(bake.CellSize - BattlefieldPathCellSize) > 0.0001f)
        {
            reason = "clearance_source";
            return false;
        }

        var bakedMap = War3MapCodec.FromRuntime(
            map.Metadata, map.Terrain, map.Objects);
        if (!bakedMap.RuntimeHash.Equals(
                map.StableHashText, StringComparison.OrdinalIgnoreCase))
        {
            reason = "baked_map_roundtrip";
            return false;
        }

        var state = simulation.CaptureRuntimeState();
        var identity = ComputeBootstrapIdentity(
            map, navigation, bake, buildings, production, technologies);
        var bootstrapPayload = RuntimeHotSnapshotCodec.Serialize(
            SimulationHotSnapshot.CurrentFormatVersion, identity, state);
        var document = new War3OfflineMapCacheDocument
        {
            FormatVersion = CurrentFormatVersion,
            MapId = map.Metadata.Id,
            SourceManifestHash = manifestHash,
            SourceAssetHash = assetHash,
            MapRuntimeHash = map.StableHashText,
            TerrainHash = map.Terrain.StableHashText,
            NavigationHash = navigation.StableHashText,
            PathCellSize = BattlefieldPathCellSize,
            ClearanceFormatVersion = bake.FormatVersion,
            ClearanceHash = bake.StableHashText,
            ClearancePayloadBase64 = Convert.ToBase64String(
                bake.CanonicalBytes.Span),
            HotSnapshotFormatVersion = SimulationHotSnapshot.CurrentFormatVersion,
            BootstrapIdentityHash = Hex(identity),
            BootstrapStateHash = Hex(state.StateHash),
            BootstrapPayloadBase64 = Convert.ToBase64String(bootstrapPayload),
            PlayerWorkers = runtime.PlayerWorkers.ToArray(),
            EnemyWorkers = runtime.EnemyWorkers.ToArray(),
            ResourceNodes = runtime.ResourceNodes
                .Select(value => value.Value)
                .ToArray(),
            BakedMap = bakedMap
        };
        var json = JsonSerializer.Serialize(document, JsonOptions) +
                   Environment.NewLine;
        byteCount = Encoding.UTF8.GetByteCount(json);
        if (!War3MapStorage.TryWriteText(path, json, out reason))
            return false;

        reason = string.Empty;
        return true;
    }

    private static ulong ComputeBootstrapIdentity(
        War3MapRuntime map,
        NavigationMapSnapshot navigation,
        ClearanceBakeSnapshot bake,
        BuildingTypeCatalogSnapshot buildings,
        ProductionCatalogSnapshot production,
        TechnologyCatalogSnapshot technologies)
    {
        var hash = new StableHash64();
        hash.Add(CurrentFormatVersion);
        hash.Add(ScenarioBootstrapVersion);
        hash.Add(SimulationHotSnapshot.CurrentFormatVersion);
        hash.Add(War3HumanScenario.Capacity);
        hash.Add(BattlefieldPathCellSize);
        hash.Add(unchecked((long)StableHash64.Compute(
            Encoding.UTF8.GetBytes(map.StableHashText))));
        hash.Add(unchecked((long)map.Terrain.StableHash));
        hash.Add(unchecked((long)navigation.StableHash));
        hash.Add(unchecked((long)bake.StableHash));
        hash.Add(unchecked((long)buildings.StableHash));
        hash.Add(unchecked((long)production.StableHash));
        hash.Add(unchecked((long)technologies.StableHash));
        return hash.Value;
    }

    private static bool TrySourceFingerprints(
        War3MapCatalogEntry entry,
        out string manifestHash,
        out string assetHash,
        out War3MapAsset? sourceAsset,
        out string reason)
    {
        manifestHash = string.Empty;
        assetHash = string.Empty;
        sourceAsset = null;
        if (!War3MapStorage.TryReadText(
                entry.ManifestPath, out var manifestJson, out reason) ||
            !War3MapStorage.TryReadText(
                entry.AssetPath, out var assetJson, out reason))
        {
            return false;
        }
        if (!War3MapCodec.TryDeserialize(assetJson, out sourceAsset, out reason) ||
            sourceAsset is null)
        {
            return false;
        }
        manifestHash = Hex(StableHash64.Compute(
            Encoding.UTF8.GetBytes(NormalizeSourceText(manifestJson))));
        assetHash = Hex(StableHash64.Compute(
            Encoding.UTF8.GetBytes(NormalizeSourceText(assetJson))));
        return true;
    }

    private static string NormalizeSourceText(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string Hex(ulong value) =>
        value.ToString("X16", CultureInfo.InvariantCulture);
}
