using RtsDemo.Simulation;

namespace RtsDemo.Tests;

public static class GameplayProfileSelfTest
{
    public static SelfTestResult Run(GameplayProfileCatalogSnapshot? loaded = null)
    {
        var canonical = DemoGameplayProfiles.CreateSnapshot();
        loaded ??= canonical;
        if (loaded.FormatVersion != GameplayProfileCatalogSnapshot.CurrentFormatVersion ||
            loaded.StableHash == 0UL ||
            loaded.UnitProfiles.Length != 3 ||
            loaded.BuildingProfiles.Length != 4 ||
            loaded.Unit(0).MovementClass != MovementClass.Small ||
            loaded.Unit(1).MovementClass != MovementClass.Medium ||
            loaded.Unit(2).MovementClass != MovementClass.Large)
        {
            return new SelfTestResult(false, "loaded gameplay profile catalog is invalid");
        }

        var repeated = GameplayProfileCatalogSnapshot.TryCreate(
            canonical.FormatVersion,
            canonical.UnitProfiles,
            canonical.BuildingProfiles,
            out var repeatedSnapshot,
            out var repeatedValidation);
        if (!repeated || repeatedSnapshot is null || !repeatedValidation.IsValid ||
            repeatedSnapshot.StableHash != canonical.StableHash ||
            !repeatedSnapshot.CanonicalBytes.Span.SequenceEqual(
                canonical.CanonicalBytes.Span))
        {
            return new SelfTestResult(false, "gameplay canonical bytes are unstable");
        }

        var invalidUnits = canonical.UnitProfiles.ToArray();
        invalidUnits[1] = invalidUnits[1] with { Id = 7 };
        var invalidAccepted = GameplayProfileCatalogSnapshot.TryCreate(
            canonical.FormatVersion,
            invalidUnits,
            canonical.BuildingProfiles,
            out _,
            out var invalidValidation);
        if (invalidAccepted ||
            invalidValidation.FirstError !=
            GameplayProfileErrorCode.NonDenseUnitProfileId)
        {
            return new SelfTestResult(false, "invalid unit profile ID was accepted");
        }

        return new SelfTestResult(
            true,
            $"format={loaded.FormatVersion}, hash={loaded.StableHashText}, " +
            $"units={loaded.UnitProfiles.Length}, " +
            $"buildings={loaded.BuildingProfiles.Length}, " +
            $"invalidUnit={invalidValidation.FirstError}");
    }
}
