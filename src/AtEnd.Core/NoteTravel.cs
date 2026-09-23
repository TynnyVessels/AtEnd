namespace AtEnd.Core;

public static class NoteTravel
{
    public static double GetProgress(
        TimingMap timingMap,
        long targetTick,
        double currentAudioTimeSeconds,
        double approachDurationSeconds)
    {
        ArgumentNullException.ThrowIfNull(timingMap);
        if (!double.IsFinite(currentAudioTimeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(currentAudioTimeSeconds));
        }

        if (!double.IsFinite(approachDurationSeconds) || approachDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approachDurationSeconds));
        }

        double targetAudioTime = timingMap.GetAudioTimeSeconds(targetTick);
        return 1d - ((targetAudioTime - currentAudioTimeSeconds) / approachDurationSeconds);
    }
}
