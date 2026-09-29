using System;
using System.IO;
using System.Linq;
using AtEnd.Core;
using Godot;

namespace AtEnd.Editor;

public partial class EditorMain
{
    private EditableChartDocument? _document;
    private LineEdit? _chartIdEdit;
    private LineEdit? _difficultyNameEdit;
    private SpinBox? _difficultyLevelEdit;
    private LineEdit? _charterEdit;
    private LineEdit? _saveFileNameEdit;
    private Label? _documentStateLabel;
    private Button? _applyMetadataButton;
    private Button? _undoButton;
    private Button? _redoButton;
    private Button? _saveAsButton;

    public override void _EnterTree()
    {
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--editor-smoke-test") >= 0)
        {
            RunEditingFoundationSmokeTest();
        }

    }
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } arrowKey
            && arrowKey.Keycode is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            MoveSelectedClickWithArrow(arrowKey.Keycode);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey
            {
                Keycode: Key.Space,
                Pressed: true,
                Echo: false,
            } && GetViewport().GuiGetFocusOwner() is not LineEdit)
        {
            TogglePlayback();
            GetViewport().SetInputAsHandled();
        }
    }


    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (key.Keycode == Key.Delete && !key.CtrlPressed
            && GetViewport().GuiGetFocusOwner() is not LineEdit
            && GetSelectedObject()?.Type == ChartObjectType.Click)
        {
            DeleteSelectedClick();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!key.CtrlPressed)
        {
            return;
        }

        if (key.Keycode == Key.Z && key.ShiftPressed)
        {
            RedoDocument();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Z)
        {
            UndoDocument();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Y)
        {
            RedoDocument();
            GetViewport().SetInputAsHandled();
        }
    }

    private void MoveSelectedClickWithArrow(Key key)
    {
        if (_timeline is null || GetSelectedObject() is not { Type: ChartObjectType.Click } item)
        {
            return;
        }

        long tick = item.StartTick;
        int lane = item.StartPoint.Lane;
        long tickStep = Math.Max(1, _timeline.SnapTicks);
        switch (key)
        {
            case Key.Left:
                lane--;
                break;
            case Key.Right:
                lane++;
                break;
            case Key.Up:
                tick = Math.Min(long.MaxValue - tickStep, tick) + tickStep;
                break;
            case Key.Down:
                tick = Math.Max(0, tick - tickStep);
                break;
            default:
                return;
        }

        lane = Math.Clamp(lane, 0, 18 - item.StartPoint.Width);
        if (tick != item.StartTick || lane != item.StartPoint.Lane)
        {
            MoveClick(item.ObjectId, tick, lane);
        }
    }

    private void InitializeEditingFoundation()
    {
        if (_workspace is null || _chartSelector is null)
        {
            return;
        }

        BuildDocumentTab();
        _chartSelector.ItemSelected += OnDocumentChartSelected;
        LoadEditableDocumentForSelection();
    }

    private void BuildDocumentTab()
    {
        var panel = new MarginContainer { Name = "Document" };
        panel.AddThemeConstantOverride("margin_left", 7);
        panel.AddThemeConstantOverride("margin_top", 7);
        panel.AddThemeConstantOverride("margin_right", 7);
        panel.AddThemeConstantOverride("margin_bottom", 7);
        _workspace!.AddChild(panel);
        _workspace.SetTabTitle(_workspace.GetTabCount() - 1, "文档");

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 3);
        panel.AddChild(layout);

        _documentStateLabel = AddLabel(layout, "未载入谱面", "d9d9db");
        AddLabel(layout, "谱面 ID", "aaaaad");
        _chartIdEdit = AddLineEdit(layout);
        AddLabel(layout, "难度名称", "aaaaad");
        _difficultyNameEdit = AddLineEdit(layout);
        AddLabel(layout, "难度等级", "aaaaad");
        _difficultyLevelEdit = new SpinBox
        {
            MinValue = 0,
            MaxValue = 99,
            Step = 1,
            AllowGreater = true,
        };
        layout.AddChild(_difficultyLevelEdit);
        AddLabel(layout, "谱师", "aaaaad");
        _charterEdit = AddLineEdit(layout);

        _applyMetadataButton = new Button { Text = "应用", TooltipText = "应用谱面信息" };
        _applyMetadataButton.Pressed += ApplyMetadata;
        layout.AddChild(_applyMetadataButton);

        var historyRow = new HBoxContainer();
        historyRow.AddThemeConstantOverride("separation", 3);
        layout.AddChild(historyRow);
        _undoButton = new Button { Text = "撤销", TooltipText = "撤销（Ctrl+Z）", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _redoButton = new Button { Text = "重做", TooltipText = "重做（Ctrl+Y）", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _undoButton.Pressed += UndoDocument;
        _redoButton.Pressed += RedoDocument;
        historyRow.AddChild(_undoButton);
        historyRow.AddChild(_redoButton);

        AddLabel(layout, "另存文件名", "aaaaad");
        _saveFileNameEdit = AddLineEdit(layout);
        _saveAsButton = new Button { Text = "另存为新谱面" };
        _saveAsButton.Pressed += SaveDocumentAs;
        layout.AddChild(_saveAsButton);
    }

    private static Label AddLabel(Container parent, string text, string color)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", new Color(color));
        parent.AddChild(label);
        return label;
    }

    private static LineEdit AddLineEdit(Container parent)
    {
        var edit = new LineEdit();
        parent.AddChild(edit);
        return edit;
    }

    private void OnDocumentChartSelected(long index)
    {
        _ = index;
        Callable.From(LoadEditableDocumentForSelection).CallDeferred();
    }

    private void LoadEditableDocumentForSelection()
    {
        _timeline?.SetSelectedObject(null);
        if (_selectedPackage is null || _selectedChart is null)
        {
            _document = null;
            RefreshDocumentControls();
            RefreshObjectControls();
            return;
        }

        string? sourcePath = Directory
            .EnumerateFiles(
                Path.Combine(_selectedPackage.DirectoryPath, "charts"),
                "*.atendchart",
                SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path =>
            {
                try
                {
                    return string.Equals(
                        SongPackageLoader.ParseChart(File.ReadAllText(path)).ChartId,
                        _selectedChart.ChartId,
                        StringComparison.Ordinal);
                }
                catch (InvalidDataException)
                {
                    return false;
                }
            });

        _document = new EditableChartDocument(_selectedChart, sourcePath);
        _saveFileNameEdit!.Text = $"{_selectedChart.ChartId}-copy.atendchart";
        RefreshDocumentControls();
        RefreshObjectControls();
    }

    private void ApplyMetadata()
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            _document.Execute(new EditChartMetadataCommand(
                _chartIdEdit!.Text.Trim(),
                _difficultyNameEdit!.Text.Trim(),
                (int)Math.Round(_difficultyLevelEdit!.Value),
                _charterEdit!.Text.Trim()));
            UseDocumentChart();
            SetStatus("谱面信息已应用到内存文档。", isError: false);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            SetStatus($"无法应用：{exception.Message}", isError: true);
        }
    }

    private void UndoDocument()
    {
        if (_document?.Undo() == true)
        {
            UseDocumentChart();
            SetStatus("已撤销上一步修改。", isError: false);
        }
    }

    private void RedoDocument()
    {
        if (_document?.Redo() == true)
        {
            UseDocumentChart();
            SetStatus("已重做修改。", isError: false);
        }
    }

    private void SaveDocumentAs()
    {
        if (_document is null || _selectedPackage is null)
        {
            return;
        }

        try
        {
            string path = ChartFileSaver.SaveAs(
                _document,
                _selectedPackage.DirectoryPath,
                _saveFileNameEdit!.Text);
            RefreshDocumentControls();
            SetStatus($"已另存并校验：{Path.GetFileName(path)}", isError: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus($"另存失败：{exception.Message}", isError: true);
        }
    }

    private void UseDocumentChart()
    {
        if (_document is null || _selectedPackage is null || _timeline is null || _seekSlider is null)
        {
            return;
        }

        _selectedChart = _document.CurrentChart;
        _timeline.SetChart(_selectedPackage.Timing, _selectedChart, _seekSlider.MaxValue,
            preserveViewport: true);
        _detailsLabel!.Text =
            $"物件  {_selectedChart.Objects.Count}"
            + $"    BPM  {_selectedPackage.Timing.TimingMap.InitialBeatsPerMinute:F3}";
        RefreshDocumentControls();
        RefreshObjectControls();
    }

    private void RefreshDocumentControls()
    {
        bool loaded = _document is not null;
        if (!loaded)
        {
            if (_documentStateLabel is not null) _documentStateLabel.Text = "未载入谱面";
            return;
        }

        ChartDefinition chart = _document!.CurrentChart;
        _chartIdEdit!.Text = chart.ChartId;
        _difficultyNameEdit!.Text = chart.Difficulty.Name;
        _difficultyLevelEdit!.Value = chart.Difficulty.Level;
        _charterEdit!.Text = chart.Charter;
        _documentStateLabel!.Text = _document.IsDirty ? "● 有未保存修改" : "✓ 内存文档已保存";
        _documentStateLabel.AddThemeColorOverride(
            "font_color",
            _document.IsDirty ? new Color("ffc56b") : new Color("78dca4"));
        _undoButton!.Disabled = !_document.CanUndo;
        _redoButton!.Disabled = !_document.CanRedo;
        _saveAsButton!.Disabled = !_document.IsDirty;

        bool lockSelection = _document.IsDirty;
        _songSelector!.Disabled = lockSelection;
        _chartSelector!.Disabled = lockSelection;
        _rescanButton!.Disabled = lockSelection;
    }

    private static void RunEditingFoundationSmokeTest()
    {
        string repositoryDirectory = Directory.GetParent(
            ProjectSettings.GlobalizePath("res://").TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar))?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository.");
        ChartDefinition source = SongPackageLoader.LoadDirectory(
            Path.Combine(repositoryDirectory, "songs", "test-song")).Charts[0];
        var document = new EditableChartDocument(source);
        document.Execute(new EditChartMetadataCommand(
            source.ChartId + "-smoke-copy",
            source.Difficulty.Name,
            source.Difficulty.Level,
            source.Charter + " Smoke"));
        if (!document.IsDirty || !document.CanUndo || document.CurrentChart.Charter == source.Charter)
        {
            throw new InvalidOperationException("Editable document command failed.");
        }

        document.Undo();
        if (document.IsDirty || !document.CanRedo)
        {
            throw new InvalidOperationException("Editable document undo failed.");
        }

        document.Redo();
        ChartObjectDefinition reference = document.CurrentChart.Objects[0];
        long addedId = document.AllocateObjectId();
        var added = new ChartObjectDefinition(
            addedId,
            ChartObjectType.Click,
            InputCategory.Rel,
            null,
            reference.StartTick + TimingMap.PulsesPerQuarterNote,
            reference.StartTick + TimingMap.PulsesPerQuarterNote,
            new[] { new LanePoint(reference.StartTick + TimingMap.PulsesPerQuarterNote, 0, 1) },
            Array.Empty<long>());
        document.Execute(new AddChartObjectCommand(added));
        if (!document.CurrentChart.Objects.Any(item => item.ObjectId == addedId))
        {
            throw new InvalidOperationException("Click insertion failed.");
        }

        document.Execute(new ReplaceChartObjectCommand(added with
        {
            StartTick = added.StartTick + 480,
            EndTick = added.EndTick + 480,
            Path = new[] { new LanePoint(added.StartTick + 480, 2, 3) },
        }));
        if (document.CurrentChart.Objects.Single(item => item.ObjectId == addedId).StartPoint
            != new LanePoint(added.StartTick + 480, 2, 3))
        {
            throw new InvalidOperationException("Click move or resize failed.");
        }

        document.Undo();
        if (document.CurrentChart.Objects.Single(item => item.ObjectId == addedId).StartPoint
            != added.StartPoint)
        {
            throw new InvalidOperationException("Click edit undo failed.");
        }

        document.Redo();
        document.Execute(new RemoveChartObjectCommand(addedId));
        if (document.CurrentChart.Objects.Any(item => item.ObjectId == addedId)
            || document.AllocateObjectId() <= addedId)
        {
            throw new InvalidOperationException("Click deletion or ID allocation failed.");
        }

        var oneObjectChart = source with
        {
            Objects = Array.AsReadOnly(new[] { source.Objects[0] }),
        };
        try
        {
            new EditableChartDocument(oneObjectChart).Execute(
                new RemoveChartObjectCommand(source.Objects[0].ObjectId));
            throw new InvalidOperationException("Last object deletion was accepted.");
        }
        catch (InvalidDataException)
        {
        }

        string temporaryPackage = Path.Combine(
            repositoryDirectory,
            ".tools",
            "state",
            $"atend-editor-smoke-{Guid.NewGuid():N}");
        try
        {
            string savedPath = ChartFileSaver.SaveAs(document, temporaryPackage, "smoke-copy");
            ChartDefinition saved = SongPackageLoader.ParseChart(File.ReadAllText(savedPath));
            if (document.IsDirty || saved.ChartId != document.CurrentChart.ChartId)
            {
                throw new InvalidOperationException("Editable document save failed.");
            }

            try
            {
                ChartFileSaver.SaveAs(document, temporaryPackage, "duplicate-id");
                throw new InvalidOperationException("Duplicate chart identifier was accepted.");
            }
            catch (InvalidDataException)
            {
            }

            try
            {
                ChartFileSaver.SaveAs(document, temporaryPackage, "smoke-copy");
                throw new InvalidOperationException("Existing chart was overwritten.");
            }
            catch (IOException)
            {
            }
        }
        finally
        {
            if (Directory.Exists(temporaryPackage))
            {
                Directory.Delete(temporaryPackage, recursive: true);
            }
        }
    }
}
