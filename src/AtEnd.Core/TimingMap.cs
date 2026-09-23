namespace AtEnd.Core;

public readonly record struct BpmChange(long Tick, double BeatsPerMinute);

public sealed class TimingMap
{
    public const int PulsesPerQuarterNote = 1920;

    private readonly Segment[] _segments;
    private readonly IReadOnlyList<BpmChange> _bpmChanges;

    public TimingMap(double audioTimeAtTickZeroSeconds, double initialBeatsPerMinute,
        IEnumerable<BpmChange>? bpmChanges = null)
    {
        ValidateFinite(audioTimeAtTickZeroSeconds, nameof(audioTimeAtTickZeroSeconds));
        ValidateBpm(initialBeatsPerMinute, nameof(initialBeatsPerMinute));

        AudioTimeAtTickZeroSeconds = audioTimeAtTickZeroSeconds;
        InitialBeatsPerMinute = initialBeatsPerMinute;

        BpmChange[] orderedChanges = (bpmChanges ?? Array.Empty<BpmChange>())
            .OrderBy(change => change.Tick)
            .ToArray();
        ValidateChanges(orderedChanges);
        _bpmChanges = Array.AsReadOnly(orderedChanges);
        _segments = BuildSegments(audioTimeAtTickZeroSeconds, initialBeatsPerMinute, orderedChanges);
    }

    public double AudioTimeAtTickZeroSeconds { get; }
    public double InitialBeatsPerMinute { get; }
    public IReadOnlyList<BpmChange> BpmChanges => _bpmChanges;

    public double GetAudioTimeSeconds(long tick)
    {
        Segment segment = _segments[FindSegmentForTick(tick)];
        double elapsedTicks = (double)tick - segment.StartTick;
        return segment.StartAudioTimeSeconds + elapsedTicks * SecondsPerTick(segment.BeatsPerMinute);
    }

    public double GetAudioTimeMilliseconds(long tick) => GetAudioTimeSeconds(tick) * 1000d;

    public double GetTickAtAudioTimeSeconds(double audioTimeSeconds)
    {
        ValidateFinite(audioTimeSeconds, nameof(audioTimeSeconds));
        Segment segment = _segments[FindSegmentForAudioTime(audioTimeSeconds)];
        double elapsedSeconds = audioTimeSeconds - segment.StartAudioTimeSeconds;
        return segment.StartTick + elapsedSeconds / SecondsPerTick(segment.BeatsPerMinute);
    }

    private static Segment[] BuildSegments(double tickZeroTime, double initialBpm,
        IReadOnlyList<BpmChange> changes)
    {
        var segments = new List<Segment> { new(0, tickZeroTime, initialBpm) };
        foreach (BpmChange change in changes)
        {
            if (change.Tick == 0)
            {
                segments[0] = new Segment(0, tickZeroTime, change.BeatsPerMinute);
                continue;
            }

            Segment previous = segments[^1];
            double changeTime = previous.StartAudioTimeSeconds
                + ((double)change.Tick - previous.StartTick) * SecondsPerTick(previous.BeatsPerMinute);
            segments.Add(new Segment(change.Tick, changeTime, change.BeatsPerMinute));
        }

        return segments.ToArray();
    }

    private int FindSegmentForTick(long tick)
    {
        int low = 0;
        int high = _segments.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (_segments[middle].StartTick <= tick)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Math.Max(0, high);
    }

    private int FindSegmentForAudioTime(double audioTimeSeconds)
    {
        int low = 0;
        int high = _segments.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (_segments[middle].StartAudioTimeSeconds <= audioTimeSeconds)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Math.Max(0, high);
    }

    private static void ValidateChanges(IReadOnlyList<BpmChange> changes)
    {
        long? previousTick = null;
        foreach (BpmChange change in changes)
        {
            if (change.Tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(changes), "BPM change ticks cannot be negative.");
            }

            ValidateBpm(change.BeatsPerMinute, nameof(changes));
            if (change.Tick == previousTick)
            {
                throw new ArgumentException("Only one BPM change may exist at a tick.", nameof(changes));
            }

            previousTick = change.Tick;
        }
    }

    private static void ValidateBpm(double beatsPerMinute, string parameterName)
    {
        ValidateFinite(beatsPerMinute, parameterName);
        if (beatsPerMinute <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "BPM must be greater than zero.");
        }
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be finite.");
        }
    }

    private static double SecondsPerTick(double beatsPerMinute) =>
        60d / (beatsPerMinute * PulsesPerQuarterNote);

    private readonly record struct Segment(
        long StartTick,
        double StartAudioTimeSeconds,
        double BeatsPerMinute);
}
