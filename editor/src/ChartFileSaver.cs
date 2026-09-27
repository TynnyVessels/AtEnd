using System;
using System.IO;
using System.Text;
using AtEnd.Core;

namespace AtEnd.Editor;

public static class ChartFileSaver
{
    public static string SaveAs(
        EditableChartDocument document,
        string songPackageDirectory,
        string requestedFileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(songPackageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedFileName);

        string fileName = requestedFileName.Trim();
        if (!fileName.EndsWith(".atendchart", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".atendchart";
        }

        if (Path.IsPathRooted(fileName)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
            || fileName is "." or ".."
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("文件名不能包含路径或非法字符。");
        }

        string chartsDirectory = Path.GetFullPath(Path.Combine(songPackageDirectory, "charts"));
        Directory.CreateDirectory(chartsDirectory);
        string destinationPath = Path.GetFullPath(Path.Combine(chartsDirectory, fileName));
        if (!string.Equals(
            Path.GetDirectoryName(destinationPath),
            chartsDirectory,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("谱面只能保存到当前歌曲包的 charts 目录。");
        }

        if (File.Exists(destinationPath))
        {
            throw new IOException($"目标谱面已存在：{fileName}");
        }

        foreach (string existingPath in Directory.EnumerateFiles(
            chartsDirectory,
            "*.atendchart",
            SearchOption.TopDirectoryOnly))
        {
            ChartDefinition existing = SongPackageLoader.ParseChart(File.ReadAllText(existingPath));
            if (string.Equals(existing.ChartId, document.CurrentChart.ChartId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"谱面 ID 已存在：{document.CurrentChart.ChartId}。另存前请修改谱面 ID。");
            }
        }

        string json = ChartSerializer.Serialize(document.CurrentChart);
        string temporaryPath = Path.Combine(
            chartsDirectory,
            $".{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            ChartDefinition reloaded = SongPackageLoader.ParseChart(File.ReadAllText(temporaryPath));
            if (!string.Equals(
                ChartSerializer.Serialize(reloaded),
                json,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException("保存后的谱面校验失败。");
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
            document.MarkSaved(destinationPath);
            return destinationPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
