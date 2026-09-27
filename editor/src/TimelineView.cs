using System;
using System.Linq;
using AtEnd.Core;
using Godot;

namespace AtEnd.Editor;

public partial class TimelineView : Control
{
    private const int LaneCount = 18;
    private const float AxisWidth = 58;
    private const double DefaultVisibleSeconds = 8;
    private TimingDefinition? _timing;
    private ChartDefinition? _chart;
    private double _durationSeconds = 1;
    private double _visibleDurationSeconds = 1;
    private double _playbackSeconds;
    private double _viewStartSeconds;

    public event Action<double>? SeekRequested;

    public string? LoadedChartId => _chart?.ChartId;

    public double PlaybackSeconds
    {
        get => _playbackSeconds;
        set
        {
            _playbackSeconds = Math.Clamp(value, 0, _durationSeconds);
            EnsurePlaybackVisible();
            QueueRedraw();
        }
    }

    public void SetChart(TimingDefinition timing, ChartDefinition chart, double durationSeconds)
    {
        _timing = timing;
        _chart = chart;
        _durationSeconds = Math.Max(durationSeconds, 0.001);
        _visibleDurationSeconds = Math.Min(DefaultVisibleSeconds, _durationSeconds);
        _playbackSeconds = 0;
        _viewStartSeconds = 0;
        QueueRedraw();
    }

    public void ClearChart()
    {
        _timing = null;
        _chart = null;
        _durationSeconds = 1;
        _visibleDurationSeconds = 1;
        _playbackSeconds = 0;
        _viewStartSeconds = 0;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouse || !mouse.Pressed || _chart is null)
        {
            return;
        }

        if (mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (mouse.CtrlPressed)
            {
                ZoomAt(mouse.Position.Y, mouse.ButtonIndex == MouseButton.WheelUp);
                AcceptEvent();
                return;
            }

            double direction = mouse.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
            double step = Math.Max(0.25, VisibleDurationSeconds * 0.12);
            SetViewStart(_viewStartSeconds + (direction * step));
            AcceptEvent();
            return;
        }

        if (mouse.ButtonIndex == MouseButton.Left && mouse.Position.X >= AxisWidth)
        {
            double seconds = _viewStartSeconds
                + ((1 - Math.Clamp(mouse.Position.Y / Math.Max(Size.Y, 1), 0, 1))
                    * VisibleDurationSeconds);
            SeekRequested?.Invoke(Math.Clamp(seconds, 0, _durationSeconds));
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("111831"));
        float contentWidth = Math.Max(1, Size.X - AxisWidth);
        float laneWidth = contentWidth / LaneCount;

