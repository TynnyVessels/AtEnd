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
    private OptionButton? _snapSelector;
    private OptionButton? _inputTypeSelector;
    private OptionButton? _colorSelector;
    private Label? _objectSummary;
    private SpinBox? _tickEdit;
    private SpinBox? _laneEdit;
    private SpinBox? _widthEdit;
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
        _selectToolButton.Pressed += () => SetTimelineTool(TimelineTool.Select);
        _placeToolButton.Pressed += () => SetTimelineTool(TimelineTool.PlaceClick);
        toolRow.AddChild(_selectToolButton);
        toolRow.AddChild(_placeToolButton);

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

        _objectSummary = AddLabel(layout, "未选中物件", "aaaaad");

        var tickRow = AddRow(layout);
        AddCaption(tickRow, "Tick");
        _tickEdit = AddIntegerSpin(tickRow, 0, 1000000000);

        var geometryRow = AddRow(layout);
        AddCaption(geometryRow, "轨道");
        _laneEdit = AddIntegerSpin(geometryRow, 0, 17);
        AddCaption(geometryRow, "宽");
        _widthEdit = AddIntegerSpin(geometryRow, 1, 18);
        _widthEdit.Value = 1;

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
        _applyObjectButton.Pressed += ApplySelectedClick;
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

    private static void AddCaption(Container parent, string caption)
    {
        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(32, 0) };
        label.AddThemeColorOverride("font_color", new Color("aaaaad"));
        parent.AddChild(label);
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
        _objectSummary.Text = item is null ? "未选中物件"
            : item.Type == ChartObjectType.Click
                ? $"Click  ·  ID {item.ObjectId}"
                : $"Hold  ·  ID {item.ObjectId}  ·  {item.StartTick}–{item.EndTick}";
        _tickEdit!.Editable = click;
        _laneEdit!.Editable = click;
        _widthEdit!.Editable = item?.Type != ChartObjectType.Hold;
        _inputTypeSelector!.Disabled = item?.Type == ChartObjectType.Hold;
        _applyObjectButton!.Disabled = !click;
        _deleteObjectButton!.Disabled = !click;

        if (item is not null)
        {
            _tickEdit.Value = item.StartTick;
            _laneEdit.Value = item.StartPoint.Lane;
            _widthEdit.Value = item.StartPoint.Width;
            _inputTypeSelector!.Select((int)item.InputType);
            _colorSelector!.Select((int)(item.Color ?? DrmColor.Red));
        }

        RefreshColorAvailability();
    }

    private void RefreshColorAvailability()
    {
        if (_inputTypeSelector is null || _colorSelector is null)
        {
            return;
        }

        _colorSelector.Disabled = _inputTypeSelector.Selected != (int)InputCategory.Drm
            || GetSelectedObject()?.Type == ChartObjectType.Hold;
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
            SetStatus(exception.Message, isError: true);
            return;
        }

        var item = new ChartObjectDefinition(id, ChartObjectType.Click, input, color,
            tick, tick, new[] { new LanePoint(tick, lane, width) }, Array.Empty<long>());
        if (ExecuteObjectCommand(new AddChartObjectCommand(item)))
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

    private void ApplySelectedClick()
    {
        ChartObjectDefinition? item = GetSelectedObject();
        if (item?.Type != ChartObjectType.Click)
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
        ExecuteObjectCommand(new ReplaceChartObjectCommand(item with
        {
            InputType = input,
            Color = color,
            StartTick = tick,
            EndTick = tick,
            Path = new[] { new LanePoint(tick, lane, width) },
        }));
    }

    private void DeleteSelectedClick()
    {
        ChartObjectDefinition? item = GetSelectedObject();
        if (item?.Type == ChartObjectType.Click)
        {
            ExecuteObjectCommand(new RemoveChartObjectCommand(item.ObjectId));
        }
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
            SetStatus($"编辑失败：{exception.Message}", isError: true);
            return false;
        }
    }
}
