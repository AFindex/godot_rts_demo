using Godot;
using System.Runtime.InteropServices;

namespace War3Rts;

/// <summary>
/// Immutable GPU animation atlas compiled from the sampled Warcraft poses.
/// Every pose has one row-group in each skeleton-slot atlas. A texel stores
/// one affine matrix row, so three RGBA32F texels represent a Transform3D.
/// </summary>
internal sealed class War3VatAnimationData
{
    private const int CacheMagic = 0x54415657; // WVAT
    private const int CacheVersion = 1;
    private const int MaximumAtlasWidth = 8_192;
    private readonly Dictionary<War3NodeFreePose, int> _poseIndexes;

    private War3VatAnimationData(
        Dictionary<War3NodeFreePose, int> poseIndexes,
        War3VatSkeletonAtlas?[] skeletons,
        War3VatPartAtlas parts)
    {
        _poseIndexes = poseIndexes;
        Skeletons = skeletons;
        Parts = parts;
    }

    public IReadOnlyList<War3VatSkeletonAtlas?> Skeletons { get; }
    public War3VatPartAtlas Parts { get; }
    public int PoseCount => _poseIndexes.Count;

    public int PoseIndex(War3NodeFreePose pose) =>
        _poseIndexes.TryGetValue(pose, out var index) ? index : 0;

    public static bool HasValidCacheHeader(War3NodeFreeModelAsset asset)
    {
        var path = CachePath(asset);
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            return ReadAndValidateHeader(reader, asset);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryLoadCache(
        War3NodeFreeModelAsset asset,
        out War3VatAnimationData? animation)
    {
        animation = null;
        var path = CachePath(asset);
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            RequireCache(ReadAndValidateHeader(reader, asset), "header");
            var poseIndexes = CreatePoseIndexes(asset);
            RequireCache(reader.ReadInt32() == poseIndexes.Count, "pose count");
            var skeletonCount = reader.ReadInt32();
            RequireCache(
                skeletonCount == asset.SkeletonSlotCount,
                "skeleton slot count");
            var skeletons = new War3VatSkeletonAtlas?[skeletonCount];
            for (var slot = 0; slot < skeletonCount; slot++)
            {
                if (!reader.ReadBoolean()) continue;
                var boneCount = reader.ReadInt32();
                var poseColumns = reader.ReadInt32();
                var width = reader.ReadInt32();
                var height = reader.ReadInt32();
                RequireCache(
                    boneCount > 0 && boneCount <= 1024,
                    "skeleton bone count");
                var bytes = ReadPixels(reader, width, height);
                skeletons[slot] = new War3VatSkeletonAtlas(
                    CreateTexture(
                        bytes,
                        width,
                        height,
                        $"VAT_{Path.GetFileNameWithoutExtension(asset.Source)}_S{slot}"),
                    boneCount,
                    poseColumns,
                    width,
                    height);
            }

            var partCount = reader.ReadInt32();
            var partPoseColumns = reader.ReadInt32();
            var partWidth = reader.ReadInt32();
            var partHeight = reader.ReadInt32();
            RequireCache(partCount == asset.Parts.Count, "part count");
            var partBytes = ReadPixels(reader, partWidth, partHeight);
            RequireCache(stream.Position == stream.Length, "trailing data");
            var parts = new War3VatPartAtlas(
                CreateTexture(
                    partBytes,
                    partWidth,
                    partHeight,
                    $"VATParts_{Path.GetFileNameWithoutExtension(asset.Source)}"),
                partCount,
                partPoseColumns,
                partWidth,
                partHeight);
            animation = new War3VatAnimationData(
                poseIndexes, skeletons, parts);
            GD.Print(
                $"WAR3_VAT_CACHE source={asset.Source} state=hit " +
                $"poses={poseIndexes.Count} bytes={stream.Length}");
            return true;
        }
        catch (Exception exception)
        {
            GD.PushWarning(
                $"VAT cache ignored ({asset.Source}): {exception.Message}");
            return false;
        }
    }

    public static War3VatAnimationData Build(War3NodeFreeModelAsset asset)
    {
        var poseIndexes = CreatePoseIndexes(asset);

        if (poseIndexes.Count == 0)
            throw new InvalidOperationException(
                $"VAT asset has no sampled poses: {asset.Source}");

        var orderedPoses = new War3NodeFreePose[poseIndexes.Count];
        foreach (var pair in poseIndexes) orderedPoses[pair.Value] = pair.Key;
        var skeletons = new War3VatSkeletonAtlas?[asset.SkeletonSlotCount];
        var skeletonPixels = new Half[]?[asset.SkeletonSlotCount];
        for (var slot = 0; slot < skeletons.Length; slot++)
        {
            var boneCount = ResolveBoneCount(asset, orderedPoses, slot);
            if (boneCount <= 0) continue;
            var built = BuildAtlas(
                asset, orderedPoses, slot, boneCount);
            skeletons[slot] = built.Atlas;
            skeletonPixels[slot] = built.Pixels;
        }
        var partBuild = BuildPartAtlas(asset, orderedPoses);
        var parts = partBuild.Atlas;
        TrySaveCache(
            asset,
            poseIndexes.Count,
            skeletons,
            skeletonPixels,
            parts,
            partBuild.Pixels);
        GD.Print(
            $"WAR3_VAT_ATLAS source={asset.Source} poses={orderedPoses.Length} " +
            $"slots={skeletons.Count(value => value is not null)} " +
            $"bones={string.Join(',', skeletons.Where(value => value is not null).Select(value => value!.BoneCount))}");
        return new War3VatAnimationData(poseIndexes, skeletons, parts);
    }

