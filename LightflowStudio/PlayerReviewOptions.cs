using System.Globalization;

namespace LightflowStudio;

internal sealed record PlaybackReviewOptions(double Speed = 1, int FrameDivisor = 1, MediaFrameRate? TargetRate = null)
{
    internal static readonly double[] Speeds = [0.125, 0.25, 0.5, 1, 2, 4];
    internal void Validate()
    {
        if (!Speeds.Contains(Speed) || FrameDivisor < 1 || FrameDivisor > 16 ||
            TargetRate is { } rate && (rate.Numerator <= 0 || rate.Denominator <= 0))
            throw new ArgumentOutOfRangeException(nameof(PlaybackReviewOptions));
    }

    // Flyleaf's selector uses ceil(speed * sourceFPS / (MaxOutputFps + 1)).
    // Put the ratio safely inside the desired integer bucket, independently of speed.
    internal double OutputThreshold(double sourceFps) => FrameDivisor == 1
        ? double.MaxValue : Speed * sourceFps / (FrameDivisor - 0.25) - 1;

    internal string AudioTempoFilter => string.Join(',', TempoStages(Speed)
        .Select(rate => "atempo=" + rate.ToString("0.###", CultureInfo.InvariantCulture)));

    private static IEnumerable<double> TempoStages(double rate)
    {
        while (rate < 0.5) { yield return 0.5; rate /= 0.5; }
        while (rate > 2) { yield return 2; rate /= 2; }
        yield return rate;
    }
}

internal sealed record CadenceChoice(string Label, int Divisor, MediaFrameRate? Rate = null)
{
    public override string ToString() => Label;
    internal static IReadOnlyList<CadenceChoice> ForSource(double fps)
    {
        var choices = new List<CadenceChoice> { new(double.IsFinite(fps) && fps > 0
            ? $"Source ({fps.ToString("0.###", CultureInfo.InvariantCulture)})" : "Source (unknown)", 1) };
        if (!double.IsFinite(fps) || fps <= 0) return choices;
        foreach (var rate in MediaFrameRate.Canonical.Reverse())
        {
            var target = rate.Value;
            if (target >= fps || Math.Abs(target - fps) < 0.0001) continue;
            var divisor = (int)Math.Round(fps / target);
            if (divisor is < 2 or > 16 || Math.Abs(fps / divisor - target) > target * 0.0001) divisor = 1;
            choices.Add(new(rate.ToString(), divisor, rate));
        }
        return choices;
    }
}

internal sealed record ViewerViewport(double Zoom = 1, double PanX = 0, double PanY = 0);
