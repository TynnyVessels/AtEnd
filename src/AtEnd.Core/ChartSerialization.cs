using System.Text.Json;
using System.Text.Json.Nodes;

namespace AtEnd.Core;

public static class ChartSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerOptions.Default)
    {
        WriteIndented = true,
    };

    public static string Serialize(ChartDefinition chart)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var speedEvents = new JsonArray();
        foreach (VisualSpeedEvent speedEvent in chart.VisualSpeedEvents)
        {
            speedEvents.Add(new JsonObject
            {
                ["tick"] = speedEvent.Tick,
                ["multiplier"] = speedEvent.Multiplier,
            });
        }

        var objects = new JsonArray();
        foreach (ChartObjectDefinition item in chart.Objects)
        {
            var node = new JsonObject
            {
                ["objectId"] = item.ObjectId,
                ["type"] = item.Type.ToString().ToLowerInvariant(),
                ["inputType"] = item.InputType.ToString().ToLowerInvariant(),
            };
            if (item.Color is not null)
            {
                node["color"] = item.Color.Value.ToString().ToLowerInvariant();
            }

            if (item.Type == ChartObjectType.Click)
            {
                node["tick"] = item.StartTick;
                node["lane"] = item.StartPoint.Lane;
                node["width"] = item.StartPoint.Width;
            }
            else
            {
                node["startTick"] = item.StartTick;
                node["endTick"] = item.EndTick;
                var path = new JsonArray();
                foreach (LanePoint point in item.Path)
                {
                    path.Add(new JsonObject
                    {
                        ["tick"] = point.Tick,
                        ["lane"] = point.Lane,
                        ["width"] = point.Width,
                    });
                }

                var judgeTicks = new JsonArray();
                foreach (long tick in item.JudgeTicks)
                {
                    judgeTicks.Add(tick);
                }

                node["path"] = path;
                node["judgeTicks"] = judgeTicks;
            }

            objects.Add(node);
        }

        var root = new JsonObject
        {
            ["formatVersion"] = chart.FormatVersion,
            ["chartId"] = chart.ChartId,
            ["difficulty"] = new JsonObject
            {
                ["name"] = chart.Difficulty.Name,
                ["level"] = chart.Difficulty.Level,
            },
            ["charter"] = chart.Charter,
            ["visualSpeedEvents"] = speedEvents,
            ["objects"] = objects,
        };

        string json = root.ToJsonString(WriteOptions) + Environment.NewLine;
        _ = SongPackageLoader.ParseChart(json);
        return json;
    }
}