    private static PartAtlasBuild BuildPartAtlas(
        War3NodeFreeModelAsset asset,
        IReadOnlyList<War3NodeFreePose> poses)
    {
        var partCount = asset.Parts.Count;
        if (partCount <= 0)
            throw new InvalidOperationException(
                $"VAT asset has no model parts: {asset.Source}");
        const int texelsPerPart = 4;
        var poseWidth = checked(partCount * texelsPerPart);
        var poseColumns = Math.Max(1, MaximumAtlasWidth / poseWidth);
        var width = checked(poseWidth * poseColumns);
        var height = (poses.Count + poseColumns - 1) / poseColumns;
        if (height > 16_384)
            throw new InvalidOperationException(
                $"VAT part atlas exceeds the compatibility texture limit: " +
                $"source={asset.Source} size={width}x{height}");

        var pixels = new Half[checked(width * height * 4)];
        for (var poseIndex = 0; poseIndex < poses.Count; poseIndex++)
        {
            var pose = poses[poseIndex];
            var poseColumn = poseIndex % poseColumns;
            var y = poseIndex / poseColumns;
            for (var part = 0; part < partCount; part++)
            {
                var partPose = part < pose.Parts.Length
                    ? pose.Parts[part]
                    : new War3NodeFreePartPose(
                        Transform3D.Identity, default, false);
                var x = poseColumn * poseWidth + part * texelsPerPart;
                WriteTransform(pixels, width, x, y, partPose.LocalTransform);
                WritePixel(pixels, width, x + 3, y,
                    partPose.Visible ? 1f : 0f, 0f, 0f, 0f);
            }
        }

        var image = Image.CreateFromData(
            width,
            height,
            false,
            Image.Format.Rgbah,
            MemoryMarshal.AsBytes(pixels.AsSpan()));
        var texture = ImageTexture.CreateFromImage(image);
        texture.ResourceName =
            $"VATParts_{Path.GetFileNameWithoutExtension(asset.Source)}";
        return new PartAtlasBuild(
            new War3VatPartAtlas(
                texture, partCount, poseColumns, width, height),
            pixels);
    }

    private static int ResolveBoneCount(
        War3NodeFreeModelAsset asset,
        IReadOnlyList<War3NodeFreePose> poses,
        int slot)
    {
        foreach (var pose in poses)
        {
            var skeleton = ResolveSkeleton(asset, pose, slot);
            if (skeleton.IsValid)
                return RenderingServer.SkeletonGetBoneCount(skeleton);
        }
        return 0;
    }

    private static SkeletonAtlasBuild BuildAtlas(
        War3NodeFreeModelAsset asset,
        IReadOnlyList<War3NodeFreePose> poses,
        int slot,
        int boneCount)
    {
        var poseWidth = checked(boneCount * 3);
        var poseColumns = Math.Max(1, MaximumAtlasWidth / poseWidth);
        var width = checked(poseWidth * poseColumns);
        var height = (poses.Count + poseColumns - 1) / poseColumns;
        if (height > 16_384)
            throw new InvalidOperationException(
                $"VAT atlas exceeds the compatibility texture limit: " +
                $"source={asset.Source} slot={slot} size={width}x{height}");

        var pixels = new Half[checked(width * height * 4)];
        for (var poseIndex = 0; poseIndex < poses.Count; poseIndex++)
        {
            var skeleton = ResolveSkeleton(asset, poses[poseIndex], slot);
            var availableBones = skeleton.IsValid
                ? RenderingServer.SkeletonGetBoneCount(skeleton)
                : 0;
            for (var bone = 0; bone < boneCount; bone++)
            {
                var transform = bone < availableBones
                    ? RenderingServer.SkeletonBoneGetTransform(skeleton, bone)
                    : Transform3D.Identity;
                WriteTransform(
                    pixels, width, poseColumns, poseWidth,
                    poseIndex, bone, transform);
            }
        }

        var image = Image.CreateFromData(
            width,
            height,
            false,
            Image.Format.Rgbah,
            MemoryMarshal.AsBytes(pixels.AsSpan()));
        var texture = ImageTexture.CreateFromImage(image);
        texture.ResourceName =
            $"VAT_{Path.GetFileNameWithoutExtension(asset.Source)}_S{slot}";
        return new SkeletonAtlasBuild(
            new War3VatSkeletonAtlas(
                texture, boneCount, poseColumns, width, height),
            pixels);
    }

    private static Dictionary<War3NodeFreePose, int> CreatePoseIndexes(
        War3NodeFreeModelAsset asset)
    {
        var poseIndexes = new Dictionary<War3NodeFreePose, int>(
            ReferenceEqualityComparer.Instance);
        foreach (var sequence in asset.Sequences)
        foreach (var pose in sequence.Poses)
            if (!poseIndexes.ContainsKey(pose))
                poseIndexes.Add(pose, poseIndexes.Count);
        return poseIndexes;
    }

