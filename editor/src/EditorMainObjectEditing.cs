using System;
using System.IO;
using System.Linq;
using AtEnd.Core;
using Godot;

namespace AtEnd.Editor;

public partial class EditorMain
{
    private Button? _selectToolButton;
    private Button? _placeToolButton;
    private Button? _placeHoldToolButton;
    private OptionButton? _snapSelector;
    private OptionButton? _inputTypeSelector;
    private OptionButton? _colorSelector;
    private Label? _objectSummary;
    private SpinBox? _tickEdit;
    private SpinBox? _endTickEdit;
    private SpinBox? _laneEdit;
    private SpinBox? _widthEdit;
    private OptionButton? _pathPointSelector;
    private OptionButton? _judgeTickSelector;
    private SpinBox? _judgeTickEdit;
    private Button? _addPathPointButton;
    private Button? _removePathPointButton;
    private Button? _addJudgeTickButton;
    private Button? _removeJudgeTickButton;
    private Button? _applyJudgeTickButton;
    private VBoxContainer? _holdControls;
    private Label? _tickCaption;
    private bool _refreshingHoldControls;
    private Button? _applyObjectButton;
    private Button? _deleteObjectButton;

    private void InitializeObjectEditing()
    {
        BuildObjectTab();
        _timeline!.SelectionChanged += OnObjectSelected;
        _timeline.ToolChanged += OnTimelineToolChanged;
        _timeline.PlaceClickRequested += PlaceClick;
        _timeline.MoveClickRequested += MoveClick;
        _timeline.ResizeClickRequested += ResizeClick;
        _timeline.PlaceHoldRequested += PlaceHold;
        _timeline.HoldEditedRequested += ApplyHoldEdit;
        _timeline.AddHoldJudgeRequested += AddHoldJudgeTick;
        RefreshObjectControls();
    }

    private void BuildObjectTab()
    {
        var panel = new MarginContainer { Name = "Objects" };
        foreach (string side in new[] { "left", "top", "right", "bottom" })
        {
            panel.AddThemeConstantOverride($"margin_{side}", 7);
        }

        _workspace!.AddChild(panel);
        _workspace.SetTabTitle(_workspace.GetTabCount() - 1, "物件");
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 4);
        panel.AddChild(layout);

