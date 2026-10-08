namespace Lightflow.Domain;

public enum AssetFlag { Rejected = -1, Unflagged = 0, Picked = 1 }
public enum AssetColorLabel { Red = 1, Yellow = 2, Green = 3, Blue = 4, Purple = 5 }

public sealed record AssetClassification(Guid AssetId, int Rating, AssetFlag Flag,
    AssetColorLabel? ColorLabel, IReadOnlyList<string> Keywords, long Revision = 0)
{
    public static AssetClassification Empty(Guid assetId) => new(assetId, 0, AssetFlag.Unflagged, null, []);
}