    private static void TrySaveCache(
        War3NodeFreeModelAsset asset,
        int poseCount,
        IReadOnlyList<War3VatSkeletonAtlas?> skeletons,
        IReadOnlyList<Half[]?> skeletonPixels,
        War3VatPartAtlas parts,
        Half[] partPixels)
    {
        // A headless RenderingServer does not contain authoritative animation
        // matrices. Never overwrite a cache generated by the rendered backend.
        if (DisplayServer.GetName().Equals(
                "headless", StringComparison.OrdinalIgnoreCase))
            return;
        var path = CachePath(asset);
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(temporary))
            using (var writer = new BinaryWriter(stream))
            {
                WriteHeader(writer, asset);
                writer.Write(poseCount);
                writer.Write(skeletons.Count);
                for (var slot = 0; slot < skeletons.Count; slot++)
                {
                    var atlas = skeletons[slot];
                    var pixels = skeletonPixels[slot];
                    writer.Write(atlas is not null && pixels is not null);
                    if (atlas is null || pixels is null) continue;
                    writer.Write(atlas.BoneCount);
                    writer.Write(atlas.PoseColumns);
                    writer.Write(atlas.Width);
                    writer.Write(atlas.Height);
                    WritePixels(writer, pixels);
                }
                writer.Write(parts.PartCount);
                writer.Write(parts.PoseColumns);
                writer.Write(parts.Width);
                writer.Write(parts.Height);
                WritePixels(writer, partPixels);
            }
            File.Move(temporary, path, overwrite: true);
            GD.Print(
                $"WAR3_VAT_CACHE source={asset.Source} state=built " +
                $"poses={poseCount} bytes={new FileInfo(path).Length}");
        }
        catch (Exception exception)
        {
            GD.PushWarning(
                $"VAT cache save failed ({asset.Source}): {exception.Message}");
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private static bool ReadAndValidateHeader(
        BinaryReader reader,
        War3NodeFreeModelAsset asset)
    {
        if (reader.ReadInt32() != CacheMagic ||
            reader.ReadInt32() != CacheVersion ||
            !reader.ReadString().Equals(
                asset.Source, StringComparison.OrdinalIgnoreCase))
            return false;
        var fingerprint = asset.CacheFingerprint;
        return reader.ReadInt64() == fingerprint.Length &&
               reader.ReadInt64() == fingerprint.LastWriteTicks &&
               reader.ReadInt32() == asset.Parts.Count &&
               reader.ReadInt32() == asset.SkeletonSlotCount &&
               reader.ReadInt32() == asset.Metadata.Sequences.Count;
    }

    private static void WriteHeader(
        BinaryWriter writer,
        War3NodeFreeModelAsset asset)
    {
        writer.Write(CacheMagic);
        writer.Write(CacheVersion);
        writer.Write(asset.Source);
        var fingerprint = asset.CacheFingerprint;
        writer.Write(fingerprint.Length);
        writer.Write(fingerprint.LastWriteTicks);
        writer.Write(asset.Parts.Count);
        writer.Write(asset.SkeletonSlotCount);
        writer.Write(asset.Metadata.Sequences.Count);
    }

    private static byte[] ReadPixels(
        BinaryReader reader,
        int width,
        int height)
    {
        RequireCache(
            width > 0 && width <= MaximumAtlasWidth &&
            height > 0 && height <= 16_384,
            "texture dimensions");
        var expected = checked(width * height * 4 * sizeof(ushort));
        RequireCache(reader.ReadInt32() == expected, "pixel byte count");
        var bytes = reader.ReadBytes(expected);
        RequireCache(bytes.Length == expected, "pixel payload");
        return bytes;
    }

    private static void WritePixels(BinaryWriter writer, Half[] pixels)
    {
        var bytes = MemoryMarshal.AsBytes(pixels.AsSpan());
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static ImageTexture CreateTexture(
        byte[] bytes,
        int width,
        int height,
        string resourceName)
    {
        var image = Image.CreateFromData(
            width, height, false, Image.Format.Rgbah, bytes);
        var texture = ImageTexture.CreateFromImage(image);
        texture.ResourceName = resourceName;
        return texture;
    }

    private static string CachePath(War3NodeFreeModelAsset asset)
    {
        var key = $"{asset.Source}|vat|{CacheVersion}";
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(key)))[..24];
        return Path.Combine(
            ProjectSettings.GlobalizePath("user://war3_node_free_model_cache"),
            $"{hash}.w3vat");
    }

    private static void RequireCache(bool condition, string field)
    {
        if (!condition)
            throw new InvalidDataException($"Invalid VAT cache {field}.");
    }

    private readonly record struct SkeletonAtlasBuild(
        War3VatSkeletonAtlas Atlas,
        Half[] Pixels);

    private readonly record struct PartAtlasBuild(
        War3VatPartAtlas Atlas,
        Half[] Pixels);

    private static Rid ResolveSkeleton(
        War3NodeFreeModelAsset asset,
        War3NodeFreePose pose,
        int slot)
    {
        for (var part = 0; part < asset.Parts.Count; part++)
        {
            if (asset.Parts[part].SkeletonSlot != slot ||
                (uint)part >= (uint)pose.Parts.Length)
                continue;
            var skeleton = pose.Parts[part].Skeleton;
            if (skeleton.IsValid) return skeleton;
        }
        return default;
    }

    private static void WriteTransform(
        Half[] pixels,
        int width,
        int poseColumns,
        int poseWidth,
        int poseIndex,
        int bone,
        Transform3D value)
    {
        var poseColumn = poseIndex % poseColumns;
        var y = poseIndex / poseColumns;
        var x = poseColumn * poseWidth + bone * 3;
        WritePixel(pixels, width, x, y,
            value.Basis.X.X, value.Basis.Y.X, value.Basis.Z.X, value.Origin.X);
        WritePixel(pixels, width, x + 1, y,
            value.Basis.X.Y, value.Basis.Y.Y, value.Basis.Z.Y, value.Origin.Y);
        WritePixel(pixels, width, x + 2, y,
            value.Basis.X.Z, value.Basis.Y.Z, value.Basis.Z.Z, value.Origin.Z);
    }

    private static void WriteTransform(
        Half[] pixels,
        int width,
        int x,
        int y,
        Transform3D value)
    {
        WritePixel(pixels, width, x, y,
            value.Basis.X.X, value.Basis.Y.X, value.Basis.Z.X, value.Origin.X);
        WritePixel(pixels, width, x + 1, y,
            value.Basis.X.Y, value.Basis.Y.Y, value.Basis.Z.Y, value.Origin.Y);
        WritePixel(pixels, width, x + 2, y,
            value.Basis.X.Z, value.Basis.Y.Z, value.Basis.Z.Z, value.Origin.Z);
    }

    private static void WritePixel(
        Half[] pixels,
        int width,
        int x,
        int y,
        float r,
        float g,
        float b,
        float a)
    {
        var offset = (y * width + x) * 4;
        pixels[offset] = (Half)r;
        pixels[offset + 1] = (Half)g;
        pixels[offset + 2] = (Half)b;
        pixels[offset + 3] = (Half)a;
    }
}

internal sealed record War3VatSkeletonAtlas(
    ImageTexture Texture,
    int BoneCount,
    int PoseColumns,
    int Width,
    int Height);

internal sealed record War3VatPartAtlas(
    ImageTexture Texture,
    int PartCount,
    int PoseColumns,
    int Width,
    int Height);

internal enum War3VatAppearance : byte
{
    Normal,
    Ghost
}

internal readonly record struct War3VatBatchLaneKey(
    War3VatAppearance Appearance,
    bool CastShadows);

/// <summary>
/// One data-oriented render batch per model/team-color asset. Actors own only
/// stable integer slots. All model parts are committed through one contiguous
/// MultiMesh buffer each, rather than one RenderingServer call per part/actor.
/// </summary>
internal sealed class War3VatModelBatch : IDisposable
{
    private const int BufferStride = 20;
    // Compatibility stores MultiMesh color/custom-data components as half
    // floats. Integers above 2048 therefore lose their least-significant
    // bits (TownHall Stand starts at VAT pose 3601 and was read as Birth pose
    // 3600). Split every pose index into two exactly representable channels.
    private const int PoseIndexRadix = 1024;
    private const float HiddenScale = 0.000001f;
    private readonly War3NodeFreeModelAsset _asset;
    private readonly War3VatAnimationData _animation;
    private readonly Rid _scenario;
    private readonly Dictionary<War3VatBatchLaneKey, BatchLane> _lanes = [];
    private readonly Stack<int> _freeSlots = [];
    private War3VatBatchLaneKey?[] _slotLanes = [];
    private bool[] _slotActive = [];
    private int _capacity;
    private int _nextSlot;
    private bool _disposed;

