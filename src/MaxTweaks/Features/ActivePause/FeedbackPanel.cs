using System;
using System.Collections.Generic;
using BeatSaberMarkupLanguage;
using MaxTweaks.Common.Pointing;
using MaxTweaks.Core.ActivePause.Flags;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MaxTweaks.Features.ActivePause;

/// <summary>
/// The flag feedback panel, shown instead of the <see cref="StepButtonPanel"/> (never both): a title
/// line, three columns of tap-to-toggle labels (<see cref="FlagLabel.All"/>: Good, Bad, Note; lit
/// gold when on, several may be on) and a bottom row with Save and Cancel. Same place as the step
/// panel (right of the lanes, low), larger plate standing on the floor. Ray + press and UI pointer
/// clicks both go through <see cref="Press"/>.
/// </summary>
internal sealed class FeedbackPanel : IDisposable
{
    // Canvas units (WorldCanvas.Scale: 1 unit = 2 cm).
    private const float ColumnWidth = 20f;
    private const float ButtonHeight = 3.8f;
    private const float Gap = 0.6f;
    private const float Padding = 1.2f;
    private const float HeaderHeight = 3.2f;
    private const float TitleHeight = 3.6f;
    private const float FontSize = 2.8f;
    private const float FloorClearance = 0.08f;

    private static readonly Color PlateColor = new(0f, 0f, 0f, 0.7f);
    private static readonly Color ButtonBackColor = new(1f, 1f, 1f, 0.08f);
    private static readonly Color LitBackColor = new(1f, 0.72f, 0.05f, 0.22f);
    private static readonly Color HeaderColor = new(1f, 1f, 1f, 0.55f);
    private static readonly FlagGroup[] Groups = { FlagGroup.Good, FlagGroup.Bad, FlagGroup.Note };

    private readonly GameObject _root;
    private readonly List<RayHoverButton> _buttons = new();
    private readonly List<Image> _backs = new();
    private readonly RayHoverTracker _rayHover = new();
    private readonly Action<IReadOnlyList<string>> _onSave;
    private readonly Action _onCancel;
    private readonly TextMeshProUGUI _title;

    /// <param name="eyeHeight">Player eye height (m); the panel faces a point this high above the origin.</param>
    /// <param name="onSave">Save pressed, with the lit label ids in catalogue order.</param>
    /// <param name="onCancel">Cancel pressed.</param>
    public FeedbackPanel(float eyeHeight, Action<IReadOnlyList<string>> onSave, Action onCancel)
    {
        _onSave = onSave;
        _onCancel = onCancel;
        var labels = FlagLabel.All;
        var maxRows = 0;
        foreach (var group in Groups)
        {
            var count = 0;
            foreach (var label in labels)
            {
                if (label.Group == group)
                {
                    count++;
                }
            }

            maxRows = Math.Max(maxRows, count);
        }

        var size = new Vector2(
            (Groups.Length * ColumnWidth) + ((Groups.Length - 1) * Gap) + (2 * Padding),
            TitleHeight + HeaderHeight + (maxRows * (ButtonHeight + Gap)) + Gap + ButtonHeight + (2 * Padding));

        // Same spot as the step panel, raised so the taller plate stands just above the floor.
        var center = StepButtonPanel.Center;
        center.y = Math.Max(center.y, FloorClearance + (size.y * WorldCanvas.Scale * 0.5f));
        var eye = new Vector3(0f, eyeHeight, 0f);
        var rotation = Quaternion.LookRotation(center - eye, Vector3.up);
        _root = WorldCanvas.Create("MaxTweaksActivePauseFlag", "ActivePause", size, center, rotation);

        var parent = (RectTransform)_root.transform;
        var plate = WorldCanvas.CreateImage("Plate", parent, PlateColor, "ActivePause");
        plate.rectTransform.anchorMin = Vector2.zero;
        plate.rectTransform.anchorMax = Vector2.one;
        plate.rectTransform.offsetMin = plate.rectTransform.offsetMax = Vector2.zero;

        var top = (size.y * 0.5f) - Padding;
        _title = CreateText(parent, "Flag", new Vector2(0f, top - (TitleHeight * 0.5f)), new Vector2(size.x - (2 * Padding), TitleHeight), FontSize);
        _title.color = Color.white;

        var headerY = top - TitleHeight - (HeaderHeight * 0.5f);
        var firstRowY = top - TitleHeight - HeaderHeight - (ButtonHeight * 0.5f);
        for (var column = 0; column < Groups.Length; column++)
        {
            var x = -(size.x * 0.5f) + Padding + (column * (ColumnWidth + Gap)) + (ColumnWidth * 0.5f);
            var header = CreateText(parent, Groups[column].ToString(), new Vector2(x, headerY), new Vector2(ColumnWidth, HeaderHeight), FontSize * 0.85f);
            header.color = HeaderColor;

            var row = 0;
            for (var i = 0; i < labels.Count; i++)
            {
                if (labels[i].Group != Groups[column])
                {
                    continue;
                }

                // FlagLabel.All is grouped Good, Bad, Note in that order, so creating the buttons
                // column by column keeps _buttons indexed exactly like FlagLabel.All.
                if (_buttons.Count != i)
                {
                    throw new InvalidOperationException("flag labels are not grouped in column order");
                }

                var y = firstRowY - (row * (ButtonHeight + Gap));
                CreateButton(parent, labels[i].Text, new Vector2(x, y), new Vector2(ColumnWidth, ButtonHeight), i);
                row++;
            }
        }


        var bottomY = -(size.y * 0.5f) + Padding + (ButtonHeight * 0.5f);
        var halfWidth = ((size.x - (2 * Padding)) - Gap) * 0.5f;
        var cell = new Vector2(halfWidth, ButtonHeight);
        CreateButton(parent, "Save", new Vector2(-(Gap + halfWidth) * 0.5f, bottomY), cell, SaveIndex);
        CreateButton(parent, "Cancel", new Vector2((Gap + halfWidth) * 0.5f, bottomY), cell, CancelIndex);

        _root.SetActive(false);
    }

