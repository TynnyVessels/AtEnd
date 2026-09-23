namespace AtEnd.Core;

[Flags]
public enum RelRegion
{
    None = 0,
    Left = 1 << 0,
    Center = 1 << 1,
    Right = 1 << 2,
    All = Left | Center | Right,
}

public enum DrmColor
{
    Red,
    Green,
    Blue,
}

public enum LogicalChannel
{
    RelLeft,
    RelCenter,
    RelRight,
    DrmRed,
    DrmGreen,
    DrmBlue,
}

public enum PhysicalKey
{
    Tab, Backspace, CapsLock, LeftShift, RightShift, Enter,
    A, S, D, Z, X, C,
    F, G, H, J, V, B, N,
    K, L, Semicolon, M, Comma, Period,
    Apostrophe, Slash, Backslash, RightBracket,
    Digit1, Digit2, Digit3, Digit4, Q, W, E, R,
    Digit5, Digit6, Digit7, Digit8, T, Y, U,
    Digit9, Digit0, Minus, Equals, I, O, P, LeftBracket,
}

public static class DefaultKeyboardBindings
{
    public static IReadOnlyDictionary<PhysicalKey, LogicalChannel> All { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<PhysicalKey, LogicalChannel>(
            new Dictionary<PhysicalKey, LogicalChannel>
            {
                [PhysicalKey.CapsLock] = LogicalChannel.RelLeft,
                [PhysicalKey.A] = LogicalChannel.RelLeft,
                [PhysicalKey.S] = LogicalChannel.RelLeft,
                [PhysicalKey.D] = LogicalChannel.RelLeft,
                [PhysicalKey.LeftShift] = LogicalChannel.RelLeft,
                [PhysicalKey.Z] = LogicalChannel.RelLeft,
                [PhysicalKey.X] = LogicalChannel.RelLeft,
                [PhysicalKey.C] = LogicalChannel.RelLeft,
                [PhysicalKey.F] = LogicalChannel.RelCenter,
                [PhysicalKey.G] = LogicalChannel.RelCenter,
                [PhysicalKey.H] = LogicalChannel.RelCenter,
                [PhysicalKey.J] = LogicalChannel.RelCenter,
                [PhysicalKey.K] = LogicalChannel.RelCenter,
                [PhysicalKey.V] = LogicalChannel.RelCenter,
                [PhysicalKey.B] = LogicalChannel.RelCenter,
                [PhysicalKey.N] = LogicalChannel.RelCenter,
                [PhysicalKey.M] = LogicalChannel.RelCenter,
                [PhysicalKey.L] = LogicalChannel.RelRight,
                [PhysicalKey.Semicolon] = LogicalChannel.RelRight,
                [PhysicalKey.Apostrophe] = LogicalChannel.RelRight,
                [PhysicalKey.Enter] = LogicalChannel.RelRight,
                [PhysicalKey.Comma] = LogicalChannel.RelRight,
                [PhysicalKey.Period] = LogicalChannel.RelRight,
                [PhysicalKey.Slash] = LogicalChannel.RelRight,
                [PhysicalKey.RightShift] = LogicalChannel.RelRight,
                [PhysicalKey.Digit1] = LogicalChannel.DrmRed,
                [PhysicalKey.Digit2] = LogicalChannel.DrmRed,
                [PhysicalKey.Digit3] = LogicalChannel.DrmRed,
                [PhysicalKey.Digit4] = LogicalChannel.DrmRed,
                [PhysicalKey.Tab] = LogicalChannel.DrmRed,
                [PhysicalKey.Q] = LogicalChannel.DrmRed,
                [PhysicalKey.W] = LogicalChannel.DrmRed,
                [PhysicalKey.E] = LogicalChannel.DrmRed,
                [PhysicalKey.Digit5] = LogicalChannel.DrmGreen,
                [PhysicalKey.Digit6] = LogicalChannel.DrmGreen,
                [PhysicalKey.Digit7] = LogicalChannel.DrmGreen,
                [PhysicalKey.Digit8] = LogicalChannel.DrmGreen,
                [PhysicalKey.R] = LogicalChannel.DrmGreen,
                [PhysicalKey.T] = LogicalChannel.DrmGreen,
                [PhysicalKey.Y] = LogicalChannel.DrmGreen,
                [PhysicalKey.U] = LogicalChannel.DrmGreen,
                [PhysicalKey.I] = LogicalChannel.DrmGreen,
                [PhysicalKey.Digit9] = LogicalChannel.DrmBlue,
                [PhysicalKey.Digit0] = LogicalChannel.DrmBlue,
                [PhysicalKey.Minus] = LogicalChannel.DrmBlue,
                [PhysicalKey.Equals] = LogicalChannel.DrmBlue,
                [PhysicalKey.Backspace] = LogicalChannel.DrmBlue,
                [PhysicalKey.O] = LogicalChannel.DrmBlue,
                [PhysicalKey.P] = LogicalChannel.DrmBlue,
                [PhysicalKey.LeftBracket] = LogicalChannel.DrmBlue,
                [PhysicalKey.RightBracket] = LogicalChannel.DrmBlue,
                [PhysicalKey.Backslash] = LogicalChannel.DrmBlue,
            });
}

public enum InputFamily
{
    Rel,
    Drm,
}

public readonly record struct InputRequirement
{
    private InputRequirement(InputFamily family, RelRegion relRegions, DrmColor drmColor)
    {
        Family = family;
        RelRegions = relRegions;
        DrmColor = drmColor;
    }

    public InputFamily Family { get; }
    public RelRegion RelRegions { get; }
    public DrmColor DrmColor { get; }

    public bool IsValid => Family switch
    {
        InputFamily.Rel => RelRegions != RelRegion.None && (RelRegions & ~RelRegion.All) == 0,
        InputFamily.Drm => Enum.IsDefined(DrmColor),
        _ => false,
    };

    public static InputRequirement Rel(RelRegion regions)
    {
        if (regions == RelRegion.None || (regions & ~RelRegion.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(regions));
        }

        return new InputRequirement(InputFamily.Rel, regions, default);
    }

    public static InputRequirement Drm(DrmColor color)
    {
        if (!Enum.IsDefined(color))
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }

        return new InputRequirement(InputFamily.Drm, default, color);
    }

