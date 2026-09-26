using System;
using System.IO;
using System.Linq;
using AtEnd.Core;
using Godot;

namespace AtEnd.App.Playfield;

public partial class PlayfieldView
{
    private const int LaneCount = 18; //轨道数
    private const float TrackBackProgress = 0;
    private const float TrackWindowBottomProgress = 1.12f; //+0.12越过判定线
    private const float NoteDepthLaneRatio = 0.42f; //note纵深相对单轨宽度，保持贴地透视
    private const float NoteCullPaddingProgress = 0.05f;
    private const float JudgmentLineHalfDepth = 0.0045f; //判定线厚度
    private static readonly Color TrackColor = new("10162b"); //轨道颜色
    private static readonly Color TrackEdgeColor = new("6f7fb5"); //轨道边缘颜色
    private static readonly Color MinorGridColor = new(0.3f, 0.38f, 0.62f, 0.28f); //次要网格线颜色
    private static readonly Color MajorGridColor = new(0.5f, 0.62f, 0.92f, 0.7f); //主要网格线颜色
    private static readonly Color JudgmentLineColor = new("f4f7ff"); //判定线颜色

    [Export]
    public int GridDensity { get; set; } = 18;

    [Export(PropertyHint.Range, "0.4,2.0,0.05")]
    public double ApproachDurationSeconds { get; set; } = 0.45;

    [Export(PropertyHint.Range, "1.0,3.0,0.05")]
    public double TravelAccelerationExponent { get; set; } = 2.3;

    private static RuntimeNote ToRuntimeNote(ChartObjectDefinition item)
    {
        LanePoint point = item.StartPoint;
        return item.InputType switch
        {
            InputCategory.Rel => new RuntimeNote(
                item.ObjectId,
                point.Lane,
                point.Width,
                item.TargetTick,
                item.InputType,
                item.GetInputRequirement(),
                new Color("f5f6ff"),
                NoteSymbol.Rel),
            InputCategory.Drm => item.Color switch
            {
                DrmColor.Red => new RuntimeNote(
                    item.ObjectId, point.Lane, point.Width, item.TargetTick, item.InputType,
                    item.GetInputRequirement(), new Color("ff4f65"), NoteSymbol.RedCross),
                DrmColor.Green => new RuntimeNote(
                    item.ObjectId, point.Lane, point.Width, item.TargetTick, item.InputType,
                    item.GetInputRequirement(), new Color("50e39a"), NoteSymbol.GreenSquare),
                DrmColor.Blue => new RuntimeNote(
                    item.ObjectId, point.Lane, point.Width, item.TargetTick, item.InputType,
                    item.GetInputRequirement(), new Color("55a8ff"), NoteSymbol.BlueCircle),
                _ => throw new InvalidDataException($"Object {item.ObjectId} has no Drm color."),
            },
            _ => throw new InvalidDataException($"Object {item.ObjectId} has an invalid input type."),
        };
    }

    private static RuntimeHold ToRuntimeHold(ChartObjectDefinition item)
    {
        RuntimeNote head = ToRuntimeNote(item);
        return new RuntimeHold(item, head.Color);
    }

