using System.Numerics;
using System.Text;

namespace RtsDemo.Simulation;

public enum GameplayProfileErrorCode
{
    None = 0,
    UnsupportedFormatVersion = 2001,
    EmptyUnitProfiles = 2101,
    NonDenseUnitProfileId = 2102,
    InvalidUnitProfile = 2103,
    EmptyBuildingProfiles = 2201,
    NonDenseBuildingProfileId = 2202,
    InvalidBuildingProfile = 2203,
    DuplicateProfileName = 2301,
    MissingResourceAsset = 2401,
    NullResourceElement = 2402
}

public readonly record struct GameplayProfileValidationIssue(
    GameplayProfileErrorCode Code,
    int ElementIndex,
    string Message);

public sealed class GameplayProfileValidationResult
{
    public GameplayProfileValidationResult(GameplayProfileValidationIssue[] issues)
    {
        Issues = issues;
    }

    public bool IsValid => Issues.Length == 0;
    public GameplayProfileValidationIssue[] Issues { get; }
    public GameplayProfileErrorCode FirstError =>
        IsValid ? GameplayProfileErrorCode.None : Issues[0].Code;
}

public readonly record struct UnitMovementProfileSnapshot(
    int Id,
    string Name,
    float PhysicalRadius,
    float MaximumSpeed,
    float Acceleration,
    MovementClass MovementClass,
    float NavigationRadius,
    float TurnRateRadiansPerSecond =
        UnitFacing.LegacyTurnRateRadiansPerSecond);

public readonly record struct BuildingFootprintProfileSnapshot(
    int Id,
    string Name,
    BuildingFootprintClass FootprintClass,
    Vector2 Size,
    MovementClass MinimumPassageClass,
    float UnitPadding);

public sealed class GameplayProfileCatalogSnapshot
{
    public const int CurrentFormatVersion = 2;

    private readonly UnitMovementProfileSnapshot[] _units;
    private readonly BuildingFootprintProfileSnapshot[] _buildings;
    private readonly byte[] _canonicalBytes;

    private GameplayProfileCatalogSnapshot(
        int formatVersion,
        UnitMovementProfileSnapshot[] units,
        BuildingFootprintProfileSnapshot[] buildings)
    {
        FormatVersion = formatVersion;
        _units = units;
        _buildings = buildings;
        _canonicalBytes = BuildCanonicalBytes();
        StableHash = ComputeStableHash(_canonicalBytes);
    }

    public int FormatVersion { get; }
    public ReadOnlySpan<UnitMovementProfileSnapshot> UnitProfiles => _units;
    public ReadOnlySpan<BuildingFootprintProfileSnapshot> BuildingProfiles => _buildings;
    public ReadOnlyMemory<byte> CanonicalBytes => _canonicalBytes;
    public ulong StableHash { get; }
    public string StableHashText => StableHash.ToString("X16");

    public UnitMovementProfileSnapshot Unit(int id) => _units[id];
    public BuildingFootprintProfileSnapshot Building(int id) => _buildings[id];

    public static bool TryCreate(
        int formatVersion,
        ReadOnlySpan<UnitMovementProfileSnapshot> unitProfiles,
        ReadOnlySpan<BuildingFootprintProfileSnapshot> buildingProfiles,
        out GameplayProfileCatalogSnapshot? snapshot,
        out GameplayProfileValidationResult validation)
    {
        var units = unitProfiles.ToArray();
        var buildings = buildingProfiles.ToArray();
        validation = Validate(formatVersion, units, buildings);
        if (!validation.IsValid)
        {
            snapshot = null;
            return false;
        }

        for (var index = 0; index < units.Length; index++)
        {
            var clearance = MovementClearance.FromPhysicalRadius(
                units[index].PhysicalRadius);
            units[index] = units[index] with
            {
                MovementClass = clearance.Class,
                NavigationRadius = clearance.NavigationRadius
            };
        }

        snapshot = new GameplayProfileCatalogSnapshot(
            formatVersion, units, buildings);
        return true;
    }