        for (int lane = 0; lane <= LaneCount; lane++)
        {
            float x = AxisWidth + (lane * laneWidth);
            Color color = lane % 6 == 0 ? new Color("34456f") : new Color("222d50");
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), color, lane % 6 == 0 ? 2 : 1);
        }

        DrawTimeGrid();
        if (_timing is not null && _chart is not null)
        {
            foreach (ChartObjectDefinition item in _chart.Objects)
            {
                DrawObject(item, laneWidth);
            }
        }

        float playheadY = TimeToY(_playbackSeconds);
        DrawLine(
            new Vector2(AxisWidth, playheadY),
            new Vector2(Size.X, playheadY),
            new Color("ff596d"),
            2);

        DrawString(
            ThemeDB.FallbackFont,
            new Vector2(6, Math.Clamp(playheadY - 5, 13, Size.Y - 3)),
            FormatTime(_playbackSeconds),
            HorizontalAlignment.Left,
            AxisWidth - 10,
            11,
            new Color("ff8290"));
    }

    private void DrawTimeGrid()
    {
        int firstSecond = Math.Max(0, (int)Math.Floor(_viewStartSeconds));
        int lastSecond = Math.Min(
            (int)Math.Ceiling(_durationSeconds),
            (int)Math.Ceiling(_viewStartSeconds + VisibleDurationSeconds));
        for (int second = firstSecond; second <= lastSecond; second++)
        {
            float y = TimeToY(second);
            DrawLine(new Vector2(AxisWidth, y), new Vector2(Size.X, y), new Color("263457"), 1);
            DrawString(
                ThemeDB.FallbackFont,
                new Vector2(6, Math.Clamp(y + 4, 13, Size.Y - 3)),
                FormatTime(second),
                HorizontalAlignment.Right,
                AxisWidth - 12,
                12,
                new Color("7182aa"));
        }
    }

    private void DrawObject(ChartObjectDefinition item, float laneWidth)
    {
        Color color = item.InputType == InputCategory.Rel
            ? new Color("f5f7ff")
            : item.Color switch
            {
                DrmColor.Red => new Color("ff5970"),
                DrmColor.Green => new Color("57dc8a"),
                _ => new Color("6a7dff"),
            };

        if (item.Type == ChartObjectType.Click)
        {
            DrawPoint(item.StartTick, item.StartPoint.Lane, item.StartPoint.Width, laneWidth, color, 5);
            return;
        }

        DrawHoldBody(item, laneWidth, color);
        DrawPoint(item.StartTick, item.StartPoint.Lane, item.StartPoint.Width, laneWidth, color, 5);
        foreach (long judgeTick in item.JudgeTicks)
        {
            (double lane, double width) = item.GetLaneGeometryAtTick(judgeTick);
            DrawJudgeDiamond(judgeTick, lane, width, laneWidth, color.Lightened(0.12f));
        }
    }

    private void DrawHoldBody(ChartObjectDefinition item, float laneWidth, Color color)
    {
        Vector2[] leftEdge = item.Path
            .Select(point => new Vector2(
                AxisWidth + (point.Lane * laneWidth),
                TickToY(point.Tick)))
            .ToArray();
        Vector2[] rightEdge = item.Path
            .Select(point => new Vector2(
                AxisWidth + ((point.Lane + point.Width) * laneWidth),
                TickToY(point.Tick)))
            .ToArray();
        Vector2[] body = leftEdge
            .Concat(rightEdge.Reverse())
            .ToArray();

        DrawColoredPolygon(body, new Color(color.R, color.G, color.B, 0.24f));
        DrawPolyline(leftEdge, new Color(color.R, color.G, color.B, 0.78f), 2, true);
        DrawPolyline(rightEdge, new Color(color.R, color.G, color.B, 0.78f), 2, true);
    }

    private void DrawJudgeDiamond(
        long tick,
        double lane,
        double width,
        float laneWidth,
        Color color)
    {
        float left = AxisWidth + ((float)lane * laneWidth);
        float right = AxisWidth + ((float)(lane + width) * laneWidth);
        float centerX = (left + right) / 2;
        float centerY = TickToY(tick);
        float halfWidth = Math.Clamp((right - left) * 0.24f, 5, 13);
        const float halfHeight = 5;
        Vector2[] diamond =
        {
            new(centerX, centerY - halfHeight),
            new(centerX + halfWidth, centerY),
            new(centerX, centerY + halfHeight),
            new(centerX - halfWidth, centerY),
        };
        DrawColoredPolygon(diamond, color);
        DrawPolyline(
            diamond.Append(diamond[0]).ToArray(),
            new Color(1, 1, 1, 0.82f),
            1,
            true);
    }

    private void DrawPoint(
        long tick,
        double lane,
        double width,
        float laneWidth,
        Color color,
        float halfHeight)
    {
        float x = AxisWidth + ((float)lane * laneWidth) + 1;
        float y = TickToY(tick);
        float noteWidth = Math.Max(2, ((float)width * laneWidth) - 2);
        DrawRect(new Rect2(x, y - halfHeight, noteWidth, halfHeight * 2), color);
    }

    private float TickToY(long tick) => _timing is null
        ? 0
        : TimeToY(_timing.TimingMap.GetAudioTimeSeconds(tick));

    private float TimeToY(double seconds) =>
        (Size.Y - 1)
        - (float)(((seconds - _viewStartSeconds) / VisibleDurationSeconds) * (Size.Y - 2));

    private double VisibleDurationSeconds => _visibleDurationSeconds;

    private void ZoomAt(float mouseY, bool zoomIn)
    {
        double ratioFromBottom = 1
            - Math.Clamp(mouseY / Math.Max(Size.Y, 1), 0, 1);
        double anchorSeconds = _viewStartSeconds
            + (ratioFromBottom * VisibleDurationSeconds);
        double minimumVisible = Math.Min(0.5, _durationSeconds);
        double maximumVisible = Math.Min(60, _durationSeconds);
        double factor = zoomIn ? 0.8 : 1.25;
        double newVisible = Math.Clamp(
            VisibleDurationSeconds * factor,
            minimumVisible,
            maximumVisible);

        _visibleDurationSeconds = newVisible;
        SetViewStart(anchorSeconds - (ratioFromBottom * newVisible));
    }

    private void SetViewStart(double seconds)
    {
        double maximum = Math.Max(0, _durationSeconds - VisibleDurationSeconds);
        _viewStartSeconds = Math.Clamp(seconds, 0, maximum);
        QueueRedraw();
    }

    private void EnsurePlaybackVisible()
    {
        double visibleEnd = _viewStartSeconds + VisibleDurationSeconds;
        if (_playbackSeconds < _viewStartSeconds || _playbackSeconds > visibleEnd)
        {
            SetViewStart(_playbackSeconds - (VisibleDurationSeconds * 0.2));
        }
    }

    private static string FormatTime(double seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }
}
