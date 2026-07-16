namespace War3Rts.Pcg;

/// <summary>Stateless seeded value noise for large authored PCG masks.</summary>
public static class PcgHashNoise
{
    public static float Fractal01(float x, float y, uint seed)
    {
        var broad = Value01(x, y, seed);
        var detail = Value01(x * 2.17f + 19.3f, y * 2.17f - 7.1f,
            seed ^ 0x9E37_79B9u);
        return broad * 0.72f + detail * 0.28f;
    }

    public static float Value01(float x, float y, uint seed)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = Smooth(x - x0);
        var ty = Smooth(y - y0);
        var top = Lerp(Hash01(x0, y0, seed), Hash01(x0 + 1, y0, seed), tx);
        var bottom = Lerp(
            Hash01(x0, y0 + 1, seed),
            Hash01(x0 + 1, y0 + 1, seed),
            tx);
        return Lerp(top, bottom, ty);
    }

    private static float Hash01(int x, int y, uint seed)
    {
        var value = unchecked(
            (uint)x * 0x8DA6_B343u ^
            (uint)y * 0xD816_3841u ^ seed);
        value ^= value >> 16;
        value = unchecked(value * 0x7FEB_352Du);
        value ^= value >> 15;
        value = unchecked(value * 0x846C_A68Bu);
        value ^= value >> 16;
        return (value & 0x00FF_FFFFu) * (1f / 16_777_216f);
    }

    private static float Smooth(float value) =>
        value * value * (3f - 2f * value);

    private static float Lerp(float from, float to, float amount) =>
        from + (to - from) * amount;
}
