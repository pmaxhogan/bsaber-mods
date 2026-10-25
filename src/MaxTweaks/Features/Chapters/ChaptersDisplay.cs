using System;
using BeatSaberMarkupLanguage;
using MaxTweaks.Common.Pointing;
using MaxTweaks.Core.Chapters;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MaxTweaks.Features.Chapters;

/// <summary>
/// World-space panel above the notes (Counters+ "AboveHighway" area: x 0, about 3 m up, 7 m ahead),
/// one row per active chapter, bottom row = longest chapter. Each row is "&lt;  Name  &gt;" plus a thin
/// progress bar. Rows are pre-built; per frame only visibility, the bar anchor and hover state change,
/// and text only when a row starts showing a different chapter.
/// Built on a BSML FloatingScreen so the pause-menu UI pointers can hover/click the arrows (it brings
/// a VRGraphicRaycaster); falls back to a plain canvas (A-button only) if that fails.
/// </summary>
internal sealed class ChaptersDisplay : IDisposable
{
    // Canvas units; the screen is scaled 0.02 so 1 unit = 2 cm.
    private const float Scale = WorldCanvas.Scale;
    private const float RowHeight = 18f;
    private const float Width = 220f;
    private const float LabelMaxWidth = 160f;
    private const float LabelMinWidth = 24f;
    private const float ArrowWidth = 18f;
    private const float Gap = 4f;
    private const float BarHeight = 0.9f;
    private const float LabelFontSize = 11f;

    // The label rect must be taller than one line of the font: with Ellipsis overflow TMP draws
    // nothing at all when the first line does not fit vertically (Teko's line height is about 1.4x the
    // font size, so 11 needs ~16 units). SetLabel also grows it from the measured preferred height.
    private const float LabelBoxHeight = 30f;
    private const float ArrowFontSize = 15f;

    private static readonly Color LabelColor = new(1f, 1f, 1f, 0.9f);
    private static readonly Color BarBackColor = new(1f, 1f, 1f, 0.15f);
    private static readonly Color BarFillColor = new(1f, 1f, 1f, 0.7f);

    private readonly GameObject _root;
    private readonly RowView[] _rows;
    private readonly bool _showBars;
    private readonly RayHoverTracker _rayHover = new();

    /// <param name="bottomY">World height of the stack's bottom edge (see Core ChaptersPlacement); the
    /// stack is centred over the track (x 0) at ChaptersPlacement.StackZ and grows upwards.</param>
    public ChaptersDisplay(int rowCount, float bottomY, bool showBars, bool showArrows, Action<int, bool> onArrowClicked)
    {
        _showBars = showBars;
        var size = new Vector2(Width, rowCount * RowHeight);
        var center = new Vector3(0f, bottomY + (size.y * Scale * 0.5f), ChaptersPlacement.StackZ);
        _root = WorldCanvas.Create("MaxTweaksChapters", "Chapters", size, center, Quaternion.identity);

        var parent = (RectTransform)_root.transform;
        _rows = new RowView[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            _rows[i] = new RowView(parent, i, showBars, showArrows, onArrowClicked);
        }
    }

    public int RowCount => _rows.Length;

    /// <summary>Shows <paramref name="chapterIndex"/> on <paramref name="row"/> with its progress (0..1).</summary>
    public void SetRow(int row, int chapterIndex, string label, float progress)
    {
        var view = _rows[row];
        if (!view.Active)
        {
            view.Active = true;
            view.Root.SetActive(true);
        }

        if (view.ChapterIndex != chapterIndex)
        {
            view.ChapterIndex = chapterIndex;
            view.SetLabel(label);
            view.Progress = -1f;
        }

        if (_showBars && Mathf.Abs(progress - view.Progress) > 0.0005f)
        {
            view.Progress = progress;
            view.BarFill.anchorMax = new Vector2(progress, 1f);
        }
    }

    /// <summary>Hides <paramref name="row"/> (no-op if already hidden).</summary>
    public void HideRow(int row)
    {
        var view = _rows[row];
        if (!view.Active)
        {
            return;
        }

        if (_rayHover.Current is ChapterArrowButton hovered && hovered.Row == row)
        {
            SetRayHover(null);
        }

        view.Active = false;
        view.ChapterIndex = -1;
        view.Root.SetActive(false);
    }