    public bool Accepts(LogicalChannel channel)
    {
        if (!Enum.IsDefined(channel))
        {
            return false;
        }

        return Family switch
        {
            InputFamily.Rel => channel switch
            {
                LogicalChannel.RelLeft => RelRegions.HasFlag(RelRegion.Left),
                LogicalChannel.RelCenter => RelRegions.HasFlag(RelRegion.Center),
                LogicalChannel.RelRight => RelRegions.HasFlag(RelRegion.Right),
                _ => false,
            },
            InputFamily.Drm => channel == (DrmColor switch
            {
                AtEnd.Core.DrmColor.Red => LogicalChannel.DrmRed,
                AtEnd.Core.DrmColor.Green => LogicalChannel.DrmGreen,
                AtEnd.Core.DrmColor.Blue => LogicalChannel.DrmBlue,
                _ => throw new ArgumentOutOfRangeException(),
            }),
            _ => false,
        };
    }

    public bool IsHeldBy(IEnumerable<LogicalChannel> heldChannels)
    {
        ArgumentNullException.ThrowIfNull(heldChannels);
        return heldChannels.Any(Accepts);
    }
}

public readonly record struct PressEvent
{
    public PressEvent(long eventId, LogicalChannel channel)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        EventId = eventId;
        Channel = channel;
    }

    public long EventId { get; }
    public LogicalChannel Channel { get; }
}

public readonly record struct ClickCandidate(long ObjectId, InputRequirement Requirement);

public readonly record struct InputMatch(long ObjectId, long EventId);

public sealed class LogicalInputState<TPhysicalKey> where TPhysicalKey : notnull
{
    private readonly IReadOnlyDictionary<TPhysicalKey, LogicalChannel> _bindings;
    private readonly HashSet<TPhysicalKey> _heldKeys = new();
    private long _nextEventId;

    public LogicalInputState(IReadOnlyDictionary<TPhysicalKey, LogicalChannel> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        foreach (LogicalChannel channel in bindings.Values)
        {
            if (!Enum.IsDefined(channel))
            {
                throw new ArgumentException("Bindings contain an invalid logical channel.", nameof(bindings));
            }
        }

        _bindings = new Dictionary<TPhysicalKey, LogicalChannel>(bindings);
    }

    public PressEvent? Press(TPhysicalKey physicalKey)
    {
        if (!_bindings.TryGetValue(physicalKey, out LogicalChannel channel)
            || !_heldKeys.Add(physicalKey))
        {
            return null;
        }

        return new PressEvent(checked(++_nextEventId), channel);
    }

    public bool Release(TPhysicalKey physicalKey) => _heldKeys.Remove(physicalKey);

