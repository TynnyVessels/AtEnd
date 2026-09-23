namespace AtEnd.Core;

public readonly record struct ClickScoringObject(
    long ObjectId,
    long TargetTick,
    InputCategory Category,
    InputRequirement Requirement);

public readonly record struct GameplayJudgment(
    long ObjectId,
    long? EventId,
    Judgment Judgment,
    TimingDirection? Direction);

public sealed class ClickGameplaySession
{
    private readonly TimingMap _timingMap;
    private readonly JudgmentEvaluator _evaluator;
    private readonly ClickScoringObject[] _objects;
    private readonly HashSet<long> _judgedObjectIds = new();
    private readonly ScoreRun _score;
    private double _latestAudioTimeSeconds = double.NegativeInfinity;

    public ClickGameplaySession(
        TimingMap timingMap,
        IEnumerable<ClickScoringObject> objects,
        JudgmentEvaluator evaluator)
    {
        _timingMap = timingMap ?? throw new ArgumentNullException(nameof(timingMap));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        ArgumentNullException.ThrowIfNull(objects);
        _objects = objects.OrderBy(item => item.TargetTick).ThenBy(item => item.ObjectId).ToArray();
        ValidateObjects(_objects);
        _score = new ScoreRun(
            _objects.Count(item => item.Category == InputCategory.Rel),
            _objects.Count(item => item.Category == InputCategory.Drm));
    }

    public bool IsComplete => _judgedObjectIds.Count == _objects.Length;
    public bool IsNormallyCompleted => _score.Snapshot().IsNormallyCompleted;

    public bool IsJudged(long objectId) => _judgedObjectIds.Contains(objectId);

    public ScoreSnapshot Snapshot() => _score.Snapshot();

    public IReadOnlyList<GameplayJudgment> JudgeBatch(
        double inputAudioTimeSeconds,
        IReadOnlyList<PressEvent> presses)
    {
        ArgumentNullException.ThrowIfNull(presses);
        List<GameplayJudgment> results = AdvanceTime(inputAudioTimeSeconds).ToList();
        var candidates = _objects
            .Where(item => !_judgedObjectIds.Contains(item.ObjectId))
            .Where(item => _evaluator.IsWithinMaximumWindow(
                GetErrorMilliseconds(item, inputAudioTimeSeconds)))
            .Select(item => new ClickCandidate(item.ObjectId, item.Requirement))
            .ToArray();

        BatchMatchResult matches = BatchInputMatcher.Match(candidates, presses);
        foreach (InputMatch match in matches.Matches)
        {
            ClickScoringObject item = _objects.First(candidate => candidate.ObjectId == match.ObjectId);
            ClickJudgment clickJudgment = _evaluator
                .EvaluateClick(GetErrorMilliseconds(item, inputAudioTimeSeconds))!.Value;
            Record(item, clickJudgment.Judgment);
            results.Add(new GameplayJudgment(
                item.ObjectId,
                match.EventId,
                clickJudgment.Judgment,
                clickJudgment.Direction));
        }

        return results;
    }

    public IReadOnlyList<GameplayJudgment> AdvanceTime(double audioTimeSeconds)
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
}
