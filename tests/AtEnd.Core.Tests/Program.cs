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
    ("All six logical channels are isolated correctly", TestLogicalChannels),
    ("Default physical keyboard bindings match the design", TestDefaultBindings),
    ("Rel requirements accept any covered region", TestRelRequirements),
    ("Batch matching maximizes overlapping Rel hits", TestGlobalRelMatching),
    ("One press event can match only one object", TestOnePressPerObject),
    ("Multiple same-channel objects require multiple presses", TestSameChannelMultiplicity),
    ("Held state can satisfy overlapping hold points", TestHeldChannelState),
    ("Physical input state filters repeats and tracks holds", TestPhysicalInputState),
    ("Input batches validate identifiers and enum values", TestInvalidInputData),
    ("Note travel uses absolute audio time", TestNoteTravel),
    ("Gameplay session judges batches and scores hits", TestGameplaySessionHits),
    ("Gameplay session auto-misses overdue objects", TestGameplaySessionMisses),
    ("Input grouping protects later same-channel notes", TestInputGroupingProtection),
    ("Input grouping preserves same-tick multiplicity", TestInputGroupingMultiplicity),
    ("Gameplay session judges hold points from held state", TestGameplaySessionHoldPoints),
    ("Gameplay session validates chart objects and time", TestGameplaySessionValidation),
    ("Song package loader reads the formal test chart", TestSongPackageLoader),
    ("Moving Rel holds interpolate point requirements", TestMovingHoldInterpolation),
    ("Chart loader rejects malformed content", TestChartLoaderValidation),
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

static void TestLogicalChannels()
{
    var requirements = new[]
    {
        InputRequirement.Rel(RelRegion.Left),
        InputRequirement.Rel(RelRegion.Center),
        InputRequirement.Rel(RelRegion.Right),
        InputRequirement.Drm(DrmColor.Red),
        InputRequirement.Drm(DrmColor.Green),
        InputRequirement.Drm(DrmColor.Blue),
    };
    LogicalChannel[] channels = Enum.GetValues<LogicalChannel>();
    Equal(6, channels.Length);

    for (int requirementIndex = 0; requirementIndex < requirements.Length; requirementIndex++)
    {
        for (int channelIndex = 0; channelIndex < channels.Length; channelIndex++)
        {
            Equal(requirementIndex == channelIndex,
                requirements[requirementIndex].Accepts(channels[channelIndex]));
        }
    }
}

static void TestDefaultBindings()
{
    var expectedCounts = new Dictionary<LogicalChannel, int>
    {
        [LogicalChannel.RelLeft] = 8,
        [LogicalChannel.RelCenter] = 9,
        [LogicalChannel.RelRight] = 8,
        [LogicalChannel.DrmRed] = 8,
        [LogicalChannel.DrmGreen] = 9,
        [LogicalChannel.DrmBlue] = 10,
    };

    Equal(52, DefaultKeyboardBindings.All.Count);
    foreach ((LogicalChannel channel, int count) in expectedCounts)
    {
        Equal(count, DefaultKeyboardBindings.All.Count(binding => binding.Value == channel));
    }

    Equal(LogicalChannel.RelLeft, DefaultKeyboardBindings.All[PhysicalKey.A]);
    Equal(LogicalChannel.RelLeft, DefaultKeyboardBindings.All[PhysicalKey.LeftShift]);
    Equal(LogicalChannel.RelCenter, DefaultKeyboardBindings.All[PhysicalKey.N]);
    Equal(LogicalChannel.RelCenter, DefaultKeyboardBindings.All[PhysicalKey.K]);
    Equal(LogicalChannel.RelRight, DefaultKeyboardBindings.All[PhysicalKey.Semicolon]);
    Equal(LogicalChannel.RelRight, DefaultKeyboardBindings.All[PhysicalKey.RightShift]);
    Equal(LogicalChannel.DrmRed, DefaultKeyboardBindings.All[PhysicalKey.Digit1]);
    Equal(LogicalChannel.DrmRed, DefaultKeyboardBindings.All[PhysicalKey.Tab]);
    Equal(LogicalChannel.DrmGreen, DefaultKeyboardBindings.All[PhysicalKey.U]);
    Equal(LogicalChannel.DrmGreen, DefaultKeyboardBindings.All[PhysicalKey.I]);
    Equal(LogicalChannel.DrmBlue, DefaultKeyboardBindings.All[PhysicalKey.LeftBracket]);
    Equal(LogicalChannel.DrmBlue, DefaultKeyboardBindings.All[PhysicalKey.Backspace]);
}