    public War3VatModelBatch(
        War3NodeFreeModelAsset asset,
        War3VatAnimationData animation,
        Rid scenario)
    {
        _asset = asset;
        _animation = animation;
        _scenario = scenario;
        Grow(16);
    }

    public int ActiveSlotCount { get; private set; }
    public int LaneCount => _lanes.Count;
    public long UploadedBytes { get; private set; }
    public int BufferUploads { get; private set; }

    public int AcquireSlot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_freeSlots.TryPop(out var reused)) return reused;
        if (_nextSlot >= _capacity) Grow(_capacity * 2);
        return _nextSlot++;
    }

    public void ReleaseSlot(int slot)
    {
        if (_disposed || (uint)slot >= (uint)_nextSlot) return;
        HideSlot(slot);
        _slotLanes[slot] = null;
        _freeSlots.Push(slot);
    }

    public void BeginSlot(
        int slot,
        War3VatBatchLaneKey laneKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)slot >= (uint)_nextSlot)
            throw new ArgumentOutOfRangeException(nameof(slot));
        var lane = EnsureLane(laneKey);
        if (_slotLanes[slot] is { } previousKey && previousKey != laneKey)
        {
            var previous = _lanes[previousKey];
            previous.Hide(slot);
            if (_slotActive[slot]) previous.Deactivate();
            if (_slotActive[slot]) ActiveSlotCount--;
            _slotActive[slot] = false;
        }
        _slotLanes[slot] = laneKey;
        if (_slotActive[slot]) return;
        _slotActive[slot] = true;
        ActiveSlotCount++;
        lane.Activate();
    }

    public void WriteActor(
        int slot,
        Transform3D transform,
        bool visible,
        int targetPose,
        int sourcePose,
        float blend,
        Color tint)
    {
        if (!_slotActive[slot] || _slotLanes[slot] is not { } laneKey)
            throw new InvalidOperationException("VAT slot must be begun before writing.");
        _lanes[laneKey].Write(
            slot,
            visible ? transform : HiddenTransform(transform),
            tint,
            targetPose,
            sourcePose,
            blend);
    }

    public void HideSlot(int slot)
    {
        if (_disposed || (uint)slot >= (uint)_nextSlot ||
            !_slotActive[slot] || _slotLanes[slot] is not { } laneKey)
            return;
        var lane = _lanes[laneKey];
        lane.Hide(slot);
        lane.Deactivate();
        _slotActive[slot] = false;
        ActiveSlotCount--;
    }

    public void Flush()
    {
        if (_disposed) return;
        UploadedBytes = 0;
        BufferUploads = 0;
        foreach (var lane in _lanes.Values)
        {
            var result = lane.Flush();
            UploadedBytes += result.Bytes;
            BufferUploads += result.Uploads;
        }
    }

    /// <summary>
    /// Builds the mesh/material lane while the map loading screen is active.
    /// Lane construction combines every source surface and creates the VAT
    /// shader materials, so leaving it lazy would charge that work to the
    /// first frame in which a building changes between ghost and normal.
    /// </summary>
    public void PrewarmLane(
        War3VatBatchLaneKey laneKey,
        bool rendererWarmup = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lane = EnsureLane(laneKey);
        if (rendererWarmup) lane.BeginRendererWarmup();
    }

    public void FinishRendererWarmup()
    {
        if (_disposed) return;
        foreach (var lane in _lanes.Values) lane.FinishRendererWarmup();
    }

    private BatchLane EnsureLane(War3VatBatchLaneKey key)
    {
        if (_lanes.TryGetValue(key, out var lane)) return lane;
        lane = new BatchLane(
            _asset, _animation, _scenario, key, _capacity);
        _lanes.Add(key, lane);
        return lane;
    }

    private void Grow(int capacity)
    {
        capacity = Math.Max(16, capacity);
        if (capacity <= _capacity) return;
        Array.Resize(ref _slotLanes, capacity);
        Array.Resize(ref _slotActive, capacity);
        _capacity = capacity;
        foreach (var lane in _lanes.Values) lane.Grow(capacity);
    }

    private static Transform3D HiddenTransform(Transform3D value) =>
        new(Basis.FromScale(Vector3.One * HiddenScale), value.Origin);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var lane in _lanes.Values) lane.Dispose();
        _lanes.Clear();
        _slotLanes = [];
        _slotActive = [];
        _freeSlots.Clear();
        ActiveSlotCount = 0;
    }

    private sealed class BatchLane : IDisposable
    {
        private readonly MultiMesh _multiMesh;
        private Mesh _renderMesh;
        private Rid _instance;
        private float[] _buffer;
        private bool _dirty;
        private int _active;
        private int _capacity;
        private bool _renderVisible;
        private bool _rendererWarmup;
        private bool _disposed;

        public BatchLane(
            War3NodeFreeModelAsset asset,
            War3VatAnimationData animation,
            Rid scenario,
            War3VatBatchLaneKey key,
            int capacity)
        {
            _capacity = capacity;
            _renderMesh = CreateRenderMesh(asset, animation, key.Appearance);
            _multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = _renderMesh,
                InstanceCount = capacity
            };
            _buffer = CreateHiddenBuffer(capacity);
            _instance = RenderingServer.InstanceCreate2(
                _multiMesh.GetRid(), scenario);
            RenderingServer.InstanceGeometrySetCastShadowsSetting(
                _instance,
                key.CastShadows
                    ? RenderingServer.ShadowCastingSetting.On
                    : RenderingServer.ShadowCastingSetting.Off);
            RenderingServer.InstanceSetIgnoreCulling(_instance, true);
            RenderingServer.InstanceSetVisible(_instance, false);
            _dirty = true;
        }

        private static Mesh CreateRenderMesh(
            War3NodeFreeModelAsset asset,
            War3VatAnimationData animation,
            War3VatAppearance appearance)
        {
            var combined = new ArrayMesh
            {
                ResourceName =
                    $"VATCombined_{Path.GetFileNameWithoutExtension(asset.Source)}"
            };
            var eightWeightSurfaces = 0;
            var collapsedLayerPairs = 0;
            for (var part = 0; part < asset.Parts.Count; part++)
            {
                var sourcePart = asset.Parts[part];
                if (sourcePart.Mesh is not ArrayMesh sourceMesh)
                    throw new InvalidOperationException(
                        $"VAT rendering requires an ArrayMesh: " +
                        $"{sourcePart.Mesh.ResourceName}");
                var materials = War3VatMaterialFactory.CreatePartMaterials(
                    sourcePart, animation, appearance, part);
                for (var surface = 0;
                     surface < sourceMesh.GetSurfaceCount();
                     surface++)
                {
                    var sourceSurface = surface;
                    var format = sourceMesh.SurfaceGetFormat(surface);
                    if ((format & Mesh.ArrayFormat.FlagUse8BoneWeights) != 0)
                        eightWeightSurfaces++;
                    var material = materials[surface];
                    if (surface + 1 < sourceMesh.GetSurfaceCount() &&
                        SurfaceGeometryMatches(
                            sourceMesh, surface, surface + 1) &&
                        War3VatMaterialFactory.TryCreateLayeredMaterial(
                            sourcePart,
                            animation,
                            appearance,
                            part,
                            surface,
                            surface + 1,
                            out var layeredMaterial))
                    {
                        // Warcraft team-color materials are commonly exported
                        // as two identical primitives: an opaque replaceable-
                        // color underlay followed by a textured alpha overlay.
                        // Keeping both primitives in a MultiMesh leaves their
                        // coplanar transparent pass vulnerable to depth/sort
                        // flicker. Composite the pair in one opaque pass.
                        material = layeredMaterial;
                        surface++;
                        collapsedLayerPairs++;
                    }
                    combined.AddSurfaceFromArrays(
                        sourceMesh.SurfaceGetPrimitiveType(sourceSurface),
                        sourceMesh.SurfaceGetArrays(sourceSurface),
                        flags: format);
                    combined.SurfaceSetMaterial(
                        combined.GetSurfaceCount() - 1,
                        material);
                }
            }
            GD.Print(
                $"WAR3_VAT_COMBINED source={asset.Source} " +
                $"parts={asset.Parts.Count} surfaces={combined.GetSurfaceCount()} " +
                $"weights8={eightWeightSurfaces} layered_pairs={collapsedLayerPairs} " +
                $"appearance={appearance}");
            return combined;
        }

        private static bool SurfaceGeometryMatches(
            ArrayMesh mesh,
            int leftSurface,
            int rightSurface)
        {
            if (mesh.SurfaceGetPrimitiveType(leftSurface) !=
                mesh.SurfaceGetPrimitiveType(rightSurface) ||
                mesh.SurfaceGetFormat(leftSurface) !=
                mesh.SurfaceGetFormat(rightSurface) ||
                mesh.SurfaceGetArrayLen(leftSurface) !=
                mesh.SurfaceGetArrayLen(rightSurface) ||
                mesh.SurfaceGetArrayIndexLen(leftSurface) !=
                mesh.SurfaceGetArrayIndexLen(rightSurface))
                return false;

            var left = mesh.SurfaceGetArrays(leftSurface);
            var right = mesh.SurfaceGetArrays(rightSurface);
            return left[(int)Mesh.ArrayType.Vertex].AsVector3Array()
                       .AsSpan().SequenceEqual(
                           right[(int)Mesh.ArrayType.Vertex]
                               .AsVector3Array()) &&
                   left[(int)Mesh.ArrayType.Normal].AsVector3Array()
                       .AsSpan().SequenceEqual(
                           right[(int)Mesh.ArrayType.Normal]
                               .AsVector3Array()) &&
                   left[(int)Mesh.ArrayType.TexUV].AsVector2Array()
                       .AsSpan().SequenceEqual(
                           right[(int)Mesh.ArrayType.TexUV]
                               .AsVector2Array()) &&
                   left[(int)Mesh.ArrayType.Bones].AsInt32Array()
                       .AsSpan().SequenceEqual(
                           right[(int)Mesh.ArrayType.Bones]
                               .AsInt32Array()) &&
                   left[(int)Mesh.ArrayType.Weights].AsFloat32Array()
                       .AsSpan().SequenceEqual(
                           right[(int)Mesh.ArrayType.Weights]
                               .AsFloat32Array()) &&
                   left[(int)Mesh.ArrayType.Index].AsInt32Array()
                       .AsSpan().SequenceEqual(
                           right[(int)Mesh.ArrayType.Index]
                               .AsInt32Array());
        }

        public void Activate()
        {
            _active++;
            if (_renderVisible) return;
            _renderVisible = true;
            RenderingServer.InstanceSetVisible(_instance, true);
        }

        public void Deactivate()
        {
            _active = Math.Max(0, _active - 1);
            if (_active > 0 || _rendererWarmup || !_renderVisible) return;
            _renderVisible = false;
            RenderingServer.InstanceSetVisible(_instance, false);
        }

        public void BeginRendererWarmup()
        {
            if (_disposed) return;
            // Upload a fully hidden instance buffer, but expose the render
            // instance for the loading frames. Godot can then compile the
            // actual normal/transparent VAT pipelines before gameplay.
            if (_dirty)
            {
                RenderingServer.MultimeshSetBuffer(
                    _multiMesh.GetRid(), _buffer.AsSpan());
                _dirty = false;
            }
            _rendererWarmup = true;
            if (_renderVisible) return;
            _renderVisible = true;
            RenderingServer.InstanceSetVisible(_instance, true);
        }

        public void FinishRendererWarmup()
        {
            if (_disposed || !_rendererWarmup) return;
            _rendererWarmup = false;
            if (_active > 0 || !_renderVisible) return;
            _renderVisible = false;
            RenderingServer.InstanceSetVisible(_instance, false);
        }

        public void Write(
            int slot,
            Transform3D transform,
            Color tint,
            int targetPose,
            int sourcePose,
            float blend)
        {
            var offset = slot * BufferStride;
            WriteTransform(_buffer, offset, transform);
            _buffer[offset + 12] = tint.R;
            _buffer[offset + 13] = tint.G;
            _buffer[offset + 14] = tint.B;
            // COLOR.a carries the blend weight. Normal actors are opaque and
            // ghost alpha is owned by the ghost material, so tint alpha does
            // not need to consume a per-instance channel.
            _buffer[offset + 15] = Math.Clamp(blend, 0f, 1f);
            WritePoseIndex(_buffer, offset + 16, targetPose);
            WritePoseIndex(_buffer, offset + 18, sourcePose);
            _dirty = true;
        }

        private static void WritePoseIndex(
            float[] buffer,
            int offset,
            int pose)
        {
            pose = Math.Max(0, pose);
            buffer[offset] = pose % PoseIndexRadix;
            buffer[offset + 1] = pose / PoseIndexRadix;
        }

        public void Hide(int slot)
        {
            var offset = slot * BufferStride;
            WriteTransform(
                _buffer, offset,
                HiddenTransform(Transform3D.Identity));
            _dirty = true;
        }

        public void Grow(int capacity)
        {
            if (capacity <= _capacity) return;
            var previous = _buffer.Length;
            Array.Resize(ref _buffer, capacity * BufferStride);
            FillHidden(_buffer, previous / BufferStride, capacity);
            _multiMesh.InstanceCount = capacity;
            _dirty = true;
            _capacity = capacity;
        }

        public (long Bytes, int Uploads) Flush()
        {
            if (!_dirty) return (0, 0);
            RenderingServer.MultimeshSetBuffer(
                _multiMesh.GetRid(), _buffer.AsSpan());
            _dirty = false;
            return (_buffer.Length * sizeof(float), 1);
        }

        private static float[] CreateHiddenBuffer(int capacity)
        {
            var output = new float[capacity * BufferStride];
            FillHidden(output, 0, capacity);
            return output;
        }

        private static void FillHidden(float[] buffer, int start, int end)
        {
            var hidden = HiddenTransform(Transform3D.Identity);
            for (var slot = start; slot < end; slot++)
            {
                var offset = slot * BufferStride;
                WriteTransform(buffer, offset, hidden);
                buffer[offset + 12] = 1f;
                buffer[offset + 13] = 1f;
                buffer[offset + 14] = 1f;
                buffer[offset + 15] = 1f;
            }
        }

        private static void WriteTransform(
            float[] buffer,
            int offset,
            Transform3D transform)
        {
            var basis = transform.Basis;
            var origin = transform.Origin;
            buffer[offset] = basis.X.X;
            buffer[offset + 1] = basis.Y.X;
            buffer[offset + 2] = basis.Z.X;
            buffer[offset + 3] = origin.X;
            buffer[offset + 4] = basis.X.Y;
            buffer[offset + 5] = basis.Y.Y;
            buffer[offset + 6] = basis.Z.Y;
            buffer[offset + 7] = origin.Y;
            buffer[offset + 8] = basis.X.Z;
            buffer[offset + 9] = basis.Y.Z;
            buffer[offset + 10] = basis.Z.Z;
            buffer[offset + 11] = origin.Z;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_instance.IsValid) RenderingServer.FreeRid(_instance);
            _instance = default;
            _renderMesh = null!;
            _buffer = [];
        }
    }
}

