using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AtEnd.App.Audio;
using AtEnd.Core;
using Godot;

namespace AtEnd.App.Playfield;

public partial class PlayfieldView : Control
{
    private const int LaneCount = 18; //轨道数
    private const float TrackBackProgress = 0;
    private const float TrackWindowBottomProgress = 1.12f; //+0.12越过判定线
    private const float NoteDepthLaneRatio = 0.42f; //note纵深相对单轨宽度，保持贴地透视
    private const float NoteCullPaddingProgress = 0.05f;
    private const float JudgmentLineHalfDepth = 0.0045f; //判定线厚度
    private static readonly Color TrackColor = new("10162b"); //轨道颜色
    private static readonly Color TrackEdgeColor = new("6f7fb5"); //轨道边缘颜色
    private static readonly Color MinorGridColor = new(0.3f, 0.38f, 0.62f, 0.28f); //次要网格线颜色
    private static readonly Color MajorGridColor = new(0.5f, 0.62f, 0.92f, 0.7f); //主要网格线颜色
    private static readonly Color JudgmentLineColor = new("f4f7ff"); //判定线颜色
    private static readonly TimingMap DemoTiming = new(0.35, 120,
        new[] { new BpmChange(5760, 180) }); //测试谱面信息
    private static readonly RuntimeNote[] DemoNotes =
    {
        new(1, 2, 4, 1920, InputCategory.Rel,
            InputRequirement.Rel(RelRegion.Left | RelRegion.Center),
            new Color("f5f6ff"), NoteSymbol.Rel),
        new(2, 9, 4, 1920, InputCategory.Rel,
            InputRequirement.Rel(RelRegion.Center | RelRegion.Right),
            new Color("f5f6ff"), NoteSymbol.Rel),
        new(3, 7, 5, 3840, InputCategory.Drm,
            InputRequirement.Drm(DrmColor.Red),
            new Color("ff4f65"), NoteSymbol.RedCross),
        new(4, 1, 3, 5760, InputCategory.Drm,
            InputRequirement.Drm(DrmColor.Green),
            new Color("50e39a"), NoteSymbol.GreenSquare),
        new(5, 13, 4, 7680, InputCategory.Drm,
            InputRequirement.Drm(DrmColor.Blue),
            new Color("55a8ff"), NoteSymbol.BlueCircle),
    };

    private const double DemoCycleSeconds = 3.2;
    private const double PostJudgmentLingerSeconds = 0.12;

    private GodotAudioClock? _audioClock;
    private AudioStreamPlayer? _audioPlayer;
    private AudioStreamWav? _silentAudioStream;
    private TimingMap _activeTiming = DemoTiming;
    private RuntimeNote[] _activeNotes = DemoNotes;
    private RuntimeHold[] _activeHolds = Array.Empty<RuntimeHold>();
    private ClickScoringObject[] _activeClickObjects = DemoNotes
        .Select(note => note.ToScoringObject())
        .ToArray();
    private HoldScoringPoint[] _activeHoldPoints = Array.Empty<HoldScoringPoint>();
    private bool _smokeTest;
    private bool _songSmokeTest;
    private bool _songCompleted;
    private int _smokeTestFrames;
    private int _songSmokeTestFrames;
    private ulong _smokeQuitAfterTicks;
    private readonly List<TimedPressEvent> _pendingPresses = new();
    private readonly HashSet<long> _missedNoteIds = new();
    private ClickGameplaySession? _gameplaySession;
    private long _activeCycle = -1;
    private int _completedCycleCount;

    public event Action<GameplayJudgment, ScoreSnapshot>? JudgmentResolved;
    public event Action<ScoreSnapshot>? CycleCompleted;

    public Func<InputRequirement, bool>? IsRequirementHeld { get; set; }

    public ScoreSnapshot CurrentScore => _gameplaySession?.Snapshot() ?? default;