static void TestRelRequirements()
{
    InputRequirement leftOrCenter = InputRequirement.Rel(RelRegion.Left | RelRegion.Center);
    Equal(true, leftOrCenter.Accepts(LogicalChannel.RelLeft));
    Equal(true, leftOrCenter.Accepts(LogicalChannel.RelCenter));
    Equal(false, leftOrCenter.Accepts(LogicalChannel.RelRight));
    Equal(false, leftOrCenter.Accepts(LogicalChannel.DrmRed));

    InputRequirement all = InputRequirement.Rel(RelRegion.All);
    Equal(true, all.Accepts(LogicalChannel.RelLeft));
    Equal(true, all.Accepts(LogicalChannel.RelCenter));
    Equal(true, all.Accepts(LogicalChannel.RelRight));
}

static void TestGlobalRelMatching()
{
    var candidates = new[]
    {
        new ClickCandidate(10, InputRequirement.Rel(RelRegion.Left | RelRegion.Center)),
        new ClickCandidate(20, InputRequirement.Rel(RelRegion.Center | RelRegion.Right)),
    };
    var presses = new[]
    {
        new PressEvent(100, LogicalChannel.RelCenter),
        new PressEvent(200, LogicalChannel.RelRight),
    };

    BatchMatchResult result = BatchInputMatcher.Match(candidates, presses);
    Equal(2, result.Matches.Count);
    Equal(new InputMatch(10, 100), result.Matches[0]);
    Equal(new InputMatch(20, 200), result.Matches[1]);
    Equal(0, result.UnmatchedObjectIds.Count);
    Equal(0, result.UnmatchedEventIds.Count);
}

static void TestOnePressPerObject()
{
    var candidates = new[]
    {
        new ClickCandidate(1, InputRequirement.Drm(DrmColor.Red)),
        new ClickCandidate(2, InputRequirement.Drm(DrmColor.Red)),
    };
    BatchMatchResult result = BatchInputMatcher.Match(candidates,
        new[] { new PressEvent(9, LogicalChannel.DrmRed) });
    Equal(1, result.Matches.Count);
    Equal(1, result.UnmatchedObjectIds.Count);
    Equal(0, result.UnmatchedEventIds.Count);
}

static void TestSameChannelMultiplicity()
{
    var candidates = new[]
    {
        new ClickCandidate(1, InputRequirement.Rel(RelRegion.Left)),
        new ClickCandidate(2, InputRequirement.Rel(RelRegion.Left)),
        new ClickCandidate(3, InputRequirement.Rel(RelRegion.Left)),
    };
    var presses = new[]
    {
        new PressEvent(11, LogicalChannel.RelLeft),
        new PressEvent(12, LogicalChannel.RelLeft),
    };

    BatchMatchResult result = BatchInputMatcher.Match(candidates, presses);
    Equal(2, result.Matches.Count);
    Equal(1, result.UnmatchedObjectIds.Count);
    Equal(3L, result.UnmatchedObjectIds[0]);

    BatchMatchResult isolated = BatchInputMatcher.Match(
        new[] { new ClickCandidate(4, InputRequirement.Drm(DrmColor.Blue)) },
        new[] { new PressEvent(13, LogicalChannel.DrmGreen) });
    Equal(0, isolated.Matches.Count);
    Equal(1, isolated.UnmatchedEventIds.Count);
}

