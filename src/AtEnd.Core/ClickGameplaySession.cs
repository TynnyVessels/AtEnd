namespace AtEnd.Core;

public readonly record struct ClickScoringObject(
    long ObjectId,
    long TargetTick,
    InputCategory Category,
    InputRequirement Requirement);

public readonly record struct HoldScoringPoint(
    long ObjectId,
    int PointIndex,
    long TargetTick,
    InputCategory Category,
    InputRequirement Requirement);

public readonly record struct TimedPressEvent
{
    public TimedPressEvent(PressEvent press, double audioTimeSeconds)
    {
        if (!double.IsFinite(audioTimeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(audioTimeSeconds));
        }

        Press = press;
        AudioTimeSeconds = audioTimeSeconds;
    }

    public PressEvent Press { get; }
    public double AudioTimeSeconds { get; }
}

public readonly record struct GameplayJudgment(
    long ObjectId,
    long? EventId,
    Judgment Judgment,
    TimingDirection? Direction,
    int? HoldPointIndex = null);

public sealed class ClickGameplaySession
{
    public const double DefaultInputGroupingWindowMilliseconds = 12;

    private readonly TimingMap _timingMap;
    private readonly JudgmentEvaluator _evaluator;
    private readonly ClickScoringObject[] _objects;
    private readonly HoldScoringPoint[] _holdPoints;
    private readonly HashSet<long> _judgedObjectIds = new();
    private readonly HashSet<(long ObjectId, int PointIndex)> _judgedHoldPoints = new();
    private readonly ScoreRun _score;
    private readonly Dictionary<LogicalChannel, TargetGroupGuard> _targetGroupGuards = new();
    private double _latestAudioTimeSeconds = double.NegativeInfinity;

    public ClickGameplaySession(
        TimingMap timingMap,
        IEnumerable<ClickScoringObject> objects,
        JudgmentEvaluator evaluator)
        : this(timingMap, objects, Array.Empty<HoldScoringPoint>(), evaluator)
    {
    }

    public ClickGameplaySession(
        TimingMap timingMap,
        IEnumerable<ClickScoringObject> objects,
        IEnumerable<HoldScoringPoint> holdPoints,
        JudgmentEvaluator evaluator)
    {
        _timingMap = timingMap ?? throw new ArgumentNullException(nameof(timingMap));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        ArgumentNullException.ThrowIfNull(objects);
        _objects = objects.OrderBy(item => item.TargetTick).ThenBy(item => item.ObjectId).ToArray();
        ArgumentNullException.ThrowIfNull(holdPoints);
        _holdPoints = holdPoints
            .OrderBy(item => item.TargetTick)
            .ThenBy(item => item.ObjectId)
            .ThenBy(item => item.PointIndex)
            .ToArray();
        ValidateObjects(_objects);
        ValidateHoldPoints(_holdPoints);
        _score = new ScoreRun(
            _objects.Count(item => item.Category == InputCategory.Rel)
                + _holdPoints.Count(item => item.Category == InputCategory.Rel),
            _objects.Count(item => item.Category == InputCategory.Drm)
                + _holdPoints.Count(item => item.Category == InputCategory.Drm));
    }

    public bool IsComplete => _judgedObjectIds.Count == _objects.Length
        && _judgedHoldPoints.Count == _holdPoints.Length;
    public bool IsNormallyCompleted => _score.Snapshot().IsNormallyCompleted;

    public bool IsJudged(long objectId) => _judgedObjectIds.Contains(objectId);

    public ScoreSnapshot Snapshot() => _score.Snapshot();

    public IReadOnlyList<GameplayJudgment> JudgeBatch(
        double inputAudioTimeSeconds,
        IReadOnlyList<PressEvent> presses) =>
        JudgeBatch(inputAudioTimeSeconds, presses, null);

    public IReadOnlyList<GameplayJudgment> JudgeBatch(
        double inputAudioTimeSeconds,
        IReadOnlyList<PressEvent> presses,
        Func<InputRequirement, bool>? isRequirementHeld)
    {
        ArgumentNullException.ThrowIfNull(presses);
        TimedPressEvent[] timedPresses = presses
            .Select(press => new TimedPressEvent(press, inputAudioTimeSeconds))
            .ToArray();
        return JudgeTimedBatch(timedPresses, isRequirementHeld);
    }

