using System;
using Godot;

namespace AtEnd.Editor;

public partial class VerticalSeekBar : Control
{
    private const float HandleHeight = 12;
    private double _value;
    private bool _dragging;

    public event Action<double>? ValueChanged;
    public event Action? DragStarted;
    public event Action<bool>? DragEnded;

    public double MinValue { get; set; }
    public double MaxValue { get; set; } = 1;
    public double Step { get; set; } = 0.001;
    public bool Editable { get; set; } = true;
    public bool IsDragging => _dragging;

    public double Value
    {
        get => _value;
        set => SetValue(value);
    }

    public float HandleTopPixels
    {
        get
        {
            double normalized = GetNormalizedValue();
            return (float)((1 - normalized) * Math.Max(0, Size.Y - HandleHeight));
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Editable || @event is not InputEventMouseButton mouse
            || mouse.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (mouse.Pressed)
        {
            BeginDrag();
            UpdateFromMouse();
        }
        else
        {
            EndDrag();
        }

        AcceptEvent();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!_dragging)
        {
            return;
        }

        if (!Input.IsMouseButtonPressed(MouseButton.Left))
        {
            EndDrag();
            return;
        }

        UpdateFromMouse();
    }

    public override void _Draw()
    {
        float centerX = Size.X / 2;
        float height = Math.Max(Size.Y, 1);
        float handleTop = HandleTopPixels;
        float handleCenter = handleTop + (HandleHeight / 2);

        DrawLine(
            new Vector2(centerX, 0),
            new Vector2(centerX, height),
            new Color("3a3a3e"),
            6);
        DrawLine(
            new Vector2(centerX, handleCenter),
            new Vector2(centerX, height),
            new Color("8d8d91"),
            6);
        DrawRect(
            new Rect2(2, handleTop, Math.Max(1, Size.X - 4), HandleHeight),
            new Color("dfdfe1"));
    }

    private void BeginDrag()
    {
        if (_dragging)
        {
            return;
        }

        _dragging = true;
        DragStarted?.Invoke();
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        DragEnded?.Invoke(true);
    }

    private void UpdateFromMouse()
    {
        float mouseY = GetLocalMousePosition().Y;
        double normalized = 1 - Math.Clamp(mouseY / Math.Max(Size.Y, 1), 0, 1);
        SetValue(MinValue + (normalized * Math.Max(0, MaxValue - MinValue)));
    }

    private void SetValue(double value)
    {
        double clamped = Math.Clamp(value, MinValue, MaxValue);
        if (Step > 0)
        {
            clamped = MinValue + (Math.Round((clamped - MinValue) / Step) * Step);
            clamped = Math.Clamp(clamped, MinValue, MaxValue);
        }

        if (Math.Abs(_value - clamped) < 0.0000001)
        {
            return;
        }

        _value = clamped;
        QueueRedraw();
        ValueChanged?.Invoke(_value);
    }

    private double GetNormalizedValue()
    {
        double range = MaxValue - MinValue;
        return range <= 0 ? 0 : Math.Clamp((_value - MinValue) / range, 0, 1);
    }
}