        var toolRow = AddRow(layout);
        _selectToolButton = new Button { Text = "选择", ToggleMode = true,
            ButtonPressed = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _placeToolButton = new Button { Text = "放置 Click", ToggleMode = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _placeHoldToolButton = new Button { Text = "放置 Hold", ToggleMode = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _selectToolButton.Pressed += () => SetTimelineTool(TimelineTool.Select);
        _placeToolButton.Pressed += () => SetTimelineTool(TimelineTool.PlaceClick);
        _placeHoldToolButton.Pressed += () => SetTimelineTool(TimelineTool.PlaceHold);
        toolRow.AddChild(_selectToolButton);
        toolRow.AddChild(_placeToolButton);
        toolRow.AddChild(_placeHoldToolButton);

        var snapRow = AddRow(layout);
        AddCaption(snapRow, "吸附");
        _snapSelector = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _snapSelector.AddItem("每拍 1/4", 480);
        _snapSelector.AddItem("每拍 1/8", 240);
        _snapSelector.AddItem("每拍 1/16", 120);
        _snapSelector.AddItem("每拍 1/32", 60);
        _snapSelector.AddItem("每拍 1/64", 30);
        _snapSelector.AddItem("每拍 1/128", 15);
        _snapSelector.AddItem("每拍 1/2", 960);
        _snapSelector.AddItem("每拍 1/1", 1920);
        _snapSelector.AddItem("自由", 0);
        _snapSelector.Select(0);
        _snapSelector.ItemSelected += index => _timeline!.SnapTicks = _snapSelector.GetItemId((int)index);
        snapRow.AddChild(_snapSelector);

        var playheadRow = AddRow(layout);
        AddCaption(playheadRow, "选择线");
        var playheadSnap = new CheckButton { Text = "吸附", ButtonPressed = false, TooltipText = "选择线定位时按当前吸附刻度对齐" };
        playheadSnap.Toggled += pressed => _timeline!.SnapPlayheadToGrid = pressed;
        playheadRow.AddChild(playheadSnap);
        var playheadInfo = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        playheadInfo.AddItem("Tick", 0);
        playheadInfo.AddItem("小节·拍", 1);
        playheadInfo.AddItem("拍内细分", 2);
        playheadInfo.AddItem("小节·拍·细分", 3);
        playheadInfo.Select(0);
        playheadInfo.ItemSelected += index => _timeline!.PlayheadInfoMode = (int)index;
        playheadRow.AddChild(playheadInfo);

        _objectSummary = AddLabel(layout, "未选中物件", "aaaaad");

        var tickRow = AddRow(layout);
        _tickCaption = AddCaption(tickRow, "Tick");
        _tickEdit = AddIntegerSpin(tickRow, 0, 1000000000);

        var endTickRow = AddRow(layout);
        AddCaption(endTickRow, "结束");
        _endTickEdit = AddIntegerSpin(endTickRow, 1, 1000000000);
        endTickRow.Visible = false;
        _holdControls = new VBoxContainer();
        _holdControls.Visible = false;
        layout.AddChild(_holdControls);

        var pathRow = AddRow(_holdControls);
        AddCaption(pathRow, "路径点");
        _pathPointSelector = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _pathPointSelector.ItemSelected += OnPathPointSelected;
        pathRow.AddChild(_pathPointSelector);
        _addPathPointButton = new Button { Text = "+点", TooltipText = "在 Tick、轨道和宽度位置插入路径点" };
        _removePathPointButton = new Button { Text = "−点", TooltipText = "删除所选中间路径点" };
        _addPathPointButton.Pressed += AddHoldPathPoint;
        _removePathPointButton.Pressed += RemoveHoldPathPoint;
        pathRow.AddChild(_addPathPointButton);
        pathRow.AddChild(_removePathPointButton);

        var judgeRow = AddRow(_holdControls);
        AddCaption(judgeRow, "判定点");
        _judgeTickSelector = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _judgeTickSelector.ItemSelected += OnJudgeTickSelected;
        judgeRow.AddChild(_judgeTickSelector);
        _judgeTickEdit = AddIntegerSpin(judgeRow, 0, 1000000000);
        _addJudgeTickButton = new Button { Text = "+", TooltipText = "添加判定点" };
        _removeJudgeTickButton = new Button { Text = "−", TooltipText = "删除所选判定点" };
        _applyJudgeTickButton = new Button { Text = "✓", TooltipText = "应用判定点时间" };
        _applyJudgeTickButton.Pressed += ApplyHoldJudgeTickFromControls;
        _addJudgeTickButton.Pressed += AddHoldJudgeTickFromControls;
        _removeJudgeTickButton.Pressed += RemoveHoldJudgeTickFromControls;
        judgeRow.AddChild(_applyJudgeTickButton);
        judgeRow.AddChild(_addJudgeTickButton);
        judgeRow.AddChild(_removeJudgeTickButton);

        var geometryRow = AddRow(layout);
        AddCaption(geometryRow, "轨道");
        _laneEdit = AddIntegerSpin(geometryRow, 0, 17);
        AddCaption(geometryRow, "宽");
        _widthEdit = AddIntegerSpin(geometryRow, 1, 18);
        _widthEdit.Value = 1;
        _widthEdit.ValueChanged += value => _timeline!.PlacementWidth = (int)Math.Round(value);

        var inputRow = AddRow(layout);
        AddCaption(inputRow, "输入");
        _inputTypeSelector = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _inputTypeSelector.AddItem("Rel", (int)InputCategory.Rel);
        _inputTypeSelector.AddItem("Drm", (int)InputCategory.Drm);
        _inputTypeSelector.ItemSelected += _ => RefreshColorAvailability();
        inputRow.AddChild(_inputTypeSelector);
        _colorSelector = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _colorSelector.AddItem("红", (int)DrmColor.Red);
        _colorSelector.AddItem("绿", (int)DrmColor.Green);
        _colorSelector.AddItem("蓝", (int)DrmColor.Blue);
        inputRow.AddChild(_colorSelector);

        var actionRow = AddRow(layout);
        _applyObjectButton = new Button { Text = "应用", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _deleteObjectButton = new Button { Text = "删除", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _applyObjectButton.Pressed += ApplySelectedObject;
        _deleteObjectButton.Pressed += DeleteSelectedClick;
        actionRow.AddChild(_applyObjectButton);
        actionRow.AddChild(_deleteObjectButton);
    }

    private static HBoxContainer AddRow(Container parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        parent.AddChild(row);
        return row;
    }

    private static Label AddCaption(Container parent, string caption)
    {
        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(32, 0) };
        label.AddThemeColorOverride("font_color", new Color("aaaaad"));
        parent.AddChild(label);
        return label;
    }

    private static SpinBox AddIntegerSpin(Container parent, int minimum, int maximum)
    {
        var spin = new SpinBox
        {
            MinValue = minimum,
            MaxValue = maximum,
            Step = 1,
            Rounded = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        parent.AddChild(spin);
        return spin;
    }

    private void SetTimelineTool(TimelineTool tool)
    {
        _timeline!.SetTool(tool);
    }

    private void OnTimelineToolChanged(TimelineTool tool)
    {
        _selectToolButton!.ButtonPressed = tool == TimelineTool.Select;
        _placeToolButton!.ButtonPressed = tool == TimelineTool.PlaceClick;
        _placeHoldToolButton!.ButtonPressed = tool == TimelineTool.PlaceHold;
    }

    private void OnObjectSelected(long? id)
    {
        if (id is not null)
        {
            _workspace!.CurrentTab = 2;
        }

        RefreshObjectControls();
    }

    private ChartObjectDefinition? GetSelectedObject()
    {
        long? id = _timeline?.SelectedObjectId;
        return id is null ? null
            : _document?.CurrentChart.Objects.FirstOrDefault(item => item.ObjectId == id);
    }

    private void RefreshObjectControls()
    {
        if (_objectSummary is null)
        {
            return;
        }

        ChartObjectDefinition? item = GetSelectedObject();
        bool click = item?.Type == ChartObjectType.Click;
        bool hold = item?.Type == ChartObjectType.Hold;
        _objectSummary.Text = item is null ? "未选中物件"
            : item.Type == ChartObjectType.Click
                ? $"Click  ·  ID {item.ObjectId}"
                : $"Hold  ·  ID {item.ObjectId}  ·  {item.StartTick}–{item.EndTick}";
        _tickEdit!.Editable = click || hold;
        _endTickEdit!.Editable = hold;
        _endTickEdit.GetParent<Control>().Visible = hold;
        _laneEdit!.Editable = click || hold;
        _widthEdit!.Editable = true;
        _inputTypeSelector!.Disabled = false;
        _applyObjectButton!.Disabled = item is null;
        _deleteObjectButton!.Disabled = item is null;
        _holdControls!.Visible = hold;
        _tickCaption!.Text = hold ? "点 Tick" : "Tick";

        if (item is not null)
        {
            _tickEdit.Value = item.StartTick;
            _endTickEdit.Value = item.EndTick;
            _laneEdit.Value = item.StartPoint.Lane;
            _widthEdit.Value = item.StartPoint.Width;
            _inputTypeSelector!.Select((int)item.InputType);
            _colorSelector!.Select((int)(item.Color ?? DrmColor.Red));
        }

        if (hold)
        {
            RefreshHoldSelectors(item!);
        }

        RefreshColorAvailability();
    }

    private void RefreshHoldSelectors(ChartObjectDefinition hold)
    {
        _refreshingHoldControls = true;
        int previousPathIndex = _pathPointSelector!.Selected >= 0
            ? (int)_pathPointSelector.GetSelectedId()
            : 0;
        int previousJudgeIndex = _judgeTickSelector!.Selected >= 0
            ? (int)_judgeTickSelector.GetSelectedId()
            : 0;
        _pathPointSelector!.Clear();
        for (int index = 0; index < hold.Path.Count; index++)
        {
            string name = index == 0 ? "起点"
                : index == hold.Path.Count - 1 ? "终点" : $"点 {index + 1}";
            _pathPointSelector.AddItem(name, index);
        }

        int selectedPathIndex = Math.Clamp(previousPathIndex, 0, hold.Path.Count - 1);
        _pathPointSelector.Select(selectedPathIndex);
        LanePoint point = hold.Path[selectedPathIndex];
        _tickEdit!.Value = point.Tick;
        _laneEdit!.Value = point.Lane;
        _widthEdit!.Value = point.Width;
        _removePathPointButton!.Disabled = selectedPathIndex == 0
            || selectedPathIndex == hold.Path.Count - 1;

        _judgeTickSelector!.Clear();
        for (int index = 0; index < hold.JudgeTicks.Count; index++)
        {
            _judgeTickSelector.AddItem($"判定 {index + 1}", index);
        }

        bool hasJudgeTicks = hold.JudgeTicks.Count > 0;
        _judgeTickSelector.Disabled = !hasJudgeTicks;
        _judgeTickEdit!.Editable = true;
        _removeJudgeTickButton!.Disabled = !hasJudgeTicks;
        _applyJudgeTickButton!.Disabled = !hasJudgeTicks;
        if (hasJudgeTicks)
        {
            _judgeTickSelector.Select(Math.Clamp(previousJudgeIndex, 0, hold.JudgeTicks.Count - 1));
        }
        _judgeTickEdit.Value = hasJudgeTicks ? hold.JudgeTicks[0] : (hold.StartTick + hold.EndTick) / 2;
        _refreshingHoldControls = false;
    }

    private void OnPathPointSelected(long index)
    {
        if (_refreshingHoldControls || GetSelectedObject() is not { Type: ChartObjectType.Hold } hold)
        {
            return;
        }

        int pointIndex = (int)index;
        if (pointIndex < 0 || pointIndex >= hold.Path.Count)
        {
            return;
        }

        LanePoint point = hold.Path[pointIndex];
        _tickEdit!.Value = point.Tick;
        _laneEdit!.Value = point.Lane;
        _widthEdit!.Value = point.Width;
        _removePathPointButton!.Disabled = pointIndex == 0 || pointIndex == hold.Path.Count - 1;
    }

    private void OnJudgeTickSelected(long index)
    {
        if (_refreshingHoldControls || GetSelectedObject() is not { Type: ChartObjectType.Hold } hold)
        {
            return;
        }

        int judgeIndex = (int)index;
        if (judgeIndex >= 0 && judgeIndex < hold.JudgeTicks.Count)
        {
            _judgeTickEdit!.Value = hold.JudgeTicks[judgeIndex];
        }
    }

    private void RefreshColorAvailability()
    {
        if (_inputTypeSelector is null || _colorSelector is null)
        {
            return;
        }

        _colorSelector.Disabled = _inputTypeSelector.Selected != (int)InputCategory.Drm;
    }

    private void PlaceClick(long tick, int lane)
    {
        if (_document is null || _widthEdit is null)
        {
            return;
        }

        int width = Math.Clamp((int)_widthEdit.Value, 1, 18 - lane);
        InputCategory input = (InputCategory)_inputTypeSelector!.Selected;
        DrmColor? color = input == InputCategory.Drm
            ? (DrmColor)_colorSelector!.Selected : null;
        long id;
        try
        {
            id = _document.AllocateObjectId();
        }
        catch (InvalidDataException exception)
        {
            SetStatus($"无法分配物件编号：{LocalizeError(exception)}", isError: true);
            return;
        }

        var item = new ChartObjectDefinition(id, ChartObjectType.Click, input, color,
            tick, tick, new[] { new LanePoint(tick, lane, width) }, Array.Empty<long>());
        if (ExecuteObjectCommand(new AddChartObjectCommand(item)))
        {
            _timeline!.SetSelectedObject(id);
        }
    }

    private void PlaceHold(long startTick, long endTick, int lane)
    {
        if (_document is null)
        {
            return;
        }

        if (endTick <= startTick)
        {
            SetStatus("Hold 的结束位置必须晚于起点。", isError: true);
            return;
        }

        int width = Math.Clamp((int)Math.Round(_widthEdit!.Value), 1, 18 - lane);
        InputCategory input = (InputCategory)_inputTypeSelector!.Selected;
        DrmColor? color = input == InputCategory.Drm
            ? (DrmColor)_colorSelector!.Selected : null;
        long id;
        try
        {
            id = _document.AllocateObjectId();
        }
        catch (InvalidDataException exception)
        {
            SetStatus($"无法分配物件编号：{LocalizeError(exception)}", isError: true);
            return;
        }

        var hold = new ChartObjectDefinition(
            id,
            ChartObjectType.Hold,
            input,
            color,
            startTick,
            endTick,
            Array.AsReadOnly(new[]
            {
                new LanePoint(startTick, lane, width),
                new LanePoint(endTick, lane, width),
            }),
            Array.Empty<long>());
        if (ExecuteObjectCommand(new AddChartObjectCommand(hold)))
        {
            _timeline!.SetSelectedObject(id);
        }
    }

    private void MoveClick(long id, long tick, int lane)
    {
        ChartObjectDefinition? item = _document?.CurrentChart.Objects
            .FirstOrDefault(value => value.ObjectId == id && value.Type == ChartObjectType.Click);
        if (item is null)
        {
            return;
        }

        lane = Math.Clamp(lane, 0, 18 - item.StartPoint.Width);
        ExecuteObjectCommand(new ReplaceChartObjectCommand(item with
        {
            StartTick = tick,
            EndTick = tick,
            Path = new[] { new LanePoint(tick, lane, item.StartPoint.Width) },
        }));
    }

    private void ResizeClick(long id, int width)
    {
        ChartObjectDefinition? item = _document?.CurrentChart.Objects
            .FirstOrDefault(value => value.ObjectId == id && value.Type == ChartObjectType.Click);
        if (item is null)
        {
            return;
        }

        width = Math.Clamp(width, 1, 18 - item.StartPoint.Lane);
        ExecuteObjectCommand(new ReplaceChartObjectCommand(item with
        {
            Path = new[] { new LanePoint(item.StartTick, item.StartPoint.Lane, width) },
        }));
    }

    private void ApplySelectedObject()
    {
        ChartObjectDefinition? item = GetSelectedObject();
        if (item is null)
        {
            return;
        }

        long tick = (long)Math.Round(_tickEdit!.Value);
        long snap = _timeline!.SnapTicks;
        if (snap > 0)
        {
            tick = (long)Math.Round(tick / (double)snap) * snap;
        }

        int lane = (int)Math.Round(_laneEdit!.Value);
        int width = (int)Math.Round(_widthEdit!.Value);
        InputCategory input = (InputCategory)_inputTypeSelector!.Selected;
        DrmColor? color = input == InputCategory.Drm
            ? (DrmColor)_colorSelector!.Selected : null;
        if (item.Type == ChartObjectType.Click)
        {
            ExecuteObjectCommand(new ReplaceChartObjectCommand(item with
            {
                InputType = input,
                Color = color,
                StartTick = tick,
                EndTick = tick,
                Path = new[] { new LanePoint(tick, lane, width) },
            }));
            return;
        }

        int pointIndex = (int)_pathPointSelector!.GetSelectedId();
        LanePoint[] path = item.Path.ToArray();
        long endTick = SnapTick((long)Math.Round(_endTickEdit!.Value));
        long editedTick = pointIndex == path.Length - 1
            ? endTick
            : SnapTick(tick);
        path[pointIndex] = new LanePoint(editedTick, lane, width);
        LanePoint last = path[^1];
        path[^1] = last with { Tick = endTick };
        ExecuteObjectCommand(new ReplaceChartObjectCommand(item with
        {
            InputType = input,
            Color = color,
            StartTick = path[0].Tick,
            EndTick = path[^1].Tick,
            Path = Array.AsReadOnly(path),
        }));
    }

    private void DeleteSelectedClick()
    {
        ChartObjectDefinition? item = GetSelectedObject();
        if (item is not null)
        {
            ExecuteObjectCommand(new RemoveChartObjectCommand(item.ObjectId));
        }
    }

    private void AddHoldPathPoint()
    {
        if (GetSelectedObject() is not { Type: ChartObjectType.Hold } hold)
        {
            return;
        }

        long tick = _timeline!.CurrentTick;
        if (tick <= hold.StartTick || tick >= hold.EndTick)
        {
            SetStatus("当前选择线 Tick 必须位于 Hold 起点和终点之间。", isError: true);
            return;
        }

        int existingIndex = Array.FindIndex(hold.Path.ToArray(), point => point.Tick == tick);
        if (existingIndex >= 0)
        {
            _pathPointSelector!.Select(existingIndex);
            OnPathPointSelected(existingIndex);
            return;
        }

        int lane = (int)Math.Round(_laneEdit!.Value);
        int width = (int)Math.Round(_widthEdit!.Value);
        int insertAt = 1;
        while (insertAt < hold.Path.Count && hold.Path[insertAt].Tick < tick)
        {
            insertAt++;
        }

        LanePoint[] path = hold.Path.ToArray();
        path = path.Take(insertAt)
            .Append(new LanePoint(tick, lane, width))
            .Concat(path.Skip(insertAt))
            .ToArray();
        ExecuteObjectCommand(new ReplaceChartObjectCommand(hold with
        {
            Path = Array.AsReadOnly(path),
        }));
    }

    private void RemoveHoldPathPoint()
    {
        if (GetSelectedObject() is not { Type: ChartObjectType.Hold } hold)
        {
            return;
        }

        int pointIndex = (int)_pathPointSelector!.GetSelectedId();
        if (pointIndex <= 0 || pointIndex >= hold.Path.Count - 1)
        {
            return;
        }

        LanePoint[] path = hold.Path.Where((_, index) => index != pointIndex).ToArray();
        ExecuteObjectCommand(new ReplaceChartObjectCommand(hold with
        {
            Path = Array.AsReadOnly(path),
        }));
    }

    private void AddHoldJudgeTickFromControls()
    {
        if (GetSelectedObject() is { Type: ChartObjectType.Hold } hold)
        {
            AddHoldJudgeTick(hold.ObjectId, _timeline!.CurrentTick);
        }
    }

    private void ApplyHoldJudgeTickFromControls()
    {
        if (GetSelectedObject() is not { Type: ChartObjectType.Hold } hold)
        {
            return;
        }

        int judgeIndex = (int)_judgeTickSelector!.GetSelectedId();
        if (judgeIndex < 0 || judgeIndex >= hold.JudgeTicks.Count)
        {
            return;
        }

        long[] judges = hold.JudgeTicks.ToArray();
        judges[judgeIndex] = SnapTick((long)Math.Round(_judgeTickEdit!.Value));
        ExecuteObjectCommand(new ReplaceChartObjectCommand(hold with
        {
            JudgeTicks = Array.AsReadOnly(judges),
        }));
    }

    private void AddHoldJudgeTick(long objectId, long tick)
    {
        ChartObjectDefinition? hold = _document?.CurrentChart.Objects
            .FirstOrDefault(item => item.ObjectId == objectId && item.Type == ChartObjectType.Hold);
        if (hold is null)
        {
            return;
        }

        long[] judges = hold.JudgeTicks.Append(tick).Distinct().OrderBy(value => value).ToArray();
        ExecuteObjectCommand(new ReplaceChartObjectCommand(hold with
        {
            JudgeTicks = Array.AsReadOnly(judges),
        }));
    }

    private void RemoveHoldJudgeTickFromControls()
    {
        if (GetSelectedObject() is not { Type: ChartObjectType.Hold } hold)
        {
            return;
        }

        int judgeIndex = (int)_judgeTickSelector!.GetSelectedId();
        if (judgeIndex < 0 || judgeIndex >= hold.JudgeTicks.Count)
        {
            return;
        }

        long[] judges = hold.JudgeTicks.Where((_, index) => index != judgeIndex).ToArray();
        ExecuteObjectCommand(new ReplaceChartObjectCommand(hold with
        {
            JudgeTicks = Array.AsReadOnly(judges),
        }));
    }

    private void ApplyHoldEdit(ChartObjectDefinition updated)
    {
        if (updated.Type == ChartObjectType.Hold)
        {
            ExecuteObjectCommand(new ReplaceChartObjectCommand(updated));
        }
    }

    private void MoveSelectedHold(long tickDelta, int laneDelta)
    {
        if (GetSelectedObject() is not { Type: ChartObjectType.Hold } hold)
        {
            return;
        }

        tickDelta = Math.Max(-hold.StartTick, tickDelta);
        int minimumLaneDelta = -hold.Path.Min(point => point.Lane);
        int maximumLaneDelta = 18 - hold.Path.Max(point => point.Lane + point.Width);
        laneDelta = Math.Clamp(laneDelta, minimumLaneDelta, maximumLaneDelta);
        LanePoint[] path = hold.Path.Select(point => point with
        {
            Tick = point.Tick + tickDelta,
            Lane = point.Lane + laneDelta,
        }).ToArray();
        long[] judges = hold.JudgeTicks.Select(tick => tick + tickDelta).ToArray();
        ApplyHoldEdit(hold with
        {
            StartTick = hold.StartTick + tickDelta,
            EndTick = hold.EndTick + tickDelta,
            Path = Array.AsReadOnly(path),
            JudgeTicks = Array.AsReadOnly(judges),
        });
    }

    private long SnapTick(long tick)
    {
        tick = Math.Max(0, tick);
        long snap = _timeline?.SnapTicks ?? 0;
        return snap <= 0 ? tick : Math.Max(0, (long)Math.Round(tick / (double)snap) * snap);
    }

    private bool ExecuteObjectCommand(IChartEditCommand command)
    {
        if (_document is null)
        {
            return false;
        }

        try
        {
            _document.Execute(command);
            UseDocumentChart();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            SetStatus($"编辑失败：{LocalizeError(exception)}", isError: true);
            return false;
        }
    }
}
