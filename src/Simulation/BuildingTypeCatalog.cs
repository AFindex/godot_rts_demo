using System.Text;

namespace RtsDemo.Simulation;

public enum BuildingTypeCatalogErrorCode
{
    None = 0,
    UnsupportedFormatVersion = 3001,
    EmptyCatalog = 3101,
    NonDenseTypeId = 3102,
    InvalidType = 3103,
    DuplicateTypeName = 3104,
    InvalidFunctionContract = 3105,
    MissingResourceAsset = 3201,
    NullResourceElement = 3202
}

public readonly record struct BuildingTypeCatalogValidationIssue(
    BuildingTypeCatalogErrorCode Code,
    int ElementIndex,
    string Message);

public sealed class BuildingTypeCatalogValidationResult
{
    public BuildingTypeCatalogValidationResult(
        BuildingTypeCatalogValidationIssue[] issues) => Issues = issues;

    public BuildingTypeCatalogValidationIssue[] Issues { get; }
    public bool IsValid => Issues.Length == 0;
    public BuildingTypeCatalogErrorCode FirstError =>
        IsValid ? BuildingTypeCatalogErrorCode.None : Issues[0].Code;
}

/// <summary>
/// Immutable, Godot-independent contract consumed by construction gameplay.
/// IDs are dense so commands and UI can use direct indexed lookup.
/// </summary>
public sealed class BuildingTypeCatalogSnapshot
{
    public const int CurrentFormatVersion = 1;

    private readonly BuildingTypeProfile[] _types;
    private readonly byte[] _canonicalBytes;

    private BuildingTypeCatalogSnapshot(
        int formatVersion,
        BuildingTypeProfile[] types)
    {
        FormatVersion = formatVersion;
        _types = types;
        _canonicalBytes = BuildCanonicalBytes();
        StableHash = ComputeStableHash(_canonicalBytes);
    }

    public int FormatVersion { get; }
    public ReadOnlySpan<BuildingTypeProfile> Types => _types;
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");

    public BuildingTypeProfile Type(int id) => _types[id];

    public static bool TryCreate(
        int formatVersion,
        ReadOnlySpan<BuildingTypeProfile> types,
        out BuildingTypeCatalogSnapshot? snapshot,
        out BuildingTypeCatalogValidationResult validation)
    {
        var copy = types.ToArray();
        validation = Validate(formatVersion, copy);
        if (!validation.IsValid)
        {
            snapshot = null;
            return false;
        }

        snapshot = new BuildingTypeCatalogSnapshot(formatVersion, copy);
        return true;
    }

