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
}
