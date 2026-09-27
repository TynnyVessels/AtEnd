using System;
using System.IO;
using System.Linq;
using AtEnd.Core;
using Godot;

namespace AtEnd.Editor;

public partial class EditorMain : Control
{
    private OptionButton? _songSelector;
    private OptionButton? _chartSelector;
    private Button? _rescanButton;
    private Button? _playPauseButton;
    private VerticalSeekBar? _seekSlider;
    private Label? _timeLabel;
    private Label? _detailsLabel;
    private Label? _statusLabel;
    private Timer? _statusTimer;
    private StyleBoxFlat? _statusBackground;
    private TimelineView? _timeline;
    private TabContainer? _workspace;
    private AudioStreamPlayer? _audioPlayer;
    private SongLibraryScanResult? _library;
    private SongPackageDefinition? _selectedPackage;
    private ChartDefinition? _selectedChart;
    private bool _updatingSeek;
    private bool _draggingSeek;

    public override void _Ready()
    {
        _workspace = GetNode<TabContainer>("RightPanel/Workspace");
        _songSelector = GetNode<OptionButton>("RightPanel/Workspace/Chart/SongSelector");
        _chartSelector = GetNode<OptionButton>("RightPanel/Workspace/Chart/ChartSelector");
        _rescanButton = GetNode<Button>("RightPanel/Workspace/Chart/RescanButton");
        _playPauseButton = GetNode<Button>("RightPanel/PlayPauseButton");
        _seekSlider = GetNode<VerticalSeekBar>("Preview/SeekSlider");
        _timeLabel = GetNode<Label>("RightPanel/TimeLabel");
        _detailsLabel = GetNode<Label>("RightPanel/Workspace/Chart/Details");
        _statusLabel = GetNode<Label>("RightPanel/Status");
        InitializeStatusNotifications();
        _timeline = GetNode<TimelineView>("Preview/Timeline");
        _audioPlayer = GetNode<AudioStreamPlayer>("AudioPlayer");

        _songSelector.ItemSelected += OnSongSelected;
        _chartSelector.ItemSelected += OnChartSelected;
        _rescanButton.Pressed += ScanSongs;
        _playPauseButton.Pressed += TogglePlayback;
        _seekSlider.ValueChanged += SeekTo;
        _seekSlider.DragStarted += OnSeekDragStarted;
        _seekSlider.DragEnded += OnSeekDragEnded;
        _timeline.SeekRequested += SeekTo;
        _audioPlayer.Finished += OnPlaybackFinished;
        _workspace.SetTabTitle(0, "谱面");

        ScanSongs();
        InitializeEditingFoundation();
        GD.Print("AtEnd chart editor is ready.");

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--editor-smoke-test") >= 0)
        {
            _seekSlider.Value = _seekSlider.MaxValue;
            bool reachesTop = _seekSlider.HandleTopPixels <= 0.001f;
            _seekSlider.Value = _seekSlider.MinValue;
            bool reachesBottom = Math.Abs(
                _seekSlider.HandleTopPixels - Math.Max(0, _seekSlider.Size.Y - 12)) <= 0.001f;
            Control seekParent = (Control)_seekSlider.GetParent();
            bool fillsPreviewHeight = Math.Abs(_seekSlider.Position.Y) <= 0.001f
                && Math.Abs(_seekSlider.Size.Y - seekParent.Size.Y) <= 0.001f;
            bool valid = _library?.Packages.Count == 1
                && _selectedPackage?.Song.SongId == "test-song"
                && _selectedChart?.ChartId == "test-song-test"
                && _timeline.LoadedChartId == "test-song-test"
                && _audioPlayer.Stream is not null
                && reachesTop
                && reachesBottom
                && fillsPreviewHeight
                && _document is not null
                && !_document.IsDirty
                && _workspace.GetTabCount() == 2
                && Math.Abs(_statusTimer!.WaitTime - 3.0) <= 0.001
                && !_statusLabel.Visible;
            if (valid)
            {
                GD.Print("Chart editor smoke test passed: package, chart, audio, and timeline loaded.");
            }
            else
            {
                GD.PushError("Chart editor smoke test failed.");
            }

            GetTree().Quit(valid ? 0 : 1);
        }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (_audioPlayer is null || _seekSlider is null || _timeline is null)
        {
            return;
        }

        if (_audioPlayer.Playing && !_audioPlayer.StreamPaused && !_draggingSeek)
        {
            double position = Math.Clamp(
                _audioPlayer.GetPlaybackPosition(),
                _seekSlider.MinValue,
                _seekSlider.MaxValue);
            SetDisplayedPosition(position);
        }
    }

