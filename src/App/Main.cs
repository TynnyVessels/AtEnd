using AtEnd.App.Input;
using AtEnd.App.Playfield;
using AtEnd.Core;
using Godot;

namespace AtEnd.App;

public partial class Main : Control
{
    private readonly GodotKeyboardInput _keyboardInput = new();
    private Label? _inputStatus;
    private Label? _judgmentStatus;
    private Label? _scoreStatus;
    private PlayfieldView? _playfield;

    public override void _Ready()
    {
        _inputStatus = GetNode<Label>("InputStatus");
        _judgmentStatus = GetNode<Label>("JudgmentStatus");
        _scoreStatus = GetNode<Label>("ScoreStatus");
        _playfield = GetNode<PlayfieldView>("Playfield");
        _playfield.JudgmentResolved += OnJudgmentResolved;
        _playfield.CycleCompleted += OnCycleCompleted;
        UpdateScore(_playfield.CurrentScore);
        GD.Print("AtEnd development shell is ready.");
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent
            || !_keyboardInput.TryHandle(keyEvent, out PressEvent? pressEvent))
        {
            return;
        }

        GetViewport().SetInputAsHandled();
        if (pressEvent is not { } press)
        {
            return;
        }

        string message = $"Last physical input: {press.Channel} (event {press.EventId})";
        if (_inputStatus is not null)
        {
            _inputStatus.Text = message;
        }

        _playfield?.QueuePress(press);
        GD.Print(message);
    }

    private void OnJudgmentResolved(GameplayJudgment result, ScoreSnapshot score)
    {
        string judgmentName = result.Judgment switch
        {
            Judgment.StrictlyPrecise => "STRICTLY PRECISE",
            Judgment.Precise => "PRECISE",
            Judgment.Misaligned => "MISALIGNED",
            _ => "CHAOTIC",
        };
        string direction = result.Direction switch
        {
            TimingDirection.Early => " · EARLY",
            TimingDirection.Late => " · LATE",
            _ => string.Empty,
        };
        if (_judgmentStatus is not null)
        {
            _judgmentStatus.Text = judgmentName + direction;
        }

        UpdateScore(score);
    }

    private void OnCycleCompleted(ScoreSnapshot score)
    {
        if (_judgmentStatus is not null)
        {
            _judgmentStatus.Text = score.HighestCompletionMark.DisplayName() ?? "CYCLE COMPLETE";
        }

        UpdateScore(score);
    }

    private void UpdateScore(ScoreSnapshot score)
    {
        if (_scoreStatus is null)
        {
            return;
        }

        _scoreStatus.Text = $"SCORE  {score.TotalScore:N0}\n"
            + $"ACC  {score.AccuracyPercent:F2}%   SP  {score.StrictlyPreciseBonus}\n"
            + $"COMBO  {score.CurrentCombo}   MAX  {score.MaximumCombo}";
    }
}