    public string LoadedSongTitle { get; private set; } = "AtEnd";
    public string LoadedSongArtist { get; private set; } = "Development Track";
    public string LoadedDifficulty { get; private set; } = "Prototype";
    public string LoadedCharter { get; private set; } = "AtEnd Team";

    [Export]
    public int GridDensity { get; set; } = 18;

    [Export(PropertyHint.Range, "0.4,2.0,0.05")]
    public double ApproachDurationSeconds { get; set; } = 0.45;

    [Export(PropertyHint.Range, "1.0,3.0,0.05")]
    public double TravelAccelerationExponent { get; set; } = 2.3;

    public override void _Ready()
    {
        _smokeTest = Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0;
        _songSmokeTest = Array.IndexOf(OS.GetCmdlineUserArgs(), "--song-smoke-test") >= 0;
        _audioPlayer = GetNode<AudioStreamPlayer>("../DemoAudioClock");
        if (_smokeTest)
        {
            _silentAudioStream = CreateSilentLoop();
            _audioPlayer.Stream = _silentAudioStream;
            _audioPlayer.VolumeDb = -80;
            _audioPlayer.Play();
            _audioClock = new GodotAudioClock(_audioPlayer);
            EnsureCycle(0);
        }
        else
        {
            LoadTestSong();
        }

        Resized += QueueRedraw;
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        StopAudioClock();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        QueueRedraw();

        if (_smokeQuitAfterTicks > 0)
        {
            if (Time.GetTicksMsec() >= _smokeQuitAfterTicks)
            {
                GetTree().Quit();
            }

            return;
        }

        if (_smokeTest)
        {
            UpdateDemoGameplay();
        }
        else
        {
            UpdateSongGameplay();
            if (_songSmokeTest)
            {
                _songSmokeTestFrames++;
                if (_songSmokeTestFrames >= 60)
                {
                    bool valid = _activeNotes.Length == 20
                        && _activeClickObjects.Length == 20
                        && _activeHolds.Length == 2
                        && _activeHoldPoints.Length == 11
                        && _audioPlayer?.Stream is not null;
                    if (valid)
                    {
                        GD.Print("Song package smoke test passed: audio, 20 heads, and 11 hold points loaded.");
                    }
                    else
                    {
                        GD.PushError("Song package smoke test failed.");
                    }

                    StopAudioClock();
                    GetTree().Quit(valid ? 0 : 1);
                }
            }

            return;
        }

        _smokeTestFrames++;
        double smokeAudioTime = _audioClock?.CurrentTimeSeconds ?? 0;
        if (smokeAudioTime >= DemoCycleSeconds + 0.1 && _completedCycleCount >= 1)
        {
            GD.Print($"Gameplay smoke test passed at {smokeAudioTime:F3} seconds after "
                + $"{_completedCycleCount} completed cycle.");
            StopAudioClock();
            _smokeQuitAfterTicks = Time.GetTicksMsec() + 250;
        }
        else if (_smokeTestFrames >= 600)
        {
            GD.PushError("Audio clock did not advance during the smoke test.");
            StopAudioClock();
            GetTree().Quit(1);
        }
    }

    public void QueuePress(PressEvent pressEvent)
    {
        double totalAudioTime = _audioClock?.CurrentTimeSeconds ?? 0;
        if (!_smokeTest)
        {
            _pendingPresses.Add(new TimedPressEvent(pressEvent, totalAudioTime));
            return;
        }

        long cycle = (long)Math.Floor(totalAudioTime / DemoCycleSeconds);
        EnsureCycle(cycle);
        _pendingPresses.Add(new TimedPressEvent(
            pressEvent,
            totalAudioTime - (cycle * DemoCycleSeconds)));
    }

    private void StopAudioClock()
    {
        _audioPlayer?.Stop();
        if (_audioPlayer is not null)
        {
            _audioPlayer.Stream = null;
        }

        _audioClock = null;
        _audioPlayer = null;
        _silentAudioStream?.Dispose();
        _silentAudioStream = null;
    }