    /// <summary>True while any arrow is hovered, by the saber ray or by the pause-menu pointer.</summary>
    public bool AnyArrowHovered
    {
        get
        {
            foreach (var view in _rows)
            {
                if (view.Active && ((view.Left != null && view.Left.IsHovered) || (view.Right != null && view.Right.IsHovered)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Chapter index currently shown on <paramref name="row"/>, or -1.</summary>
    public int ChapterAt(int row) => row >= 0 && row < _rows.Length && _rows[row].Active ? _rows[row].ChapterIndex : -1;

    /// <summary>Finds the arrow of a visible row hit by <paramref name="ray"/>.</summary>
    public ChapterArrowButton? HitTest(Ray ray)
    {
        if (!PointerRays.TryHitPlane(_root.transform, ray, out var point))
        {
            return null;
        }

        for (var i = 0; i < _rows.Length; i++)
        {
            var view = _rows[i];
            if (!view.Active)
            {
                continue;
            }

            if (view.Left != null && view.Left.Contains(point))
            {
                return view.Left;
            }

            if (view.Right != null && view.Right.Contains(point))
            {
                return view.Right;
            }
        }

        return null;
    }

    /// <summary>Highlights the arrow the saber ray points at (null clears it).</summary>
    public void SetRayHover(ChapterArrowButton? arrow) => _rayHover.Set(arrow);

    public void Dispose()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
        }
    }

    private static Image CreateImage(string name, RectTransform parent, Color color) =>
        WorldCanvas.CreateImage(name, parent, color, "Chapters");

    private static bool loggedMetrics;

    private sealed class RowView
    {
        public RowView(RectTransform parent, int index, bool showBars, bool showArrows, Action<int, bool> onArrowClicked)
        {
            Root = new GameObject($"Row{index}", typeof(RectTransform)) { layer = 5 };
            var rect = (RectTransform)Root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(Width, RowHeight);
            rect.anchoredPosition = new Vector2(0f, (index * RowHeight) + (RowHeight * 0.5f));

            Label = BeatSaberUI.CreateText(rect, string.Empty, new Vector2(0f, 1.5f), new Vector2(LabelMaxWidth, LabelBoxHeight));
            Label.fontSize = LabelFontSize;
            Label.alignment = TextAlignmentOptions.Center;
            Label.enableWordWrapping = false;
            Label.overflowMode = TextOverflowModes.Ellipsis;
            Label.richText = false;
            Label.color = LabelColor;
            Label.raycastTarget = false;

            var barBack = CreateImage("Bar", rect, BarBackColor);
            BarBack = barBack.rectTransform;
            BarBack.anchoredPosition = new Vector2(0f, -(RowHeight * 0.5f) + 2f);
            BarBack.sizeDelta = new Vector2(LabelMaxWidth, BarHeight);
            var fill = CreateImage("Fill", BarBack, BarFillColor);
            BarFill = fill.rectTransform;
            BarFill.anchorMin = Vector2.zero;
            BarFill.anchorMax = new Vector2(0f, 1f);
            BarFill.offsetMin = BarFill.offsetMax = Vector2.zero;
            BarBack.gameObject.SetActive(showBars);

            if (showArrows)
            {
                Left = CreateArrow(rect, "<", index, false, onArrowClicked);
                Right = CreateArrow(rect, ">", index, true, onArrowClicked);
            }

            Root.SetActive(false);
        }

        public GameObject Root { get; }

        public TextMeshProUGUI Label { get; }

        public RectTransform BarBack { get; }

        public RectTransform BarFill { get; }

        public ChapterArrowButton? Left { get; }

        public ChapterArrowButton? Right { get; }

        public bool Active { get; set; }

        public int ChapterIndex { get; set; } = -1;

        public float Progress { get; set; } = -1f;

        public void SetLabel(string text)
        {
            Label.text = text;
            var preferred = Label.GetPreferredValues(text);
            if (preferred.y + 2f > Label.rectTransform.sizeDelta.y)
            {
                // Never let the box be shorter than a line: Ellipsis would then hide the whole label.
                Label.rectTransform.sizeDelta = new Vector2(LabelMaxWidth, preferred.y + 2f);
            }

            if (!loggedMetrics)
            {
                loggedMetrics = true;
                Plugin.Log.Debug($"[Chapters] label metrics: preferred {preferred.x:0.0}x{preferred.y:0.0}, box {Label.rectTransform.sizeDelta.x:0}x{Label.rectTransform.sizeDelta.y:0}, font {Label.fontSize}");
            }

            var width = Mathf.Clamp(preferred.x, LabelMinWidth, LabelMaxWidth);
            BarBack.sizeDelta = new Vector2(width, BarHeight);
            var arrowX = (width * 0.5f) + Gap + (ArrowWidth * 0.5f);
            if (Left != null)
            {
                Left.Rect.anchoredPosition = new Vector2(-arrowX, 0f);
            }

            if (Right != null)
            {
                Right.Rect.anchoredPosition = new Vector2(arrowX, 0f);
            }
        }

        private static ChapterArrowButton CreateArrow(RectTransform parent, string glyph, int row, bool toEnd, Action<int, bool> onClick)
        {
            var text = BeatSaberUI.CreateText(parent, glyph, Vector2.zero, new Vector2(ArrowWidth, RowHeight));
            text.fontSize = ArrowFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.richText = false;
            text.raycastTarget = true;
            var button = text.gameObject.AddComponent<ChapterArrowButton>();
            button.Init(text, row, toEnd, onClick);
            return button;
        }
    }
}
