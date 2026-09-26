using System;
using AtEnd.App.Input;
using AtEnd.App.Playfield;
using AtEnd.Core;
using Godot;

namespace AtEnd.App;

public enum AppFlowState
{
    SongSelect,
    Playing,
    Result,
}

public partial class Main : Control
{
    private const string DefaultSongPackagePath = "res://songs/test-song";
    private const string DefaultChartId = "test-song-test";

    private readonly GodotKeyboardInput _keyboardInput = new();
    private Label? _inputStatus;
    private RichTextLabel? _judgmentStatus;
    private Label? _scoreStatus;
    private Label? _title;
    private PlayfieldView? _playfield;
    private Tween? _judgmentFadeTween;

    public AppFlowState CurrentFlowState { get; private set; } = AppFlowState.SongSelect;

    public override void _Ready()
    {
        _inputStatus = GetNode<Label>("InputStatus");
        _judgmentStatus = GetNode<RichTextLabel>("JudgmentStatus");
        _scoreStatus = GetNode<Label>("ScoreStatus");
        _title = GetNode<Label>("Title");
        _playfield = GetNode<PlayfieldView>("Playfield");
        _playfield.IsRequirementHeld = _keyboardInput.IsRequirementHeld;
        _playfield.JudgmentResolved += OnJudgmentResolved;
        _playfield.CycleCompleted += OnCycleCompleted;
        _playfield.SongCompleted += OnSongCompleted;
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke-test") >= 0)
        {
            CurrentFlowState = AppFlowState.Playing;
        }
        else
        {
            StartSong(DefaultSongPackagePath, DefaultChartId);
        }

        UpdateSongInfo();
        UpdateScore(_playfield.CurrentScore);
        GD.Print("AtEnd development shell is ready.");
    }

    private void StartSong(string packageDirectoryPath, string chartId)
    {
        if (_playfield is null)
        {
            throw new InvalidOperationException("The playfield is not available.");
        }

        _playfield.StartSong(packageDirectoryPath, chartId);
        CurrentFlowState = AppFlowState.Playing;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent
            || !_keyboardInput.TryHandle(keyEvent, out PressEvent? pressEvent))
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        if (pressEvent is not { } press)
        {
            return;
        }

        string message = $"INPUT  ·  {press.Channel}  ·  #{press.EventId}";
        if (_inputStatus is not null)
        {
            _inputStatus.Text = message;
        }

        _playfield?.QueuePress(press);
        GD.Print(message);
    }

    private void OnJudgmentResolved(GameplayJudgment result, ScoreSnapshot score)
    {
        string judgmentText = result.Judgment switch
        {
            Judgment.StrictlyPrecise => "[rainbow freq=0.35 sat=0.80 val=1.0 speed=0]STRICTLY PRECISE[/rainbow]",
            Judgment.Precise => "[rainbow freq=0.55 sat=0.72 val=1.0 speed=0]PRECISE[/rainbow]",
            Judgment.Misaligned => "[color=#ffb52e]MISALIGNED[/color]",
            _ => "[color=#ff4057]CHAOTIC[/color]",
        };
        string direction = result.Direction switch
        {
            TimingDirection.Early => "[font_size=14][color=#c9d1ea] · EARLY[/color][/font_size]",
            TimingDirection.Late => "[font_size=14][color=#c9d1ea] · LATE[/color][/font_size]",
            _ => string.Empty,
        };
        if (_judgmentStatus is not null)
        {
            bool strictlyPrecise = result.Judgment == Judgment.StrictlyPrecise;
            _judgmentStatus.AddThemeConstantOverride("outline_size", strictlyPrecise ? 4 : 0);
            _judgmentStatus.AddThemeColorOverride(
                "font_outline_color",
                strictlyPrecise ? Colors.White : Colors.Transparent);
            _judgmentStatus.Text = $"[center]{judgmentText}{direction}[/center]";
            _judgmentStatus.Modulate = Colors.White;
            _judgmentStatus.Visible = true;

            _judgmentFadeTween?.Kill();
            _judgmentFadeTween = CreateTween();
            _judgmentFadeTween.TweenInterval(0.38);
            _judgmentFadeTween
                .TweenProperty(_judgmentStatus, "modulate:a", 0.0, 0.62)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.In);
            _judgmentFadeTween.TweenCallback(Callable.From(() =>
            {
                if (_judgmentStatus is not null)
                {
                    _judgmentStatus.Visible = false;
                }
            }));
        }

        UpdateScore(score);
    }

    private void OnCycleCompleted(ScoreSnapshot score)
    {
        UpdateScore(score);
    }

    private void OnSongCompleted(ScoreSnapshot score)
    {
        CurrentFlowState = AppFlowState.Result;
        UpdateScore(score);
    }

    private void UpdateSongInfo()
    {
        if (_title is null || _playfield is null)
        {
            return;
        }

        _title.Text = $"{_playfield.LoadedSongTitle}\n"
            + $"{_playfield.LoadedSongArtist}\n"
            + $"{_playfield.LoadedDifficulty}\n"
            + $"{_playfield.LoadedCharter}";
    }

    private void UpdateScore(ScoreSnapshot score)
    {
        if (_scoreStatus is null)
        {
            return;
        }

        _scoreStatus.Text = $"SCORE  {score.TotalScore:N0}\n"
            + $"ACC  {score.AccuracyPercent:F2}%   SP  {score.StrictlyPreciseBonus}\n"
            + $"COMBO  {score.CurrentCombo}   MAX  {score.MaximumCombo}";
    }
}