internal static class War3VatMaterialFactory
{
    private static readonly Dictionary<string, Shader> NormalShaders = [];
    private static readonly Dictionary<string, Shader> LayeredShaders = [];
    private static Shader? _ghostShader;
    private const string VatVertexFunctions = """
        uniform sampler2D vat_texture : filter_nearest, repeat_disable;
        uniform int vat_bone_count = 0;
        uniform int vat_pose_columns = 1;
        uniform bool vat_skinned = false;
        uniform sampler2D vat_part_texture : filter_nearest, repeat_disable;
        uniform int vat_part_count = 1;
        uniform int vat_part_pose_columns = 1;
        uniform int vat_part_index = 0;
        varying float vat_part_visible;

        float vat_pose_index(vec2 encoded) {
            return floor(encoded.x + 0.5) +
                floor(encoded.y + 0.5) * 1024.0;
        }

        vec4 vat_row(float pose_value, int bone, int row_index) {
            int pose = max(0, int(pose_value + 0.5));
            int pose_column = pose % vat_pose_columns;
            int pose_row = pose / vat_pose_columns;
            int x = pose_column * vat_bone_count * 3 + bone * 3 + row_index;
            return texelFetch(vat_texture, ivec2(x, pose_row), 0);
        }

        vec3 vat_position(
                float pose,
                vec3 value,
                uvec4 bone_indices,
                vec4 bone_weights) {
            vec4 input_value = vec4(value, 1.0);
            vec3 output_value = vec3(0.0);
            float total_weight = 0.0;
            for (int influence = 0; influence < 4; influence++) {
                float weight = bone_weights[influence];
                int bone = int(bone_indices[influence]);
                if (weight <= 0.000001 || bone < 0 || bone >= vat_bone_count) {
                    continue;
                }
                output_value += vec3(
                    dot(vat_row(pose, bone, 0), input_value),
                    dot(vat_row(pose, bone, 1), input_value),
                    dot(vat_row(pose, bone, 2), input_value)) * weight;
                total_weight += weight;
            }
            return total_weight > 0.000001 ? output_value : value;
        }

        vec3 vat_normal(
                float pose,
                vec3 value,
                uvec4 bone_indices,
                vec4 bone_weights) {
            vec3 output_value = vec3(0.0);
            float total_weight = 0.0;
            for (int influence = 0; influence < 4; influence++) {
                float weight = bone_weights[influence];
                int bone = int(bone_indices[influence]);
                if (weight <= 0.000001 || bone < 0 || bone >= vat_bone_count) {
                    continue;
                }
                output_value += vec3(
                    dot(vat_row(pose, bone, 0).xyz, value),
                    dot(vat_row(pose, bone, 1).xyz, value),
                    dot(vat_row(pose, bone, 2).xyz, value)) * weight;
                total_weight += weight;
            }
            return total_weight > 0.000001 ? normalize(output_value) : value;
        }

        vec4 vat_part_row(float pose_value, int row_index) {
            int pose = max(0, int(pose_value + 0.5));
            int pose_column = pose % vat_part_pose_columns;
            int pose_row = pose / vat_part_pose_columns;
            int x = pose_column * vat_part_count * 4 +
                vat_part_index * 4 + row_index;
            return texelFetch(vat_part_texture, ivec2(x, pose_row), 0);
        }

        vec3 vat_part_position(float pose, vec3 value) {
            vec4 input_value = vec4(value, 1.0);
            return vec3(
                dot(vat_part_row(pose, 0), input_value),
                dot(vat_part_row(pose, 1), input_value),
                dot(vat_part_row(pose, 2), input_value));
        }

        vec3 vat_part_normal(float pose, vec3 value) {
            return normalize(vec3(
                dot(vat_part_row(pose, 0).xyz, value),
                dot(vat_part_row(pose, 1).xyz, value),
                dot(vat_part_row(pose, 2).xyz, value)));
        }

        void vertex() {
            float target_pose = vat_pose_index(INSTANCE_CUSTOM.xy);
            float source_pose = vat_pose_index(INSTANCE_CUSTOM.zw);
            float blend = clamp(COLOR.a, 0.0, 1.0);
            vec3 target_vertex = VERTEX;
            vec3 source_vertex = VERTEX;
            vec3 target_normal = NORMAL;
            vec3 source_normal = NORMAL;
            if (vat_skinned) {
                source_vertex = vat_position(
                    source_pose, VERTEX, BONE_INDICES, BONE_WEIGHTS);
                target_vertex = vat_position(
                    target_pose, VERTEX, BONE_INDICES, BONE_WEIGHTS);
                source_normal = vat_normal(
                    source_pose, NORMAL, BONE_INDICES, BONE_WEIGHTS);
                target_normal = vat_normal(
                    target_pose, NORMAL, BONE_INDICES, BONE_WEIGHTS);
            }
            source_vertex = vat_part_position(source_pose, source_vertex);
            target_vertex = vat_part_position(target_pose, target_vertex);
            source_normal = vat_part_normal(source_pose, source_normal);
            target_normal = vat_part_normal(target_pose, target_normal);
            vec3 local_vertex = mix(source_vertex, target_vertex, blend);
            vec3 local_normal = normalize(mix(
                source_normal, target_normal, blend));
            float source_visible = vat_part_row(source_pose, 3).r;
            float target_visible = vat_part_row(target_pose, 3).r;
            vat_part_visible = blend < 0.5
                ? source_visible
                : target_visible;
            VERTEX = (MODELVIEW_MATRIX * vec4(local_vertex, 1.0)).xyz;
            NORMAL = normalize(mat3(MODELVIEW_MATRIX) * local_normal);
        }
        """;

