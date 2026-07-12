using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class BuildingTypeCatalogSelfTest
{
    public static SelfTestResult Run(BuildingTypeCatalogSnapshot? loaded = null)
    {
        var canonical = DemoBuildingTypes.CreateCatalog();
        loaded ??= canonical;
        var roundTrip = BuildingTypeCatalogSnapshot.TryCreate(
            loaded.FormatVersion,
            loaded.Types,
            out var repeated,
            out var repeatedValidation);
        if (!roundTrip || repeated is null || !repeatedValidation.IsValid ||
            repeated.StableHash != loaded.StableHash ||
            !repeated.CanonicalBytes.Span.SequenceEqual(
                loaded.CanonicalBytes.Span))
        {
            return new SelfTestResult(false, "canonical building bytes are unstable");
        }

        var invalid = loaded.Types.ToArray();
        invalid[3] = invalid[3] with { RequiresVespeneNode = false };
        var invalidAccepted = BuildingTypeCatalogSnapshot.TryCreate(
            loaded.FormatVersion,
            invalid,
            out _,
            out var invalidValidation);
        var changed = loaded.Types.ToArray();
        changed[1] = changed[1] with
        {
            Cost = changed[1].Cost with { Minerals = 175 }
        };
        var changedCreated = BuildingTypeCatalogSnapshot.TryCreate(
            loaded.FormatVersion, changed, out var changedSnapshot, out _);
        var diff = changedCreated && changedSnapshot is not null
            ? BuildingTypeCatalogDiff.Compare(loaded, changedSnapshot)
            : default;
        var profilesMatch = loaded.Types.SequenceEqual(canonical.Types);
        var valid = loaded.Types.Length == 5 && profilesMatch &&
                    loaded.Type(0).Size.X == 48f &&
                    loaded.Type(1).Size.X == 112f &&
                    loaded.Type(2).Size.X == 160f &&
                    loaded.Type(0).Armor == 0f &&
                    loaded.Type(1).Armor == 1f &&
                    loaded.Type(2).Armor == 2f &&
                    loaded.Types.ToArray().All(value =>
                        (value.Attributes & CombatAttribute.Structure) != 0 &&
                        value.ArmorUpgradePerLevel == 1f) &&
                    loaded.Type(3).RequiresVespeneNode &&
                    loaded.Type(4).Function == BuildingFunctionKind.Research &&
                    !invalidAccepted &&
                    diff is { Changed: true, ChangedTypes: 1 } &&
                    invalidValidation.FirstError ==
                        BuildingTypeCatalogErrorCode.InvalidFunctionContract;
        return new SelfTestResult(
            valid,
            $"format={loaded.FormatVersion}, hash={loaded.StableHashText}, " +
            $"types={loaded.Types.Length}, roundTrip={roundTrip}, " +
            $"resourceMatches={profilesMatch}, changed={diff.ChangedTypes}, " +
            $"invalid={invalidValidation.FirstError}");
    }
}
