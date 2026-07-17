using War3Rts;
using War3Rts.Audio;
using RtsDemo.Demos.War3;

namespace RtsDemo.Tests;

public static class War3TreeHarvestFeedbackSelfTest
{
    public static SelfTestResult Run()
    {
        var profile = War3TreeHarvestFeedbackCatalog.Resolve(
            War3HumanContent.DataCatalog, "hpea");
        var profileReady = profile.DataDriven &&
                           profile.WeaponSlot == 1 &&
                           profile.SoundFamily.Equals(
                               "AxeMediumChop", StringComparison.OrdinalIgnoreCase) &&
                           MathF.Abs(profile.CooldownSeconds - 1.1f) < 0.001f &&
                           MathF.Abs(profile.DamagePointSeconds - 0.433f) < 0.001f;
        var clockReady =
            War3TreeHarvestFeedbackCatalog.StrikeIndex(profile, 4f, 4f) == -1 &&
            War3TreeHarvestFeedbackCatalog.StrikeIndex(
                profile, 4f, 3.568f) == -1 &&
            War3TreeHarvestFeedbackCatalog.StrikeIndex(
                profile, 4f, 3.567f) == 0 &&
            War3TreeHarvestFeedbackCatalog.StrikeIndex(
                profile, 4f, 2.467f) == 1 &&
            War3TreeHarvestFeedbackCatalog.StrikeIndex(
                profile, 4f, 0.267f) == 3;

        var treeMetadata = War3RuntimeAssets.LoadMetadata(
            War3HumanContent.TreeSource(0));
        var hitAnimationReady = treeMetadata.Sequences.Any(value =>
            value.Name.Equals("Stand Hit", StringComparison.OrdinalIgnoreCase));

        var audio = War3AudioCatalog.Open(
            War3AssetPack.AbsolutePath("data/audio_catalog"));
        var audioReady = audio.TryGetUnitBinding("hpea", out var binding) &&
                         binding.Weapons.Any(value => value.Slot == 1 &&
                             value.ImpactPrefix.Equals(
                                 "AxeMediumChop",
                                 StringComparison.OrdinalIgnoreCase)) &&
                         audio.ContainsCue("AxeMediumChopWood");
        var passed = profileReady && clockReady && hitAnimationReady && audioReady;
        return new SelfTestResult(
            passed,
            $"profile={profile.DataDriven}/{profile.WeaponSlot}/" +
            $"{profile.SoundFamily}/{profile.CooldownSeconds:0.###}/" +
            $"{profile.DamagePointSeconds:0.###},clock={clockReady}," +
            $"tree_hit={hitAnimationReady},audio={audioReady}");
    }
}
