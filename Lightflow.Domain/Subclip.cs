using System.Globalization;
namespace LightflowStudio;

public sealed record Subclip(
    Guid SubclipId,
    Guid AssetId,
    string Name,
    int Ordinal,
    TimeSpan In,
    TimeSpan Out,
    TimeSpan SourceDuration,
    long Revision,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public static class SubclipCurrentOrder
{
    public static IOrderedEnumerable<Subclip> Apply(IEnumerable<Subclip> subclips) =>
        subclips.OrderBy(item => item.In).ThenBy(item => item.SubclipId);

    public static int Compare(Subclip left, Subclip right)
    {
        var byIn = left.In.CompareTo(right.In);
        return byIn != 0 ? byIn : left.SubclipId.CompareTo(right.SubclipId);
    }
}

public sealed record SubclipOrder(Guid SubclipId, long ExpectedRevision);
public sealed record SubclipCreateResult(Subclip Subclip, bool Created);

public sealed record SubclipCreationEligibility(bool CanCreate, MediaRange? MaterializedRange, string? Problem)
{
    public static SubclipCreationEligibility Evaluate(bool hasCatalogVideoTarget, MediaRange? workingRange,
        TimeSpan? authoritativeSourceDuration)
    {
        if (!hasCatalogVideoTarget)
            return new(false, null, "A Catalog-backed video is required to create a Subclip.");
        if (workingRange is null || workingRange.IsFullSource)
            return new(false, null, "Set an In or Out point before creating a Subclip.");
        if (authoritativeSourceDuration is not { } duration || duration <= TimeSpan.Zero)
            return new(false, null, "The authoritative source duration is unavailable.");
        var materialized = new MediaRange(duration, workingRange.In, workingRange.Out);
        var issues = materialized.Validate();
        if (issues.Count != 0)
            return new(false, null, string.Join(" ", issues.Select(issue => issue.Message)));
        return new(true, new(duration, materialized.EffectiveIn, materialized.EffectiveOut), null);
    }

    public static MediaRange Materialize(MediaRange workingRange)
    {
        ArgumentNullException.ThrowIfNull(workingRange);
        var eligibility = Evaluate(true, workingRange, workingRange.SourceDuration);
        return eligibility.MaterializedRange ?? throw new ArgumentException(eligibility.Problem, nameof(workingRange));
    }
}

public static class SubclipDefaultName
{
    public static string Create(string sourceRelativePath, TimeSpan @in, TimeSpan @out)
    {
        var sourceBaseName = Path.GetFileNameWithoutExtension(sourceRelativePath?.Trim() ?? "");
        if (string.IsNullOrWhiteSpace(sourceBaseName))
            throw new ArgumentException("The source filename could not be resolved for the Subclip.", nameof(sourceRelativePath));
        return $"{sourceBaseName}_{FormatTimestamp(@in)}_{FormatTimestamp(@out)}";
    }

    public static string FormatTimestamp(TimeSpan timestamp)
    {
        if (timestamp < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timestamp));
        var totalHours = timestamp.Ticks / TimeSpan.TicksPerHour;
        var minutes = timestamp.Minutes;
        var seconds = timestamp.Seconds;
        var milliseconds = timestamp.Ticks % TimeSpan.TicksPerSecond / TimeSpan.TicksPerMillisecond;
        return FormattableString.Invariant($"{totalHours:00}-{minutes:00}-{seconds:00}.{milliseconds:000}");
    }
}

public sealed class SubclipConcurrencyException(string message) : InvalidOperationException(message);
