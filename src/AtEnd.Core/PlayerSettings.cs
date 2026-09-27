namespace AtEnd.Core;

public readonly record struct PlayerSettings
{
    public const double MinimumScrollSpeed = 1;
    public const double MaximumScrollSpeed = 10;
    public const double NormalScrollSpeed = 7;
    public const double MinimumTimingOffsetMilliseconds = -500;
    public const double MaximumTimingOffsetMilliseconds = 500;

    public PlayerSettings(
        double scrollSpeed,
        double globalTimingOffsetMilliseconds,
        double inputOffsetMilliseconds,
        int gridDensity)
    {
        if (!double.IsFinite(scrollSpeed)
            || scrollSpeed < MinimumScrollSpeed
            || scrollSpeed > MaximumScrollSpeed)
        {
            throw new ArgumentOutOfRangeException(nameof(scrollSpeed));
        }

        ValidateTimingOffset(
            globalTimingOffsetMilliseconds,
            nameof(globalTimingOffsetMilliseconds));
        ValidateTimingOffset(inputOffsetMilliseconds, nameof(inputOffsetMilliseconds));
        if (gridDensity is not (3 or 9 or 18))
        {
            throw new ArgumentOutOfRangeException(nameof(gridDensity));
        }

        ScrollSpeed = scrollSpeed;
        GlobalTimingOffsetMilliseconds = globalTimingOffsetMilliseconds;
        InputOffsetMilliseconds = inputOffsetMilliseconds;
        GridDensity = gridDensity;
    }

    public static PlayerSettings Default { get; } = new(NormalScrollSpeed, 0, 0, 18);

    public double ScrollSpeed { get; }
    public double VisualSpeedMultiplier => ScrollSpeed / NormalScrollSpeed;
    public double GlobalTimingOffsetMilliseconds { get; }
    public double InputOffsetMilliseconds { get; }
    public int GridDensity { get; }

    private static void ValidateTimingOffset(double value, string parameterName)
    {
        if (!double.IsFinite(value)
            || value < MinimumTimingOffsetMilliseconds
            || value > MaximumTimingOffsetMilliseconds)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
