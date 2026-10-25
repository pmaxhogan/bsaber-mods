using System.Collections.Generic;
using MaxTweaks.Core.Debugging;
using UnityEngine;

namespace MaxTweaks.Common.Debugging;

/// <summary>Source of virtual input consulted by ControllerInput's debug-only hook.</summary>
internal interface IControllerInputOverride
{
    bool IsButtonDown(ControllerButton button);

    bool TryGetRightStick(out Vector2 value);
}

/// <summary>
/// Virtual face buttons and right stick driven by the "button" / "stick" debug commands. Timed
/// holds use unscaled real time, so they also expire while the game is paused.
/// </summary>
internal sealed class VirtualController : IControllerInputOverride
{
    // Release time per held button; float.PositiveInfinity = held until "up".
    private readonly Dictionary<ControllerButton, float> _heldUntil = new();
    private Vector2 _stick;
    private float _stickUntil;
    private bool _stickActive;

    public bool IsButtonDown(ControllerButton button) =>
        _heldUntil.TryGetValue(button, out var until) && Time.realtimeSinceStartup < until;

    public bool TryGetRightStick(out Vector2 value)
    {
        if (_stickActive && Time.realtimeSinceStartup >= _stickUntil)
        {
            _stickActive = false;
        }

        value = _stickActive ? _stick : Vector2.zero;
        return _stickActive;
    }

    public void Apply(ButtonRequest request)
    {
        var button = ToControllerButton(request.Button);
        var now = Time.realtimeSinceStartup;
        switch (request.Action)
        {
            case DebugButtonAction.Up:
                _heldUntil.Remove(button);
                break;
            case DebugButtonAction.Down:
                _heldUntil[button] = request.Seconds > 0f ? now + request.Seconds : float.PositiveInfinity;
                break;
            default:
                _heldUntil[button] = now + request.Seconds;
                break;
        }
    }

    public void Apply(StickRequest request)
    {
        _stick = new Vector2(request.X, request.Y);
        _stickActive = _stick != Vector2.zero;
        _stickUntil = request.Seconds > 0f ? Time.realtimeSinceStartup + request.Seconds : float.PositiveInfinity;
    }

    public void ReleaseAll()
    {
        _heldUntil.Clear();
        _stickActive = false;
    }

    public string Describe()
    {
        var held = new List<string>();
        foreach (var pair in _heldUntil)
        {
            if (IsButtonDown(pair.Key))
            {
                held.Add(pair.Key.ToString());
            }
        }

        TryGetRightStick(out var stick);
        return $"virtualButtons=[{string.Join(",", held)}] virtualStick={stick.x:0.##},{stick.y:0.##}";
    }

    private static ControllerButton ToControllerButton(DebugButton button) => button switch
    {
        DebugButton.A => ControllerButton.A,
        DebugButton.B => ControllerButton.B,
        DebugButton.X => ControllerButton.X,
        DebugButton.Y => ControllerButton.Y,
        DebugButton.LeftTrigger => ControllerButton.LeftTrigger,
        DebugButton.RightTrigger => ControllerButton.RightTrigger,
        _ => throw new System.ArgumentOutOfRangeException(nameof(button), button, null),
    };
}