    private void ScanSongs()
    {
        if (_songSelector is null || _chartSelector is null)
        {
            return;
        }

        StopPlayback();
        _songSelector.Clear();
        _chartSelector.Clear();
        _selectedPackage = null;
        _selectedChart = null;

        try
        {
            string editorDirectory = ProjectSettings.GlobalizePath("res://");
            string repositoryDirectory = Directory.GetParent(
                editorDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate the AtEnd repository.");
            string songsDirectory = Path.Combine(repositoryDirectory, "songs");
            _library = SongLibraryScanner.ScanDirectory(songsDirectory);
            foreach (SongPackageDefinition package in _library.Packages)
            {
                _songSelector.AddItem($"{package.Song.Title}  —  {package.Song.Artist}");
            }

            if (_library.Packages.Count == 0)
            {
                SetStatus("没有找到可用歌曲包。", isError: true);
                SetControlsEnabled(false);
                return;
            }

            SetControlsEnabled(true);
            _songSelector.Select(0);
            OnSongSelected(0);
            if (_library.Failures.Count > 0)
            {
                SetStatus(
                    $"跳过 {_library.Failures.Count} 个无效歌曲包。",
                    isError: true);
            }
        }
        catch (Exception exception)
        {
            _library = null;
            SetControlsEnabled(false);
            SetStatus($"加载失败：{exception.Message}", isError: true);
        }
    }

    private void OnSongSelected(long index)
    {
        if (_library is null || _chartSelector is null
            || index < 0 || index >= _library.Packages.Count)
        {
            return;
        }

        StopPlayback();
        _selectedPackage = _library.Packages[(int)index];
        _chartSelector.Clear();
        foreach (ChartDefinition chart in _selectedPackage.Charts)
        {
            _chartSelector.AddItem(
                $"{chart.Difficulty.Name}  ·  LEVEL {chart.Difficulty.Level}  ·  {chart.Charter}");
        }

        _chartSelector.Select(0);
        OnChartSelected(0);
    }

    private void OnChartSelected(long index)
    {
        if (_selectedPackage is null || _timeline is null || _audioPlayer is null
            || _seekSlider is null || index < 0 || index >= _selectedPackage.Charts.Count)
        {
            return;
        }

        StopPlayback();
        try
        {
            _selectedChart = _selectedPackage.Charts[(int)index];
            string audioPath = Path.Combine(
                _selectedPackage.DirectoryPath,
                _selectedPackage.Song.AudioFile);
            _audioPlayer.Stream = LoadAudio(audioPath);
            double duration = Math.Max(_audioPlayer.Stream.GetLength(), GetChartEndSeconds());
            _seekSlider.MinValue = 0;
            _seekSlider.MaxValue = Math.Max(duration, 0.001);
            _seekSlider.Step = 0.001;
            _timeline.SetChart(_selectedPackage.Timing, _selectedChart, duration);
            SetDisplayedPosition(0);
            _detailsLabel!.Text =
                $"{_selectedPackage.Song.Title}  ·  {_selectedChart.Difficulty.Name} {_selectedChart.Difficulty.Level}"
                + $"  ·  {_selectedChart.Objects.Count} 个物件"
                + $"  ·  {_selectedPackage.Timing.TimingMap.InitialBeatsPerMinute:F3} BPM";
        }
        catch (Exception exception)
        {
            _selectedChart = null;
            _audioPlayer.Stream = null;
            _timeline.ClearChart();
            SetStatus($"谱面加载失败：{exception.Message}", isError: true);
        }
    }

    private static AudioStream LoadAudio(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".ogg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("当前制谱器预览仅支持 OGG 音频。");
        }

        return AudioStreamOggVorbis.LoadFromFile(path)
            ?? throw new InvalidDataException($"无法加载音频：{path}");
    }

