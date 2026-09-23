namespace AtEnd.Core;

public enum Judgment
{
    Chaotic = 0,
    Misaligned = 1,
    Precise = 2,
    StrictlyPrecise = 3,
}

public enum TimingDirection
{
    Early,
    Exact,
    Late,
}

public readonly record struct ClickJudgment(Judgment Judgment, TimingDirection Direction);

public sealed record JudgmentWindows
{
    public static JudgmentWindows Default { get; } = new(45, 75, 120, 165);

    public JudgmentWindows(double strictlyPreciseMilliseconds, double preciseMilliseconds,
        double misalignedMilliseconds, double chaoticMilliseconds)
    {
        if (strictlyPreciseMilliseconds < 0 ||
            strictlyPreciseMilliseconds > preciseMilliseconds ||
            preciseMilliseconds > misalignedMilliseconds ||
            misalignedMilliseconds > chaoticMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(strictlyPreciseMilliseconds),
                "Judgment windows must be non-negative and ordered from strictest to widest.");
        }

        StrictlyPreciseMilliseconds = strictlyPreciseMilliseconds;
        PreciseMilliseconds = preciseMilliseconds;
        MisalignedMilliseconds = misalignedMilliseconds;
        ChaoticMilliseconds = chaoticMilliseconds;
    }

    public double StrictlyPreciseMilliseconds { get; }
    public double PreciseMilliseconds { get; }
    public double MisalignedMilliseconds { get; }
    public double ChaoticMilliseconds { get; }
}

public sealed class JudgmentEvaluator
{
    private readonly JudgmentWindows _windows;

    public JudgmentEvaluator(JudgmentWindows windows)
    {
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
    }

    public ClickJudgment? EvaluateClick(double errorMilliseconds)
    {
        if (!double.IsFinite(errorMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(errorMilliseconds));
        }

        double absoluteError = Math.Abs(errorMilliseconds);
        Judgment? judgment = absoluteError <= _windows.StrictlyPreciseMilliseconds
            ? Judgment.StrictlyPrecise
            : absoluteError <= _windows.PreciseMilliseconds
                ? Judgment.Precise
                : absoluteError <= _windows.MisalignedMilliseconds
                    ? Judgment.Misaligned
                    : absoluteError <= _windows.ChaoticMilliseconds
                        ? Judgment.Chaotic
                        : null;

        if (judgment is null)
        {
            return null;
        }

        TimingDirection direction = errorMilliseconds < 0
            ? TimingDirection.Early
            : errorMilliseconds > 0
                ? TimingDirection.Late
                : TimingDirection.Exact;
        return new ClickJudgment(judgment.Value, direction);
    }

    public Judgment? EvaluateOverdue(double elapsedAfterTargetMilliseconds) =>
        elapsedAfterTargetMilliseconds > _windows.ChaoticMilliseconds
            ? Judgment.Chaotic
            : null;

    public static Judgment EvaluateHoldPoint(bool isLegalChannelHeld) =>
        isLegalChannelHeld ? Judgment.StrictlyPrecise : Judgment.Chaotic;
}
