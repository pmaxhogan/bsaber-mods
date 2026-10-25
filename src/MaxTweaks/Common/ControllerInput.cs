using System.Collections.Generic;
using MaxTweaks.Common.Debugging;
using SiraUtil.Tools.FPFC;
using UnityEngine;
using UnityEngine.XR;

namespace MaxTweaks.Common;

internal enum ControllerButton
{
    /// <summary>Right hand primary (A on Quest/Touch).</summary>
    A,

    /// <summary>Right hand secondary (B).</summary>
    B,

    /// <summary>Left hand primary (X).</summary>
    X,

    /// <summary>Left hand secondary (Y).</summary>
    Y,

    /// <summary>Left hand index trigger (pressed past the runtime's click threshold).</summary>
    LeftTrigger,

    /// <summary>Right hand index trigger.</summary>
    RightTrigger,
}

internal enum ControllerHand
{
    Left,
    Right,
}

/// <summary>
/// Face buttons, triggers and right thumbstick, read straight from Unity XR (the game itself binds no
/// face buttons). In FPFC desktop mode there is no controller, so keyboard fallbacks stand in for QA:
/// A = Enter, B = Backspace, X = X, Y = Y, left trigger = Left Ctrl, right trigger = Space, right
/// stick = arrow keys. (SiraUtil's FPFC already uses W/A/S/D/Q/E for the camera, so those are avoided.)
/// Poll from Update/Tick only (main thread).
/// </summary>
internal class ControllerInput
{
    private readonly IFPFCSettings _fpfc;
    private readonly List<InputDevice> _devices = new();
    private readonly Dictionary<ControllerButton, bool> _previous = new();
    private readonly Dictionary<ControllerButton, bool> _current = new();
    private int _lastFrame = -1;

    public ControllerInput(IFPFCSettings fpfc)
    {
        _fpfc = fpfc;
    }

    public bool IsFpfc => _fpfc.Enabled;

    /// <summary>
    /// DEBUG-ONLY HOOK. Set by Common/Debug (the --debug command channel) to inject virtual button
    /// presses and stick values; null in every normal run. Virtual input is OR-ed with real input
    /// inside ReadButton, so GetButtonDown/GetButtonUp edges work exactly as for real presses.
    /// </summary>
    internal IControllerInputOverride? DebugOverride { get; set; }

    public bool GetButton(ControllerButton button)
    {
        Refresh();
        return _current.TryGetValue(button, out var down) && down;
    }

    public bool GetButtonDown(ControllerButton button)
    {
        Refresh();
        return GetButton(button) && !(_previous.TryGetValue(button, out var was) && was);
    }

    public bool GetButtonUp(ControllerButton button)
    {
        Refresh();
        return !GetButton(button) && _previous.TryGetValue(button, out var was) && was;
    }

    /// <summary>
    /// A "press" by <paramref name="hand"/>: A (kept for everyone used to it) or that hand's trigger,
    /// on the frame it goes down.
    /// </summary>
    public bool PressDown(ControllerHand hand) =>
        GetButtonDown(ControllerButton.A)
        || GetButtonDown(hand == ControllerHand.Left ? ControllerButton.LeftTrigger : ControllerButton.RightTrigger);

    /// <summary>Right thumbstick, x = left/right, y = forward(+)/back(-), each in [-1, 1].</summary>
    public Vector2 GetRightStick()
    {
        // DEBUG-ONLY HOOK (see DebugOverride).
        if (DebugOverride != null && DebugOverride.TryGetRightStick(out var debugStick))
        {
            return debugStick;
        }

        if (_fpfc.Enabled)
        {
            var x = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            var y = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            return new Vector2(x, y);
        }

        var device = GetDevice(XRNode.RightHand);
        return device.isValid && device.TryGetFeatureValue(CommonUsages.primary2DAxis, out var axis) ? axis : Vector2.zero;
    }

    private void Refresh()
    {
        if (_lastFrame == Time.frameCount)
        {
            return;
        }

        _lastFrame = Time.frameCount;
        foreach (ControllerButton button in System.Enum.GetValues(typeof(ControllerButton)))
        {
            _previous[button] = _current.TryGetValue(button, out var down) && down;
            _current[button] = ReadButton(button);
        }
    }

    private bool ReadButton(ControllerButton button)
    {
        // DEBUG-ONLY HOOK (see DebugOverride).
        if (DebugOverride != null && DebugOverride.IsButtonDown(button))
        {
            return true;
        }

        if (_fpfc.Enabled)
        {
            return button switch
            {
                ControllerButton.A => Input.GetKey(KeyCode.Return),
                ControllerButton.B => Input.GetKey(KeyCode.Backspace),
                ControllerButton.X => Input.GetKey(KeyCode.X),
                ControllerButton.Y => Input.GetKey(KeyCode.Y),
                ControllerButton.LeftTrigger => Input.GetKey(KeyCode.LeftControl),
                ControllerButton.RightTrigger => Input.GetKey(KeyCode.Space),
                _ => false,
            };
        }

        var (node, usage) = button switch
        {
            ControllerButton.A => (XRNode.RightHand, CommonUsages.primaryButton),
            ControllerButton.B => (XRNode.RightHand, CommonUsages.secondaryButton),
            ControllerButton.X => (XRNode.LeftHand, CommonUsages.primaryButton),
            ControllerButton.Y => (XRNode.LeftHand, CommonUsages.secondaryButton),
            ControllerButton.LeftTrigger => (XRNode.LeftHand, CommonUsages.triggerButton),
            _ => (XRNode.RightHand, CommonUsages.triggerButton),
        };
        var device = GetDevice(node);
        return device.isValid && device.TryGetFeatureValue(usage, out var pressed) && pressed;
    }

    private InputDevice GetDevice(XRNode node)
    {
        // Devices can be invalidated (controller sleep, runtime restart): re-acquire every call.
        _devices.Clear();
        InputDevices.GetDevicesAtXRNode(node, _devices);
        return _devices.Count > 0 ? _devices[0] : default;
    }
}