    public static ShaderMaterial[] CreatePartMaterials(
        War3NodeFreeMeshPart part,
        War3VatAnimationData animation,
        War3VatAppearance appearance,
        int partIndex)
    {
        var materials = new ShaderMaterial[part.Materials.Length];
        for (var surface = 0; surface < materials.Length; surface++)
        {
            var source = part.Materials[surface] as StandardMaterial3D;
            materials[surface] = appearance == War3VatAppearance.Ghost
                ? CreateGhostMaterial(part, animation, partIndex)
                : CreateNormalMaterial(part, animation, partIndex, source);
        }
        return materials;
    }

    public static bool TryCreateLayeredMaterial(
        War3NodeFreeMeshPart part,
        War3VatAnimationData animation,
        War3VatAppearance appearance,
        int partIndex,
        int baseSurface,
        int overlaySurface,
        out ShaderMaterial material)
    {
        material = null!;
        if ((uint)baseSurface >= (uint)part.Materials.Length ||
            (uint)overlaySurface >= (uint)part.Materials.Length ||
            part.Materials[baseSurface] is not StandardMaterial3D underlay ||
            part.Materials[overlaySurface] is not StandardMaterial3D overlay ||
            underlay.Transparency !=
                BaseMaterial3D.TransparencyEnum.Disabled ||
            overlay.Transparency != BaseMaterial3D.TransparencyEnum.Alpha ||
            overlay.BlendMode != BaseMaterial3D.BlendModeEnum.Mix)
            return false;

        material = appearance == War3VatAppearance.Ghost
            ? CreateGhostMaterial(part, animation, partIndex)
            : CreateLayeredNormalMaterial(
                part, animation, partIndex, underlay, overlay);
        return true;
    }

