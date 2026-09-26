using System;
using System.IO;
using System.Linq;
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
    private Control? _songSelectOverlay;
    private Label? _selectionSongTitle;
    private Label? _selectionArtist;
    private Label? _selectionDifficulty;
    private Label? _selectionCharter;
    private Label? _selectionError;
    private Label? _selectionJacketPlaceholder;
    private TextureRect? _selectionJacket;
    private Button? _selectionSongButton;
    private Button? _selectionStartButton;
    private SongPackageDefinition? _selectedPackage;
    private ChartDefinition? _selectedChart;

    public AppFlowState CurrentFlowState { get; private set; } = AppFlowState.SongSelect;

    public override void _Ready()
    {
        _inputStatus = GetNode<Label>("InputStatus");
        _judgmentStatus = GetNode<RichTextLabel>("JudgmentStatus");
        _scoreStatus = GetNode<Label>("ScoreStatus");
        _title = GetNode<Label>("Title");
        _playfield = GetNode<PlayfieldView>("Playfield");
        _songSelectOverlay = GetNode<Control>("SongSelectOverlay");
        _selectionSongTitle = GetNode<Label>("SongSelectOverlay/Panel/SongTitle");
        _selectionArtist = GetNode<Label>("SongSelectOverlay/Panel/Artist");
        _selectionDifficulty = GetNode<Label>("SongSelectOverlay/Panel/Difficulty");
        _selectionCharter = GetNode<Label>("SongSelectOverlay/Panel/Charter");
        _selectionError = GetNode<Label>("SongSelectOverlay/Panel/Error");
        _selectionJacketPlaceholder =
            GetNode<Label>("SongSelectOverlay/Panel/JacketFrame/JacketPlaceholder");
        _selectionJacket = GetNode<TextureRect>("SongSelectOverlay/Panel/JacketFrame/Jacket");
        _selectionSongButton = GetNode<Button>("SongSelectOverlay/Panel/SongList/SongButton");
        _selectionStartButton = GetNode<Button>("SongSelectOverlay/Panel/StartButton");
        _selectionStartButton.Pressed += StartSelectedSong;
        _playfield.IsRequirementHeld = _keyboardInput.IsRequirementHeld;
        _playfield.JudgmentResolved += OnJudgmentResolved;
        _playfield.CycleCompleted += OnCycleCompleted;
        _playfield.SongCompleted += OnSongCompleted;
        string[] arguments = OS.GetCmdlineUserArgs();
        bool demoSmokeTest = Array.IndexOf(arguments, "--smoke-test") >= 0;
        bool songSmokeTest = Array.IndexOf(arguments, "--song-smoke-test") >= 0;
        bool selectionSmokeTest = Array.IndexOf(arguments, "--selection-smoke-test") >= 0;
        if (demoSmokeTest)
        {
            CurrentFlowState = AppFlowState.Playing;
            _songSelectOverlay.Visible = false;
        }
        else
        {
            PrepareSongSelection(DefaultSongPackagePath, DefaultChartId);
            if (songSmokeTest)
            {
                StartSelectedSong();
            }
            else
            {
                ShowSongSelect();
                if (selectionSmokeTest)
                {
                    RunSelectionSmokeTest();
                }
            }
        }

        UpdateScore(_playfield.CurrentScore);
        GD.Print("AtEnd development shell is ready.");
    }

    private void PrepareSongSelection(string packageDirectoryPath, string chartId)
    {
        try
        {
            string filesystemPath = ProjectSettings.GlobalizePath(packageDirectoryPath);
            SongPackageDefinition package = SongPackageLoader.LoadDirectory(filesystemPath);
            ChartDefinition? chart = package.Charts
                .SingleOrDefault(item => item.ChartId == chartId);
            if (chart is null)
            {
                throw new InvalidDataException(
                    $"Song package '{package.Song.SongId}' does not contain chart '{chartId}'.");
            }

            _selectedPackage = package;
            _selectedChart = chart;
            if (_selectionSongButton is not null)
            {
                _selectionSongButton.Text = package.Song.Title;
            }

            if (_selectionSongTitle is not null)
            {
                _selectionSongTitle.Text = package.Song.Title;
            }

            if (_selectionArtist is not null)
            {
                _selectionArtist.Text = package.Song.Artist;
            }

            if (_selectionDifficulty is not null)
            {
                _selectionDifficulty.Text =
                    $"{chart.Difficulty.Name}  ·  LEVEL {chart.Difficulty.Level}";
            }

            if (_selectionCharter is not null)
            {
                _selectionCharter.Text = $"CHART  ·  {chart.Charter}";
            }

            LoadSelectionJacket(package);

            if (_selectionStartButton is not null)
            {
                _selectionStartButton.Disabled = false;
            }

            if (_selectionError is not null)
            {
                _selectionError.Visible = false;
            }
        }
        catch (Exception exception)
        {
            _selectedPackage = null;
            _selectedChart = null;
            ShowSelectionError(exception.Message);
        }
    }

    private void LoadSelectionJacket(SongPackageDefinition package)
    {
        if (_selectionJacket is null || _selectionJacketPlaceholder is null)
        {
            return;
        }

        _selectionJacket.Texture = null;
        _selectionJacketPlaceholder.Text = $"NO JACKET\n{package.Song.Title}";
        _selectionJacketPlaceholder.Visible = true;
        if (package.Song.JacketFile is null)
        {
            return;
        }

        string jacketPath = Path.Combine(package.DirectoryPath, package.Song.JacketFile);
        var image = new Image();
        Error loadError = image.Load(jacketPath);
        if (loadError != Error.Ok)
        {
            throw new InvalidDataException(
                $"Godot could not load jacket '{jacketPath}' ({loadError}).");
        }

        _selectionJacket.Texture = ImageTexture.CreateFromImage(image);
        _selectionJacketPlaceholder.Visible = false;
    }

    private void StartSelectedSong()
    {
        if (_playfield is null || _selectedPackage is null || _selectedChart is null)
        {
            return;
        }

        try
        {
            _playfield.StartSong(_selectedPackage, _selectedChart.ChartId);
            CurrentFlowState = AppFlowState.Playing;
            if (_songSelectOverlay is not null)
            {
                _songSelectOverlay.Visible = false;
            }

            UpdateSongInfo();
            UpdateScore(_playfield.CurrentScore);
        }
        catch (Exception exception)
        {
            ShowSongSelect();
            ShowSelectionError(exception.Message);
        }
    }

    private void ShowSongSelect()
    {
        CurrentFlowState = AppFlowState.SongSelect;
        if (_songSelectOverlay is not null)
        {
            _songSelectOverlay.Visible = true;
        }
    }

    private void ShowSelectionError(string message)
    {
        if (_selectionError is not null)
        {
            _selectionError.Text = $"LOAD ERROR  ·  {message}";
            _selectionError.Visible = true;
        }

        if (_selectionStartButton is not null)
        {
            _selectionStartButton.Disabled = true;
        }
    }

    private void RunSelectionSmokeTest()
    {
        bool valid = CurrentFlowState == AppFlowState.SongSelect
            && _playfield?.PlaybackState == SongPlaybackState.Idle
            && _songSelectOverlay?.Visible == true
            && _selectionStartButton?.Disabled == false
            && _selectedPackage is not null
            && _selectedChart is not null;
        if (valid)
        {
            GD.Print("Song selection smoke test passed: metadata loaded and playback remains idle.");
        }
        else
        {
            GD.PushError("Song selection smoke test failed.");
        }

        GetTree().Quit(valid ? 0 : 1);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent)
        {
            return;
        }

        if (CurrentFlowState == AppFlowState.SongSelect)
        {
            if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Enter)
            {
                StartSelectedSong();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (CurrentFlowState != AppFlowState.Playing
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
