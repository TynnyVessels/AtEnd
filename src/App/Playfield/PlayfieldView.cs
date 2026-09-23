using System;
using System.Collections.Generic;
using System.Linq;
using AtEnd.App.Audio;
using AtEnd.Core;
using Godot;

namespace AtEnd.App.Playfield;

public partial class PlayfieldView : Control
{
    private const int LaneCount = 18;
    private static readonly Color TrackColor = new("10162b");
    private static readonly Color TrackEdgeColor = new("6f7fb5");
    private static readonly Color MinorGridColor = new(0.3f, 0.38f, 0.62f, 0.28f);
    private static readonly Color MajorGridColor = new(0.5f, 0.62f, 0.92f, 0.7f);
    private static readonly Color JudgmentLineColor = new("f4f7ff");
    private static readonly TimingMap DemoTiming = new(0.35, 120,
        new[] { new BpmChange(5760, 180) });
    private static readonly DemoNote[] DemoNotes =
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

    private const double ApproachDurationSeconds = 1.8;
    private const double DemoCycleSeconds = 3.2;
    private const double PostJudgmentLingerSeconds = 0.12;

    private GodotAudioClock? _audioClock;
    private AudioStreamPlayer? _audioPlayer;
    private AudioStreamWav? _silentAudioStream;
    private bool _smokeTest;
    private int _smokeTestFrames;
    private ulong _smokeQuitAfterTicks;
    private readonly List<PressEvent> _pendingPresses = new();
    private ClickGameplaySession? _gameplaySession;
    private long _activeCycle = -1;
    private double _pendingInputTime;
    private int _completedCycleCount;

    public event Action<GameplayJudgment, ScoreSnapshot>? JudgmentResolved;
    public event Action<ScoreSnapshot>? CycleCompleted;

    public ScoreSnapshot CurrentScore => _gameplaySession?.Snapshot() ?? default;

    [Export]
    public int GridDensity { get; set; } = 18;

    public override void _Ready()
    {
        _audioPlayer = GetNode<AudioStreamPlayer>("../DemoAudioClock");
        _silentAudioStream = CreateSilentLoop();
        _audioPlayer.Stream = _silentAudioStream;
        if (!_audioPlayer.Playing)
        {
            _audioPlayer.Play();
        }

        _audioClock = new GodotAudioClock(_audioPlayer);
        EnsureCycle(0);
        _smokeTest = Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0;
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

        UpdateGameplay();
        if (!_smokeTest)
        {
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
        long cycle = (long)Math.Floor(totalAudioTime / DemoCycleSeconds);
        EnsureCycle(cycle);
        if (_pendingPresses.Count == 0)
        {
            _pendingInputTime = totalAudioTime - (cycle * DemoCycleSeconds);
        }

        _pendingPresses.Add(pressEvent);
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

    private void UpdateGameplay()
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
            Publish(_gameplaySession.JudgeBatch(_pendingInputTime, _pendingPresses));
            _pendingPresses.Clear();
        }

        if (_gameplaySession is not null)
        {
            Publish(_gameplaySession.AdvanceTime(cycleAudioTime));
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

    private void Publish(IEnumerable<GameplayJudgment> judgments)
    {
        if (_gameplaySession is null)
        {
            return;
        }

        foreach (GameplayJudgment judgment in judgments)
        {
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
        DrawMovingNotes();
        DrawJudgmentLine();
    }

    private void DrawTrack()
    {
        Vector2[] track =
        {
            TrackPoint(0, 0),
            TrackPoint(LaneCount, 0),
            TrackPoint(LaneCount, 1),
            TrackPoint(0, 1),
        };
        DrawColoredPolygon(track, TrackColor);
        DrawPolyline(Close(track), TrackEdgeColor, 3, true);
    }

    private void DrawGuides()
    {
        int density = GridDensity is 3 or 9 or 18 ? GridDensity : 18;
        int laneStep = LaneCount / density;
        for (int lane = laneStep; lane < LaneCount; lane += laneStep)
        {
            bool majorBoundary = lane % 6 == 0;
            DrawLine(
                TrackPoint(lane, 0),
                TrackPoint(lane, 1),
                majorBoundary ? MajorGridColor : MinorGridColor,
                majorBoundary ? 2.5f : 1,
                true);
        }

        for (int guide = 1; guide < 9; guide++)
        {
            float progress = guide / 9f;
            DrawLine(
                TrackPoint(0, progress),
                TrackPoint(LaneCount, progress),
                new Color(0.36f, 0.43f, 0.68f, 0.14f),
                1,
                true);
        }
    }

    private void DrawMovingNotes()
    {
        double currentAudioTime = (_audioClock?.CurrentTimeSeconds ?? 0) % DemoCycleSeconds;
        foreach (DemoNote note in DemoNotes)
        {
            if (_gameplaySession?.IsJudged(note.ObjectId) == true)
            {
                continue;
            }

            double targetTime = DemoTiming.GetAudioTimeSeconds(note.TargetTick);
            double effectiveAudioTime = currentAudioTime;
            if (targetTime - currentAudioTime < -PostJudgmentLingerSeconds)
            {
                effectiveAudioTime -= DemoCycleSeconds;
            }

            float progress = (float)NoteTravel.GetProgress(
                DemoTiming,
                note.TargetTick,
                effectiveAudioTime,
                ApproachDurationSeconds);
            if (progress is >= 0 and <= 1.07f)
            {
                DrawNote(note.StartLane, note.LaneWidth, progress, note.Color, note.Symbol);
            }
        }
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
        const float halfThickness = 0.017f;
        float nearProgress = Math.Clamp(progress + halfThickness, 0, 1);
        float farProgress = Math.Clamp(progress - halfThickness, 0, 1);
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

        Vector2 center = (TrackPoint(startLane, progress) + TrackPoint(startLane + laneWidth, progress)) / 2;
        float symbolSize = MathF.Max(5, polygon[1].DistanceTo(polygon[0]) * 0.12f);
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
        DrawLine(TrackPoint(0, 1), TrackPoint(LaneCount, 1), JudgmentLineColor, 6, true);
    }

    private Vector2 TrackPoint(float lane, float progress)
    {
        float topY = Size.Y * 0.14f;
        float bottomY = Size.Y * 0.88f;
        float topHalfWidth = MathF.Min(Size.X * 0.18f, 250);
        float bottomHalfWidth = MathF.Min(Size.X * 0.46f, 620);
        float halfWidth = Mathf.Lerp(topHalfWidth, bottomHalfWidth, progress);
        float left = (Size.X / 2) - halfWidth;
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

    private readonly record struct DemoNote(
        long ObjectId,
        int StartLane,
        int LaneWidth,
        long TargetTick,
        InputCategory Category,
        InputRequirement Requirement,
        Color Color,
        NoteSymbol Symbol);
}