    public override void _Draw()
    {
        if (Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        DrawTrack();
        DrawGuides();
        DrawMovingHolds();
        DrawMovingNotes();
        DrawJudgmentLine();
    }

    private void DrawTrack()
    {
        Vector2[] track =
        {
            TrackPoint(0, TrackBackProgress),
            TrackPoint(LaneCount, TrackBackProgress),
            TrackPoint(LaneCount, 1),
            TrackPoint(LaneCount, TrackWindowBottomProgress),
            TrackPoint(0, TrackWindowBottomProgress),
            TrackPoint(0, 1),
        };
        DrawColoredPolygon(track, TrackColor);
        Vector2 leftBack = TrackPoint(0, TrackBackProgress);
        Vector2 leftJudgment = TrackPoint(0, 1);
        Vector2 leftFront = TrackPoint(0, TrackWindowBottomProgress);
        Vector2 rightBack = TrackPoint(LaneCount, TrackBackProgress);
        Vector2 rightJudgment = TrackPoint(LaneCount, 1);
        Vector2 rightFront = TrackPoint(LaneCount, TrackWindowBottomProgress);
        DrawPolyline(new[] { leftBack, leftJudgment, leftFront },
            new Color(0.2f, 0.35f, 0.78f, 0.2f), 12, true);
        DrawPolyline(new[] { rightBack, rightJudgment, rightFront },
            new Color(0.2f, 0.35f, 0.78f, 0.2f), 12, true);
        DrawPolyline(new[] { leftBack, leftJudgment, leftFront }, TrackEdgeColor, 2.5f, true);
        DrawPolyline(new[] { rightBack, rightJudgment, rightFront }, TrackEdgeColor, 2.5f, true);
    }

    private void DrawGuides()
    {
        int density = GridDensity is 3 or 9 or 18 ? GridDensity : 18;
        int laneStep = LaneCount / density;
        for (int lane = laneStep; lane < LaneCount; lane += laneStep)
        {
            bool majorBoundary = lane % 6 == 0;
            DrawPolyline(
                new[]
                {
                    TrackPoint(lane, TrackBackProgress),
                    TrackPoint(lane, 1),
                    TrackPoint(lane, TrackWindowBottomProgress),
                },
                majorBoundary ? MajorGridColor : MinorGridColor,
                majorBoundary ? 2.5f : 1,
                true);
        }

    }

    private void DrawMovingNotes()
    {
        double rawAudioTime = _audioClock?.CurrentTimeSeconds ?? 0;
        double currentAudioTime = _smokeTest
            ? rawAudioTime % DemoCycleSeconds
            : rawAudioTime;
        foreach (RuntimeNote note in _activeNotes)
        {
            bool missed = _missedNoteIds.Contains(note.ObjectId);
            bool judged = _gameplaySession?.IsJudged(note.ObjectId) == true;
            if (!PlayfieldPresentation.ShouldRenderNoteHead(judged, missed))
            {
                continue;
            }

            double targetTime = _activeTiming.GetAudioTimeSeconds(note.TargetTick);
            double effectiveAudioTime = currentAudioTime;
            if (_smokeTest && targetTime - currentAudioTime < -PostJudgmentLingerSeconds)
            {
                effectiveAudioTime -= DemoCycleSeconds;
            }

            double linearProgress = NoteTravel.GetProgress(
                _activeTiming,
                note.TargetTick,
                effectiveAudioTime,
                ApproachDurationSeconds);
            if (PlayfieldPresentation.IsProgressVisible(
                linearProgress,
                TrackWindowBottomProgress + NoteCullPaddingProgress))
            {
                float progress = (float)PlayfieldPresentation.ApplyAcceleration(
                    linearProgress,
                    TravelAccelerationExponent);
                DrawNote(note.StartLane, note.LaneWidth, progress, note.Color, note.Symbol);
            }
        }
    }

    private void DrawMovingHolds()
    {
        double currentAudioTime = _audioClock?.CurrentTimeSeconds ?? 0;
        foreach (RuntimeHold hold in _activeHolds)
        {
            ChartObjectDefinition definition = hold.Definition;
            bool clipAtJudgmentLine = IsHoldCurrentlyCaught(definition, currentAudioTime);
            foreach ((LanePoint first, LanePoint second) in definition.Path.Zip(
                definition.Path.Skip(1),
                (first, second) => (first, second)))
            {
                int subdivisions = Math.Max(1, (int)Math.Ceiling((second.Tick - first.Tick) / 480d));
                for (int part = 0; part < subdivisions; part++)
                {
                    double fromRatio = part / (double)subdivisions;
                    double toRatio = (part + 1) / (double)subdivisions;
                    long fromTick = first.Tick + (long)Math.Round((second.Tick - first.Tick) * fromRatio);
                    long toTick = first.Tick + (long)Math.Round((second.Tick - first.Tick) * toRatio);
                    DrawHoldSlice(hold, fromTick, toTick, currentAudioTime, clipAtJudgmentLine);
                }
            }
        }
    }

    private bool IsHoldCurrentlyCaught(
        ChartObjectDefinition definition,
        double currentAudioTime)
    {
        double currentTick = _activeTiming.GetTickAtAudioTimeSeconds(currentAudioTime);
        long requirementTick = (long)Math.Round(Math.Clamp(
            currentTick,
            definition.StartTick,
            definition.EndTick));
        InputRequirement requirement = definition.GetInputRequirementAtTick(requirementTick);
        return IsRequirementHeld?.Invoke(requirement) == true;
    }

    private void DrawHoldSlice(
        RuntimeHold hold,
        long fromTick,
        long toTick,
        double currentAudioTime,
        bool clipAtJudgmentLine)
    {
        double fromLinear = NoteTravel.GetProgress(
            _activeTiming, fromTick, currentAudioTime, ApproachDurationSeconds);
        double toLinear = NoteTravel.GetProgress(
            _activeTiming, toTick, currentAudioTime, ApproachDurationSeconds);
        if ((fromLinear < 0 && toLinear < 0) || (fromLinear > 1.07 && toLinear > 1.07))
        {
            return;
        }

        HoldSliceClipMode clipMode = PlayfieldPresentation.GetHoldSliceClipMode(
            fromLinear,
            toLinear,
            clipAtJudgmentLine);
        if (clipMode == HoldSliceClipMode.Hidden)
        {
            return;
        }

        (double fromLane, double fromWidth) = hold.Definition.GetLaneGeometryAtTick(fromTick);
        (double toLane, double toWidth) = hold.Definition.GetLaneGeometryAtTick(toTick);
        if (clipMode is HoldSliceClipMode.ClipFrom or HoldSliceClipMode.ClipTo)
        {
            double judgmentTick = _activeTiming.GetTickAtAudioTimeSeconds(currentAudioTime);
            double clipRatio = Math.Clamp(
                (judgmentTick - fromTick) / (toTick - (double)fromTick),
                0,
                1);
            double clippedLane = fromLane + ((toLane - fromLane) * clipRatio);
            double clippedWidth = fromWidth + ((toWidth - fromWidth) * clipRatio);
            if (clipMode == HoldSliceClipMode.ClipFrom)
            {
                fromLinear = 1;
                fromLane = clippedLane;
                fromWidth = clippedWidth;
            }
            else
            {
                toLinear = 1;
                toLane = clippedLane;
                toWidth = clippedWidth;
            }
        }

        float fromProgress = (float)PlayfieldPresentation.ApplyAcceleration(
            fromLinear,
            TravelAccelerationExponent);
        float toProgress = (float)PlayfieldPresentation.ApplyAcceleration(
            toLinear,
            TravelAccelerationExponent);
        Vector2[] polygon =
        {
            TrackPoint((float)fromLane, fromProgress),
            TrackPoint((float)(fromLane + fromWidth), fromProgress),
            TrackPoint((float)(toLane + toWidth), toProgress),
            TrackPoint((float)toLane, toProgress),
        };
        Color bodyColor = hold.Color;
        bodyColor.A = 0.42f;
        DrawColoredPolygon(polygon, bodyColor);
        DrawLine(polygon[0], polygon[3], new Color(1, 1, 1, 0.55f), 1.5f, true);
        DrawLine(polygon[1], polygon[2], new Color(1, 1, 1, 0.55f), 1.5f, true);
    }

    private void DrawNote(int startLane, int laneWidth, float progress, Color color, NoteSymbol symbol)
    {
        float lanePixelWidth = MathF.Abs(TrackPoint(1, progress).X - TrackPoint(0, progress).X);
        float halfHeight = lanePixelWidth * NoteDepthLaneRatio / 2;
        float trackPixelHeight = MathF.Abs(TrackPoint(0, 1).Y - TrackPoint(0, 0).Y);
        float halfDepthProgress = trackPixelHeight > float.Epsilon
            ? halfHeight / trackPixelHeight
            : 0;
        float farProgress = progress - halfDepthProgress;
        float nearProgress = progress + halfDepthProgress;
        Vector2[] polygon =
        {
            TrackPoint(startLane, farProgress),
            TrackPoint(startLane + laneWidth, farProgress),
            TrackPoint(startLane + laneWidth, nearProgress),
            TrackPoint(startLane, nearProgress),
        };

        DrawColoredPolygon(polygon, color);
        DrawPolyline(Close(polygon), new Color(1, 1, 1, 0.92f), 2, true);

        if (symbol == NoteSymbol.Rel)
        {
            return;
        }

        Vector2 center = (polygon[0] + polygon[1] + polygon[2] + polygon[3]) / 4;
        float noteWidth = polygon[1].DistanceTo(polygon[0]);
        float noteHeight = polygon[3].DistanceTo(polygon[0]);
        float symbolSize = MathF.Max(1, MathF.Min(noteWidth * 0.12f, noteHeight * 0.32f));
        Color symbolColor = new(0.05f, 0.07f, 0.12f, 0.95f);
        switch (symbol)
        {
            case NoteSymbol.RedCross:
                DrawLine(center - new Vector2(symbolSize, symbolSize),
                    center + new Vector2(symbolSize, symbolSize), symbolColor, 3, true);
                DrawLine(center + new Vector2(symbolSize, -symbolSize),
                    center + new Vector2(-symbolSize, symbolSize), symbolColor, 3, true);
                break;
            case NoteSymbol.GreenSquare:
                Vector2[] square =
                {
                    center + new Vector2(-symbolSize, -symbolSize),
                    center + new Vector2(symbolSize, -symbolSize),
                    center + new Vector2(symbolSize, symbolSize),
                    center + new Vector2(-symbolSize, symbolSize),
                };
                DrawPolyline(Close(square), symbolColor, 3, true);
                break;
            case NoteSymbol.BlueCircle:
                DrawCircle(center, symbolSize, symbolColor, false, 3, true);
                break;
        }
    }

    private void DrawJudgmentLine()
    {
        Vector2[] polygon =
        {
            TrackPoint(0, 1 - JudgmentLineHalfDepth),
            TrackPoint(LaneCount, 1 - JudgmentLineHalfDepth),
            TrackPoint(LaneCount, 1 + JudgmentLineHalfDepth),
            TrackPoint(0, 1 + JudgmentLineHalfDepth),
        };
        DrawColoredPolygon(polygon, JudgmentLineColor);
    }

    private Vector2 TrackPoint(float lane, float progress)
    {
        float topY = Size.Y * 0.07f;
        float bottomY = Size.Y * 0.90f;
        float farHalfWidth = MathF.Min(Size.X * 0.025f, 40);
        float nearHalfWidth = MathF.Min(Size.X * 0.46f, 620);
        // Extrapolate the perspective past the judgment line so the road keeps
        // widening along the same rays instead of bending into vertical walls.
        float roadProgress = progress;
        float halfWidth = Mathf.Lerp(farHalfWidth, nearHalfWidth, roadProgress);
        float centerX = Size.X * 0.50f;
        float left = centerX - halfWidth;
        return new Vector2(
            left + (lane / LaneCount) * halfWidth * 2,
            Mathf.Lerp(topY, bottomY, progress));
    }

    private static Vector2[] Close(Vector2[] points)
    {
        var closed = new Vector2[points.Length + 1];
        points.CopyTo(closed, 0);
        closed[^1] = points[0];
        return closed;
    }

    private enum NoteSymbol
    {
        Rel,
        RedCross,
        GreenSquare,
        BlueCircle,
    }

    private readonly record struct RuntimeNote(
        long ObjectId,
        int StartLane,
        int LaneWidth,
        long TargetTick,
        InputCategory Category,
        InputRequirement Requirement,
        Color Color,
        NoteSymbol Symbol)
    {
        public ClickScoringObject ToScoringObject() => new(
            ObjectId,
            TargetTick,
            Category,
            Requirement);
    }

    private readonly record struct RuntimeHold(
        ChartObjectDefinition Definition,
        Color Color);
}
