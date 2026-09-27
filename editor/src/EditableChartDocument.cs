using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AtEnd.Core;

namespace AtEnd.Editor;

public interface IChartEditCommand
{
    string Description { get; }

    ChartDefinition Apply(ChartDefinition chart);
}

public sealed record EditChartMetadataCommand(
    string ChartId,
    string DifficultyName,
    int DifficultyLevel,
    string Charter) : IChartEditCommand
{
    public string Description => "修改谱面信息";

    public ChartDefinition Apply(ChartDefinition chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var candidate = chart with
        {
            ChartId = ChartId,
            Difficulty = new ChartDifficulty(DifficultyName, DifficultyLevel),
            Charter = Charter,
        };
        return SongPackageLoader.ParseChart(ChartSerializer.Serialize(candidate));
    }
}

public sealed class EditableChartDocument
{
    private sealed record HistoryEntry(
        string Description,
        ChartDefinition Before,
        ChartDefinition After);

    private readonly List<HistoryEntry> _history = new();
    private int _historyPosition;
    private string _savedSnapshot;

    public EditableChartDocument(ChartDefinition sourceChart, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(sourceChart);
        CurrentChart = Clone(sourceChart);
        SourcePath = sourcePath;
        _savedSnapshot = ChartSerializer.Serialize(CurrentChart);
    }

    public ChartDefinition CurrentChart { get; private set; }

    public string? SourcePath { get; }

    public string? SavedPath { get; private set; }

    public bool IsDirty => !string.Equals(
        ChartSerializer.Serialize(CurrentChart),
        _savedSnapshot,
        StringComparison.Ordinal);

    public bool CanUndo => _historyPosition > 0;

    public bool CanRedo => _historyPosition < _history.Count;

    public string? UndoDescription => CanUndo ? _history[_historyPosition - 1].Description : null;

    public string? RedoDescription => CanRedo ? _history[_historyPosition].Description : null;

    public void Execute(IChartEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ChartDefinition before = CurrentChart;
        ChartDefinition after = Clone(command.Apply(before));
        if (string.Equals(
            ChartSerializer.Serialize(before),
            ChartSerializer.Serialize(after),
            StringComparison.Ordinal))
        {
            return;
        }

        if (_historyPosition < _history.Count)
        {
            _history.RemoveRange(_historyPosition, _history.Count - _historyPosition);
        }

        _history.Add(new HistoryEntry(command.Description, before, after));
        _historyPosition++;
        CurrentChart = after;
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        _historyPosition--;
        CurrentChart = _history[_historyPosition].Before;
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        CurrentChart = _history[_historyPosition].After;
        _historyPosition++;
        return true;
    }

    public void MarkSaved(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        SavedPath = Path.GetFullPath(destinationPath);
        _savedSnapshot = ChartSerializer.Serialize(CurrentChart);
    }

    private static ChartDefinition Clone(ChartDefinition source) => new(
        source.FormatVersion,
        source.ChartId,
        source.Difficulty,
        source.Charter,
        Array.AsReadOnly(source.VisualSpeedEvents.ToArray()),
        Array.AsReadOnly(source.Objects.Select(item => item with
        {
            Path = Array.AsReadOnly(item.Path.ToArray()),
            JudgeTicks = Array.AsReadOnly(item.JudgeTicks.ToArray()),
        }).ToArray()));
}
