using System.Globalization;
namespace LightflowStudio;
public sealed record TimelineMarker(Guid MarkerId, Guid AssetId, TimeSpan Position, string Name,
    long Revision, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc)
{
    public string DisplayName => string.IsNullOrEmpty(Name) ? "Marker" : Name;
    public string ChoiceLabel => $"{DisplayName} · {PositionLabel}";
    public string PositionLabel => Position.ToString("c", CultureInfo.InvariantCulture);
}
public sealed record MarkerCreateResult(TimelineMarker Marker, bool Created);
public sealed record MarkerSummary(int Count) { public bool HasMarkers => Count > 0; }
public sealed class MarkerConcurrencyException() : InvalidOperationException("The marker changed. Reload it before editing.");
public interface IMarkerService
{
    Task<IReadOnlyList<TimelineMarker>> ListAsync(Guid assetId, CancellationToken token = default);
    Task<IReadOnlyDictionary<Guid, MarkerSummary>> SummariesAsync(IReadOnlyCollection<Guid> assets, CancellationToken token = default);
    Task<MarkerCreateResult> CreateAsync(Guid assetId, TimeSpan position, CancellationToken token = default);
    Task RenameAsync(Guid markerId, long revision, string name, CancellationToken token = default);
    Task DeleteAsync(Guid markerId, long revision, CancellationToken token = default);
}

public static class MarkerNavigation
{
    public static TimelineMarker? Previous(IEnumerable<TimelineMarker> markers, TimeSpan position) =>
        markers.Where(m => m.Position < position).OrderByDescending(m => m.Position).FirstOrDefault();
    public static TimelineMarker? Next(IEnumerable<TimelineMarker> markers, TimeSpan position) =>
        markers.Where(m => m.Position > position).OrderBy(m => m.Position).FirstOrDefault();
    public static double Fraction(TimeSpan position, TimeSpan duration) => duration.Ticks <= 0 ? 0 :
        Math.Clamp((double)position.Ticks / duration.Ticks, 0, 1);
}

/// <summary>Catalog-only point annotations. No source file or destination identity is involved.</summary>
