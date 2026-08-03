namespace IranDirect.Testing.Performance.Workloads;

internal static class DeterministicRandom
{
    public static ulong Mix(int seed, int index, int salt)
    {
        ulong value = 0x9E3779B97F4A7C15UL;
        value += (ulong)(uint)seed;
        value = Finalize(value);
        value += (ulong)(uint)index;
        value = Finalize(value);
        value += (ulong)(uint)salt;
        return Finalize(value);
    }

    private static ulong Finalize(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

internal sealed class SeededRandomStream
{
    private ulong _state;

    public SeededRandomStream(ulong seed)
    {
        _state = seed;
    }

    public int Next(int maxExclusive)
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % (ulong)maxExclusive);
    }

    public int Next(int minInclusive, int maxExclusive) =>
        minInclusive + Next(maxExclusive - minInclusive);
}