    private static BuildingTypeCatalogValidationResult Validate(
        int formatVersion,
        BuildingTypeProfile[] types)
    {
        var issues = new List<BuildingTypeCatalogValidationIssue>();
        if (formatVersion != CurrentFormatVersion)
        {
            Add(issues, BuildingTypeCatalogErrorCode.UnsupportedFormatVersion, -1,
                $"Expected building type format {CurrentFormatVersion}, got {formatVersion}.");
        }
        if (types.Length == 0)
        {
            Add(issues, BuildingTypeCatalogErrorCode.EmptyCatalog, -1,
                "At least one building type is required.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < types.Length; index++)
        {
            var type = types[index];
            if (type.Id != index)
            {
                Add(issues, BuildingTypeCatalogErrorCode.NonDenseTypeId, index,
                    $"Building type ID must equal dense index {index}.");
            }
            if (string.IsNullOrWhiteSpace(type.Name) ||
                !IsPositive(type.Size.X) || !IsPositive(type.Size.Y) ||
                !Enum.IsDefined(type.Function) ||
                !Enum.IsDefined(type.MinimumPassageClass) ||
                !Enum.IsDefined(type.ConstructionMethod) ||
                !type.Cost.IsValid || !IsPositive(type.BuildSeconds) ||
                !IsPositive(type.MaximumHealth) || type.SupplyProvided < 0 ||
                !float.IsFinite(type.CancelRefundFraction) ||
                type.CancelRefundFraction is < 0f or > 1f)
            {
                Add(issues, BuildingTypeCatalogErrorCode.InvalidType, index,
                    "Name, enums, size, cost, build time, health, supply and refund must be valid.");
            }
            if (!string.IsNullOrWhiteSpace(type.Name) && !names.Add(type.Name))
            {
                Add(issues, BuildingTypeCatalogErrorCode.DuplicateTypeName, index,
                    $"Duplicate building type name '{type.Name}'.");
            }

            var functionValid = type.Function switch
            {
                BuildingFunctionKind.Refinery =>
                    type.RequiresVespeneNode && type.SupplyProvided == 0,
                BuildingFunctionKind.Supply =>
                    !type.RequiresVespeneNode && type.SupplyProvided > 0,
                BuildingFunctionKind.TownHall => !type.RequiresVespeneNode,
                BuildingFunctionKind.Production =>
                    !type.RequiresVespeneNode && type.SupplyProvided == 0,
                BuildingFunctionKind.Research =>
                    !type.RequiresVespeneNode && type.SupplyProvided == 0,
                _ => false
            };
            if (!functionValid)
            {
                Add(issues, BuildingTypeCatalogErrorCode.InvalidFunctionContract, index,
                    "Refineries require a Vespene node; only supply/town-hall types may provide supply.");
            }
        }

        return new BuildingTypeCatalogValidationResult(issues.ToArray());
    }

    private byte[] BuildCanonicalBytes()
    {
        using var stream = new MemoryStream(512);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatVersion);
        writer.Write(_types.Length);
        foreach (var type in _types)
        {
            writer.Write(type.Id);
            WriteString(writer, type.Name);
            writer.Write((byte)type.Function);
            writer.Write(BitConverter.SingleToInt32Bits(type.Size.X));
            writer.Write(BitConverter.SingleToInt32Bits(type.Size.Y));
            writer.Write((byte)type.MinimumPassageClass);
            writer.Write(type.Cost.Minerals);
            writer.Write(type.Cost.VespeneGas);
            writer.Write(type.Cost.Supply);
            writer.Write(BitConverter.SingleToInt32Bits(type.BuildSeconds));
            writer.Write(BitConverter.SingleToInt32Bits(type.MaximumHealth));
            writer.Write(type.SupplyProvided);
            writer.Write(BitConverter.SingleToInt32Bits(type.CancelRefundFraction));
            writer.Write((byte)type.ConstructionMethod);
            writer.Write(type.RequiresVespeneNode);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static ulong ComputeStableHash(ReadOnlySpan<byte> data)
    {
        var hash = 14695981039346656037UL;
        foreach (var value in data)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static bool IsPositive(float value) =>
        float.IsFinite(value) && value > 0f;

    private static void Add(
        List<BuildingTypeCatalogValidationIssue> issues,
        BuildingTypeCatalogErrorCode code,
        int index,
        string message) =>
        issues.Add(new BuildingTypeCatalogValidationIssue(code, index, message));
}

public readonly record struct BuildingTypeCatalogDiff(
    bool Changed,
    int ChangedTypes)
{
    public static BuildingTypeCatalogDiff Compare(
        BuildingTypeCatalogSnapshot current,
        BuildingTypeCatalogSnapshot candidate)
    {
        var currentTypes = current.Types;
        var candidateTypes = candidate.Types;
        var shared = Math.Min(currentTypes.Length, candidateTypes.Length);
        var changed = Math.Abs(currentTypes.Length - candidateTypes.Length);
        for (var index = 0; index < shared; index++)
        {
            changed += currentTypes[index] == candidateTypes[index] ? 0 : 1;
        }
        return new BuildingTypeCatalogDiff(
            current.StableHash != candidate.StableHash,
            changed);
    }
}
