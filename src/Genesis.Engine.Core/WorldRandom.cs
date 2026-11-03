namespace Genesis.Engine.Core;

/// <summary>Explicit SplitMix64 state for repeatable simulation snapshots. Not cryptographic.</summary>
internal sealed class WorldRandom : Random
{
    public ulong State { get; set; }
    public WorldRandom(int seed) => State = unchecked((ulong)(long)seed);
    private ulong Bits()
    {
        ulong z = unchecked(State += 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }
    protected override double Sample() => NextDouble();
    public override double NextDouble() => (Bits() >> 11) * (1.0 / (1UL << 53));
    public override float NextSingle() => (Bits() >> 40) * (1f / (1 << 24));
    public override int Next() => Next(int.MaxValue);
    public override int Next(int maxValue)
    {
        if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
        return (int)(NextDouble() * maxValue);
    }
    public override int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
        return (int)(minValue + (long)(NextDouble() * ((long)maxValue - minValue)));
    }
    public override void NextBytes(byte[] buffer) { ArgumentNullException.ThrowIfNull(buffer); NextBytes(buffer.AsSpan()); }
    public override void NextBytes(Span<byte> buffer) { for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)Bits(); }
}
