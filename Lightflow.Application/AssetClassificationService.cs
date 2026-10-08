using Lightflow.Actions;
using Lightflow.Domain;

namespace Lightflow.Application;

/// <summary>Executes captured Browser classification mutations through the authoritative store.</summary>
public sealed class AssetClassificationService(
    IAssetClassificationStore store, IClassificationMutationAdmission admission)
{
    /// <summary>
    /// Copies the ordered identities before awaiting admission. Each UpdateAsync owns its fresh
    /// read/mutate/save/readback and per-asset commit; failures propagate without rolling back
    /// earlier assets or invoking completion. The synchronous completion projection runs on the
    /// caller's continuation context inside the outer admission, preserving its existing lifetime.
    /// The caller owns scope validation and publication; no UI/context policy enters this service.
    /// </summary>
    public Task<T> ExecuteAsync<T>(IReadOnlyList<Guid> capturedAssetIds, ActionArguments arguments,
        Func<IReadOnlyList<AssetClassification>, T> complete, CancellationToken token = default)
    {
        var ids = capturedAssetIds.ToArray();
        return admission.RunAsync(async () => {
            var values = new List<AssetClassification>();
            foreach (var id in ids)
                values.Add(await store.UpdateAsync(id, value => arguments switch {
                    SetRatingArguments rating => value with { Rating = AssetClassificationCommandPolicy.SetRating(value.Rating, rating.Rating, rating.ToggleCurrent) },
                    SetFlagArguments flag => value with { Flag = (AssetFlag)flag.Flag },
                    StepFlagArguments step => value with { Flag = AssetClassificationCommandPolicy.StepFlag(value.Flag, (int)step.Direction) },
                    SetColorLabelArguments label => value with { ColorLabel = label.Label is { } color ? (AssetColorLabel)color : null },
                    _ => throw new ArgumentOutOfRangeException(nameof(arguments))
                }, token));
            return complete(values.AsReadOnly());
        }, token);
    }
}