    private static ShaderMaterial CreateNormalMaterial(
        War3NodeFreeMeshPart part,
        War3VatAnimationData animation,
        int partIndex,
        StandardMaterial3D? source)
    {
        source ??= new StandardMaterial3D();
        var transparency = source.Transparency;
        var alphaScissor = transparency ==
                           BaseMaterial3D.TransparencyEnum.AlphaScissor;
        var opaque = transparency == BaseMaterial3D.TransparencyEnum.Disabled ||
                     alphaScissor;
        var renderModes = new List<string>
        {
            BlendMode(source.BlendMode),
            opaque
                ? "depth_draw_opaque"
                : "depth_draw_never",
            CullMode(source.CullMode),
            "skip_vertex_transform"
        };
        if (source.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded)
            renderModes.Add("unshaded");
        // Merely writing ALPHA moves a Godot spatial shader into the
        // transparent pipeline. Keep opaque and alpha-tested Warcraft meshes
        // in the depth-writing opaque pass; explicit discard gives alpha-test
        // semantics without making an entire MultiMesh transparency-sorted.
        var alphaFragment = alphaScissor
            ? "if (color.a < 0.5) discard;"
            : opaque
                ? string.Empty
                : "ALPHA = color.a;";
        var renderMode = string.Join(", ", renderModes);
        var shaderKey = $"{renderMode}|alpha_scissor={alphaScissor}";
        if (!NormalShaders.TryGetValue(shaderKey, out var shader))
        {
            shader = new Shader
            {
                Code = $$"""
                shader_type spatial;
                render_mode {{renderMode}};

                uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
                uniform bool has_albedo_texture = false;
                uniform vec4 albedo_color : source_color = vec4(1.0);
                uniform float material_roughness = 0.9;
                uniform float material_metallic = 0.0;

                {{VatVertexFunctions}}

                void fragment() {
                    if (vat_part_visible < 0.5) discard;
                    vec4 texel = has_albedo_texture
                        ? texture(albedo_texture, UV)
                        : vec4(1.0);
                    vec4 color = texel * albedo_color *
                        vec4(COLOR.rgb, 1.0);
                    ALBEDO = color.rgb;
                    ROUGHNESS = material_roughness;
                    METALLIC = material_metallic;
                    {{alphaFragment}}
                }
                """
            };
            NormalShaders.Add(shaderKey, shader);
        }
        var material = new ShaderMaterial { Shader = shader };
        ConfigureVat(material, part, animation, partIndex);
        material.SetShaderParameter(
            "has_albedo_texture", source.AlbedoTexture is not null);
        if (source.AlbedoTexture is not null)
            material.SetShaderParameter("albedo_texture", source.AlbedoTexture);
        material.SetShaderParameter("albedo_color", source.AlbedoColor);
        material.SetShaderParameter("material_roughness", source.Roughness);
        material.SetShaderParameter("material_metallic", source.Metallic);
        return material;
    }

