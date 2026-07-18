using System.Collections.Immutable;
using System.Globalization;
using RtsDemo.Simulation;

namespace War3Rts.Data;

/// <summary>
/// Inventory capabilities exported by Warcraft's AInv behavior family.
/// DataA..E map to the five fields declared by inv1..inv5 metadata; no unit,
/// race or hero identity participates in the decision.
/// </summary>
public sealed record War3InventoryAbilityProfile(
    string AbilityObjectId,
    int Capacity,
    bool DropItemsOnDeath,
    bool CanUseItems,
    bool CanGetItems,
    bool CanDropItems,
    ImmutableArray<AbilityRequirementProfile> Requirements);

public sealed class War3InventoryDataAdapter(
    War3ObjectDataCatalog abilityData,
    War3AbilityMetadataCatalog metadata)
{
    public IReadOnlyDictionary<string, War3InventoryAbilityProfile> Build(
        AbilityCatalogSnapshot runtimeCatalog)
    {
        var result = new Dictionary<string, War3InventoryAbilityProfile>(
            StringComparer.Ordinal);
        foreach (var runtime in runtimeCatalog.Abilities)
        {
            if (!abilityData.TryGet(runtime.RawId, out var source) ||
                !source.Identity.BaseCode.Equals(
                    "AInv", StringComparison.Ordinal))
                continue;
            if (source.Summary.Levels.Length != 1 ||
                runtime.Levels.Length != 1)
                throw new InvalidDataException(
                    $"Inventory ability {source.Id} must contain exactly one level.");
            var level = source.Summary.Levels[0];
            var capacityField = Field(source.Id, dataIndex: 1, "int");
            result.Add(source.Id, new War3InventoryAbilityProfile(
                source.Id,
                Integer(
                    level, "A",
                    RequiredIntegerBound(
                        source.Id, capacityField.Minimum, "minimum"),
                    RequiredIntegerBound(
                        source.Id, capacityField.Maximum, "maximum")),
                Boolean(source.Id, level, "B", 2),
                Boolean(source.Id, level, "C", 3),
                Boolean(source.Id, level, "D", 4),
                Boolean(source.Id, level, "E", 5),
                runtime.Levels[0].Requirements.IsDefault
                    ? []
                    : runtime.Levels[0].Requirements));
        }
        return result;
    }

    private War3AbilityMetadataBindingField Field(
        string abilityId,
        int dataIndex,
        string valueType)
    {
        if (!metadata.TryGetBinding(abilityId, out var binding))
            throw new InvalidDataException(
                $"Inventory ability {abilityId} has no metadata binding.");
        var field = binding.Fields.SingleOrDefault(value =>
            value.Field?.Equals("Data", StringComparison.OrdinalIgnoreCase) ==
                true &&
            value.DataIndex == dataIndex);
        if (field is null ||
            !string.Equals(field.ValueType, valueType,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Inventory ability {abilityId} Data{dataIndex} metadata " +
                $"must be {valueType}.");
        return field;
    }

    private static int RequiredIntegerBound(
        string abilityId,
        float? value,
        string label)
    {
        if (value is { } bound && float.IsInteger(bound) &&
            bound is >= int.MinValue and <= int.MaxValue)
            return checked((int)bound);
        throw new InvalidDataException(
            $"Inventory ability {abilityId} capacity metadata has no " +
            $"integer {label}.");
    }

    private static int Integer(
        War3ObjectLevel level,
        string field,
        int minimum,
        int maximum)
    {
        if (level.Data.TryGetValue(field, out var text) &&
            int.TryParse(text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value) &&
            value >= minimum && value <= maximum)
            return value;
        throw new InvalidDataException(
            $"Inventory level {level.Level} requires JSON Data{field} " +
            $"in [{minimum}, {maximum}].");
    }

    private bool Boolean(
        string abilityId,
        War3ObjectLevel level,
        string field,
        int dataIndex)
    {
        _ = Field(abilityId, dataIndex, "bool");
        var value = Integer(level, field, 0, 1);
        return value == 1;
    }
}
