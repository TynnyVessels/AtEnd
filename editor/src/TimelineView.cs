using System;
using System.Collections.Generic;
using System.Linq;
using AtEnd.Core;
using Godot;

namespace AtEnd.Editor;

public enum TimelineTool
{
    Select,
    PlaceClick,
}

public partial class TimelineView : Control
{
    private const int LaneCount = 18;
    private const float AxisWidth = 86;
    private const double DefaultVisibleSeconds = 8;
    private TimingDefinition? _timing;
    private ChartDefinition? _chart;
    private double _durationSeconds = 1;
    private double _visibleDurationSeconds = 1;
    private double _playbackSeconds;
    private double _viewStartSeconds;
    private long? _selectedObjectId;
    private long? _dragObjectId;
    private bool _resizingClick;
    private bool _dragMoved;
    private Vector2 _dragStartPosition;
    private int _dragLaneOffset;
    private long _previewTick;
    private int _previewLane;
    private int _previewWidth;
    private bool _seekingByDrag;
    private bool _seekDragMoved;
    private int _displayedBeatSubdivision = 1;

    public event Action<double>? SeekRequested;
    public event Action<long?>? SelectionChanged;
    public event Action<long, int>? PlaceClickRequested;
    public event Action<long, long, int>? MoveClickRequested;
    public event Action<long, int>? ResizeClickRequested;
    public event Action<TimelineTool>? ToolChanged;

    public string? LoadedChartId => _chart?.ChartId;
    public TimelineTool Tool { get; private set; } = TimelineTool.Select;
    public long SnapTicks { get; set; } = 480;
    public long? SelectedObjectId => _selectedObjectId;

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

    public void SetChart(TimingDefinition timing, ChartDefinition chart, double durationSeconds,
        bool preserveViewport = false)
    {
        double previousVisible = _visibleDurationSeconds;
        double previousStart = _viewStartSeconds;
        double previousPlayback = _playbackSeconds;
        _timing = timing;
        _chart = chart;
        _durationSeconds = Math.Max(durationSeconds, 0.001);
        _visibleDurationSeconds = preserveViewport
            ? Math.Min(previousVisible, _durationSeconds)
            : Math.Min(DefaultVisibleSeconds, _durationSeconds);
        _playbackSeconds = preserveViewport ? Math.Clamp(previousPlayback, 0, _durationSeconds) : 0;
        _viewStartSeconds = preserveViewport
            ? Math.Clamp(previousStart, 0, Math.Max(0, _durationSeconds - _visibleDurationSeconds))
            : 0;
        _dragObjectId = null;
        if (_selectedObjectId is long id && !chart.Objects.Any(item => item.ObjectId == id))
        {
            SetSelectedObject(null);
        }
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
        SetSelectedObject(null);
        _dragObjectId = null;
        QueueRedraw();
    }

    public void SetSelectedObject(long? objectId)
    {
        if (_selectedObjectId == objectId)
        {
            return;
        }

        _selectedObjectId = objectId;
        SelectionChanged?.Invoke(objectId);
        QueueRedraw();
    }

    public void SetTool(TimelineTool tool)
    {
        if (Tool == tool)
        {
            return;
        }

        Tool = tool;
        ToolChanged?.Invoke(tool);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_chart is null || _timing is null)
        {
            return;
        }

        if (@event is InputEventMouseMotion motion && _dragObjectId is not null)
        {
            UpdateDrag(motion.Position);
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion seekMotion && _seekingByDrag)
        {
            _seekDragMoved |= seekMotion.Position.DistanceTo(_dragStartPosition) >= 3;
            if (_seekDragMoved)
            {
                SeekRequested?.Invoke(ToSeconds(seekMotion.Position.Y));
            }
            AcceptEvent();
            return;
        }

        if (@event is not InputEventMouseButton mouse)
        {
            return;
        }

        if (mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed && _dragObjectId is not null)
        {
            FinishDrag(mouse.Position);
            AcceptEvent();
            return;
        }

        if (mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed && _seekingByDrag)
        {
            if (_seekDragMoved)
            {
                SeekRequested?.Invoke(ToSeconds(mouse.Position.Y));
            }
            _seekingByDrag = false;
            _seekDragMoved = false;
            AcceptEvent();
            return;
        }

        if (!mouse.Pressed)
        {
            return;
        }