static void TestHeldChannelState()
{
    InputRequirement requirement = InputRequirement.Rel(RelRegion.Left | RelRegion.Center);
    var held = new[] { LogicalChannel.RelCenter };
    Equal(true, requirement.IsHeldBy(held));
    Equal(true, requirement.IsHeldBy(held));
    Equal(false, InputRequirement.Drm(DrmColor.Red).IsHeldBy(held));
    Equal(true, InputRequirement.Drm(DrmColor.Red)
        .IsHeldBy(new[] { LogicalChannel.DrmRed }));
}

static void TestPhysicalInputState()
{
    var input = new LogicalInputState<string>(new Dictionary<string, LogicalChannel>
    {
        ["A"] = LogicalChannel.RelLeft,
        ["S"] = LogicalChannel.RelLeft,
        ["1"] = LogicalChannel.DrmRed,
    });

    PressEvent first = input.Press("A")!.Value;
    Equal(LogicalChannel.RelLeft, first.Channel);
    Equal<PressEvent?>(null, input.Press("A"));
    Equal<PressEvent?>(null, input.Press("unmapped"));
    Equal(true, input.IsChannelHeld(LogicalChannel.RelLeft));
    Equal(true, input.IsRequirementHeld(InputRequirement.Rel(RelRegion.Left)));

    PressEvent second = input.Press("S")!.Value;
    Equal(first.EventId + 1, second.EventId);
    Equal(true, input.Release("A"));
    Equal(true, input.IsChannelHeld(LogicalChannel.RelLeft));
    Equal(false, input.Release("A"));
    Equal(true, input.Release("S"));
    Equal(false, input.IsChannelHeld(LogicalChannel.RelLeft));

    PressEvent third = input.Press("A")!.Value;
    Equal(second.EventId + 1, third.EventId);
}

static void TestInvalidInputData()
{
    Throws<ArgumentOutOfRangeException>(() => InputRequirement.Rel(RelRegion.None));
    Throws<ArgumentOutOfRangeException>(() => InputRequirement.Rel((RelRegion)8));
    Throws<ArgumentOutOfRangeException>(() => InputRequirement.Drm((DrmColor)99));
    Throws<ArgumentOutOfRangeException>(() => new PressEvent(1, (LogicalChannel)99));
    Throws<ArgumentException>(() => new LogicalInputState<string>(
        new Dictionary<string, LogicalChannel> { ["bad"] = (LogicalChannel)99 }));

    var requirement = InputRequirement.Rel(RelRegion.Left);
    Throws<ArgumentException>(() => BatchInputMatcher.Match(
        new[] { new ClickCandidate(1, requirement), new ClickCandidate(1, requirement) },
        Array.Empty<PressEvent>()));
    Throws<ArgumentException>(() => BatchInputMatcher.Match(
        Array.Empty<ClickCandidate>(),
        new[]
        {
            new PressEvent(1, LogicalChannel.RelLeft),
            new PressEvent(1, LogicalChannel.RelCenter),
        }));
    Throws<ArgumentException>(() => BatchInputMatcher.Match(
        new[] { new ClickCandidate(1, default) },
        Array.Empty<PressEvent>()));
}

static void TestNoteTravel()
{
    var timing = new TimingMap(0.25, 120, new[] { new BpmChange(3840, 240) });
    const long targetTick = 5760;
    double targetTime = timing.GetAudioTimeSeconds(targetTick);

    Near(0, NoteTravel.GetProgress(timing, targetTick, targetTime - 2, 2));
    Near(0.5, NoteTravel.GetProgress(timing, targetTick, targetTime - 1, 2));
    Near(1, NoteTravel.GetProgress(timing, targetTick, targetTime, 2));
    Near(1.25, NoteTravel.GetProgress(timing, targetTick, targetTime + 0.5, 2));
    Near(0.75, NoteTravel.GetProgress(timing, targetTick, targetTime - 0.5, 2));

    Throws<ArgumentOutOfRangeException>(() =>
        NoteTravel.GetProgress(timing, targetTick, double.NaN, 2));
    Throws<ArgumentOutOfRangeException>(() =>
        NoteTravel.GetProgress(timing, targetTick, 0, 0));
}

