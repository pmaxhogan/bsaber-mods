using System;
using System.Collections.Generic;
using BeatSaberMarkupLanguage;
using MaxTweaks.Common.Pointing;
using MaxTweaks.Core.ActivePause;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MaxTweaks.Features.ActivePause;

/// <summary>
/// The twelve seek buttons, in two rows of six (backward steps on top, forward below), and a third
/// row with Flag (opens the <see cref="FeedbackPanel"/>; shows the selected note count) and Clear
/// (deselects every note), on a small world-space panel low and to the right of the note lanes,
/// turned to face the player's eye. Hidden except while active-paused. Built on
/// <see cref="WorldCanvas"/> so a UI pointer can click the buttons too.
/// </summary>
internal sealed class StepButtonPanel : IDisposable
{
    /// <summary>Button index of Flag (after the steps 0-11).</summary>
    public const int FlagIndex = 12;

    /// <summary>Button index of Clear.</summary>
    public const int ClearIndex = 13;

    // Canvas units (WorldCanvas.Scale: 1 unit = 2 cm).
    private const int Columns = 6;
    private const float ButtonWidth = 8f;
    private const float ButtonHeight = 4.4f;
    private const float Gap = 0.8f;
    private const float Padding = 1.2f;
    private const float FontSize = 3.2f;

    // Panel centre (metres): right of the lanes, below the lowest notes, a little ahead.
    public static readonly Vector3 Center = new(0.75f, 0.42f, 1.9f);

    private static readonly Color PlateColor = new(0f, 0f, 0f, 0.55f);
    private static readonly Color ButtonBackColor = new(1f, 1f, 1f, 0.08f);

    private readonly GameObject _root;
    private readonly List<RayHoverButton> _buttons = new();
    private readonly List<Image> _backs = new();
    private readonly RayHoverTracker _rayHover = new();

    /// <param name="eyeHeight">Player eye height (m); the panel faces a point this high above the origin.</param>
    /// <param name="onClick">
    /// Called with the button index on a pointer click: an index into <see cref="ActivePauseStep.All"/>,
    /// <see cref="FlagIndex"/> or <see cref="ClearIndex"/>.
    /// </param>
    public StepButtonPanel(float eyeHeight, Action<int> onClick)
    {
        var steps = ActivePauseStep.All;
        var stepRows = (steps.Count + Columns - 1) / Columns;
        var rows = stepRows + 1;
        var size = new Vector2(
            (Columns * ButtonWidth) + ((Columns - 1) * Gap) + (2 * Padding),
            (rows * ButtonHeight) + ((rows - 1) * Gap) + (2 * Padding));
        var eye = new Vector3(0f, eyeHeight, 0f);
        var rotation = Quaternion.LookRotation(Center - eye, Vector3.up);
        _root = WorldCanvas.Create("MaxTweaksActivePause", "ActivePause", size, Center, rotation);

        var parent = (RectTransform)_root.transform;
        var plate = CreateImage("Plate", parent, PlateColor);
        plate.rectTransform.anchorMin = Vector2.zero;
        plate.rectTransform.anchorMax = Vector2.one;
        plate.rectTransform.offsetMin = plate.rectTransform.offsetMax = Vector2.zero;

        var cell = new Vector2(ButtonWidth, ButtonHeight);
        for (var i = 0; i < steps.Count; i++)
        {
            var row = i / Columns;
            var column = i % Columns;
            var x = -(size.x * 0.5f) + Padding + (column * (ButtonWidth + Gap)) + (ButtonWidth * 0.5f);
            var y = RowY(size, row);
            CreateButton(parent, steps[i].Label, new Vector2(x, y), cell, i, onClick);
        }

        // Flag and Clear share the last row, three columns each.
        var wide = new Vector2((3 * ButtonWidth) + (2 * Gap), ButtonHeight);
        var lastY = RowY(size, stepRows);
        var leftX = -(size.x * 0.5f) + Padding + (wide.x * 0.5f);
        CreateButton(parent, "Flag", new Vector2(leftX, lastY), wide, FlagIndex, onClick).SetHoverScale(1.15f);
        CreateButton(parent, "Clear", new Vector2(-leftX, lastY), wide, ClearIndex, onClick).SetHoverScale(1.15f);
        SetSelectedCount(0);

        _root.SetActive(false);
    }

    public bool Visible => _root != null && _root.activeSelf;

    public void Show()
    {
        if (_root != null && !_root.activeSelf)
        {
            _root.SetActive(true);
        }
    }

    public void Hide()
    {
        _rayHover.Set(null);
        if (_root != null && _root.activeSelf)
        {
            _root.SetActive(false);
        }
    }

    /// <summary>Index of the (shown) button hit by <paramref name="ray"/>, or -1.</summary>
    public int HitTest(Ray ray)
    {
        if (!Visible || !PointerRays.TryHitPlane(_root.transform, ray, out var point))
        {
            return -1;
        }

        for (var i = 0; i < _buttons.Count; i++)
        {
            if (_buttons[i].gameObject.activeSelf && _buttons[i].Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Highlights button <paramref name="index"/> as ray-hovered (-1 clears it).</summary>
    public void SetRayHover(int index) => _rayHover.Set(index >= 0 && index < _buttons.Count ? _buttons[index] : null);

    /// <summary>"Flag" or "Flag (n)"; Clear is dimmed when nothing is selected.</summary>
    public void SetSelectedCount(int count)
    {
        _buttons[FlagIndex].SetLabel(count > 0 ? $"Flag ({count})" : "Flag");
        _buttons[ClearIndex].SetDimmed(count == 0);
    }

    /// <summary>Hides the Flag button (levels without a map folder cannot be flagged).</summary>
    public void SetFlagAvailable(bool available)
    {
        _buttons[FlagIndex].gameObject.SetActive(available);
        _backs[FlagIndex].gameObject.SetActive(available);
    }

    public void Dispose()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
        }
    }

    private static float RowY(Vector2 size, int row) =>
        (size.y * 0.5f) - Padding - (row * (ButtonHeight + Gap)) - (ButtonHeight * 0.5f);

    private RayHoverButton CreateButton(RectTransform parent, string label, Vector2 position, Vector2 cell, int index, Action<int> onClick)
    {
        var back = CreateImage("Back" + index, parent, ButtonBackColor);
        back.rectTransform.anchoredPosition = position;
        back.rectTransform.sizeDelta = cell;

        var text = BeatSaberUI.CreateText(parent, label, position, cell);
        text.fontSize = FontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.richText = false;
        text.raycastTarget = true;
        var button = text.gameObject.AddComponent<RayHoverButton>();
        button.Init(text, "ActivePause", () => onClick(index));
        _buttons.Add(button);
        _backs.Add(back);
        return button;
    }

    private static Image CreateImage(string name, RectTransform parent, Color color) =>
        WorldCanvas.CreateImage(name, parent, color, "ActivePause");
}
