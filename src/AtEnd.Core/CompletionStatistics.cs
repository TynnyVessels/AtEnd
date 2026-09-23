namespace AtEnd.Core;

public readonly record struct CompletionCounts(int FullCombo, int AllPrecise, int AllStrictlyPrecise);

public sealed class ChartCompletionStatistics
{
    public CompletionMark HighestMark { get; private set; }
    public int FullComboCount { get; private set; }
    public int AllPreciseCount { get; private set; }
    public int AllStrictlyPreciseCount { get; private set; }

    public void Record(CompletionMark mark)
    {
        if (mark is < CompletionMark.None or > CompletionMark.AllStrictlyPrecise)
        {
            throw new ArgumentOutOfRangeException(nameof(mark));
        }

        HighestMark = (CompletionMark)Math.Max((int)HighestMark, (int)mark);
        if (mark >= CompletionMark.FullCombo)
        {
            FullComboCount++;
        }

        if (mark >= CompletionMark.AllPrecise)
        {
            AllPreciseCount++;
        }

        if (mark >= CompletionMark.AllStrictlyPrecise)
        {
            AllStrictlyPreciseCount++;
        }
    }
}

public sealed class PlayerCompletionStatistics
{
    private readonly Dictionary<string, ChartCompletionStatistics> _charts = new(StringComparer.Ordinal);

    public CompletionCounts TotalAchievements { get; private set; }

    public ChartCompletionStatistics GetChart(string chartId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chartId);
        if (!_charts.TryGetValue(chartId, out ChartCompletionStatistics? statistics))
        {
            statistics = new ChartCompletionStatistics();
            _charts.Add(chartId, statistics);
        }

        return statistics;
    }

    public void Record(string chartId, CompletionMark mark)
    {
        ChartCompletionStatistics chart = GetChart(chartId);
        chart.Record(mark);
        TotalAchievements = new CompletionCounts(
            TotalAchievements.FullCombo + (mark >= CompletionMark.FullCombo ? 1 : 0),
            TotalAchievements.AllPrecise + (mark >= CompletionMark.AllPrecise ? 1 : 0),
            TotalAchievements.AllStrictlyPrecise + (mark >= CompletionMark.AllStrictlyPrecise ? 1 : 0));
    }

    public CompletionCounts ChartsWithAchievement => new(
        _charts.Values.Count(value => value.HighestMark >= CompletionMark.FullCombo),
        _charts.Values.Count(value => value.HighestMark >= CompletionMark.AllPrecise),
        _charts.Values.Count(value => value.HighestMark >= CompletionMark.AllStrictlyPrecise));
}