    private double GetChartEndSeconds()
    {
        if (_selectedPackage is null || _selectedChart is null)
        {
            return 0;
        }

        long endTick = _selectedChart.Objects.Max(item => item.EndTick);
        return _selectedPackage.Timing.TimingMap.GetAudioTimeSeconds(endTick);
    }

    private void TogglePlayback()
    {
        if (_audioPlayer?.Stream is null || _seekSlider is null || _playPauseButton is null)
        {
            return;
        }

        if (_audioPlayer.Playing)
        {
            _audioPlayer.StreamPaused = !_audioPlayer.StreamPaused;
        }
        else
        {
            _audioPlayer.Play((float)_seekSlider.Value);
        }

        _playPauseButton.Text = _audioPlayer.StreamPaused ? "播放" : "暂停";
    }

    private void StopPlayback()
    {
        if (_audioPlayer is not null)
        {
            _audioPlayer.Stop();
            _audioPlayer.StreamPaused = false;
            _audioPlayer.Stream = null;
        }

        if (_playPauseButton is not null)
        {
            _playPauseButton.Text = "播放";
        }
    }

    private void SeekTo(double seconds)
    {
        if (_updatingSeek || _seekSlider is null || _timeline is null)
        {
            return;
        }

        double position = Math.Clamp(seconds, _seekSlider.MinValue, _seekSlider.MaxValue);
        if (_audioPlayer?.Playing == true)
        {
            _audioPlayer.Seek((float)position);
        }

        SetDisplayedPosition(position);
    }

    private void OnSeekDragStarted()
    {
        _draggingSeek = true;
    }

    private void OnSeekDragEnded(bool valueChanged)
    {
        _draggingSeek = false;
        if (valueChanged && _seekSlider is not null)
        {
            SeekTo(_seekSlider.Value);
        }
    }

    private void SetDisplayedPosition(double seconds)
    {
        if (_seekSlider is null || _timeline is null || _timeLabel is null)
        {
            return;
        }

        _updatingSeek = true;
        _seekSlider.Value = seconds;
        _updatingSeek = false;
        _timeline.PlaybackSeconds = seconds;
        _timeLabel.Text = $"{FormatTime(seconds)} / {FormatTime(_seekSlider.MaxValue)}";
    }

    private void OnPlaybackFinished()
    {
        if (_playPauseButton is not null)
        {
            _playPauseButton.Text = "播放";
        }

        if (_seekSlider is not null)
        {
            SetDisplayedPosition(_seekSlider.MaxValue);
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        if (_songSelector is not null) _songSelector.Disabled = !enabled;
        if (_chartSelector is not null) _chartSelector.Disabled = !enabled;
        if (_playPauseButton is not null) _playPauseButton.Disabled = !enabled;
        if (_seekSlider is not null) _seekSlider.Editable = enabled;
    }

    private void InitializeStatusNotifications()
    {
        _statusTimer = new Timer { OneShot = true, WaitTime = 3.0 };
        _statusTimer.Timeout += () =>
        {
            if (_statusLabel is not null)
            {
                _statusLabel.Visible = false;
            }
        };
        AddChild(_statusTimer);

        _statusBackground = new StyleBoxFlat
        {
            BgColor = new Color("18213d"),
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 14,
            ContentMarginTop = 10,
            ContentMarginRight = 14,
            ContentMarginBottom = 10,
        };
        _statusLabel!.AddThemeStyleboxOverride("normal", _statusBackground);
        _statusLabel.Visible = false;
        _statusLabel.ZIndex = 100;
    }

    private void SetStatus(string message, bool isError)
    {

        if (_statusLabel is null)
        {
            return;
        }

        _statusLabel.Text = message;
        _statusLabel.Visible = true;
        _statusLabel.AddThemeColorOverride(
            "font_color",
            isError ? new Color("ffd5da") : new Color("dce5ff"));
        if (_statusBackground is not null)
        {
            _statusBackground.BgColor = isError ? new Color("572936") : new Color("18213d");
        }

        _statusTimer?.Stop();
        _statusTimer?.Start();
    }

    private static string FormatTime(double seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }
}
