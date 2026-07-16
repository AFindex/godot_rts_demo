namespace War3Rts.Pcg;

/// <summary>
/// Small, engine-independent PCG32 stream used by battlefield generation.
/// The explicit stream keeps authored generators reproducible across runs.
/// </summary>
public struct DeterministicPcgRandom
{
    private ulong _state;
    private readonly ulong _increment;

    public DeterministicPcgRandom(ulong seed, ulong stream = 1UL)
    {
        _state = 0UL;
        _increment = (stream << 1) | 1UL;
        NextUInt();
        _state = unchecked(_state + seed);
        NextUInt();
    }

    public uint NextUInt()
    {
        var oldState = _state;
        _state = unchecked(oldState * 6364136223846793005UL + _increment);
        var xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        var rotation = (int)(oldState >> 59);
        return (xorShifted >> rotation) |
               (xorShifted << ((-rotation) & 31));
    }

    public float NextSingle() =>
        (NextUInt() >> 8) * (1f / 16_777_216f);

    public float Range(float minimum, float maximum) =>
        minimum + (maximum - minimum) * NextSingle();
}