    /// <summary>Button index of Save (after the labels).</summary>
    public static int SaveIndex => FlagLabel.All.Count;

    /// <summary>Button index of Cancel.</summary>
    public static int CancelIndex => FlagLabel.All.Count + 1;

    public bool Visible => _root != null && _root.activeSelf;

    /// <summary>Ids of the lit labels, in catalogue order.</summary>
    public IReadOnlyList<string> LitLabels
    {
        get
        {
            var lit = new List<string>();
            for (var i = 0; i < FlagLabel.All.Count; i++)
            {
                if (_buttons[i].Lit)
                {
                    lit.Add(FlagLabel.All[i].Id);
                }
            }

            return lit;
        }
    }

    /// <summary>Shows the panel with <paramref name="title"/> on top (e.g. "Flag at 42.317s, 2 notes").</summary>
    public void Show(string title)
    {
        _title.text = title;
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

    /// <summary>Index of the button hit by <paramref name="ray"/> (label index, <see cref="SaveIndex"/>, <see cref="CancelIndex"/>), or -1.</summary>
    public int HitTest(Ray ray)
    {
        if (!Visible || !PointerRays.TryHitPlane(_root.transform, ray, out var point))
        {
            return -1;
        }

        for (var i = 0; i < _buttons.Count; i++)
        {
            if (_buttons[i].Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Highlights button <paramref name="index"/> as ray-hovered (-1 clears it).</summary>
    public void SetRayHover(int index) => _rayHover.Set(index >= 0 && index < _buttons.Count ? _buttons[index] : null);

    /// <summary>Toggles a label, or runs Save / Cancel.</summary>
    public void Press(int index)
    {
        if (index == SaveIndex)
        {
            _onSave(LitLabels);
        }
        else if (index == CancelIndex)
        {
            _onCancel();
        }
        else if (index >= 0 && index < FlagLabel.All.Count)
        {
            SetLit(index, !_buttons[index].Lit);
        }
    }

    /// <summary>Toggles the label with this id; false when there is none.</summary>
    public bool Toggle(string id)
    {
        for (var i = 0; i < FlagLabel.All.Count; i++)
        {
            if (FlagLabel.All[i].Id == id)
            {
                Press(i);
                return true;
            }
        }

        return false;
    }

    public void ClearLabels()
    {
        for (var i = 0; i < FlagLabel.All.Count; i++)
        {
            SetLit(i, false);
        }
    }

    public void Dispose()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
        }
    }

    private void SetLit(int index, bool lit)
    {
        _buttons[index].SetLit(lit);
        _backs[index].color = lit ? LitBackColor : ButtonBackColor;
    }

    private void CreateButton(RectTransform parent, string label, Vector2 position, Vector2 cell, int index)
    {
        var back = WorldCanvas.CreateImage("Back" + index, parent, ButtonBackColor, "ActivePause");
        back.rectTransform.anchoredPosition = position;
        back.rectTransform.sizeDelta = cell;

        var text = CreateText(parent, label, position, cell, FontSize);
        text.raycastTarget = true;
        var button = text.gameObject.AddComponent<RayHoverButton>();
        button.Init(text, "ActivePause", () => Press(index));
        button.SetHoverScale(1.1f);
        _buttons.Add(button);
        _backs.Add(back);
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string text, Vector2 position, Vector2 cell, float fontSize)
    {
        var label = BeatSaberUI.CreateText(parent, text, position, cell);
        label.fontSize = fontSize;
        label.enableAutoSizing = true;
        label.fontSizeMin = fontSize * 0.6f;
        label.fontSizeMax = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.richText = false;
        label.raycastTarget = false;
        return label;
    }
}
