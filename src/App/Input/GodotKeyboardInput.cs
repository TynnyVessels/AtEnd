using System;
using System.Collections.Generic;
using System.Linq;
using AtEnd.Core;
using Godot;

namespace AtEnd.App.Input;

public sealed class GodotKeyboardInput
{
    private static readonly IReadOnlyDictionary<PhysicalKey, Key> GodotPhysicalKeys =
        new Dictionary<PhysicalKey, Key>
        {
            [PhysicalKey.A] = Key.A,
            [PhysicalKey.S] = Key.S,
            [PhysicalKey.D] = Key.D,
            [PhysicalKey.Z] = Key.Z,
            [PhysicalKey.X] = Key.X,
            [PhysicalKey.C] = Key.C,
            [PhysicalKey.F] = Key.F,
            [PhysicalKey.G] = Key.G,
            [PhysicalKey.H] = Key.H,
            [PhysicalKey.J] = Key.J,
            [PhysicalKey.V] = Key.V,
            [PhysicalKey.B] = Key.B,
            [PhysicalKey.N] = Key.N,
            [PhysicalKey.K] = Key.K,
            [PhysicalKey.L] = Key.L,
            [PhysicalKey.Semicolon] = Key.Semicolon,
            [PhysicalKey.M] = Key.M,
            [PhysicalKey.Comma] = Key.Comma,
            [PhysicalKey.Period] = Key.Period,
            [PhysicalKey.Digit1] = Key.Key1,
            [PhysicalKey.Digit2] = Key.Key2,
            [PhysicalKey.Digit3] = Key.Key3,
            [PhysicalKey.Digit4] = Key.Key4,
            [PhysicalKey.Q] = Key.Q,
            [PhysicalKey.W] = Key.W,
            [PhysicalKey.E] = Key.E,
            [PhysicalKey.R] = Key.R,
            [PhysicalKey.Digit5] = Key.Key5,
            [PhysicalKey.Digit6] = Key.Key6,
            [PhysicalKey.Digit7] = Key.Key7,
            [PhysicalKey.Digit8] = Key.Key8,
            [PhysicalKey.T] = Key.T,
            [PhysicalKey.Y] = Key.Y,
            [PhysicalKey.U] = Key.U,
            [PhysicalKey.Digit9] = Key.Key9,
            [PhysicalKey.Digit0] = Key.Key0,
            [PhysicalKey.Minus] = Key.Minus,
            [PhysicalKey.Equals] = Key.Equal,
            [PhysicalKey.I] = Key.I,
            [PhysicalKey.O] = Key.O,
            [PhysicalKey.P] = Key.P,
            [PhysicalKey.LeftBracket] = Key.Bracketleft,
        };

    private readonly LogicalInputState<Key> _state = new(GodotBindings);

    public bool TryHandle(InputEventKey keyEvent, out PressEvent? pressEvent)
    {
        ArgumentNullException.ThrowIfNull(keyEvent);
        pressEvent = null;
        Key physicalKey = keyEvent.PhysicalKeycode;
        if (!GodotBindings.ContainsKey(physicalKey))
        {
            return false;
        }

        if (keyEvent.Pressed)
        {
            if (!keyEvent.Echo)
            {
                pressEvent = _state.Press(physicalKey);
            }
        }
        else
        {
            _state.Release(physicalKey);
        }

        return true;
    }

    public bool IsRequirementHeld(InputRequirement requirement) =>
        _state.IsRequirementHeld(requirement);

    private static IReadOnlyDictionary<Key, LogicalChannel> GodotBindings { get; } =
        CreateGodotBindings();

    private static IReadOnlyDictionary<Key, LogicalChannel> CreateGodotBindings()
    {
        return DefaultKeyboardBindings.All.ToDictionary(
            binding => GodotPhysicalKeys[binding.Key],
            binding => binding.Value);
    }
}
