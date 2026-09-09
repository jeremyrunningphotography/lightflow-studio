using System.Globalization;

namespace LightflowStudio;

internal sealed record PlaybackReviewOptions(double Speed = 1, int FrameDivisor = 1)
{
    internal static readonly double[] Speeds = [0.125, 0.25, 0.5, 1, 2, 4];
    internal void Validate()
    {
        if (!Speeds.Contains(Speed) || FrameDivisor < 1 || FrameDivisor > 16)
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

internal sealed record CadenceChoice(string Label, int Divisor)
{
    public override string ToString() => Label;
    internal static IReadOnlyList<CadenceChoice> ForSource(double fps)
    {
        var choices = new List<CadenceChoice> { new("Source", 1) };
        if (!double.IsFinite(fps) || fps <= 0) return choices;
        // Keep integer and 1000/1001 families distinct; do not use Browser display buckets.
        foreach (var target in new[] { 60d, 60000d / 1001, 50, 30, 30000d / 1001, 25, 24, 24000d / 1001 })
        {
            var divisor = (int)Math.Round(fps / target);
            if (divisor is < 2 or > 16 || Math.Abs(fps / divisor - target) > target * 0.0001) continue;
            choices.Add(new($"{target:0.###} fps", divisor));
        }
        return choices;
    }
}

internal sealed record ViewerViewport(double Zoom = 1, double PanX = 0, double PanY = 0);