    public IReadOnlyList<GameplayJudgment> JudgeTimedBatch(
        IReadOnlyList<TimedPressEvent> timedPresses,
        Func<InputRequirement, bool>? isRequirementHeld = null)
    {
        ArgumentNullException.ThrowIfNull(timedPresses);
        if (timedPresses.Count == 0)
        {
            return Array.Empty<GameplayJudgment>();
        }

        TimedPressEvent[] orderedPresses = timedPresses
            .OrderBy(item => item.AudioTimeSeconds)
            .ThenBy(item => item.Press.EventId)
            .ToArray();
        EnsureUniqueEventIds(orderedPresses);
        double firstInputTime = orderedPresses[0].AudioTimeSeconds;
        List<GameplayJudgment> results = AdvanceTime(firstInputTime, isRequirementHeld).ToList();
        var candidates = _objects
            .Where(item => !_judgedObjectIds.Contains(item.ObjectId))
            .Where(item => orderedPresses.Any(press =>
                item.Requirement.Accepts(press.Press.Channel)
                && _evaluator.IsWithinMaximumWindow(
                    GetErrorMilliseconds(item, press.AudioTimeSeconds))))
            .Select(item => new ClickCandidate(item.ObjectId, item.Requirement))
            .ToArray();

        var protectedTargetTicks = new Dictionary<long, long?>();
        foreach (TimedPressEvent timedPress in orderedPresses)
        {
            protectedTargetTicks[timedPress.Press.EventId] = SelectProtectedTargetTick(timedPress);
        }

        PressEvent[] presses = orderedPresses.Select(item => item.Press).ToArray();
        BatchMatchResult matches = BatchInputMatcher.Match(
            candidates,
            presses,
            (candidate, press) =>
            {
                long? protectedTick = protectedTargetTicks[press.EventId];
                if (protectedTick is null || !candidate.Requirement.Accepts(press.Channel))
                {
                    return false;
                }

                ClickScoringObject item = _objects.First(value => value.ObjectId == candidate.ObjectId);
                return item.TargetTick == protectedTick.Value;
            });
        foreach (InputMatch match in matches.Matches)
        {
            ClickScoringObject item = _objects.First(candidate => candidate.ObjectId == match.ObjectId);
            double eventTime = orderedPresses
                .First(press => press.Press.EventId == match.EventId)
                .AudioTimeSeconds;
            ClickJudgment clickJudgment = _evaluator
                .EvaluateClick(GetErrorMilliseconds(item, eventTime))!.Value;
            Record(item, clickJudgment.Judgment);
            results.Add(new GameplayJudgment(
                item.ObjectId,
                match.EventId,
                clickJudgment.Judgment,
                clickJudgment.Direction));
        }

        return results;
    }

    private long? SelectProtectedTargetTick(TimedPressEvent timedPress)
    {
        LogicalChannel channel = timedPress.Press.Channel;
        if (_targetGroupGuards.TryGetValue(channel, out TargetGroupGuard guard)
            && timedPress.AudioTimeSeconds >= guard.StartAudioTimeSeconds
            && (timedPress.AudioTimeSeconds - guard.StartAudioTimeSeconds) * 1000d
                <= DefaultInputGroupingWindowMilliseconds)
        {
            return guard.TargetTick;
        }

        ClickScoringObject? nearest = _objects
            .Where(item => !_judgedObjectIds.Contains(item.ObjectId))
            .Where(item => item.Requirement.Accepts(channel))
            .Where(item => _evaluator.IsWithinMaximumWindow(
                GetErrorMilliseconds(item, timedPress.AudioTimeSeconds)))
            .OrderBy(item => Math.Abs(GetErrorMilliseconds(item, timedPress.AudioTimeSeconds)))
            .ThenBy(item => item.TargetTick)
            .ThenBy(item => item.ObjectId)
            .Select(item => (ClickScoringObject?)item)
            .FirstOrDefault();
        if (nearest is null)
        {
            _targetGroupGuards.Remove(channel);
            return null;
        }

        _targetGroupGuards[channel] = new TargetGroupGuard(
            nearest.Value.TargetTick,
            timedPress.AudioTimeSeconds);
        return nearest.Value.TargetTick;
    }

    private static void EnsureUniqueEventIds(IReadOnlyList<TimedPressEvent> timedPresses)
    {
        var ids = new HashSet<long>();
        if (timedPresses.Any(item => !ids.Add(item.Press.EventId)))
        {
            throw new ArgumentException("Timed presses must have unique event identifiers.", nameof(timedPresses));
        }
    }

    public IReadOnlyList<GameplayJudgment> AdvanceTime(double audioTimeSeconds) =>
        AdvanceTime(audioTimeSeconds, null);