    private void LoadTestSong()
    {
        if (_audioPlayer is null)
        {
            throw new InvalidOperationException("The audio player is not available.");
        }

        string packagePath = ProjectSettings.GlobalizePath("res://songs/test-song");
        SongPackageDefinition package = SongPackageLoader.LoadDirectory(packagePath);
        ChartDefinition chart = package.Charts.Single(item => item.ChartId == "test-song-test");
        _activeTiming = package.Timing.TimingMap;
        _activeClickObjects = chart.CreateHeadScoringObjects().ToArray();
        _activeHoldPoints = chart.CreateHoldScoringPoints().ToArray();
        _activeNotes = chart.Objects.Select(ToRuntimeNote).ToArray();
        _activeHolds = chart.Objects
            .Where(item => item.Type == ChartObjectType.Hold)
            .Select(ToRuntimeHold)
            .ToArray();
        _gameplaySession = CreateActiveGameplaySession();
        LoadedSongTitle = package.Song.Title;
        LoadedSongArtist = package.Song.Artist;
        LoadedDifficulty = $"{chart.Difficulty.Name}  {chart.Difficulty.Level}";
        LoadedCharter = chart.Charter;

        string audioFilePath = Path.Combine(packagePath, package.Song.AudioFile);
        AudioStream stream = AudioStreamOggVorbis.LoadFromFile(audioFilePath)
            ?? throw new InvalidDataException($"Godot could not load {audioFilePath}.");
        _audioPlayer.Stream = stream;
        _audioPlayer.VolumeDb = 0;
        _audioPlayer.Play();
        _audioClock = new GodotAudioClock(_audioPlayer);
        GD.Print($"Loaded {chart.ChartId}: {chart.Objects.Count} objects, "
            + $"{package.Timing.TimingMap.InitialBeatsPerMinute:F3} BPM, "
            + $"tick zero at {package.Timing.TimingMap.AudioTimeAtTickZeroSeconds:F6}s.");
    }

    private static RuntimeNote ToRuntimeNote(ChartObjectDefinition item)
    {
        LanePoint point = item.StartPoint;
        return item.InputType switch
        {
            InputCategory.Rel => new RuntimeNote(
                item.ObjectId,
                point.Lane,
                point.Width,
                item.TargetTick,
                item.InputType,
                item.GetInputRequirement(),
                new Color("f5f6ff"),
                NoteSymbol.Rel),
            InputCategory.Drm => item.Color switch
            {
                DrmColor.Red => new RuntimeNote(
                    item.ObjectId, point.Lane, point.Width, item.TargetTick, item.InputType,
                    item.GetInputRequirement(), new Color("ff4f65"), NoteSymbol.RedCross),
                DrmColor.Green => new RuntimeNote(
                    item.ObjectId, point.Lane, point.Width, item.TargetTick, item.InputType,
                    item.GetInputRequirement(), new Color("50e39a"), NoteSymbol.GreenSquare),
                DrmColor.Blue => new RuntimeNote(
                    item.ObjectId, point.Lane, point.Width, item.TargetTick, item.InputType,
                    item.GetInputRequirement(), new Color("55a8ff"), NoteSymbol.BlueCircle),
                _ => throw new InvalidDataException($"Object {item.ObjectId} has no Drm color."),
            },
            _ => throw new InvalidDataException($"Object {item.ObjectId} has an invalid input type."),
        };
    }

    private static RuntimeHold ToRuntimeHold(ChartObjectDefinition item)
    {
        RuntimeNote head = ToRuntimeNote(item);
        return new RuntimeHold(item, head.Color);
    }

    private void UpdateDemoGameplay()
    {
        if (_audioClock is null)
        {
            return;
        }

        double totalAudioTime = _audioClock.CurrentTimeSeconds;
        long cycle = (long)Math.Floor(totalAudioTime / DemoCycleSeconds);
        EnsureCycle(cycle);
        double cycleAudioTime = totalAudioTime - (cycle * DemoCycleSeconds);

        if (_pendingPresses.Count > 0 && _gameplaySession is not null)
        {
            Publish(_gameplaySession.JudgeTimedBatch(_pendingPresses, IsRequirementHeld));
            _pendingPresses.Clear();
        }

        if (_gameplaySession is not null)
        {
            Publish(_gameplaySession.AdvanceTime(cycleAudioTime, IsRequirementHeld));
        }
    }

