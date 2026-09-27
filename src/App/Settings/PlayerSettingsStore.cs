using System;
using AtEnd.Core;
using Godot;

namespace AtEnd.App.Settings;

public sealed class PlayerSettingsStore
{
    private const int CurrentFormatVersion = 2;
    private readonly string _path;

    public PlayerSettingsStore(string path = "user://settings.cfg")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public PlayerSettings Load()
    {
        if (!FileAccess.FileExists(_path))
        {
            return PlayerSettings.Default;
        }

        var config = new ConfigFile();
        if (config.Load(_path) != Error.Ok
            || config.GetValue("meta", "format_version", 0).AsInt32()
                != CurrentFormatVersion)
        {
            return PlayerSettings.Default;
        }

        try
        {
            return new PlayerSettings(
                config.GetValue(
                    "gameplay",
                    "scroll_speed",
                    PlayerSettings.Default.ScrollSpeed).AsDouble(),
                config.GetValue(
                    "timing",
                    "global_offset_ms",
                    PlayerSettings.Default.GlobalTimingOffsetMilliseconds).AsDouble(),
                config.GetValue(
                    "timing",
                    "input_offset_ms",
                    PlayerSettings.Default.InputOffsetMilliseconds).AsDouble(),
                config.GetValue(
                    "playfield",
                    "grid_density",
                    PlayerSettings.Default.GridDensity).AsInt32());
        }
        catch (ArgumentOutOfRangeException)
        {
            return PlayerSettings.Default;
        }
    }

    public void Save(PlayerSettings settings)
    {
        var config = new ConfigFile();
        config.SetValue("meta", "format_version", CurrentFormatVersion);
        config.SetValue("gameplay", "scroll_speed", settings.ScrollSpeed);
        config.SetValue(
            "timing",
            "global_offset_ms",
            settings.GlobalTimingOffsetMilliseconds);
        config.SetValue("timing", "input_offset_ms", settings.InputOffsetMilliseconds);
        config.SetValue("playfield", "grid_density", settings.GridDensity);
        Error saveError = config.Save(_path);
        if (saveError != Error.Ok)
        {
            throw new InvalidOperationException(
                $"Could not save player settings to '{_path}' ({saveError}).");
        }
    }
}