        if (mouse.ButtonIndex == MouseButton.Right)
        {
            SetTool(Tool == TimelineTool.Select
                ? TimelineTool.PlaceClick
                : TimelineTool.Select);
            AcceptEvent();
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

        if (mouse.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        ChartObjectDefinition? hit = HitTest(mouse.Position);
        if (hit is not null)
        {
            bool wasSelected = _selectedObjectId == hit.ObjectId;
            SetSelectedObject(hit.ObjectId);
            if (Tool == TimelineTool.Select && hit.Type == ChartObjectType.Click)
            {
                _dragObjectId = hit.ObjectId;
                _resizingClick = wasSelected && IsNearRightEdge(hit, mouse.Position.X);
                _dragMoved = false;
                _dragStartPosition = mouse.Position;
                _dragLaneOffset = Math.Clamp(ToLane(mouse.Position.X) - hit.StartPoint.Lane,
                    0, hit.StartPoint.Width - 1);
                _previewTick = hit.StartTick;
                _previewLane = hit.StartPoint.Lane;
                _previewWidth = hit.StartPoint.Width;
            }
            else if (Tool == TimelineTool.Select && hit.Type == ChartObjectType.Hold)
            {
                _seekingByDrag = true;
                _seekDragMoved = false;
                _dragStartPosition = mouse.Position;
            }

            AcceptEvent();
            return;
        }

        if (Tool == TimelineTool.PlaceClick && mouse.Position.X >= AxisWidth)
        {
            PlaceClickRequested?.Invoke(ToTick(mouse.Position.Y), ToLane(mouse.Position.X));
        }
        else
        {
            SetSelectedObject(null);
            SeekRequested?.Invoke(ToSeconds(mouse.Position.Y));
            _seekingByDrag = Tool == TimelineTool.Select;
            _seekDragMoved = false;
            _dragStartPosition = mouse.Position;
        }

        AcceptEvent();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("171719"));
        float contentWidth = Math.Max(1, Size.X - AxisWidth);
        float laneWidth = contentWidth / LaneCount;

        for (int lane = 0; lane <= LaneCount; lane++)
        {
            float x = AxisWidth + (lane * laneWidth);
            Color color = lane % 6 == 0 ? new Color("505054") : new Color("303034");
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), color, lane % 6 == 0 ? 2 : 1);
        }

        DrawTimeGrid();
        DrawMeasureGrid();
        if (_timing is not null && _chart is not null)
        {
            foreach (ChartObjectDefinition item in _chart.Objects)
            {
                ChartObjectDefinition displayed = item;
                if (_dragMoved && _dragObjectId == item.ObjectId)
                {
                    displayed = item with
                    {
                        StartTick = _previewTick,
                        EndTick = _previewTick,
                        Path = new[] { new LanePoint(_previewTick, _previewLane, _previewWidth) },
                    };
                }

                DrawObject(displayed, laneWidth);
                if (_selectedObjectId == item.ObjectId)
                {
                    DrawSelection(displayed, laneWidth);
                }
            }
        }

        float playheadY = TimeToY(_playbackSeconds);
        DrawLine(
            new Vector2(AxisWidth, playheadY),
            new Vector2(Size.X, playheadY),
            new Color("f0b976"),
            2);

        DrawString(
            ThemeDB.FallbackFont,
            new Vector2(6, Math.Clamp(playheadY - 5, 13, Size.Y - 3)),
            FormatTime(_playbackSeconds),
            HorizontalAlignment.Left,
            AxisWidth - 10,
            11,
            new Color("f0c89a"));
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
            DrawLine(new Vector2(AxisWidth, y), new Vector2(Size.X, y), new Color("3a3a3e"), 1);
            DrawString(
                ThemeDB.FallbackFont,
                new Vector2(2, Math.Clamp(y + 4, 13, Size.Y - 3)),
                FormatTime(second),
                HorizontalAlignment.Right,
                34,
            10,
                new Color("929296"));
        }
    }

    private void DrawMeasureGrid()
    {
        if (_timing is null || Size.Y <= 0)
        {
            return;
        }

        const float measureRailX = 42;
        DrawLine(new Vector2(39, 0), new Vector2(39, Size.Y), new Color("66563d"), 1);
        long firstVisibleTick = Math.Max(0, RawTick(Size.Y));
        long lastVisibleTick = Math.Max(firstVisibleTick, RawTick(0));
        long lastTick = Math.Max(lastVisibleTick, (long)Math.Ceiling(
            _timing.TimingMap.GetTickAtAudioTimeSeconds(_durationSeconds)));
        IReadOnlyList<TimeSignatureChange> signatures = _timing.TimeSignatures;

        int numerator = 4;
        int denominator = 4;
        int signatureIndex = 0;
        while (signatureIndex < signatures.Count && signatures[signatureIndex].Tick == 0)
        {
            numerator = signatures[signatureIndex].Numerator;
            denominator = signatures[signatureIndex].Denominator;
            signatureIndex++;
        }

        long tick = 0;
        int measureNumber = 1;
        int visibleSubdivision = 1;
        int guard = 0;
        while (tick <= lastTick && guard++ < 100000)
        {
            if (signatureIndex < signatures.Count && signatures[signatureIndex].Tick == tick)
            {
                numerator = signatures[signatureIndex].Numerator;
                denominator = signatures[signatureIndex].Denominator;
                signatureIndex++;
            }

            long nextSignatureTick = signatureIndex < signatures.Count
                ? signatures[signatureIndex].Tick : long.MaxValue;
            double beatTicks = TimingMap.PulsesPerQuarterNote * 4d / denominator;
            double measureTicks = beatTicks * numerator;
            long measureEnd = tick + (long)Math.Round(measureTicks);
            if (nextSignatureTick > tick && nextSignatureTick < measureEnd)
            {
                measureEnd = nextSignatureTick;
            }

            double startSeconds = _timing.TimingMap.GetAudioTimeSeconds(tick);
            double endSeconds = _timing.TimingMap.GetAudioTimeSeconds(measureEnd);
            float measurePixels = Math.Abs(TimeToY(endSeconds) - TimeToY(startSeconds));
            int measureStride = 1;
            while (measurePixels / measureStride < 30 && measureStride < 64)
            {
                measureStride *= 2;
            }

            int subdivisions = 1;
            float beatPixels = measurePixels / Math.Max(1, numerator);
            while (beatPixels / subdivisions > 34 && subdivisions < 16)
            {
                subdivisions *= 2;
            }

            if (tick >= firstVisibleTick && tick <= lastVisibleTick
                && measureNumber % measureStride == 1 % measureStride)
            {
                float y = TimeToY(startSeconds);
                DrawLine(new Vector2(AxisWidth, y), new Vector2(Size.X, y), new Color("76603d"), 1.5f);
                DrawString(ThemeDB.FallbackFont, new Vector2(measureRailX, Math.Clamp(y + 4, 13, Size.Y - 3)),
                    measureNumber.ToString(), HorizontalAlignment.Left, AxisWidth - measureRailX - 2,
                    10, new Color("e0b875"));
            }

            for (int subdivisionTick = 1; subdivisionTick < numerator * subdivisions; subdivisionTick++)
            {
                long gridTick = tick + (long)Math.Round(beatTicks * subdivisionTick / subdivisions);
                if (gridTick >= measureEnd || gridTick < firstVisibleTick || gridTick > lastVisibleTick)
                {
                    continue;
                }

                float y = TimeToY(_timing.TimingMap.GetAudioTimeSeconds(gridTick));
                bool beatLine = subdivisionTick % subdivisions == 0;
                Color color = beatLine ? new Color("504639") : new Color("39332a");
                DrawLine(new Vector2(AxisWidth, y), new Vector2(Size.X, y), color, 1);
            }

            if (measurePixels > 0 && TimeToY(startSeconds) >= 0 && TimeToY(startSeconds) <= Size.Y)
            {
                visibleSubdivision = subdivisions;
            }

            tick = measureEnd;
            measureNumber++;
        }

        _displayedBeatSubdivision = visibleSubdivision;
        DrawString(ThemeDB.FallbackFont, new Vector2(measureRailX, 13),
            $"1/{_displayedBeatSubdivision}", HorizontalAlignment.Left,
            AxisWidth - measureRailX - 2, 9, new Color("b99a6b"));
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

    private void DrawSelection(ChartObjectDefinition item, float laneWidth)
    {
        Color outline = new("ffce74");
        if (item.Type == ChartObjectType.Click)
        {
            float x = AxisWidth + (item.StartPoint.Lane * laneWidth);
            float y = TickToY(item.StartTick);
            float width = item.StartPoint.Width * laneWidth;
            DrawRect(new Rect2(x, y - 7, width, 14), outline, false, 2);
            DrawRect(new Rect2(x + width - 3, y - 4, 3, 8), outline);
            return;
        }

        Vector2[] left = item.Path.Select(point => new Vector2(
            AxisWidth + (point.Lane * laneWidth), TickToY(point.Tick))).ToArray();
        Vector2[] right = item.Path.Select(point => new Vector2(
            AxisWidth + ((point.Lane + point.Width) * laneWidth), TickToY(point.Tick))).ToArray();
        DrawPolyline(left, outline, 2, true);
        DrawPolyline(right, outline, 2, true);
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

    private ChartObjectDefinition? HitTest(Vector2 position)
    {
        if (_chart is null || _timing is null || position.X < AxisWidth)
        {
            return null;
        }

        float laneWidth = Math.Max(1, Size.X - AxisWidth) / LaneCount;
        foreach (ChartObjectDefinition item in _chart.Objects.Reverse())
        {
            if (item.Type != ChartObjectType.Click
                || Math.Abs(position.Y - TickToY(item.StartTick)) > 8)
            {
                continue;
            }

            float left = AxisWidth + (item.StartPoint.Lane * laneWidth);
            float right = left + (item.StartPoint.Width * laneWidth);
            if (position.X >= left - 2 && position.X <= right + 2)
            {
                return item;
            }
        }

        foreach (ChartObjectDefinition item in _chart.Objects.Reverse())
        {
            if (item.Type != ChartObjectType.Hold)
            {
                continue;
            }

            float startY = TickToY(item.StartTick);
            float endY = TickToY(item.EndTick);
            if (position.Y < Math.Min(startY, endY) - 3
                || position.Y > Math.Max(startY, endY) + 3)
            {
                continue;
            }

            long tick = Math.Clamp(RawTick(position.Y), item.StartTick, item.EndTick);
            (double lane, double width) = item.GetLaneGeometryAtTick(tick);
            float left = AxisWidth + ((float)lane * laneWidth);
            float right = left + ((float)width * laneWidth);
            if (position.X >= left - 2 && position.X <= right + 2)
            {
                return item;
            }
        }

        return null;
    }

    private bool IsNearRightEdge(ChartObjectDefinition item, float mouseX)
    {
        float laneWidth = Math.Max(1, Size.X - AxisWidth) / LaneCount;
        float right = AxisWidth + ((item.StartPoint.Lane + item.StartPoint.Width) * laneWidth);
        return Math.Abs(mouseX - right) <= 7;
    }

    private void UpdateDrag(Vector2 position)
    {
        if (_chart is null || _dragObjectId is not long id)
        {
            return;
        }

        ChartObjectDefinition? item = _chart.Objects.FirstOrDefault(value => value.ObjectId == id);
        if (item is null)
        {
            _dragObjectId = null;
            return;
        }

        _dragMoved |= position.DistanceTo(_dragStartPosition) >= 3;
        if (!_dragMoved)
        {
            return;
        }

        if (_resizingClick)
        {
            float laneWidth = Math.Max(1, Size.X - AxisWidth) / LaneCount;
            float left = AxisWidth + (item.StartPoint.Lane * laneWidth);
            _previewWidth = Math.Clamp((int)Math.Ceiling((position.X - left) / laneWidth),
                1, LaneCount - item.StartPoint.Lane);
        }
        else
        {
            _previewTick = ToTick(position.Y);
            _previewLane = Math.Clamp(ToLane(position.X) - _dragLaneOffset,
                0, LaneCount - item.StartPoint.Width);
        }

        QueueRedraw();
    }

    private void FinishDrag(Vector2 position)
    {
        UpdateDrag(position);
        long? id = _dragObjectId;
        bool changed = _dragMoved;
        bool resizing = _resizingClick;
        long tick = _previewTick;
        int lane = _previewLane;
        int width = _previewWidth;
        _dragObjectId = null;
        _dragMoved = false;
        QueueRedraw();
        if (changed && id is long objectId)
        {
            if (resizing)
            {
                ResizeClickRequested?.Invoke(objectId, width);
            }
            else
            {
                MoveClickRequested?.Invoke(objectId, tick, lane);
            }
        }
    }

    private double ToSeconds(float y) => Math.Clamp(_viewStartSeconds
        + ((1 - Math.Clamp(y / Math.Max(Size.Y, 1), 0, 1)) * VisibleDurationSeconds),
        0, _durationSeconds);

    private long RawTick(float y) => Math.Max(0,
        (long)Math.Round(_timing!.TimingMap.GetTickAtAudioTimeSeconds(ToSeconds(y))));

    private long ToTick(float y)
    {
        long raw = RawTick(y);
        return SnapTicks <= 0 ? raw : Math.Max(0,
            (long)Math.Round(raw / (double)SnapTicks) * SnapTicks);
    }

    private int ToLane(float x)
    {
        float laneWidth = Math.Max(1, Size.X - AxisWidth) / LaneCount;
        return Math.Clamp((int)Math.Floor((x - AxisWidth) / laneWidth), 0, LaneCount - 1);
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
