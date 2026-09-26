namespace AtEnd.Core;

public enum HoldSliceClipMode
{
    None,
    Hidden,
    ClipFrom,
    ClipTo,
}

public static class PlayfieldPresentation
{
    public static double ApplyAcceleration(double linearProgress, double exponent)
    {
        ValidateFinite(linearProgress, nameof(linearProgress));
        if (!double.IsFinite(exponent) || exponent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exponent));
        }

        return Math.Pow(Math.Max(0, linearProgress), exponent);
    }

    public static bool IsProgressVisible(double linearProgress, double maximumProgress)
    {
        ValidateFinite(linearProgress, nameof(linearProgress));
        if (!double.IsFinite(maximumProgress) || maximumProgress < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumProgress));
        }

        return linearProgress >= 0 && linearProgress <= maximumProgress;
    }

    public static bool ShouldRenderNoteHead(bool isJudged, bool wasMissed) =>
        !isJudged || wasMissed;

    public static HoldSliceClipMode GetHoldSliceClipMode(
        double fromLinearProgress,
        double toLinearProgress,
        bool isCurrentlyHeld)
    {
        ValidateFinite(fromLinearProgress, nameof(fromLinearProgress));
        ValidateFinite(toLinearProgress, nameof(toLinearProgress));
        if (!isCurrentlyHeld)
        {
            return HoldSliceClipMode.None;
        }

        if (fromLinearProgress >= 1 && toLinearProgress >= 1)
        {
            return HoldSliceClipMode.Hidden;
        }

        if (fromLinearProgress > 1)
        {
            return HoldSliceClipMode.ClipFrom;
        }

        return toLinearProgress > 1
            ? HoldSliceClipMode.ClipTo
            : HoldSliceClipMode.None;
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