    private void UpdateSongGameplay()
    {
        if (_audioClock is null || _audioPlayer is null || _gameplaySession is null)
        {
            return;
        }

        double audioTime = _audioClock.CurrentTimeSeconds;
        if (_pendingPresses.Count > 0)
        {
            Publish(_gameplaySession.JudgeTimedBatch(_pendingPresses, IsRequirementHeld));
            _pendingPresses.Clear();
        }

        Publish(_gameplaySession.AdvanceTime(audioTime, IsRequirementHeld));
        if (!_songCompleted && !_audioPlayer.Playing)
        {
            double endTime = _audioPlayer.Stream?.GetLength() ?? audioTime;
            _gameplaySession.CompleteNormally(Math.Max(audioTime, endTime));
            _songCompleted = true;
            CycleCompleted?.Invoke(_gameplaySession.Snapshot());
        }
    }

    private void EnsureCycle(long cycle)
    {
        if (_activeCycle == cycle && _gameplaySession is not null)
        {
            return;
        }

        if (_gameplaySession is not null)
        {
            _gameplaySession.CompleteNormally(DemoCycleSeconds);
            _completedCycleCount++;
            CycleCompleted?.Invoke(_gameplaySession.Snapshot());
        }

        _pendingPresses.Clear();
        _missedNoteIds.Clear();
        _gameplaySession = CreateGameplaySession();
        _activeCycle = cycle;
    }

    private static ClickGameplaySession CreateGameplaySession()
    {
        ClickScoringObject[] objects = DemoNotes
            .Select(note => new ClickScoringObject(
                note.ObjectId,
                note.TargetTick,
                note.Category,
                note.Requirement))
            .ToArray();
        return new ClickGameplaySession(
            DemoTiming,
            objects,
            new JudgmentEvaluator(JudgmentWindows.Default));
    }

    private ClickGameplaySession CreateActiveGameplaySession() => new(
        _activeTiming,
        _activeClickObjects,
        _activeHoldPoints,
        new JudgmentEvaluator(JudgmentWindows.Default));

    private void Publish(IEnumerable<GameplayJudgment> judgments)
    {
        if (_gameplaySession is null)
        {
            return;
        }

        foreach (GameplayJudgment judgment in judgments)
        {
            if (judgment.EventId is null && judgment.HoldPointIndex is null)
            {
                _missedNoteIds.Add(judgment.ObjectId);
            }

            JudgmentResolved?.Invoke(judgment, _gameplaySession.Snapshot());
        }
    }