static void TestGameplaySessionHits()
{
    var timing = new TimingMap(0, 120);
    var session = new ClickGameplaySession(timing, new[]
    {
        new ClickScoringObject(1, 1920, InputCategory.Rel,
            InputRequirement.Rel(RelRegion.Left | RelRegion.Center)),
        new ClickScoringObject(2, 1920, InputCategory.Rel,
            InputRequirement.Rel(RelRegion.Center | RelRegion.Right)),
        new ClickScoringObject(3, 3840, InputCategory.Drm,
            InputRequirement.Drm(DrmColor.Red)),
    }, new JudgmentEvaluator(JudgmentWindows.Default));

    IReadOnlyList<GameplayJudgment> chord = session.JudgeBatch(0.5, new[]
    {
        new PressEvent(10, LogicalChannel.RelCenter),
        new PressEvent(11, LogicalChannel.RelRight),
    });
    Equal(2, chord.Count);
    Equal(Judgment.StrictlyPrecise, chord[0].Judgment);
    Equal(Judgment.StrictlyPrecise, chord[1].Judgment);
    Equal(2, session.Snapshot().CurrentCombo);

    IReadOnlyList<GameplayJudgment> drm = session.JudgeBatch(1.06,
        new[] { new PressEvent(12, LogicalChannel.DrmRed) });
    Equal(1, drm.Count);
    Equal(Judgment.Precise, drm[0].Judgment);
    Equal(true, session.IsComplete);
    Equal(1_000_002, session.Snapshot().TotalScore);

    session.CompleteNormally(1.2);
    Equal(CompletionMark.AllPrecise, session.Snapshot().HighestCompletionMark);
}

static void TestGameplaySessionMisses()
{
    var timing = new TimingMap(0, 120);
    var session = new ClickGameplaySession(timing, new[]
    {
        new ClickScoringObject(1, 1920, InputCategory.Rel,
            InputRequirement.Rel(RelRegion.Left)),
        new ClickScoringObject(2, 3840, InputCategory.Drm,
            InputRequirement.Drm(DrmColor.Blue)),
    }, new JudgmentEvaluator(JudgmentWindows.Default));

    Equal(0, session.AdvanceTime(0.665).Count);
    IReadOnlyList<GameplayJudgment> misses = session.AdvanceTime(0.666);
    Equal(1, misses.Count);
    Equal(Judgment.Chaotic, misses[0].Judgment);
    Equal<TimingDirection?>(null, misses[0].Direction);
    Equal(0, session.Snapshot().CurrentCombo);

    Equal(0, session.JudgeBatch(0.7,
        new[] { new PressEvent(20, LogicalChannel.RelLeft) }).Count);
    session.CompleteNormally(2);
    Equal(true, session.IsNormallyCompleted);
    Equal(CompletionMark.None, session.Snapshot().HighestCompletionMark);
}