    public IReadOnlyList<GameplayJudgment> AdvanceTime(
        double audioTimeSeconds,
        Func<InputRequirement, bool>? isRequirementHeld)
    {
        ValidateTime(audioTimeSeconds);
        var results = new List<GameplayJudgment>();
        foreach (ClickScoringObject item in _objects)
        {
            if (_judgedObjectIds.Contains(item.ObjectId)
                || _evaluator.EvaluateOverdue(GetErrorMilliseconds(item, audioTimeSeconds)) is null)
            {
                continue;
            }

            Record(item, Judgment.Chaotic);
            results.Add(new GameplayJudgment(item.ObjectId, null, Judgment.Chaotic, null));
        }

        foreach (HoldScoringPoint point in _holdPoints)
        {
            var key = (point.ObjectId, point.PointIndex);
            if (_judgedHoldPoints.Contains(key)
                || _timingMap.GetAudioTimeSeconds(point.TargetTick) > audioTimeSeconds)
            {
                continue;
            }

            Judgment judgment = JudgmentEvaluator.EvaluateHoldPoint(
                isRequirementHeld?.Invoke(point.Requirement) == true);
            _judgedHoldPoints.Add(key);
            _score.Add(point.Category, judgment);
            results.Add(new GameplayJudgment(
                point.ObjectId,
                null,
                judgment,
                null,
                point.PointIndex));
        }

        return results;
    }

    public void CompleteNormally(double audioTimeSeconds)
    {
        AdvanceTime(audioTimeSeconds);
        _score.CompleteNormally();
    }

    private void Record(ClickScoringObject item, Judgment judgment)
    {
        _judgedObjectIds.Add(item.ObjectId);
        _score.Add(item.Category, judgment);
    }

    private double GetErrorMilliseconds(ClickScoringObject item, double audioTimeSeconds) =>
        (audioTimeSeconds - _timingMap.GetAudioTimeSeconds(item.TargetTick)) * 1000d;

    private void ValidateTime(double audioTimeSeconds)
    {
        if (!double.IsFinite(audioTimeSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(audioTimeSeconds));
        }

        if (audioTimeSeconds < _latestAudioTimeSeconds)
        {
            throw new InvalidOperationException("Gameplay audio time cannot move backwards within a session.");
        }

        _latestAudioTimeSeconds = audioTimeSeconds;
    }

    private static void ValidateObjects(IReadOnlyList<ClickScoringObject> objects)
    {
        if (objects.Count == 0)
        {
            throw new ArgumentException("A gameplay session must contain at least one object.", nameof(objects));
        }

        var ids = new HashSet<long>();
        foreach (ClickScoringObject item in objects)
        {
            if (!ids.Add(item.ObjectId))
            {
                throw new ArgumentException("Gameplay object identifiers must be unique.", nameof(objects));
            }

            if (item.TargetTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(objects), "Target ticks cannot be negative.");
            }

            if (!Enum.IsDefined(item.Category) || !item.Requirement.IsValid)
            {
                throw new ArgumentException("Gameplay objects contain invalid input data.", nameof(objects));
            }

            bool familyMatchesCategory = item.Category switch
            {
                InputCategory.Rel => item.Requirement.Family == InputFamily.Rel,
                InputCategory.Drm => item.Requirement.Family == InputFamily.Drm,
                _ => false,
            };
            if (!familyMatchesCategory)
            {
                throw new ArgumentException("Input category and requirement family must match.", nameof(objects));
            }
        }
    }

    private static void ValidateHoldPoints(IReadOnlyList<HoldScoringPoint> points)
    {
        var ids = new HashSet<(long ObjectId, int PointIndex)>();
        foreach (HoldScoringPoint point in points)
        {
            if (point.ObjectId <= 0 || point.PointIndex < 0 || point.TargetTick < 0
                || !ids.Add((point.ObjectId, point.PointIndex))
                || !Enum.IsDefined(point.Category) || !point.Requirement.IsValid)
            {
                throw new ArgumentException("Hold scoring points contain invalid or duplicate data.", nameof(points));
            }

            bool familyMatchesCategory = point.Category switch
            {
                InputCategory.Rel => point.Requirement.Family == InputFamily.Rel,
                InputCategory.Drm => point.Requirement.Family == InputFamily.Drm,
                _ => false,
            };
            if (!familyMatchesCategory)
            {
                throw new ArgumentException("Hold point category and requirement family must match.", nameof(points));
            }
        }
    }

    private readonly record struct TargetGroupGuard(
        long TargetTick,
        double StartAudioTimeSeconds);
}