    public bool IsChannelHeld(LogicalChannel channel)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        return _heldKeys.Any(key => _bindings[key] == channel);
    }

    public bool IsRequirementHeld(InputRequirement requirement)
    {
        if (!requirement.IsValid)
        {
            throw new ArgumentException("Input requirement is invalid.", nameof(requirement));
        }

        return _heldKeys.Any(key => requirement.Accepts(_bindings[key]));
    }
}

public sealed class BatchMatchResult
{
    internal BatchMatchResult(
        IEnumerable<InputMatch> matches,
        IEnumerable<long> unmatchedObjectIds,
        IEnumerable<long> unmatchedEventIds)
    {
        Matches = Array.AsReadOnly(matches.ToArray());
        UnmatchedObjectIds = Array.AsReadOnly(unmatchedObjectIds.ToArray());
        UnmatchedEventIds = Array.AsReadOnly(unmatchedEventIds.ToArray());
    }

    public IReadOnlyList<InputMatch> Matches { get; }
    public IReadOnlyList<long> UnmatchedObjectIds { get; }
    public IReadOnlyList<long> UnmatchedEventIds { get; }
}

public static class BatchInputMatcher
{
    public static BatchMatchResult Match(
        IReadOnlyList<ClickCandidate> candidates,
        IReadOnlyList<PressEvent> presses) =>
        Match(candidates, presses, static (candidate, press) =>
            candidate.Requirement.Accepts(press.Channel));

    public static BatchMatchResult Match(
        IReadOnlyList<ClickCandidate> candidates,
        IReadOnlyList<PressEvent> presses,
        Func<ClickCandidate, PressEvent, bool> canMatch)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(presses);
        ArgumentNullException.ThrowIfNull(canMatch);
        if (candidates.Any(candidate => !candidate.Requirement.IsValid))
        {
            throw new ArgumentException("Candidates contain an invalid input requirement.", nameof(candidates));
        }

        EnsureUnique(candidates.Select(candidate => candidate.ObjectId), nameof(candidates));
        EnsureUnique(presses.Select(press => press.EventId), nameof(presses));

        int[] pressToCandidate = Enumerable.Repeat(-1, presses.Count).ToArray();
        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            var visitedPresses = new bool[presses.Count];
            TryAssign(candidateIndex, candidates, presses, pressToCandidate, visitedPresses, canMatch);
        }

        int[] candidateToPress = Enumerable.Repeat(-1, candidates.Count).ToArray();
        for (int pressIndex = 0; pressIndex < pressToCandidate.Length; pressIndex++)
        {
            int candidateIndex = pressToCandidate[pressIndex];
            if (candidateIndex >= 0)
            {
                candidateToPress[candidateIndex] = pressIndex;
            }
        }

        var matches = new List<InputMatch>();
        var unmatchedObjects = new List<long>();
        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            int pressIndex = candidateToPress[candidateIndex];
            if (pressIndex >= 0)
            {
                matches.Add(new InputMatch(
                    candidates[candidateIndex].ObjectId,
                    presses[pressIndex].EventId));
            }
            else
            {
                unmatchedObjects.Add(candidates[candidateIndex].ObjectId);
            }
        }

        var unmatchedEvents = new List<long>();
        for (int pressIndex = 0; pressIndex < presses.Count; pressIndex++)
        {
            if (pressToCandidate[pressIndex] < 0)
            {
                unmatchedEvents.Add(presses[pressIndex].EventId);
            }
        }

        return new BatchMatchResult(matches, unmatchedObjects, unmatchedEvents);
    }

    private static bool TryAssign(
        int candidateIndex,
        IReadOnlyList<ClickCandidate> candidates,
        IReadOnlyList<PressEvent> presses,
        int[] pressToCandidate,
        bool[] visitedPresses,
        Func<ClickCandidate, PressEvent, bool> canMatch)
    {
        for (int pressIndex = 0; pressIndex < presses.Count; pressIndex++)
        {
            if (visitedPresses[pressIndex]
                || !canMatch(candidates[candidateIndex], presses[pressIndex]))
            {
                continue;
            }

            visitedPresses[pressIndex] = true;
            int previousCandidate = pressToCandidate[pressIndex];
            if (previousCandidate < 0
                || TryAssign(previousCandidate, candidates, presses, pressToCandidate,
                    visitedPresses, canMatch))
            {
                pressToCandidate[pressIndex] = candidateIndex;
                return true;
            }
        }

        return false;
    }

    private static void EnsureUnique(IEnumerable<long> ids, string parameterName)
    {
        var seen = new HashSet<long>();
        if (ids.Any(id => !seen.Add(id)))
        {
            throw new ArgumentException("Identifiers within a batch must be unique.", parameterName);
        }
    }
}