    public override void _Draw()
    {
        if (Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        DrawTrack();
        DrawGuides();
        DrawMovingHolds();
        DrawMovingNotes();
        DrawJudgmentLine();
    }

    private void DrawTrack()
    {
        Vector2[] track =
        {
            TrackPoint(0, TrackBackProgress),
            TrackPoint(LaneCount, TrackBackProgress),
            TrackPoint(LaneCount, 1),
            TrackPoint(LaneCount, TrackWindowBottomProgress),
            TrackPoint(0, TrackWindowBottomProgress),
            TrackPoint(0, 1),
        };
        DrawColoredPolygon(track, TrackColor);
        Vector2 leftBack = TrackPoint(0, TrackBackProgress);
        Vector2 leftJudgment = TrackPoint(0, 1);
        Vector2 leftFront = TrackPoint(0, TrackWindowBottomProgress);
        Vector2 rightBack = TrackPoint(LaneCount, TrackBackProgress);
        Vector2 rightJudgment = TrackPoint(LaneCount, 1);
        Vector2 rightFront = TrackPoint(LaneCount, TrackWindowBottomProgress);
        DrawPolyline(new[] { leftBack, leftJudgment, leftFront },
            new Color(0.2f, 0.35f, 0.78f, 0.2f), 12, true);
        DrawPolyline(new[] { rightBack, rightJudgment, rightFront },
            new Color(0.2f, 0.35f, 0.78f, 0.2f), 12, true);
        DrawPolyline(new[] { leftBack, leftJudgment, leftFront }, TrackEdgeColor, 2.5f, true);
        DrawPolyline(new[] { rightBack, rightJudgment, rightFront }, TrackEdgeColor, 2.5f, true);
    }

    private void DrawGuides()
    {
        int density = GridDensity is 3 or 9 or 18 ? GridDensity : 18;
        int laneStep = LaneCount / density;
        for (int lane = laneStep; lane < LaneCount; lane += laneStep)
        {
            bool majorBoundary = lane % 6 == 0;
            DrawPolyline(
                new[]
                {
                    TrackPoint(lane, TrackBackProgress),
                    TrackPoint(lane, 1),
                    TrackPoint(lane, TrackWindowBottomProgress),
                },
                majorBoundary ? MajorGridColor : MinorGridColor,
                majorBoundary ? 2.5f : 1,
                true);
        }

    }

    private void DrawMovingNotes()
    {
        double rawAudioTime = _audioClock?.CurrentTimeSeconds ?? 0;
        double currentAudioTime = _smokeTest
            ? rawAudioTime % DemoCycleSeconds
            : rawAudioTime;
        foreach (RuntimeNote note in _activeNotes)
        {
            bool missed = _missedNoteIds.Contains(note.ObjectId);
            if (_gameplaySession?.IsJudged(note.ObjectId) == true && !missed)
            {
                continue;
            }

            double targetTime = _activeTiming.GetAudioTimeSeconds(note.TargetTick);
            double effectiveAudioTime = currentAudioTime;
            if (_smokeTest && targetTime - currentAudioTime < -PostJudgmentLingerSeconds)
            {
                effectiveAudioTime -= DemoCycleSeconds;
            }

            double linearProgress = NoteTravel.GetProgress(
                _activeTiming,
                note.TargetTick,
                effectiveAudioTime,
                ApproachDurationSeconds);
            if (linearProgress is >= 0 and <= TrackWindowBottomProgress + NoteCullPaddingProgress)
            {
                float progress = AccelerateProgress(linearProgress);
                DrawNote(note.StartLane, note.LaneWidth, progress, note.Color, note.Symbol);
            }
        }
    }

    private void DrawMovingHolds()
    {
        double currentAudioTime = _audioClock?.CurrentTimeSeconds ?? 0;
        foreach (RuntimeHold hold in _activeHolds)
        {
            ChartObjectDefinition definition = hold.Definition;
            bool clipAtJudgmentLine = IsHoldCurrentlyCaught(definition, currentAudioTime);
            foreach ((LanePoint first, LanePoint second) in definition.Path.Zip(
                definition.Path.Skip(1),
                (first, second) => (first, second)))
            {
                int subdivisions = Math.Max(1, (int)Math.Ceiling((second.Tick - first.Tick) / 480d));
                for (int part = 0; part < subdivisions; part++)
                {
                    double fromRatio = part / (double)subdivisions;
                    double toRatio = (part + 1) / (double)subdivisions;
                    long fromTick = first.Tick + (long)Math.Round((second.Tick - first.Tick) * fromRatio);
                    long toTick = first.Tick + (long)Math.Round((second.Tick - first.Tick) * toRatio);
                    DrawHoldSlice(hold, fromTick, toTick, currentAudioTime, clipAtJudgmentLine);
                }
            }
        }
    }

    private bool IsHoldCurrentlyCaught(
        ChartObjectDefinition definition,
        double currentAudioTime)
    {
        double currentTick = _activeTiming.GetTickAtAudioTimeSeconds(currentAudioTime);
        long requirementTick = (long)Math.Round(Math.Clamp(
            currentTick,
            definition.StartTick,
            definition.EndTick));
        InputRequirement requirement = definition.GetInputRequirementAtTick(requirementTick);
        return IsRequirementHeld?.Invoke(requirement) == true;
    }

    private void DrawHoldSlice(
        RuntimeHold hold,
        long fromTick,
        long toTick,
        double currentAudioTime,
        bool clipAtJudgmentLine)
    {
        double fromLinear = NoteTravel.GetProgress(
            _activeTiming, fromTick, currentAudioTime, ApproachDurationSeconds);
        double toLinear = NoteTravel.GetProgress(
            _activeTiming, toTick, currentAudioTime, ApproachDurationSeconds);
        if ((fromLinear < 0 && toLinear < 0) || (fromLinear > 1.07 && toLinear > 1.07))
        {
            return;
        }

        if (clipAtJudgmentLine && fromLinear >= 1 && toLinear >= 1)
        {
            return;
        }

        (double fromLane, double fromWidth) = hold.Definition.GetLaneGeometryAtTick(fromTick);
        (double toLane, double toWidth) = hold.Definition.GetLaneGeometryAtTick(toTick);
        if (clipAtJudgmentLine && (fromLinear > 1 || toLinear > 1))
        {
            double judgmentTick = _activeTiming.GetTickAtAudioTimeSeconds(currentAudioTime);
            double clipRatio = Math.Clamp(
                (judgmentTick - fromTick) / (toTick - (double)fromTick),
                0,
                1);
            double clippedLane = fromLane + ((toLane - fromLane) * clipRatio);
            double clippedWidth = fromWidth + ((toWidth - fromWidth) * clipRatio);
            if (fromLinear > 1)
            {
                fromLinear = 1;
                fromLane = clippedLane;
                fromWidth = clippedWidth;
            }
            else
            {
                toLinear = 1;
                toLane = clippedLane;
                toWidth = clippedWidth;
            }
        }

        float fromProgress = AccelerateProgress(fromLinear);
        float toProgress = AccelerateProgress(toLinear);
        Vector2[] polygon =
        {
            TrackPoint((float)fromLane, fromProgress),
            TrackPoint((float)(fromLane + fromWidth), fromProgress),
            TrackPoint((float)(toLane + toWidth), toProgress),
            TrackPoint((float)toLane, toProgress),
        };
        Color bodyColor = hold.Color;
        bodyColor.A = 0.42f;
        DrawColoredPolygon(polygon, bodyColor);
        DrawLine(polygon[0], polygon[3], new Color(1, 1, 1, 0.55f), 1.5f, true);
        DrawLine(polygon[1], polygon[2], new Color(1, 1, 1, 0.55f), 1.5f, true);
    }

    private float AccelerateProgress(double linearProgress)
    {
        double nonNegativeProgress = Math.Max(0, linearProgress);
        return (float)Math.Pow(nonNegativeProgress, TravelAccelerationExponent);
    }

    private static AudioStreamWav CreateSilentLoop()
    {
        const int sampleRate = 8000;
        const int durationSeconds = 64;
        int sampleCount = sampleRate * durationSeconds;
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate = sampleRate,
            Stereo = false,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0,
            LoopEnd = sampleCount,
            Data = new byte[sampleCount],
        };
    }

