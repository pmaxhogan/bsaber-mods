using System.Collections.Generic;
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
}

/// <summary>
/// Face buttons and right thumbstick, read straight from Unity XR (the game itself binds no face
/// buttons). In FPFC desktop mode there is no controller, so keyboard fallbacks stand in for QA:
/// A = Enter, B = Backspace, X = X, Y = Y, right stick = arrow keys. (SiraUtil's FPFC already uses
/// W/A/S/D/Q/E for the camera, so those are avoided.)
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

    /// <summary>Right thumbstick, x = left/right, y = forward(+)/back(-), each in [-1, 1].</summary>
    public Vector2 GetRightStick()
    {
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
        if (_fpfc.Enabled)
        {
            return button switch
            {
                ControllerButton.A => Input.GetKey(KeyCode.Return),
                ControllerButton.B => Input.GetKey(KeyCode.Backspace),
                ControllerButton.X => Input.GetKey(KeyCode.X),
                _ => Input.GetKey(KeyCode.Y),
            };
        }

        var (node, usage) = button switch
        {
            ControllerButton.A => (XRNode.RightHand, CommonUsages.primaryButton),
            ControllerButton.B => (XRNode.RightHand, CommonUsages.secondaryButton),
            ControllerButton.X => (XRNode.LeftHand, CommonUsages.primaryButton),
            _ => (XRNode.LeftHand, CommonUsages.secondaryButton),
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
