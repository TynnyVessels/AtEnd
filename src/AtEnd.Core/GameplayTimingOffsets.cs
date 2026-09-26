namespace AtEnd.Core;

public readonly record struct GameplayTimingOffsets
{
    public GameplayTimingOffsets(
        double globalOffsetMilliseconds,
        double inputOffsetMilliseconds)
    {
        ValidateFinite(globalOffsetMilliseconds, nameof(globalOffsetMilliseconds));
        ValidateFinite(inputOffsetMilliseconds, nameof(inputOffsetMilliseconds));
        GlobalOffsetMilliseconds = globalOffsetMilliseconds;
        InputOffsetMilliseconds = inputOffsetMilliseconds;
    }

    public double GlobalOffsetMilliseconds { get; }
    public double InputOffsetMilliseconds { get; }

    public double AdjustAudioTime(double rawAudioTimeSeconds)
    {
        ValidateFinite(rawAudioTimeSeconds, nameof(rawAudioTimeSeconds));
        return rawAudioTimeSeconds + (GlobalOffsetMilliseconds / 1000d);
    }

    public double AdjustInputTime(double rawAudioTimeSeconds) =>
        AdjustAudioTime(rawAudioTimeSeconds) + (InputOffsetMilliseconds / 1000d);

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