    private void DrawNote(int startLane, int laneWidth, float progress, Color color, NoteSymbol symbol)
    {
        float lanePixelWidth = MathF.Abs(TrackPoint(1, progress).X - TrackPoint(0, progress).X);
        float halfHeight = lanePixelWidth * NoteDepthLaneRatio / 2;
        float trackPixelHeight = MathF.Abs(TrackPoint(0, 1).Y - TrackPoint(0, 0).Y);
        float halfDepthProgress = trackPixelHeight > float.Epsilon
            ? halfHeight / trackPixelHeight
            : 0;
        float farProgress = progress - halfDepthProgress;
        float nearProgress = progress + halfDepthProgress;
        Vector2[] polygon =
        {
            TrackPoint(startLane, farProgress),
            TrackPoint(startLane + laneWidth, farProgress),
            TrackPoint(startLane + laneWidth, nearProgress),
            TrackPoint(startLane, nearProgress),
        };

        DrawColoredPolygon(polygon, color);
        DrawPolyline(Close(polygon), new Color(1, 1, 1, 0.92f), 2, true);

        if (symbol == NoteSymbol.Rel)
        {
            return;
        }

        Vector2 center = (polygon[0] + polygon[1] + polygon[2] + polygon[3]) / 4;
        float noteWidth = polygon[1].DistanceTo(polygon[0]);
        float noteHeight = polygon[3].DistanceTo(polygon[0]);
        float symbolSize = MathF.Max(1, MathF.Min(noteWidth * 0.12f, noteHeight * 0.32f));
        Color symbolColor = new(0.05f, 0.07f, 0.12f, 0.95f);
        switch (symbol)
        {
            case NoteSymbol.RedCross:
                DrawLine(center - new Vector2(symbolSize, symbolSize),
                    center + new Vector2(symbolSize, symbolSize), symbolColor, 3, true);
                DrawLine(center + new Vector2(symbolSize, -symbolSize),
                    center + new Vector2(-symbolSize, symbolSize), symbolColor, 3, true);
                break;
            case NoteSymbol.GreenSquare:
                Vector2[] square =
                {
                    center + new Vector2(-symbolSize, -symbolSize),
                    center + new Vector2(symbolSize, -symbolSize),
                    center + new Vector2(symbolSize, symbolSize),
                    center + new Vector2(-symbolSize, symbolSize),
                };
                DrawPolyline(Close(square), symbolColor, 3, true);
                break;
            case NoteSymbol.BlueCircle:
                DrawCircle(center, symbolSize, symbolColor, false, 3, true);
                break;
        }
    }

