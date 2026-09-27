using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AtEnd.App.Input;
using AtEnd.App.Playfield;
using AtEnd.App.Settings;
using AtEnd.Core;
using Godot;

namespace AtEnd.App;

public enum AppFlowState
{
    SongSelect,
    Playing,
    Paused,
    Result,
}

public partial class Main : Control
{
    private const string SongsDirectoryPath = "res://songs";

    private readonly GodotKeyboardInput _keyboardInput = new();
    private readonly PlayerSettingsStore _settingsStore = new();
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
    private VBoxContainer? _selectionSongList;
    private Label? _selectionHint;
    private Button? _selectionStartButton;
    private Control? _resultOverlay;
    private Label? _resultSongTitle;
    private Label? _resultMark;
    private Label? _resultScore;
    private Label? _resultAccuracy;
    private Label? _resultDetails;
    private Label? _resultJudgments;
    private Button? _resultRetryButton;
    private Button? _resultBackButton;
    private Control? _pauseOverlay;
    private Button? _pauseContinueButton;
    private Button? _pauseRetryButton;
    private Button? _pauseExitButton;
    private Control? _settingsOverlay;
    private Button? _settingsOpenButton;
    private HSlider? _settingsSpeedSlider;
    private Label? _settingsSpeedValue;
    private SpinBox? _settingsGlobalOffset;
    private SpinBox? _settingsInputOffset;
    private OptionButton? _settingsGridDensity;
    private Label? _settingsError;
    private Button? _settingsSaveButton;
    private Button? _settingsCancelButton;
    private Button? _settingsDefaultsButton;
    private SongPackageDefinition? _selectedPackage;
    private ChartDefinition? _selectedChart;
    private SongLibraryScanResult? _songLibrary;
    private readonly List<Button> _selectionSongButtons = new();
    private PlayerSettings _playerSettings = PlayerSettings.Default;

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
        _selectionSongList = GetNode<VBoxContainer>(
            "SongSelectOverlay/Panel/SongList/Scroll/Songs");
        _selectionHint = GetNode<Label>("SongSelectOverlay/Panel/Hint");
        _selectionStartButton = GetNode<Button>("SongSelectOverlay/Panel/StartButton");
        _selectionStartButton.Pressed += StartSelectedSong;
        _resultOverlay = GetNode<Control>("ResultOverlay");
        _resultSongTitle = GetNode<Label>("ResultOverlay/Panel/SongTitle");
        _resultMark = GetNode<Label>("ResultOverlay/Panel/CompletionMark");
        _resultScore = GetNode<Label>("ResultOverlay/Panel/Score");
        _resultAccuracy = GetNode<Label>("ResultOverlay/Panel/Accuracy");
        _resultDetails = GetNode<Label>("ResultOverlay/Panel/Details");
        _resultJudgments = GetNode<Label>("ResultOverlay/Panel/Judgments");
        _resultRetryButton = GetNode<Button>("ResultOverlay/Panel/RetryButton");
        _resultBackButton = GetNode<Button>("ResultOverlay/Panel/BackButton");
        _resultRetryButton.Pressed += RetrySelectedSong;
        _resultBackButton.Pressed += ReturnToSongSelect;
        _pauseOverlay = GetNode<Control>("PauseOverlay");
        _pauseContinueButton = GetNode<Button>("PauseOverlay/Panel/ContinueButton");
        _pauseRetryButton = GetNode<Button>("PauseOverlay/Panel/RetryButton");
        _pauseExitButton = GetNode<Button>("PauseOverlay/Panel/ExitButton");
        _pauseContinueButton.Pressed += ResumeGameplay;
        _pauseRetryButton.Pressed += RetrySelectedSong;
        _pauseExitButton.Pressed += ReturnToSongSelect;
        _settingsOverlay = GetNode<Control>("SettingsOverlay");
        _settingsOpenButton = GetNode<Button>("SongSelectOverlay/SettingsButton");
        _settingsSpeedSlider = GetNode<HSlider>("SettingsOverlay/Panel/SpeedSlider");
        _settingsSpeedValue = GetNode<Label>("SettingsOverlay/Panel/SpeedValue");
        _settingsGlobalOffset = GetNode<SpinBox>("SettingsOverlay/Panel/GlobalOffset");
        _settingsInputOffset = GetNode<SpinBox>("SettingsOverlay/Panel/InputOffset");
        _settingsGridDensity = GetNode<OptionButton>("SettingsOverlay/Panel/GridDensity");
        _settingsError = GetNode<Label>("SettingsOverlay/Panel/Error");
        _settingsSaveButton = GetNode<Button>("SettingsOverlay/Panel/SaveButton");
        _settingsCancelButton = GetNode<Button>("SettingsOverlay/Panel/CancelButton");
        _settingsDefaultsButton = GetNode<Button>("SettingsOverlay/Panel/DefaultsButton");
        _settingsOpenButton.Pressed += ShowSettings;
        _settingsSpeedSlider.ValueChanged += OnSettingsSpeedChanged;
        _settingsSaveButton.Pressed += SaveSettings;
        _settingsCancelButton.Pressed += CloseSettings;
        _settingsDefaultsButton.Pressed += LoadDefaultSettingsControls;
        _settingsGridDensity.AddItem("3 格", 3);
        _settingsGridDensity.AddItem("9 格", 9);
        _settingsGridDensity.AddItem("18 格", 18);
        _playfield.IsRequirementHeld = _keyboardInput.IsRequirementHeld;
        _playfield.JudgmentResolved += OnJudgmentResolved;
        _playfield.CycleCompleted += OnCycleCompleted;
        _playfield.SongCompleted += OnSongCompleted;
        _playerSettings = _settingsStore.Load();
        _playfield.ApplyPlayerSettings(_playerSettings);
        LoadSettingsControls(_playerSettings);
        string[] arguments = OS.GetCmdlineUserArgs();
        bool demoSmokeTest = Array.IndexOf(arguments, "--smoke-test") >= 0;
        bool songSmokeTest = Array.IndexOf(arguments, "--song-smoke-test") >= 0;
        bool selectionSmokeTest = Array.IndexOf(arguments, "--selection-smoke-test") >= 0;
        bool resultSmokeTest = Array.IndexOf(arguments, "--result-smoke-test") >= 0;
        bool pauseSmokeTest = Array.IndexOf(arguments, "--pause-smoke-test") >= 0;
        bool settingsSmokeTest = Array.IndexOf(arguments, "--settings-smoke-test") >= 0;
        if (demoSmokeTest)
        {
            CurrentFlowState = AppFlowState.Playing;
            _songSelectOverlay.Visible = false;
        }
        else
        {
            PrepareSongLibrary(SongsDirectoryPath);
            if (songSmokeTest)
            {
                StartSelectedSong();
            }
            else if (pauseSmokeTest)
            {
                StartSelectedSong();
                RunPauseSmokeTest();
            }
            else if (resultSmokeTest)
            {
                RunResultSmokeTest();
            }
            else if (settingsSmokeTest)
            {
                ShowSongSelect();
                RunSettingsSmokeTest();
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

    private void PrepareSongLibrary(string libraryDirectoryPath)
    {
        try
        {
            string filesystemPath = ProjectSettings.GlobalizePath(libraryDirectoryPath);
            _songLibrary = SongLibraryScanner.ScanDirectory(filesystemPath);
            PopulateSongList(_songLibrary.Packages);
            foreach (SongPackageLoadFailure failure in _songLibrary.Failures)
            {
                GD.PushWarning(
                    $"Skipped song package '{Path.GetFileName(failure.DirectoryPath)}': {failure.Message}");
            }

            if (_songLibrary.Packages.Count == 0)
            {
                _selectedPackage = null;
                _selectedChart = null;
                ShowSelectionError("No valid song packages were found.");
                UpdateSongLibraryHint();
                return;
            }

            SelectSong(
                _songLibrary.Packages[0],
                _songLibrary.Packages[0].Charts[0],
                _selectionSongButtons[0]);
        }
        catch (Exception exception)
        {
            _songLibrary = null;
            _selectedPackage = null;
            _selectedChart = null;
            ShowSelectionError(exception.Message);
        }
    }

    private void PopulateSongList(IReadOnlyList<SongPackageDefinition> packages)
    {
        if (_selectionSongList is null)
        {
            return;
        }

        foreach (Node child in _selectionSongList.GetChildren())
        {
            _selectionSongList.RemoveChild(child);
            child.QueueFree();
        }

        _selectionSongButtons.Clear();
        foreach (SongPackageDefinition package in packages)
        {
            ChartDefinition chart = package.Charts[0];
            var button = new Button
            {
                Text = package.Song.Title,
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 52),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ToggleMode = true,
            };
            button.AddThemeFontSizeOverride("font_size", 16);
            button.Pressed += () => SelectSong(package, chart, button);
            _selectionSongList.AddChild(button);
            _selectionSongButtons.Add(button);
        }

        UpdateSongLibraryHint();
    }

    private void SelectSong(
        SongPackageDefinition package,
        ChartDefinition chart,
        Button selectedButton)
    {
        try
        {
            _selectedPackage = package;
            _selectedChart = chart;
            foreach (Button button in _selectionSongButtons)
            {
                button.SetPressedNoSignal(button == selectedButton);
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
                UpdateSongLibraryWarning();
            }
        }
        catch (Exception exception)
        {
            _selectedPackage = null;
            _selectedChart = null;
            ShowSelectionError(exception.Message);
        }
    }

    private void UpdateSongLibraryHint()
    {
        if (_selectionHint is null || _songLibrary is null)
        {
            return;
        }

        _selectionHint.Text = _songLibrary.Failures.Count == 0
            ? $"{_songLibrary.Packages.Count} SONGS AVAILABLE"
            : $"{_songLibrary.Packages.Count} AVAILABLE  ·  {_songLibrary.Failures.Count} SKIPPED";
    }

    private void UpdateSongLibraryWarning()
    {
        if (_selectionError is null)
        {
            return;
        }

        if (_songLibrary is null || _songLibrary.Failures.Count == 0)
        {
            _selectionError.Visible = false;
            return;
        }

        SongPackageLoadFailure first = _songLibrary.Failures[0];
        _selectionError.Text = $"SKIPPED  ·  {Path.GetFileName(first.DirectoryPath)}  ·  {first.Message}";
        _selectionError.Visible = true;
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
            _keyboardInput.ReleaseAll();
            _playfield.StartSong(_selectedPackage, _selectedChart.ChartId);
            CurrentFlowState = AppFlowState.Playing;
            if (_songSelectOverlay is not null)
            {
                _songSelectOverlay.Visible = false;
            }

            if (_resultOverlay is not null)
            {
                _resultOverlay.Visible = false;
            }

            if (_pauseOverlay is not null)
            {
                _pauseOverlay.Visible = false;
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

        if (_resultOverlay is not null)
        {
            _resultOverlay.Visible = false;
        }

        if (_pauseOverlay is not null)
        {
            _pauseOverlay.Visible = false;
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
            && _selectedPackage?.Song.SongId == "test-song"
            && _selectedChart?.ChartId == "test-song-test"
            && _songLibrary?.Packages.Count == 1
            && _songLibrary.Failures.Count == 0
            && _selectionSongButtons.Count == 1;
        if (valid)
        {
            GD.Print(
                "Song selection smoke test passed: discovered packages populate the dynamic list.");
        }
        else
        {
            GD.PushError("Song selection smoke test failed.");
        }

        GetTree().Quit(valid ? 0 : 1);
    }

    private void RunResultSmokeTest()
    {
        var scoreRun = new ScoreRun(1, 0);
        scoreRun.Add(InputCategory.Rel, Judgment.StrictlyPrecise);
        scoreRun.CompleteNormally();
        ScoreSnapshot score = scoreRun.Snapshot();
        ShowResult(score);

        bool valid = CurrentFlowState == AppFlowState.Result
            && _playfield?.PlaybackState == SongPlaybackState.Idle
            && _resultOverlay?.Visible == true
            && _resultScore?.Text == "1,000,001"
            && _resultMark?.Text == "ALL STRICTLY PRECISE";
        if (valid)
        {
            GD.Print("Result smoke test passed: score data displayed while playback remains idle.");
        }
        else
        {
            GD.PushError("Result smoke test failed.");
        }

        GetTree().Quit(valid ? 0 : 1);
    }

    private void RunPauseSmokeTest()
    {
        PauseGameplay();
        bool paused = CurrentFlowState == AppFlowState.Paused
            && _playfield?.PlaybackState == SongPlaybackState.Paused
            && _playfield.IsAudioPaused
            && _pauseOverlay?.Visible == true;
        ResumeGameplay();
        bool resumed = CurrentFlowState == AppFlowState.Playing
            && _playfield?.PlaybackState == SongPlaybackState.Playing
            && !_playfield.IsAudioPaused
            && _pauseOverlay?.Visible == false;
        bool valid = paused && resumed;
        if (valid)
        {
            GD.Print("Pause smoke test passed: audio and gameplay pause and resume together.");
        }
        else
        {
            GD.PushError("Pause smoke test failed.");
        }

        _playfield?.StopSong();
        GetTree().Quit(valid ? 0 : 1);
    }

    private void RunSettingsSmokeTest()
    {
        const string smokePath = "user://settings-smoke.cfg";
        string filesystemPath = ProjectSettings.GlobalizePath(smokePath);
        bool valid;
        try
        {
            var store = new PlayerSettingsStore(smokePath);
            var expected = new PlayerSettings(8.5, -40, 25, 9);
            store.Save(expected);
            PlayerSettings loaded = store.Load();
            ShowSettings();
            LoadSettingsControls(loaded);
            valid = loaded == expected
                && _settingsOverlay?.Visible == true
                && Math.Abs((_settingsSpeedSlider?.Value ?? 0) - 8.5) < 0.0001
                && _settingsGridDensity is not null
                && _settingsGridDensity.GetItemId(_settingsGridDensity.Selected) == 9;
        }
        finally
        {
            if (File.Exists(filesystemPath))
            {
                File.Delete(filesystemPath);
            }
        }

        if (valid)
        {
            GD.Print("Settings smoke test passed: values save, load, and populate controls.");
        }
        else
        {
            GD.PushError("Settings smoke test failed.");
        }

        GetTree().Quit(valid ? 0 : 1);
    }

    private void PauseGameplay()
    {
        if (CurrentFlowState != AppFlowState.Playing || _playfield is null)
        {
            return;
        }

        _keyboardInput.ReleaseAll();
        _playfield.PauseSong();
        if (_playfield.PlaybackState != SongPlaybackState.Paused)
        {
            return;
        }

        CurrentFlowState = AppFlowState.Paused;
        if (_pauseOverlay is not null)
        {
            _pauseOverlay.Visible = true;
        }
    }

    private void ResumeGameplay()
    {
        if (CurrentFlowState != AppFlowState.Paused || _playfield is null)
        {
            return;
        }

        _keyboardInput.ReleaseAll();
        _playfield.ResumeSong();
        if (_playfield.PlaybackState != SongPlaybackState.Playing)
        {
            return;
        }

        CurrentFlowState = AppFlowState.Playing;
        if (_pauseOverlay is not null)
        {
            _pauseOverlay.Visible = false;
        }
    }

    private void ShowSettings()
    {
        if (CurrentFlowState != AppFlowState.SongSelect)
        {
            return;
        }

        LoadSettingsControls(_playerSettings);
        if (_settingsError is not null)
        {
            _settingsError.Visible = false;
        }

        if (_settingsOverlay is not null)
        {
            _settingsOverlay.Visible = true;
        }
    }

    private void CloseSettings()
    {
        LoadSettingsControls(_playerSettings);
        if (_settingsOverlay is not null)
        {
            _settingsOverlay.Visible = false;
        }
    }

    private void SaveSettings()
    {
        if (_settingsSpeedSlider is null
            || _settingsGlobalOffset is null
            || _settingsInputOffset is null
            || _settingsGridDensity is null)
        {
            return;
        }

        try
        {
            int gridDensity = _settingsGridDensity.GetItemId(
                _settingsGridDensity.Selected);
            var settings = new PlayerSettings(
                _settingsSpeedSlider.Value,
                _settingsGlobalOffset.Value,
                _settingsInputOffset.Value,
                gridDensity);
            _settingsStore.Save(settings);
            _playerSettings = settings;
            _playfield?.ApplyPlayerSettings(settings);
            CloseSettings();
        }
        catch (Exception exception)
        {
            if (_settingsError is not null)
            {
                _settingsError.Text = $"保存失败  ·  {exception.Message}";
                _settingsError.Visible = true;
            }
        }
    }

    private void LoadDefaultSettingsControls()
    {
        LoadSettingsControls(PlayerSettings.Default);
    }

    private void LoadSettingsControls(PlayerSettings settings)
    {
        if (_settingsSpeedSlider is not null)
        {
            _settingsSpeedSlider.Value = settings.ScrollSpeed;
        }

        if (_settingsGlobalOffset is not null)
        {
            _settingsGlobalOffset.Value = settings.GlobalTimingOffsetMilliseconds;
        }

        if (_settingsInputOffset is not null)
        {
            _settingsInputOffset.Value = settings.InputOffsetMilliseconds;
        }

        if (_settingsGridDensity is not null)
        {
            for (int index = 0; index < _settingsGridDensity.ItemCount; index++)
            {
                if (_settingsGridDensity.GetItemId(index) == settings.GridDensity)
                {
                    _settingsGridDensity.Select(index);
                    break;
                }
            }
        }

        OnSettingsSpeedChanged(settings.ScrollSpeed);
    }

    private void OnSettingsSpeedChanged(double value)
    {
        if (_settingsSpeedValue is not null)
        {
            _settingsSpeedValue.Text = $"{value:F1}";
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent)
        {
            return;
        }

        if (CurrentFlowState == AppFlowState.SongSelect)
        {
            if (_settingsOverlay?.Visible == true)
            {
                if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Escape)
                {
                    CloseSettings();
                    GetViewport().SetInputAsHandled();
                }

                return;
            }

            if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Escape)
            {
                ShowSettings();
                GetViewport().SetInputAsHandled();
            }
            else if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Enter)
            {
                StartSelectedSong();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (CurrentFlowState == AppFlowState.Result)
        {
            if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Enter)
            {
                RetrySelectedSong();
                GetViewport().SetInputAsHandled();
            }
            else if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Escape)
            {
                ReturnToSongSelect();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (CurrentFlowState == AppFlowState.Paused)
        {
            if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Escape)
            {
                ResumeGameplay();
                GetViewport().SetInputAsHandled();
            }
            else if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Enter)
            {
                RetrySelectedSong();
                GetViewport().SetInputAsHandled();
            }
            else if (keyEvent.Pressed
                && !keyEvent.Echo
                && keyEvent.Keycode == Key.Backspace)
            {
                ReturnToSongSelect();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (CurrentFlowState == AppFlowState.Playing
            && keyEvent.Pressed
            && !keyEvent.Echo
            && keyEvent.Keycode == Key.Escape)
        {
            PauseGameplay();
            GetViewport().SetInputAsHandled();
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
        _keyboardInput.ReleaseAll();
        UpdateScore(score);
        ShowResult(score);
    }

    private void ShowResult(ScoreSnapshot score)
    {
        CurrentFlowState = AppFlowState.Result;
        if (_resultOverlay is not null)
        {
            _resultOverlay.Visible = true;
        }

        if (_resultSongTitle is not null)
        {
            _resultSongTitle.Text = _selectedPackage?.Song.Title ?? "Unknown Song";
        }

        if (_resultMark is not null)
        {
            _resultMark.Text = score.HighestCompletionMark.DisplayName() ?? "COMPLETED";
        }

        if (_resultScore is not null)
        {
            _resultScore.Text = score.TotalScore.ToString("N0");
        }

        if (_resultAccuracy is not null)
        {
            _resultAccuracy.Text = $"ACCURACY  {score.AccuracyPercent:F2}%";
        }

        if (_resultDetails is not null)
        {
            _resultDetails.Text = $"SP BONUS  {score.StrictlyPreciseBonus:N0}"
                + $"     MAX COMBO  {score.MaximumCombo:N0}";
        }

        if (_resultJudgments is not null)
        {
            _resultJudgments.Text =
                $"STRICTLY PRECISE  {score.StrictlyPreciseCount:N0}\n"
                + $"PRECISE           {score.PreciseCount:N0}\n"
                + $"MISALIGNED        {score.MisalignedCount:N0}\n"
                + $"CHAOTIC           {score.ChaoticCount:N0}";
        }
    }

    private void RetrySelectedSong()
    {
        StartSelectedSong();
    }

    private void ReturnToSongSelect()
    {
        _keyboardInput.ReleaseAll();
        _playfield?.StopSong();
        ShowSongSelect();
        UpdateScore(default);
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
