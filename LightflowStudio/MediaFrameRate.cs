using System.Globalization;

namespace LightflowStudio;

internal readonly record struct MediaFrameRate(int Numerator, int Denominator)
{
    internal double Value => (double)Numerator / Denominator;
    internal double DisplayValue => Math.Round(Value, 3);
    public override string ToString() => DisplayValue.ToString("0.###", CultureInfo.InvariantCulture);
    internal static readonly MediaFrameRate[] Canonical =
    [new(24000, 1001), new(24, 1), new(25, 1), new(30000, 1001), new(30, 1), new(50, 1), new(60000, 1001), new(60, 1)];
}

/// <summary>Selects source frames in rational presentation-time slots without changing their timestamps.</summary>
internal sealed class RationalCadenceSelector(MediaFrameRate rate)
{
    private long? _origin;
    private long _slot = -1;
    internal bool Select(long ticks)
    {
        _origin ??= ticks;
        var slot = (long)((Int128)Math.Max(0, ticks - _origin.Value) * rate.Numerator /
            ((Int128)TimeSpan.TicksPerSecond * rate.Denominator));
        if (slot <= _slot) return false;
        _slot = slot;
        return true;
    }
}