static void TestInputGroupingProtection()
{
    var timing = new TimingMap(0, 120);
    var session = new ClickGameplaySession(timing, new[]
    {
        new ClickScoringObject(1, 1920, InputCategory.Rel,
            InputRequirement.Rel(RelRegion.Left)),
        new ClickScoringObject(2, 2140, InputCategory.Rel,
            InputRequirement.Rel(RelRegion.Left)),
    }, new JudgmentEvaluator(JudgmentWindows.Default));

    IReadOnlyList<GameplayJudgment> protectedBatch = session.JudgeTimedBatch(new[]
    {
        new TimedPressEvent(new PressEvent(1, LogicalChannel.RelLeft), 0.500),
        new TimedPressEvent(new PressEvent(2, LogicalChannel.RelLeft), 0.507),
    });
    Equal(1, protectedBatch.Count);
    Equal(1L, protectedBatch[0].ObjectId);
    Equal(false, session.IsJudged(2));

    IReadOnlyList<GameplayJudgment> laterPress = session.JudgeTimedBatch(new[]
    {
        new TimedPressEvent(new PressEvent(3, LogicalChannel.RelLeft), 0.520),
    });
    Equal(1, laterPress.Count);
    Equal(2L, laterPress[0].ObjectId);
}

static void TestInputGroupingMultiplicity()
{
    var timing = new TimingMap(0, 120);
    var session = new ClickGameplaySession(timing, new[]
    {
        new ClickScoringObject(1, 1920, InputCategory.Rel,
            InputRequirement.Rel(RelRegion.Left)),
        new ClickScoringObject(2, 1920, InputCategory.Rel,
            InputRequirement.Rel(RelRegion.Left)),
    }, new JudgmentEvaluator(JudgmentWindows.Default));

    IReadOnlyList<GameplayJudgment> chord = session.JudgeTimedBatch(new[]
    {
        new TimedPressEvent(new PressEvent(1, LogicalChannel.RelLeft), 0.500),
        new TimedPressEvent(new PressEvent(2, LogicalChannel.RelLeft), 0.507),
    });
    Equal(2, chord.Count);
    Equal(true, session.IsComplete);
}

static void TestGameplaySessionHoldPoints()
{
    var timing = new TimingMap(0, 120);
    var session = new ClickGameplaySession(
        timing,
        new[]
        {
            new ClickScoringObject(1, 0, InputCategory.Rel,
                InputRequirement.Rel(RelRegion.Left)),
        },
        new[]
        {
            new HoldScoringPoint(1, 0, 1920, InputCategory.Rel,
                InputRequirement.Rel(RelRegion.Left)),
            new HoldScoringPoint(1, 1, 3840, InputCategory.Rel,
                InputRequirement.Rel(RelRegion.Right)),
        },
        new JudgmentEvaluator(JudgmentWindows.Default));

    Equal(1, session.JudgeBatch(0,
        new[] { new PressEvent(1, LogicalChannel.RelLeft) },
        requirement => requirement.Accepts(LogicalChannel.RelLeft)).Count);

    IReadOnlyList<GameplayJudgment> held = session.AdvanceTime(
        0.5,
        requirement => requirement.Accepts(LogicalChannel.RelLeft));
    Equal(1, held.Count);
    Equal(Judgment.StrictlyPrecise, held[0].Judgment);
    Equal(0, held[0].HoldPointIndex);
    Equal<TimingDirection?>(null, held[0].Direction);

    IReadOnlyList<GameplayJudgment> released = session.AdvanceTime(1.0, _ => false);
    Equal(1, released.Count);
    Equal(Judgment.Chaotic, released[0].Judgment);
    Equal(1, released[0].HoldPointIndex);
    Equal(true, session.IsComplete);
    Equal(0, session.Snapshot().CurrentCombo);
    Equal(2, session.Snapshot().MaximumCombo);
}

static void TestGameplaySessionValidation()
{
    var timing = new TimingMap(0, 120);
    var evaluator = new JudgmentEvaluator(JudgmentWindows.Default);
    Throws<ArgumentException>(() => new ClickGameplaySession(timing,
        Array.Empty<ClickScoringObject>(), evaluator));
    Throws<ArgumentException>(() => new ClickGameplaySession(timing, new[]
    {
        new ClickScoringObject(1, 1, InputCategory.Rel, InputRequirement.Rel(RelRegion.Left)),
        new ClickScoringObject(1, 2, InputCategory.Rel, InputRequirement.Rel(RelRegion.Right)),
    }, evaluator));
    Throws<ArgumentException>(() => new ClickGameplaySession(timing, new[]
    {
        new ClickScoringObject(1, 1, InputCategory.Rel, InputRequirement.Drm(DrmColor.Red)),
    }, evaluator));

    var session = new ClickGameplaySession(timing, new[]
    {
        new ClickScoringObject(1, 1920, InputCategory.Rel,
            InputRequirement.Rel(RelRegion.Left)),
    }, evaluator);
    session.AdvanceTime(0.4);
    Throws<InvalidOperationException>(() => session.AdvanceTime(0.3));
}

