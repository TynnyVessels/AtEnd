namespace AtEnd.Core;

public sealed record SongPackageLoadFailure(string DirectoryPath, string Message);

public sealed record SongLibraryScanResult(
    IReadOnlyList<SongPackageDefinition> Packages,
    IReadOnlyList<SongPackageLoadFailure> Failures);

public static class SongLibraryScanner
{
    public static SongLibraryScanResult ScanDirectory(string libraryDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryDirectoryPath);
        string fullLibraryPath = Path.GetFullPath(libraryDirectoryPath);
        if (!Directory.Exists(fullLibraryPath))
        {
            throw new DirectoryNotFoundException(fullLibraryPath);
        }

        var loaded = new List<SongPackageDefinition>();
        var failures = new List<SongPackageLoadFailure>();
        foreach (string packageDirectory in Directory
            .EnumerateDirectories(fullLibraryPath, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                loaded.Add(SongPackageLoader.LoadDirectory(packageDirectory));
            }
            catch (Exception exception) when (IsPackageError(exception))
            {
                failures.Add(new SongPackageLoadFailure(packageDirectory, exception.Message));
            }
        }

        var conflictMessages = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (IGrouping<string, SongPackageDefinition> group in loaded
            .GroupBy(package => package.Song.SongId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            foreach (SongPackageDefinition package in group)
            {
                AddConflict(
                    conflictMessages,
                    package.DirectoryPath,
                    $"Duplicate songId '{group.Key}'.");
            }
        }

        foreach (IGrouping<string, SongPackageDefinition> group in loaded
            .SelectMany(package => package.Charts.Select(chart => (Package: package, chart.ChartId)))
            .GroupBy(item => item.ChartId, item => item.Package, StringComparer.Ordinal)
            .Where(group => group.Select(package => package.DirectoryPath).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            foreach (SongPackageDefinition package in group.DistinctBy(item => item.DirectoryPath))
            {
                AddConflict(
                    conflictMessages,
                    package.DirectoryPath,
                    $"Duplicate chartId '{group.Key}'.");
            }
        }

        foreach ((string directoryPath, List<string> messages) in conflictMessages)
        {
            failures.Add(new SongPackageLoadFailure(directoryPath, string.Join(" ", messages)));
        }

        SongPackageDefinition[] packages = loaded
            .Where(package => !conflictMessages.ContainsKey(package.DirectoryPath))
            .OrderBy(package => package.Song.Title, StringComparer.CurrentCulture)
            .ThenBy(package => package.Song.SongId, StringComparer.Ordinal)
            .ToArray();
        SongPackageLoadFailure[] orderedFailures = failures
            .OrderBy(failure => failure.DirectoryPath, StringComparer.Ordinal)
            .ToArray();
        return new SongLibraryScanResult(
            Array.AsReadOnly(packages),
            Array.AsReadOnly(orderedFailures));
    }

    private static void AddConflict(
        IDictionary<string, List<string>> conflicts,
        string directoryPath,
        string message)
    {
        if (!conflicts.TryGetValue(directoryPath, out List<string>? messages))
        {
            messages = new List<string>();
            conflicts.Add(directoryPath, messages);
        }

        messages.Add(message);
    }

    private static bool IsPackageError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidDataException;
}
