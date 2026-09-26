namespace AtEnd.Core;

public sealed class VisualSpeedMap
{
    private readonly TimingMap _timingMap;
    private readonly IReadOnlyList<VisualSpeedEvent> _events;
    private readonly SpeedPoint[] _points;

    public VisualSpeedMap(
        TimingMap timingMap,
        IEnumerable<VisualSpeedEvent>? events = null)
    {
        ArgumentNullException.ThrowIfNull(timingMap);
        _timingMap = timingMap;

        VisualSpeedEvent[] orderedEvents = (events ?? Array.Empty<VisualSpeedEvent>())
            .OrderBy(item => item.Tick)
            .ToArray();
        for (int index = 0; index < orderedEvents.Length; index++)
        {
            VisualSpeedEvent item = orderedEvents[index];
            if (item.Tick < 0 || !double.IsFinite(item.Multiplier))
            {
                throw new ArgumentOutOfRangeException(nameof(events));
            }

            if (index > 0 && orderedEvents[index - 1].Tick == item.Tick)
            {
                throw new ArgumentException(
                    $"Visual speed events cannot share tick {item.Tick}.",
                    nameof(events));
            }
        }

        _events = Array.AsReadOnly(orderedEvents);
        _points = orderedEvents
            .Select(item => new SpeedPoint(
                _timingMap.GetAudioTimeSeconds(item.Tick),
                item.Multiplier))
            .ToArray();
    }

    public IReadOnlyList<VisualSpeedEvent> Events => _events;

    public double GetVisualDistanceSeconds(
        double fromAudioTimeSeconds,
        double toAudioTimeSeconds)
    {
        ValidateFinite(fromAudioTimeSeconds, nameof(fromAudioTimeSeconds));
        ValidateFinite(toAudioTimeSeconds, nameof(toAudioTimeSeconds));
        if (fromAudioTimeSeconds == toAudioTimeSeconds)
        {
            return 0;
        }

        if (fromAudioTimeSeconds > toAudioTimeSeconds)
        {
            return -GetVisualDistanceSeconds(toAudioTimeSeconds, fromAudioTimeSeconds);
        }

        double cursor = fromAudioTimeSeconds;
        double multiplier = GetMultiplierAtAudioTime(fromAudioTimeSeconds);
        double distance = 0;
        foreach (SpeedPoint point in _points)
        {
            if (point.AudioTimeSeconds <= fromAudioTimeSeconds)
            {
                continue;
            }

            if (point.AudioTimeSeconds >= toAudioTimeSeconds)
            {
                break;
            }

            distance += (point.AudioTimeSeconds - cursor) * multiplier;
            cursor = point.AudioTimeSeconds;
            multiplier = point.Multiplier;
        }

        return distance + ((toAudioTimeSeconds - cursor) * multiplier);
    }

    public double GetProgress(
        long targetTick,
        double currentAudioTimeSeconds,
        double approachDurationSeconds,
        double playerSpeedMultiplier = 1)
    {
        if (!double.IsFinite(approachDurationSeconds) || approachDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approachDurationSeconds));
        }

        if (!double.IsFinite(playerSpeedMultiplier) || playerSpeedMultiplier <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerSpeedMultiplier));
        }

        double targetAudioTime = _timingMap.GetAudioTimeSeconds(targetTick);
        double remainingVisualDistance = GetVisualDistanceSeconds(
            currentAudioTimeSeconds,
            targetAudioTime);
        return 1 - ((remainingVisualDistance * playerSpeedMultiplier)
            / approachDurationSeconds);
    }

    private double GetMultiplierAtAudioTime(double audioTimeSeconds)
    {
        double multiplier = 1;
        foreach (SpeedPoint point in _points)
        {
            if (point.AudioTimeSeconds > audioTimeSeconds)
            {
                break;
            }

            multiplier = point.Multiplier;
        }

        return multiplier;
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private readonly record struct SpeedPoint(
        double AudioTimeSeconds,
        double Multiplier);
}
