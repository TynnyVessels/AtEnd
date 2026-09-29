using Godot;

namespace AtEnd.Editor;

public partial class PlaybackGlyph : Control
{
    private bool _playing;

    public bool Playing
    {
        get => _playing;
        set
        {
            _playing = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Color color = new("eeeeef");
        float centerX = Size.X / 2;
        float centerY = Size.Y / 2;
        if (Playing)
        {
            DrawRect(new Rect2(centerX - 5, centerY - 5, 3, 10), color);
            DrawRect(new Rect2(centerX + 2, centerY - 5, 3, 10), color);
        }
        else
        {
            DrawColoredPolygon(new[]
            {
                new Vector2(centerX - 4, centerY - 6),
                new Vector2(centerX + 6, centerY),
                new Vector2(centerX - 4, centerY + 6),
            }, color);
        }
    }
}