    private static ShaderMaterial CreateLayeredNormalMaterial(
        War3NodeFreeMeshPart part,
        War3VatAnimationData animation,
        int partIndex,
        StandardMaterial3D underlay,
        StandardMaterial3D overlay)
    {
        var renderModes = new List<string>
        {
            "blend_mix",
            "depth_draw_opaque",
            CullMode(underlay.CullMode),
            "skip_vertex_transform"
        };
        if (underlay.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded)
            renderModes.Add("unshaded");
        var renderMode = string.Join(", ", renderModes);
        if (!LayeredShaders.TryGetValue(renderMode, out var shader))
        {
            shader = new Shader
            {
                Code = $$"""
                shader_type spatial;
                render_mode {{renderMode}};

                uniform sampler2D underlay_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
                uniform bool has_underlay_texture = false;
                uniform vec4 underlay_color : source_color = vec4(1.0);
                uniform sampler2D overlay_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
                uniform bool has_overlay_texture = false;
                uniform vec4 overlay_color : source_color = vec4(1.0);
                uniform float material_roughness = 0.9;
                uniform float material_metallic = 0.0;

                {{VatVertexFunctions}}

                void fragment() {
                    if (vat_part_visible < 0.5) discard;
                    vec4 underlay_texel = has_underlay_texture
                        ? texture(underlay_texture, UV)
                        : vec4(1.0);
                    vec4 overlay_texel = has_overlay_texture
                        ? texture(overlay_texture, UV)
                        : vec4(1.0);
                    vec4 base_color = underlay_texel * underlay_color;
                    vec4 top_color = overlay_texel * overlay_color;
                    float top_alpha = clamp(top_color.a, 0.0, 1.0);
                    ALBEDO = mix(base_color.rgb, top_color.rgb, top_alpha) *
                        COLOR.rgb;
                    ROUGHNESS = material_roughness;
                    METALLIC = material_metallic;
                }
                """
            };
            LayeredShaders.Add(renderMode, shader);
        }

        var material = new ShaderMaterial { Shader = shader };
        ConfigureVat(material, part, animation, partIndex);
        material.SetShaderParameter(
            "has_underlay_texture", underlay.AlbedoTexture is not null);
        if (underlay.AlbedoTexture is not null)
            material.SetShaderParameter(
                "underlay_texture", underlay.AlbedoTexture);
        material.SetShaderParameter("underlay_color", underlay.AlbedoColor);
        material.SetShaderParameter(
            "has_overlay_texture", overlay.AlbedoTexture is not null);
        if (overlay.AlbedoTexture is not null)
            material.SetShaderParameter(
                "overlay_texture", overlay.AlbedoTexture);
        material.SetShaderParameter("overlay_color", overlay.AlbedoColor);
        material.SetShaderParameter(
            "material_roughness", underlay.Roughness);
        material.SetShaderParameter(
            "material_metallic", underlay.Metallic);
        return material;
    }

    private static ShaderMaterial CreateGhostMaterial(
        War3NodeFreeMeshPart part,
        War3VatAnimationData animation,
        int partIndex)
    {
        _ghostShader ??= new Shader
        {
            Code = $$"""
                shader_type spatial;
                render_mode blend_mix, depth_draw_never, cull_disabled,
                    unshaded, skip_vertex_transform;

                {{VatVertexFunctions}}

                void fragment() {
                    if (vat_part_visible < 0.5) discard;
                    ALBEDO = COLOR.rgb;
                    ALPHA = 0.44;
                    EMISSION = COLOR.rgb * 0.85;
                }
                """
        };
        var material = new ShaderMaterial { Shader = _ghostShader };
        ConfigureVat(material, part, animation, partIndex);
        return material;
    }

    private static void ConfigureVat(
        ShaderMaterial material,
        War3NodeFreeMeshPart part,
        War3VatAnimationData animation,
        int partIndex)
    {
        War3VatSkeletonAtlas? atlas = null;
        if (part.SkeletonSlot >= 0 &&
            part.SkeletonSlot < animation.Skeletons.Count)
            atlas = animation.Skeletons[part.SkeletonSlot];
        material.SetShaderParameter("vat_skinned", atlas is not null);
        material.SetShaderParameter("vat_bone_count", atlas?.BoneCount ?? 0);
        material.SetShaderParameter("vat_pose_columns", atlas?.PoseColumns ?? 1);
        if (atlas is not null)
            material.SetShaderParameter("vat_texture", atlas.Texture);
        material.SetShaderParameter(
            "vat_part_texture", animation.Parts.Texture);
        material.SetShaderParameter(
            "vat_part_count", animation.Parts.PartCount);
        material.SetShaderParameter(
            "vat_part_pose_columns", animation.Parts.PoseColumns);
        material.SetShaderParameter("vat_part_index", partIndex);
    }

    private static string BlendMode(BaseMaterial3D.BlendModeEnum value) =>
        value switch
        {
            BaseMaterial3D.BlendModeEnum.Add => "blend_add",
            BaseMaterial3D.BlendModeEnum.Sub => "blend_sub",
            BaseMaterial3D.BlendModeEnum.Mul => "blend_mul",
            _ => "blend_mix"
        };

    private static string CullMode(BaseMaterial3D.CullModeEnum value) =>
        value switch
        {
            BaseMaterial3D.CullModeEnum.Front => "cull_front",
            BaseMaterial3D.CullModeEnum.Disabled => "cull_disabled",
            _ => "cull_back"
        };
}
