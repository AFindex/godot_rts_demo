using Godot;
using War3Rts;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Tests;

public static class War3CursorCatalogSelfTest
{
    public static SelfTestResult Run()
    {
        try
        {
            var loaded = 0;
            foreach (var race in War3CursorCatalog.Races)
            {
                var texture = War3RuntimeAssets.LoadTexture(
                    War3CursorCatalog.AtlasPath(race));
                if (texture?.GetWidth() == War3CursorCatalog.AtlasWidth &&
                    texture.GetHeight() == War3CursorCatalog.AtlasHeight)
                    loaded++;
            }

            var normal = War3CursorCatalog.ResolveFrame(
                War3CursorMode.Normal, 0);
            var selected = War3CursorCatalog.ResolveFrame(
                War3CursorMode.Select, 9);
            var targeted = War3CursorCatalog.ResolveFrame(
                War3CursorMode.TargetSelect, 10);
            var invalid = War3CursorCatalog.ResolveFrame(
                War3CursorMode.InvalidTarget, 0);
            var scroll = War3CursorCatalog.ResolveFrame(
                War3CursorMode.ScrollUp, 4);
            var parsed = War3CursorCatalog.TryParseRace(
                "night-elf", out var nightElf) &&
                nightElf == War3CursorRace.NightElf;
            var frames = normal == new War3CursorFrame(
                             0, 3, 0, new Vector2(3f, 3f)) &&
                         selected.Column == 1 && selected.Row == 0 &&
                         targeted.Column == 2 && targeted.Row == 2 &&
                         invalid.Column == 2 && invalid.Row == 3 &&
                         scroll.Column == 6 && scroll.EighthTurns == 6;
            var presenter = new War3WorldPresenter();
            presenter.SetAbilityPointerPreview(
                new NVector2(120f, 140f),
                new NVector2(60f, 70f),
                80f,
                32f,
                valid: true);
            var indicators = presenter.AbilityPointerPreviewVisible &&
                             presenter.AbilityRangePreviewVisible;
            presenter.HideAbilityPointerPreview();
            indicators &= !presenter.AbilityPointerPreviewVisible &&
                          !presenter.AbilityRangePreviewVisible;
            presenter.Free();
            var passed = loaded == 4 && parsed && frames && indicators;
            return new SelfTestResult(
                passed,
                $"themes={loaded}/4, parse={parsed}, frames={frames}, " +
                $"indicators={indicators}, " +
                $"normal={normal.Column}:{normal.Row}, " +
                $"target={targeted.Column}:{targeted.Row}, " +
                $"scroll={scroll.Column}:{scroll.Row}/r{scroll.EighthTurns}");
        }
        catch (Exception exception)
        {
            return new SelfTestResult(false, exception.ToString());
        }
    }
}
