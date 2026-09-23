using AtEnd.Core;

var tests = new (string Name, Action Body)[]
{
    ("Click judgment positive and negative boundaries", TestClickBoundaries),
    ("Judgment windows are configurable and validated", TestWindowConfiguration),
    ("Hold points are binary Strictly Precise or Chaotic", TestHoldPoints),
    ("Rel and Drm pools score independently with floor rounding", TestPoolsAndRounding),
    ("Missing Rel receives completion compensation only", TestMissingRel),
    ("Missing Drm receives completion compensation only", TestMissingDrm),
    ("SP, 101 percent accuracy, and combo", TestPerfectRun),
    ("Combo resets on Chaotic and retains maximum", TestComboReset),
    ("Completion marks select highest display", TestCompletionMarks),
    ("Completion statistics progress cumulatively", TestCompletionStatistics),
    ("Invalid and incomplete charts are rejected", TestInvalidStates),
    ("Timing map uses PPQN and audio offset", TestTimingMapBasics),
    ("Timing map integrates BPM changes at exact boundaries", TestBpmChanges),
    ("Timing conversion is reversible", TestTimingRoundTrip),
    ("Timing map rejects invalid data", TestInvalidTimingData),
};

int failures = 0;
foreach ((string name, Action body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void TestClickBoundaries()
{
    var evaluator = new JudgmentEvaluator(JudgmentWindows.Default);
    foreach (double sign in new[] { -1d, 1d })
    {
        Equal(Judgment.StrictlyPrecise, evaluator.EvaluateClick(sign * 45)!.Value.Judgment);
        Equal(Judgment.Precise, evaluator.EvaluateClick(sign * 45.001)!.Value.Judgment);
        Equal(Judgment.Precise, evaluator.EvaluateClick(sign * 75)!.Value.Judgment);
        Equal(Judgment.Misaligned, evaluator.EvaluateClick(sign * 75.001)!.Value.Judgment);
        Equal(Judgment.Misaligned, evaluator.EvaluateClick(sign * 120)!.Value.Judgment);
        Equal(Judgment.Chaotic, evaluator.EvaluateClick(sign * 120.001)!.Value.Judgment);
        Equal(Judgment.Chaotic, evaluator.EvaluateClick(sign * 165)!.Value.Judgment);
        Equal<ClickJudgment?>(null, evaluator.EvaluateClick(sign * 165.001));
    }

    Equal(TimingDirection.Early, evaluator.EvaluateClick(-1)!.Value.Direction);
    Equal(TimingDirection.Exact, evaluator.EvaluateClick(0)!.Value.Direction);
    Equal(TimingDirection.Late, evaluator.EvaluateClick(1)!.Value.Direction);
    Equal<Judgment?>(null, evaluator.EvaluateOverdue(165));
    Equal(Judgment.Chaotic, evaluator.EvaluateOverdue(165.001));
}

static void TestWindowConfiguration()
{
    var evaluator = new JudgmentEvaluator(new JudgmentWindows(10, 20, 30, 40));
    Equal(Judgment.StrictlyPrecise, evaluator.EvaluateClick(10)!.Value.Judgment);
    Equal(Judgment.Precise, evaluator.EvaluateClick(10.1)!.Value.Judgment);
    Throws<ArgumentOutOfRangeException>(() => new JudgmentWindows(45, 40, 120, 165));
}

static void TestHoldPoints()
{
    Equal(Judgment.StrictlyPrecise, JudgmentEvaluator.EvaluateHoldPoint(true));
    Equal(Judgment.Chaotic, JudgmentEvaluator.EvaluateHoldPoint(false));
}

static void TestPoolsAndRounding()
{
    var run = new ScoreRun(3, 2);
    run.Add(InputCategory.Rel, Judgment.StrictlyPrecise);
    run.Add(InputCategory.Rel, Judgment.Precise);
    run.Add(InputCategory.Rel, Judgment.Misaligned);
    run.Add(InputCategory.Drm, Judgment.Misaligned);
    run.Add(InputCategory.Drm, Judgment.Chaotic);
    run.CompleteNormally();
    ScoreSnapshot score = run.Snapshot();
    Equal(666_666, score.RelScore);
    Equal(50_000, score.DrmScore);
    Equal(716_666, score.BaseScore);
    Equal(716_667, score.TotalScore);
}

static void TestMissingRel()
{
    var run = new ScoreRun(0, 1);
    run.Add(InputCategory.Drm, Judgment.Precise);
    Equal(0, run.Snapshot().RelScore);
    run.CompleteNormally();
    ScoreSnapshot score = run.Snapshot();
    Equal(800_000, score.RelScore);
    Equal(1_000_000, score.TotalScore);
    Equal(0, score.StrictlyPreciseBonus);
    Equal(100m, score.AccuracyPercent);
    Equal(1, score.MaximumCombo);
}

static void TestMissingDrm()
{
    var run = new ScoreRun(1, 0);
    run.Add(InputCategory.Rel, Judgment.StrictlyPrecise);
    Equal(0, run.Snapshot().DrmScore);
    run.CompleteNormally();
    ScoreSnapshot score = run.Snapshot();
    Equal(200_000, score.DrmScore);
    Equal(1_000_001, score.TotalScore);
    Equal(101m, score.AccuracyPercent);
}

static void TestPerfectRun()
{
    var run = new ScoreRun(2, 1);
    run.Add(InputCategory.Rel, Judgment.StrictlyPrecise);
    run.Add(InputCategory.Drm, JudgmentEvaluator.EvaluateHoldPoint(true));
    run.Add(InputCategory.Rel, Judgment.StrictlyPrecise);
    run.CompleteNormally();
    ScoreSnapshot score = run.Snapshot();
    Equal(3, score.StrictlyPreciseBonus);
    Equal(1_000_003, score.TotalScore);
    Equal(101m, score.AccuracyPercent);
    Equal(3, score.CurrentCombo);
    Equal(3, score.MaximumCombo);
}

static void TestComboReset()
{
    var run = new ScoreRun(4, 0);
    run.Add(InputCategory.Rel, Judgment.Precise);
    run.Add(InputCategory.Rel, Judgment.Misaligned);
    run.Add(InputCategory.Rel, Judgment.Chaotic);
    run.Add(InputCategory.Rel, Judgment.Precise);
    ScoreSnapshot score = run.Snapshot();
    Equal(1, score.CurrentCombo);
    Equal(2, score.MaximumCombo);
}

static void TestCompletionMarks()
{
    Equal(CompletionMark.AllStrictlyPrecise, CompletedMark(Judgment.StrictlyPrecise));
    Equal(CompletionMark.AllPrecise, CompletedMark(Judgment.Precise));
    Equal(CompletionMark.FullCombo, CompletedMark(Judgment.Misaligned));
    Equal(CompletionMark.None, CompletedMark(Judgment.Chaotic));
    Equal("ALL STRICTLY PRECISE", CompletionMark.AllStrictlyPrecise.DisplayName());
    Equal("ASP", CompletionMark.AllStrictlyPrecise.Abbreviation());
}

static CompletionMark CompletedMark(Judgment judgment)
{
    var run = new ScoreRun(1, 0);
    run.Add(InputCategory.Rel, judgment);
    Equal(CompletionMark.None, run.Snapshot().HighestCompletionMark);
    run.CompleteNormally();
    return run.Snapshot().HighestCompletionMark;
}

static void TestCompletionStatistics()
{
    var player = new PlayerCompletionStatistics();
    player.Record("chart-a", CompletionMark.FullCombo);
    player.Record("chart-a", CompletionMark.AllPrecise);
    player.Record("chart-a", CompletionMark.AllStrictlyPrecise);
    player.Record("chart-a", CompletionMark.FullCombo);
    player.Record("chart-b", CompletionMark.AllPrecise);
    player.Record("chart-c", CompletionMark.None);

    ChartCompletionStatistics chart = player.GetChart("chart-a");
    Equal(CompletionMark.AllStrictlyPrecise, chart.HighestMark);
    Equal(4, chart.FullComboCount);
    Equal(2, chart.AllPreciseCount);
    Equal(1, chart.AllStrictlyPreciseCount);
    Equal(new CompletionCounts(5, 3, 1), player.TotalAchievements);
    Equal(new CompletionCounts(2, 2, 1), player.ChartsWithAchievement);
}

static void TestInvalidStates()
{
    Throws<ArgumentException>(() => new ScoreRun(0, 0));
    var incomplete = new ScoreRun(2, 0);
    incomplete.Add(InputCategory.Rel, Judgment.Precise);
    Throws<InvalidOperationException>(incomplete.CompleteNormally);
    Throws<InvalidOperationException>(() => incomplete.Add(InputCategory.Drm, Judgment.Precise));
}

static void TestTimingMapBasics()
{
    Equal(1920, TimingMap.PulsesPerQuarterNote);
    var timing = new TimingMap(1.25, 120);
    Near(1.25, timing.GetAudioTimeSeconds(0));
    Near(1.75, timing.GetAudioTimeSeconds(1920));
    Near(2.25, timing.GetAudioTimeSeconds(3840));
    Near(750, timing.GetAudioTimeMilliseconds(-1920));
}

static void TestBpmChanges()
{
    var timing = new TimingMap(0.5, 120, new[]
    {
        new BpmChange(7680, 60),
        new BpmChange(3840, 240),
    });

    Equal(2, timing.BpmChanges.Count);
    Equal(3840L, timing.BpmChanges[0].Tick);
    Near(1.5, timing.GetAudioTimeSeconds(3840));
    Near(1.75, timing.GetAudioTimeSeconds(5760));
    Near(2.0, timing.GetAudioTimeSeconds(7680));
    Near(3.0, timing.GetAudioTimeSeconds(9600));

    var tickZeroChange = new TimingMap(0, 120, new[] { new BpmChange(0, 60) });
    Near(1.0, tickZeroChange.GetAudioTimeSeconds(1920));
}

static void TestTimingRoundTrip()
{
    var timing = new TimingMap(-0.125, 137.5, new[]
    {
        new BpmChange(1920, 200),
        new BpmChange(7777, 83.25),
    });

    foreach (long tick in new[] { -3840L, 0L, 1L, 1919L, 1920L, 7777L, 100_000L })
    {
        double audioTime = timing.GetAudioTimeSeconds(tick);
        Near(tick, timing.GetTickAtAudioTimeSeconds(audioTime), 1e-7);
    }

    Near(1920, timing.GetTickAtAudioTimeSeconds(timing.GetAudioTimeSeconds(1920)));
    Near(7777, timing.GetTickAtAudioTimeSeconds(timing.GetAudioTimeSeconds(7777)));
}

static void TestInvalidTimingData()
{
    Throws<ArgumentOutOfRangeException>(() => new TimingMap(0, 0));
    Throws<ArgumentOutOfRangeException>(() => new TimingMap(double.NaN, 120));
    Throws<ArgumentOutOfRangeException>(() => new TimingMap(0, 120,
        new[] { new BpmChange(-1, 120) }));
    Throws<ArgumentOutOfRangeException>(() => new TimingMap(0, 120,
        new[] { new BpmChange(1, double.PositiveInfinity) }));
    Throws<ArgumentException>(() => new TimingMap(0, 120,
        new[] { new BpmChange(100, 120), new BpmChange(100, 150) }));

    var timing = new TimingMap(0, 120);
    Throws<ArgumentOutOfRangeException>(() => timing.GetTickAtAudioTimeSeconds(double.NaN));
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void Near(double expected, double actual, double tolerance = 1e-9)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"Expected {expected} ± {tolerance}, got {actual}.");
    }
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