    private static GameplayProfileValidationResult Validate(
        int formatVersion,
        UnitMovementProfileSnapshot[] units,
        BuildingFootprintProfileSnapshot[] buildings)
    {
        var issues = new List<GameplayProfileValidationIssue>();
        if (formatVersion != CurrentFormatVersion)
        {
            Add(issues, GameplayProfileErrorCode.UnsupportedFormatVersion, -1,
                $"Expected gameplay profile format {CurrentFormatVersion}, got {formatVersion}.");
        }

        if (units.Length == 0)
        {
            Add(issues, GameplayProfileErrorCode.EmptyUnitProfiles, -1,
                "At least one unit movement profile is required.");
        }

        for (var index = 0; index < units.Length; index++)
        {
            var profile = units[index];
            if (profile.Id != index)
            {
                Add(issues, GameplayProfileErrorCode.NonDenseUnitProfileId, index,
                    $"Unit profile ID must equal dense index {index}.");
            }

            if (string.IsNullOrWhiteSpace(profile.Name) ||
                !IsPositive(profile.PhysicalRadius) ||
                !IsPositive(profile.MaximumSpeed) ||
                !IsPositive(profile.Acceleration) ||
                !IsPositive(profile.TurnRateRadiansPerSecond))
            {
                Add(issues, GameplayProfileErrorCode.InvalidUnitProfile, index,
                    "Unit name, radius, maximum speed and acceleration must be valid.");
            }
        }

        if (buildings.Length == 0)
        {
            Add(issues, GameplayProfileErrorCode.EmptyBuildingProfiles, -1,
                "At least one building footprint profile is required.");
        }

        for (var index = 0; index < buildings.Length; index++)
        {
            var profile = buildings[index];
            if (profile.Id != index)
            {
                Add(issues, GameplayProfileErrorCode.NonDenseBuildingProfileId, index,
                    $"Building profile ID must equal dense index {index}.");
            }

            if (string.IsNullOrWhiteSpace(profile.Name) ||
                !IsPositive(profile.Size.X) || !IsPositive(profile.Size.Y) ||
                !float.IsFinite(profile.UnitPadding) || profile.UnitPadding < 0f ||
                !Enum.IsDefined(profile.FootprintClass) ||
                !Enum.IsDefined(profile.MinimumPassageClass))
            {
                Add(issues, GameplayProfileErrorCode.InvalidBuildingProfile, index,
                    "Building name, class, size, passage class and padding must be valid.");
            }
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < units.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(units[index].Name) &&
                !names.Add(units[index].Name))
            {
                Add(issues, GameplayProfileErrorCode.DuplicateProfileName, index,
                    $"Duplicate gameplay profile name '{units[index].Name}'.");
            }
        }

        for (var index = 0; index < buildings.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(buildings[index].Name) &&
                !names.Add(buildings[index].Name))
            {
                Add(issues, GameplayProfileErrorCode.DuplicateProfileName, index,
                    $"Duplicate gameplay profile name '{buildings[index].Name}'.");
            }
        }

        return new GameplayProfileValidationResult(issues.ToArray());
    }

    private byte[] BuildCanonicalBytes()
    {
        using var stream = new MemoryStream(512);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatVersion);
        writer.Write(_units.Length);
        for (var index = 0; index < _units.Length; index++)
        {
            var value = _units[index];
            writer.Write(value.Id);
            WriteString(writer, value.Name);
            writer.Write(BitConverter.SingleToInt32Bits(value.PhysicalRadius));
            writer.Write(BitConverter.SingleToInt32Bits(value.MaximumSpeed));
            writer.Write(BitConverter.SingleToInt32Bits(value.Acceleration));
            writer.Write((byte)value.MovementClass);
            writer.Write(BitConverter.SingleToInt32Bits(value.NavigationRadius));
            writer.Write(BitConverter.SingleToInt32Bits(
                value.TurnRateRadiansPerSecond));
        }

        writer.Write(_buildings.Length);
        for (var index = 0; index < _buildings.Length; index++)
        {
            var value = _buildings[index];
            writer.Write(value.Id);
            WriteString(writer, value.Name);
            writer.Write((byte)value.FootprintClass);
            writer.Write(BitConverter.SingleToInt32Bits(value.Size.X));
            writer.Write(BitConverter.SingleToInt32Bits(value.Size.Y));
            writer.Write((byte)value.MinimumPassageClass);
            writer.Write(BitConverter.SingleToInt32Bits(value.UnitPadding));
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static ulong ComputeStableHash(ReadOnlySpan<byte> data)
    {
        var hash = 14695981039346656037UL;
        for (var index = 0; index < data.Length; index++)
        {
            hash ^= data[index];
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static bool IsPositive(float value) =>
        float.IsFinite(value) && value > 0f;

    private static void Add(
        List<GameplayProfileValidationIssue> issues,
        GameplayProfileErrorCode code,
        int index,
        string message) =>
        issues.Add(new GameplayProfileValidationIssue(code, index, message));
}

public static class DemoGameplayProfiles
{
    private static readonly GameplayProfileCatalogSnapshot Snapshot = Build();

    public static GameplayProfileCatalogSnapshot CreateSnapshot() => Snapshot;

    private static GameplayProfileCatalogSnapshot Build()
    {
        var created = GameplayProfileCatalogSnapshot.TryCreate(
            GameplayProfileCatalogSnapshot.CurrentFormatVersion,
            [
                new(0, "Scout", 5.5f, 150f, 780f, default, 0f),
                new(1, "Marine", 7.5f, 128f, 720f, default, 0f),
                new(2, "Heavy", 10f, 105f, 600f, default, 0f)
            ],
            [
                new(0, "Pylon", BuildingFootprintClass.Small,
                    new Vector2(32f, 32f), MovementClass.Medium, 2f),
                new(1, "Barracks", BuildingFootprintClass.Medium,
                    new Vector2(64f, 48f), MovementClass.Medium, 2f),
                new(2, "Factory", BuildingFootprintClass.Large,
                    new Vector2(112f, 80f), MovementClass.Medium, 2f),
                new(3, "CommandCenter", BuildingFootprintClass.Huge,
                    new Vector2(160f, 120f), MovementClass.Medium, 2f)
            ],
            out var snapshot,
            out var validation);
        if (!created || snapshot is null)
        {
            throw new InvalidOperationException(
                $"Built-in gameplay profiles are invalid: {validation.FirstError}.");
        }

        return snapshot;
    }
}