    private void DrawJudgmentLine()
    {
        Vector2[] polygon =
        {
            TrackPoint(0, 1 - JudgmentLineHalfDepth),
            TrackPoint(LaneCount, 1 - JudgmentLineHalfDepth),
            TrackPoint(LaneCount, 1 + JudgmentLineHalfDepth),
            TrackPoint(0, 1 + JudgmentLineHalfDepth),
        };
        DrawColoredPolygon(polygon, JudgmentLineColor);
    }

    private Vector2 TrackPoint(float lane, float progress)
    {
        float topY = Size.Y * 0.07f;
        float bottomY = Size.Y * 0.90f;
        float farHalfWidth = MathF.Min(Size.X * 0.025f, 40);
        float nearHalfWidth = MathF.Min(Size.X * 0.46f, 620);
        // Extrapolate the perspective past the judgment line so the road keeps
        // widening along the same rays instead of bending into vertical walls.
        float roadProgress = progress;
        float halfWidth = Mathf.Lerp(farHalfWidth, nearHalfWidth, roadProgress);
        float centerX = Size.X * 0.50f;
        float left = centerX - halfWidth;
        return new Vector2(
            left + (lane / LaneCount) * halfWidth * 2,
            Mathf.Lerp(topY, bottomY, progress));
    }

    private static Vector2[] Close(Vector2[] points)
    {
        var closed = new Vector2[points.Length + 1];
        points.CopyTo(closed, 0);
        closed[^1] = points[0];
        return closed;
    }

    private enum NoteSymbol
    {
        Rel,
        RedCross,
        GreenSquare,
        BlueCircle,
    }

    private readonly record struct RuntimeNote(
        long ObjectId,
        int StartLane,
        int LaneWidth,
        long TargetTick,
        InputCategory Category,
        InputRequirement Requirement,
        Color Color,
        NoteSymbol Symbol)
    {
        public ClickScoringObject ToScoringObject() => new(
            ObjectId,
            TargetTick,
            Category,
            Requirement);
    }

    private readonly record struct RuntimeHold(
        ChartObjectDefinition Definition,
        Color Color);
}
