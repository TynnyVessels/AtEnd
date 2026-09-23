using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtEnd.Core;

public enum ChartObjectType
{
    Click,
    Hold,
}

public readonly record struct ChartDifficulty(string Name, int Level);

public readonly record struct LanePoint(long Tick, int Lane, int Width);

public readonly record struct VisualSpeedEvent(long Tick, double Multiplier);

public sealed record ChartObjectDefinition(
    long ObjectId,
    ChartObjectType Type,
    InputCategory InputType,
    DrmColor? Color,
    long StartTick,
    long EndTick,
    IReadOnlyList<LanePoint> Path,
    IReadOnlyList<long> JudgeTicks)
{
    public long TargetTick => StartTick;

    public LanePoint StartPoint => Path[0];

    public InputRequirement GetInputRequirement() => GetInputRequirementAtTick(StartTick);

    public (double Lane, double Width) GetLaneGeometryAtTick(long tick)
    {
        if (tick < StartTick || tick > EndTick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        if (Path.Count == 1 || tick == StartTick)
        {
            return (StartPoint.Lane, StartPoint.Width);
        }

        for (int index = 1; index < Path.Count; index++)
        {
            LanePoint next = Path[index];
            if (tick > next.Tick)
            {
                continue;
            }

            LanePoint previous = Path[index - 1];
            double progress = (tick - previous.Tick) / (double)(next.Tick - previous.Tick);
            return (
                previous.Lane + ((next.Lane - previous.Lane) * progress),
                previous.Width + ((next.Width - previous.Width) * progress));
        }

        LanePoint end = Path[^1];
        return (end.Lane, end.Width);
    }

    public InputRequirement GetInputRequirementAtTick(long tick)
    {
        if (InputType == InputCategory.Drm)
        {
            return InputRequirement.Drm(Color!.Value);
        }

        (double lane, double width) = GetLaneGeometryAtTick(tick);
        double endLane = lane + width;
        RelRegion regions = RelRegion.None;
        if (lane < 6 && endLane > 0)
        {
            regions |= RelRegion.Left;
        }

        if (lane < 12 && endLane > 6)
        {
            regions |= RelRegion.Center;
        }

        if (lane < 18 && endLane > 12)
        {
            regions |= RelRegion.Right;
        }

        return InputRequirement.Rel(regions);
    }
}

public sealed record ChartDefinition(
    int FormatVersion,
    string ChartId,
    ChartDifficulty Difficulty,
    string Charter,
    IReadOnlyList<VisualSpeedEvent> VisualSpeedEvents,
    IReadOnlyList<ChartObjectDefinition> Objects)
{
    public IReadOnlyList<ClickScoringObject> CreateClickScoringObjects() => Objects
        .Where(item => item.Type == ChartObjectType.Click)
        .Select(item => new ClickScoringObject(
            item.ObjectId,
            item.TargetTick,
            item.InputType,
            item.GetInputRequirement()))
        .ToArray();

    public IReadOnlyList<ClickScoringObject> CreateHeadScoringObjects() => Objects
        .Select(item => new ClickScoringObject(
            item.ObjectId,
            item.TargetTick,
            item.InputType,
            item.GetInputRequirement()))
        .ToArray();

    public IReadOnlyList<HoldScoringPoint> CreateHoldScoringPoints() => Objects
        .Where(item => item.Type == ChartObjectType.Hold)
        .SelectMany(item => item.JudgeTicks.Select((tick, index) => new HoldScoringPoint(
            item.ObjectId,
            index,
            tick,
            item.InputType,
            item.GetInputRequirementAtTick(tick))))
        .ToArray();
}

public readonly record struct TimeSignatureChange(
    long Tick,
    int Numerator,
    int Denominator);

public sealed record TimingDefinition(
    int FormatVersion,
    TimingMap TimingMap,
    IReadOnlyList<TimeSignatureChange> TimeSignatures);

public sealed record SongMetadata(
    int FormatVersion,
    string SongId,
    string Title,
    string Artist,
    string AudioFile,
    string? JacketFile,
    double PreviewStartSeconds,
    double PreviewLengthSeconds);

public sealed record SongPackageDefinition(
    string DirectoryPath,
    SongMetadata Song,
    TimingDefinition Timing,
    IReadOnlyList<ChartDefinition> Charts);

public static class SongPackageLoader
{
    private const int SupportedFormatVersion = 1;
    private const int LaneCount = 18;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static SongPackageDefinition LoadDirectory(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        string fullDirectoryPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectoryPath))
        {
            throw new DirectoryNotFoundException(fullDirectoryPath);
        }

        SongMetadata song = ParseSong(File.ReadAllText(Path.Combine(fullDirectoryPath, "song.json")));
        TimingDefinition timing = ParseTiming(File.ReadAllText(Path.Combine(fullDirectoryPath, "timing.json")));
        string audioPath = ResolvePackageFile(fullDirectoryPath, song.AudioFile);
        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("The song audio file does not exist.", audioPath);
        }

        if (song.JacketFile is not null)
        {
            string jacketPath = ResolvePackageFile(fullDirectoryPath, song.JacketFile);
            if (!File.Exists(jacketPath))
            {
                throw new FileNotFoundException("The song jacket file does not exist.", jacketPath);
            }
        }

        string chartsDirectory = Path.Combine(fullDirectoryPath, "charts");
        ChartDefinition[] charts = Directory.Exists(chartsDirectory)
            ? Directory.EnumerateFiles(chartsDirectory, "*.atendchart", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => ParseChart(File.ReadAllText(path)))
                .ToArray()
            : Array.Empty<ChartDefinition>();
        if (charts.Length == 0)
        {
            throw new InvalidDataException("A song package must contain at least one .atendchart file.");
        }

        if (charts.Select(chart => chart.ChartId).Distinct(StringComparer.Ordinal).Count() != charts.Length)
        {
            throw new InvalidDataException("Chart identifiers must be unique within a song package.");
        }

        return new SongPackageDefinition(fullDirectoryPath, song, timing, charts);
    }

    public static ChartDefinition ParseChart(string json)
    {
        ChartDto dto = Deserialize<ChartDto>(json, "chart");
        RequireVersion(dto.FormatVersion, "chart");
        RequireText(dto.ChartId, "chartId");
        RequireText(dto.Charter, "charter");
        if (dto.Difficulty is null)
        {
            throw new InvalidDataException("difficulty is required.");
        }

        RequireText(dto.Difficulty.Name, "difficulty.name");
        if (dto.Difficulty.Level < 0)
        {
            throw new InvalidDataException("difficulty.level cannot be negative.");
        }

        ChartObjectDto[] objectDtos = dto.Objects
            ?? throw new InvalidDataException("objects is required.");
        if (objectDtos.Length == 0)
        {
            throw new InvalidDataException("A chart must contain at least one object.");
        }

        var ids = new HashSet<long>();
        ChartObjectDefinition[] objects = objectDtos.Select((item, index) =>
        {
            if (item.ObjectId <= 0)
            {
                throw new InvalidDataException($"objects[{index}].objectId must be positive.");
            }

            if (!ids.Add(item.ObjectId))
            {
                throw new InvalidDataException($"Duplicate objectId {item.ObjectId}.");
            }

            return ConvertObject(item, index);
        }).ToArray();

        VisualSpeedEvent[] speedEvents = (dto.VisualSpeedEvents ?? Array.Empty<VisualSpeedEventDto>())
            .Select((item, index) =>
            {
                if (item.Tick < 0 || !double.IsFinite(item.Multiplier) || item.Multiplier <= 0)
                {
                    throw new InvalidDataException($"visualSpeedEvents[{index}] is invalid.");
                }

                return new VisualSpeedEvent(item.Tick, item.Multiplier);
            })
            .OrderBy(item => item.Tick)
            .ToArray();

        return new ChartDefinition(
            dto.FormatVersion,
            dto.ChartId!,
            new ChartDifficulty(dto.Difficulty.Name!, dto.Difficulty.Level),
            dto.Charter!,
            Array.AsReadOnly(speedEvents),
            Array.AsReadOnly(objects));
    }

    public static TimingDefinition ParseTiming(string json)
    {
        TimingDto dto = Deserialize<TimingDto>(json, "timing");
        RequireVersion(dto.FormatVersion, "timing");
        BpmChange[] bpmChanges = (dto.BpmChanges ?? Array.Empty<BpmChangeDto>())
            .Select(item => new BpmChange(item.Tick, item.BeatsPerMinute))
            .ToArray();
        var timingMap = new TimingMap(
            dto.AudioTimeAtTickZeroSeconds,
            dto.InitialBpm,
            bpmChanges);

        TimeSignatureDto[] signatureDtos = dto.TimeSignatures
            ?? throw new InvalidDataException("timeSignatures is required.");
        if (signatureDtos.Length == 0)
        {
            throw new InvalidDataException("At least one time signature is required.");
        }

        var signatures = new List<TimeSignatureChange>(signatureDtos.Length);
        long previousTick = -1;
        foreach (TimeSignatureDto item in signatureDtos)
        {
            if (item.Tick < 0 || item.Tick <= previousTick || item.Numerator <= 0
                || item.Denominator <= 0 || (item.Denominator & (item.Denominator - 1)) != 0)
            {
                throw new InvalidDataException("Time signatures must be ordered and use positive power-of-two denominators.");
            }

            signatures.Add(new TimeSignatureChange(item.Tick, item.Numerator, item.Denominator));
            previousTick = item.Tick;
        }

        if (signatures[0].Tick != 0)
        {
            throw new InvalidDataException("The first time signature must begin at tick zero.");
        }

        return new TimingDefinition(dto.FormatVersion, timingMap, signatures.AsReadOnly());
    }

    public static SongMetadata ParseSong(string json)
    {
        SongDto dto = Deserialize<SongDto>(json, "song");
        RequireVersion(dto.FormatVersion, "song");
        RequireText(dto.SongId, "songId");
        RequireText(dto.Title, "title");
        RequireText(dto.Artist, "artist");
        RequireFileName(dto.AudioFile, "audioFile");
        if (dto.JacketFile is not null)
        {
            RequireFileName(dto.JacketFile, "jacketFile");
        }

        if (dto.Preview is null || !double.IsFinite(dto.Preview.StartSeconds)
            || dto.Preview.StartSeconds < 0 || !double.IsFinite(dto.Preview.LengthSeconds)
            || dto.Preview.LengthSeconds <= 0)
        {
            throw new InvalidDataException("preview must contain a non-negative start and positive length.");
        }

        return new SongMetadata(
            dto.FormatVersion,
            dto.SongId!,
            dto.Title!,
            dto.Artist!,
            dto.AudioFile!,
            dto.JacketFile,
            dto.Preview.StartSeconds,
            dto.Preview.LengthSeconds);
    }

    private static ChartObjectDefinition ConvertObject(ChartObjectDto dto, int index)
    {
        string location = $"objects[{index}]";
        ChartObjectType type = ParseEnum<ChartObjectType>(dto.Type, $"{location}.type");
        InputCategory inputType = ParseEnum<InputCategory>(dto.InputType, $"{location}.inputType");
        DrmColor? color = inputType == InputCategory.Drm
            ? ParseEnum<DrmColor>(dto.Color, $"{location}.color")
            : null;
        if (inputType == InputCategory.Rel && dto.Color is not null)
        {
            throw new InvalidDataException($"{location}.color is only valid for Drm objects.");
        }

        if (type == ChartObjectType.Click)
        {
            long tick = dto.Tick ?? throw new InvalidDataException($"{location}.tick is required.");
            int lane = dto.Lane ?? throw new InvalidDataException($"{location}.lane is required.");
            int width = dto.Width ?? throw new InvalidDataException($"{location}.width is required.");
            ValidateLanePoint(new LanePoint(tick, lane, width), location);
            return new ChartObjectDefinition(
                dto.ObjectId,
                type,
                inputType,
                color,
                tick,
                tick,
                Array.AsReadOnly(new[] { new LanePoint(tick, lane, width) }),
                Array.Empty<long>());
        }

        long startTick = dto.StartTick
            ?? throw new InvalidDataException($"{location}.startTick is required.");
        long endTick = dto.EndTick
            ?? throw new InvalidDataException($"{location}.endTick is required.");
        if (startTick < 0 || endTick <= startTick)
        {
            throw new InvalidDataException($"{location} must end after its non-negative start tick.");
        }

        LanePointDto[] pathDtos = dto.Path
            ?? throw new InvalidDataException($"{location}.path is required.");
        if (pathDtos.Length < 2)
        {
            throw new InvalidDataException($"{location}.path requires at least a start and end point.");
        }

        LanePoint[] path = pathDtos.Select(item => new LanePoint(item.Tick, item.Lane, item.Width)).ToArray();
        long previousTick = -1;
        foreach (LanePoint point in path)
        {
            ValidateLanePoint(point, $"{location}.path");
            if (point.Tick <= previousTick || point.Tick < startTick || point.Tick > endTick)
            {
                throw new InvalidDataException($"{location}.path ticks must increase within the hold range.");
            }

            previousTick = point.Tick;
        }

        if (path[0].Tick != startTick || path[^1].Tick != endTick)
        {
            throw new InvalidDataException($"{location}.path must begin and end at the hold boundaries.");
        }

        long[] judgeTicks = dto.JudgeTicks
            ?? throw new InvalidDataException($"{location}.judgeTicks is required.");
        previousTick = startTick;
        foreach (long tick in judgeTicks)
        {
            if (tick <= previousTick || tick >= endTick)
            {
                throw new InvalidDataException($"{location}.judgeTicks must increase strictly inside the hold.");
            }

            previousTick = tick;
        }

        return new ChartObjectDefinition(
            dto.ObjectId,
            type,
            inputType,
            color,
            startTick,
            endTick,
            Array.AsReadOnly(path),
            Array.AsReadOnly(judgeTicks));
    }

    private static void ValidateLanePoint(LanePoint point, string location)
    {
        if (point.Tick < 0 || point.Lane < 0 || point.Lane >= LaneCount
            || point.Width <= 0 || point.Lane + point.Width > LaneCount)
        {
            throw new InvalidDataException($"{location} contains an invalid tick, lane, or width.");
        }
    }

    private static TEnum ParseEnum<TEnum>(string? value, string location)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse(value, true, out TEnum result)
            || !Enum.IsDefined(result))
        {
            throw new InvalidDataException($"{location} has an unsupported value.");
        }

        return result;
    }

    private static T Deserialize<T>(string json, string kind)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidDataException($"The {kind} JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The {kind} JSON is invalid: {exception.Message}", exception);
        }
    }

    private static void RequireVersion(int version, string kind)
    {
        if (version != SupportedFormatVersion)
        {
            throw new InvalidDataException($"Unsupported {kind} format version {version}.");
        }
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{field} is required.");
        }
    }

    private static void RequireFileName(string? value, string field)
    {
        RequireText(value, field);
        if (Path.GetFileName(value) != value)
        {
            throw new InvalidDataException($"{field} must be a file name without a path.");
        }
    }

    private static string ResolvePackageFile(string directoryPath, string fileName) =>
        Path.Combine(directoryPath, fileName);

    private sealed class ChartDto
    {
        public int FormatVersion { get; set; }
        public string? ChartId { get; set; }
        public DifficultyDto? Difficulty { get; set; }
        public string? Charter { get; set; }
        public VisualSpeedEventDto[]? VisualSpeedEvents { get; set; }
        public ChartObjectDto[]? Objects { get; set; }
    }

    private sealed class DifficultyDto
    {
        public string? Name { get; set; }
        public int Level { get; set; }
    }

    private sealed class VisualSpeedEventDto
    {
        public long Tick { get; set; }
        public double Multiplier { get; set; }
    }

    private sealed class ChartObjectDto
    {
        public long ObjectId { get; set; }
        public string? Type { get; set; }
        public string? InputType { get; set; }
        public string? Color { get; set; }
        public long? Tick { get; set; }
        public int? Lane { get; set; }
        public int? Width { get; set; }
        public long? StartTick { get; set; }
        public long? EndTick { get; set; }
        public LanePointDto[]? Path { get; set; }
        public long[]? JudgeTicks { get; set; }
    }

    private sealed class LanePointDto
    {
        public long Tick { get; set; }
        public int Lane { get; set; }
        public int Width { get; set; }
    }

    private sealed class TimingDto
    {
        public int FormatVersion { get; set; }
        public double AudioTimeAtTickZeroSeconds { get; set; }
        public double InitialBpm { get; set; }
        public BpmChangeDto[]? BpmChanges { get; set; }
        public TimeSignatureDto[]? TimeSignatures { get; set; }
    }

    private sealed class BpmChangeDto
    {
        public long Tick { get; set; }
        public double BeatsPerMinute { get; set; }
    }

    private sealed class TimeSignatureDto
    {
        public long Tick { get; set; }
        public int Numerator { get; set; }
        public int Denominator { get; set; }
    }

    private sealed class SongDto
    {
        public int FormatVersion { get; set; }
        public string? SongId { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? AudioFile { get; set; }
        public string? JacketFile { get; set; }
        public PreviewDto? Preview { get; set; }
    }

    private sealed class PreviewDto
    {
        public double StartSeconds { get; set; }
        public double LengthSeconds { get; set; }
    }
}
