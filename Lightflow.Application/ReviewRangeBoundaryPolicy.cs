namespace LightflowStudio;

public static class ReviewRangeBoundaryPolicy
{
    public static MediaRange SetIn(TimeSpan sourceDuration, MediaRange? current, TimeSpan requestedIn) =>
        new(sourceDuration, requestedIn,
            current?.Out is { } savedOut && requestedIn < savedOut ? savedOut : null);

    public static MediaRange SetOut(TimeSpan sourceDuration, MediaRange? current, TimeSpan requestedOut) =>
        new(sourceDuration,
            current?.In is { } savedIn && requestedOut > savedIn ? savedIn : null, requestedOut);
}

