namespace AtEnd.Core;

public enum InputCategory
{
    Rel,
    Drm,
}

public enum CompletionMark
{
    None = 0,
    FullCombo = 1,
    AllPrecise = 2,
    AllStrictlyPrecise = 3,
}

public static class CompletionMarkNames
{
    public static string? DisplayName(this CompletionMark mark) => mark switch
    {
        CompletionMark.FullCombo => "FULL COMBO",
        CompletionMark.AllPrecise => "ALL PRECISE",
        CompletionMark.AllStrictlyPrecise => "ALL STRICTLY PRECISE",
        _ => null,
    };

    public static string? Abbreviation(this CompletionMark mark) => mark switch
    {
        CompletionMark.FullCombo => "FC",
        CompletionMark.AllPrecise => "AP",
        CompletionMark.AllStrictlyPrecise => "ASP",
        _ => null,
    };
}

public readonly record struct ScoreSnapshot(
    int RelScore,
    int DrmScore,
    int BaseScore,
    int StrictlyPreciseBonus,
    int TotalScore,
    decimal AccuracyPercent,
    int CurrentCombo,
    int MaximumCombo,
    int JudgedCount,
    int TotalObjects,
    bool IsNormallyCompleted,
    CompletionMark HighestCompletionMark);

public sealed class ScoreRun
{
    public const int RelPool = 800_000;
    public const int DrmPool = 200_000;

    private readonly int _relCount;
    private readonly int _drmCount;
    private int _relUnits;
    private int _drmUnits;
    private int _strictlyPrecise;
    private int _precise;
    private int _misaligned;
    private int _chaotic;
    private int _currentCombo;
    private int _maximumCombo;
    private bool _normallyCompleted;

    public ScoreRun(int relCount, int drmCount)
    {
        if (relCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(relCount));
        }

        if (drmCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(drmCount));
        }

        if (relCount + drmCount == 0)
        {
            throw new ArgumentException("A chart must contain at least one scoring object.");
        }

        _relCount = relCount;
        _drmCount = drmCount;
    }

    public int TotalObjects => _relCount + _drmCount;
    public int JudgedCount => _strictlyPrecise + _precise + _misaligned + _chaotic;

    public void Add(InputCategory category, Judgment judgment)
    {
        if (_normallyCompleted)
        {
            throw new InvalidOperationException("A completed run cannot accept more judgments.");
        }

        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        if (!Enum.IsDefined(judgment))
        {
            throw new ArgumentOutOfRangeException(nameof(judgment));
        }

        int categoryJudged = category == InputCategory.Rel
            ? GetRelJudgedCount()
            : GetDrmJudgedCount();
        int categoryTotal = category == InputCategory.Rel ? _relCount : _drmCount;
        if (categoryJudged >= categoryTotal)
        {
            throw new InvalidOperationException($"All {category} objects have already been judged.");
        }

        int units = judgment switch
        {
            Judgment.StrictlyPrecise or Judgment.Precise => 2,
            Judgment.Misaligned => 1,
            _ => 0,
        };

        if (category == InputCategory.Rel)
        {
            _relUnits += units;
            _relJudgments++;
        }
        else
        {
            _drmUnits += units;
            _drmJudgments++;
        }

        switch (judgment)
        {
            case Judgment.StrictlyPrecise:
                _strictlyPrecise++;
                IncreaseCombo();
                break;
            case Judgment.Precise:
                _precise++;
                IncreaseCombo();
                break;
            case Judgment.Misaligned:
                _misaligned++;
                IncreaseCombo();
                break;
            case Judgment.Chaotic:
                _chaotic++;
                _currentCombo = 0;
                break;
        }
    }

    private int _relJudgments;
    private int _drmJudgments;
    private int GetRelJudgedCount() => _relJudgments;
    private int GetDrmJudgedCount() => _drmJudgments;

    public void CompleteNormally()
    {
        if (JudgedCount != TotalObjects)
        {
            throw new InvalidOperationException("Every scoring object must be judged before normal completion.");
        }

        _normallyCompleted = true;
    }

    public ScoreSnapshot Snapshot()
    {
        int relScore = CalculatePool(RelPool, _relUnits, _relCount, _normallyCompleted);
        int drmScore = CalculatePool(DrmPool, _drmUnits, _drmCount, _normallyCompleted);
        int baseScore = checked(relScore + drmScore);
        decimal accuracy = JudgedCount == 0
            ? 0m
            : (100m * (_strictlyPrecise + _precise) + 50m * _misaligned + _strictlyPrecise) / JudgedCount;
        CompletionMark mark = _normallyCompleted ? GetHighestMark() : CompletionMark.None;

        return new ScoreSnapshot(relScore, drmScore, baseScore, _strictlyPrecise,
            checked(baseScore + _strictlyPrecise), accuracy, _currentCombo, _maximumCombo,
            JudgedCount, TotalObjects, _normallyCompleted, mark);
    }

    private static int CalculatePool(int pool, int earnedUnits, int objectCount, bool completed)
    {
        if (objectCount == 0)
        {
            return completed ? pool : 0;
        }

        return (int)((long)pool * earnedUnits / (2L * objectCount));
    }

    private CompletionMark GetHighestMark()
    {
        if (_chaotic > 0)
        {
            return CompletionMark.None;
        }

        if (_misaligned > 0)
        {
            return CompletionMark.FullCombo;
        }

        return _precise > 0
            ? CompletionMark.AllPrecise
            : CompletionMark.AllStrictlyPrecise;
    }

    private void IncreaseCombo()
    {
        _currentCombo++;
        _maximumCombo = Math.Max(_maximumCombo, _currentCombo);
    }
}
