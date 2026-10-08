using Lightflow.Domain;

namespace Lightflow.Application;

public static class AssetClassificationCommandPolicy
{
    public static int SetRating(int current, int requested, bool toggleCurrent)
    {
        if (requested is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(requested));
        return toggleCurrent && requested > 0 && current == requested ? 0 : requested;
    }

    public static AssetFlag StepFlag(AssetFlag current, int delta) =>
        (AssetFlag)Math.Clamp((int)current + Math.Sign(delta), (int)AssetFlag.Rejected, (int)AssetFlag.Picked);

    public static AssetFlag ToggleFlag(AssetFlag current, AssetFlag requested)
    {
        if (requested == AssetFlag.Unflagged) throw new ArgumentOutOfRangeException(nameof(requested));
        return current == requested ? AssetFlag.Unflagged : requested;
    }
}