static void TestSongPackageLoader()
{
    string? root = AppContext.BaseDirectory;
    while (root is not null && !File.Exists(Path.Combine(root, "project.godot")))
    {
        root = Directory.GetParent(root)?.FullName;
    }

    if (root is null)
    {
        throw new InvalidOperationException("Could not locate the AtEnd repository root.");
    }

    SongPackageDefinition package = SongPackageLoader.LoadDirectory(
        Path.Combine(root, "songs", "test-song"));
    Equal("test-song", package.Song.SongId);
    Equal("大国奏音", package.Song.Artist);
    Near(1.846, package.Timing.TimingMap.AudioTimeAtTickZeroSeconds);
    Near(260, package.Timing.TimingMap.InitialBeatsPerMinute);
    Equal(1, package.Charts.Count);

    ChartDefinition chart = package.Charts[0];
    Equal("test-song-test", chart.ChartId);
    Equal("Test", chart.Difficulty.Name);
    Equal(3, chart.Difficulty.Level);
    Equal("TynnyVessels", chart.Charter);
    Equal(20, chart.Objects.Count);
    Equal(18, chart.Objects.Count(item => item.Type == ChartObjectType.Click));
    Equal(2, chart.Objects.Count(item => item.Type == ChartObjectType.Hold));
    Equal(2, chart.Objects[0].Path.Count);
    Equal(21120L, chart.Objects[0].Path[^1].Tick);
    Equal(59520L, chart.Objects[^1].EndTick);
    Near(7.153846153846154,
        package.Timing.TimingMap.GetAudioTimeSeconds(chart.Objects[^1].EndTick)
        - package.Timing.TimingMap.AudioTimeAtTickZeroSeconds);
    Equal(18, chart.CreateClickScoringObjects().Count);
    Equal(20, chart.CreateHeadScoringObjects().Count);
    Equal(11, chart.CreateHoldScoringPoints().Count);
    Equal(RelRegion.Right, chart.Objects[1].GetInputRequirement().RelRegions);
    Equal(DrmColor.Green, chart.Objects[2].GetInputRequirement().DrmColor);
}

static void TestMovingHoldInterpolation()
{
    var hold = new ChartObjectDefinition(
        1,
        ChartObjectType.Hold,
        InputCategory.Rel,
        null,
        0,
        1920,
        new[]
        {
            new LanePoint(0, 0, 6),
            new LanePoint(1920, 12, 6),
        },
        new[] { 960L });

    (double lane, double width) = hold.GetLaneGeometryAtTick(960);
    Near(6, lane);
    Near(6, width);
    Equal(RelRegion.Center, hold.GetInputRequirementAtTick(960).RelRegions);
    Throws<ArgumentOutOfRangeException>(() => hold.GetLaneGeometryAtTick(1921));
}

static void TestChartLoaderValidation()
{
    const string invalid = """
        {
          "formatVersion": 1,
          "chartId": "invalid",
          "difficulty": { "name": "Test", "level": 1 },
          "charter": "Tester",
          "visualSpeedEvents": [],
          "objects": [
            { "objectId": 1, "type": "click", "inputType": "rel", "tick": 0, "lane": 17, "width": 2 }
          ]
        }
        """;
    Throws<InvalidDataException>(() => SongPackageLoader.ParseChart(invalid));
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
