using System;
using System.Collections.Generic;
using BeatSaberMarkupLanguage;
using MaxTweaks.Core.DifficultySwitch;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>
/// World-space panel in front of the player just below eye height (1.7 m ahead, in front of the
/// pause menu panels so it is readable while paused, and over the note lanes):
/// a title and one row per difficulty, hardest at the top. The current difficulty is gold and marked
/// "(now)"; a translucent bar marks the highlighted row and slides between rows. Under it, shown
/// after the first left/right flick, a horizontal NJS row (<see cref="NjsRow"/>): a window of
/// <see cref="NjsSlots"/> values around the highlight, the NJS in effect now in gold, the
/// difficulty's own NJS underlined, the same translucent bar on the highlighted value. Built once,
/// then only shown/hidden, texts set on show/move and the bars' anchored positions animated (no
/// per-frame allocations). No raycast targets: it never takes pointer input.
/// </summary>
internal sealed class DifficultyListOverlay : IDisposable
{
    // Canvas units; scale 0.01 so 1 unit = 1 cm.
    private const float Scale = 0.01f;
    private const float RowHeight = 16f;
    private const float TitleHeight = 12f;
    private const float Width = 90f;
    private const float Padding = 3f;
    private const float RowFontSize = 10f;
    private const float TitleFontSize = 6f;
    private const float HighlightRate = 22f;

    // NJS row, below the list panel.
    private const int NjsSlots = 7;
    private const float NjsWidth = 112f;
    private const float NjsGap = 2f;
    private const float NjsTitleHeight = 8f;
    private const float NjsSlotHeight = 14f;
    private const float NjsFontSize = 8f;
    private const float NjsTitleFontSize = 5f;
    private const float NjsSlotWidth = (NjsWidth - (Padding * 2f)) / NjsSlots;

    private static readonly Vector3 Center = new(0f, 1.4f, 1.7f);
    private static readonly Color PanelColor = new(0f, 0f, 0f, 0.72f);
    private static readonly Color HighlightColor = new(1f, 1f, 1f, 0.28f);
    private static readonly Color RowColor = new(1f, 1f, 1f, 0.92f);
    private static readonly Color CurrentColor = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Color TitleColor = new(1f, 1f, 1f, 0.6f);

    private static Sprite? whitePixel;
    private static Material? noGlow;
    private static bool lookedUpAssets;

    private readonly GameObject _root;
    private readonly RectTransform _highlight;
    private readonly TextMeshProUGUI[] _rows;
    private readonly int _rowCount;
    private readonly TextMeshProUGUI _title;
    private readonly GameObject _njsRoot;
    private readonly RectTransform _njsHighlight;
    private readonly RectTransform _njsUnderline;
    private readonly TextMeshProUGUI[] _njsSlots;
    private float _highlightY;
    private float _targetY;
    private float _njsHighlightX;
    private float _njsTargetX;
    private bool _visible;
    private NjsRow? _njsRow;

    public DifficultyListOverlay(IReadOnlyList<string> labels)
    {
        _rowCount = labels.Count;
        var height = (Padding * 2f) + TitleHeight + (_rowCount * RowHeight);
        _root = CreateRoot(new Vector2(Width, height));
        var parent = (RectTransform)_root.transform;

        var panel = CreateImage("Panel", parent, PanelColor);
        Stretch(panel.rectTransform);

        _highlight = CreateImage("Highlight", parent, HighlightColor).rectTransform;
        _highlight.anchorMin = _highlight.anchorMax = new Vector2(0.5f, 1f);
        _highlight.pivot = new Vector2(0.5f, 0.5f);
        _highlight.sizeDelta = new Vector2(Width - (Padding * 2f), RowHeight - 2f);

        _title = CreateText(parent, TitleText(false), TitleFontSize, TitleColor, TitleHeight);
        _title.rectTransform.anchoredPosition = new Vector2(0f, -(Padding + (TitleHeight * 0.5f)));

        _rows = new TextMeshProUGUI[_rowCount];
        for (var i = 0; i < _rowCount; i++)
        {
            var row = CreateText(parent, labels[i], RowFontSize, RowColor, RowHeight);
            row.rectTransform.anchoredPosition = new Vector2(0f, RowY(i));
            _rows[i] = row;
        }

        _njsRoot = CreateNjsRow(parent, out _njsHighlight, out _njsUnderline, out _njsSlots);
        _njsRoot.SetActive(false);
        _root.SetActive(false);
    }

    public bool IsVisible => _visible;

    /// <summary>Shows the list with <paramref name="current"/> marked and the bar on <paramref name="highlight"/>; the NJS row starts hidden.</summary>
    /// <param name="njsAvailable">The title mentions left/right for NJS.</param>
    public void Show(int current, IReadOnlyList<string> labels, int highlight, bool njsAvailable)
    {
        _title.text = TitleText(njsAvailable);
        HideNjsRow();
        for (var i = 0; i < _rowCount; i++)
        {
            var isCurrent = i == current;
            _rows[i].text = isCurrent ? labels[i] + "  (now)" : labels[i];
            _rows[i].color = isCurrent ? CurrentColor : RowColor;
        }

        _targetY = _highlightY = RowY(Mathf.Clamp(highlight, 0, Math.Max(0, _rowCount - 1)));
        _highlight.anchoredPosition = new Vector2(0f, _highlightY);
        _visible = true;
        _root.SetActive(true);
    }

    public void SetHighlight(int row)
    {
        _targetY = RowY(Mathf.Clamp(row, 0, Math.Max(0, _rowCount - 1)));
    }

    public void Hide()
    {
        HideNjsRow();
        if (_visible)
        {
            _visible = false;
            _root.SetActive(false);
        }
    }

    /// <summary>Shows the NJS row of <paramref name="row"/> with the bar on <paramref name="highlight"/> (no slide).</summary>
    public void ShowNjsRow(NjsRow row, int highlight)
    {
        _njsRow = row;
        FillNjsSlots(highlight);
        _njsHighlightX = _njsTargetX;
        _njsHighlight.anchoredPosition = new Vector2(_njsHighlightX, NjsSlotY);
        _njsRoot.SetActive(true);
    }

    /// <summary>Moves the NJS bar to <paramref name="highlight"/> (scrolling the window when needed).</summary>
    public void SetNjsHighlight(int highlight)
    {
        if (_njsRow != null)
        {
            FillNjsSlots(highlight);
        }
    }

    /// <summary>Slides the highlight bar; call every frame while visible.</summary>
    public void Tick(float deltaTime)
    {
        if (!_visible)
        {
            return;
        }

        if (_highlightY != _targetY)
        {
            _highlightY = Smoothing.Approach(_highlightY, _targetY, deltaTime, HighlightRate, 0.05f);
            _highlight.anchoredPosition = new Vector2(0f, _highlightY);
        }

        if (_njsRow != null && _njsHighlightX != _njsTargetX)
        {
            _njsHighlightX = Smoothing.Approach(_njsHighlightX, _njsTargetX, deltaTime, HighlightRate, 0.05f);
            _njsHighlight.anchoredPosition = new Vector2(_njsHighlightX, NjsSlotY);
        }
    }

    public void Dispose()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
        }
    }

    private static float NjsSlotY => -(Padding + NjsTitleHeight + (NjsSlotHeight * 0.5f));

    private static float SlotX(int slot) => -(NjsWidth * 0.5f) + Padding + ((slot + 0.5f) * NjsSlotWidth);

    private static string TitleText(bool njsAvailable) =>
        njsAvailable ? "Difficulty  (up/down: difficulty, left/right: NJS, release Y: apply)" : "Difficulty  (stick: move, release Y: switch)";

    private void HideNjsRow()
    {
        _njsRow = null;
        _njsRoot.SetActive(false);
    }

    private void FillNjsSlots(int highlight)
    {
        var row = _njsRow!;
        highlight = Mathf.Clamp(highlight, 0, Math.Max(0, row.Count - 1));
        var start = NjsRow.WindowStart(highlight, row.Count, NjsSlots);
        var underlined = false;
        for (var slot = 0; slot < NjsSlots; slot++)
        {
            var index = start + slot;
            var label = _njsSlots[slot];
            if (index >= row.Count)
            {
                label.text = string.Empty;
                continue;
            }

            label.text = NjsRow.Label(row.Values[index]);
            label.color = index == row.CurrentIndex ? CurrentColor : RowColor;
            if (index == row.DefaultIndex)
            {
                underlined = true;
                _njsUnderline.anchoredPosition = new Vector2(SlotX(slot), NjsSlotY - (NjsSlotHeight * 0.32f));
            }
        }

        _njsUnderline.gameObject.SetActive(underlined);
        _njsTargetX = SlotX(highlight - start);
    }

    private static GameObject CreateNjsRow(RectTransform parent, out RectTransform highlight, out RectTransform underline, out TextMeshProUGUI[] slots)
    {
        var go = new GameObject("NjsRow", typeof(RectTransform)) { layer = 5 };
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(NjsWidth, (Padding * 2f) + NjsTitleHeight + NjsSlotHeight);
        rect.anchoredPosition = new Vector2(0f, -NjsGap);

        var panel = CreateImage("Panel", rect, PanelColor);
        Stretch(panel.rectTransform);

        highlight = CreateImage("Highlight", rect, HighlightColor).rectTransform;
        highlight.anchorMin = highlight.anchorMax = new Vector2(0.5f, 1f);
        highlight.pivot = new Vector2(0.5f, 0.5f);
        highlight.sizeDelta = new Vector2(NjsSlotWidth - 1f, NjsSlotHeight - 2f);

        underline = CreateImage("Default", rect, CurrentColor).rectTransform;
        underline.anchorMin = underline.anchorMax = new Vector2(0.5f, 1f);
        underline.pivot = new Vector2(0.5f, 0.5f);
        underline.sizeDelta = new Vector2(NjsSlotWidth * 0.55f, 0.8f);

        var title = CreateText(rect, "NJS  (underlined: this difficulty's own)", NjsTitleFontSize, TitleColor, NjsTitleHeight, NjsWidth - (Padding * 2f));
        title.rectTransform.anchoredPosition = new Vector2(0f, -(Padding + (NjsTitleHeight * 0.5f)));

        slots = new TextMeshProUGUI[NjsSlots];
        for (var i = 0; i < NjsSlots; i++)
        {
            var label = CreateText(rect, string.Empty, NjsFontSize, RowColor, NjsSlotHeight, NjsSlotWidth);
            label.overflowMode = TextOverflowModes.Overflow;
            label.rectTransform.anchoredPosition = new Vector2(SlotX(i), NjsSlotY);
            slots[i] = label;
        }

        return go;
    }

    private static float RowY(int row) => -(Padding + TitleHeight + (row * RowHeight) + (RowHeight * 0.5f));

    private static TextMeshProUGUI CreateText(RectTransform parent, string text, float size, Color color, float height, float width = Width - (Padding * 2f))
    {
        var label = BeatSaberUI.CreateText(parent, text, Vector2.zero, new Vector2(width, height));
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.richText = false;
        label.color = color;
        label.raycastTarget = false;
        return label;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static GameObject CreateRoot(Vector2 size)
    {
        // CurvedCanvasSettings is required by the CurvedTextMeshPro that BeatSaberUI.CreateText makes.
        var go = new GameObject("MaxTweaksDifficultySwitch", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(HMUI.CurvedCanvasSettings)) { layer = 5 };
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;
        go.GetComponent<HMUI.CurvedCanvasSettings>().SetRadius(0f);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = size;
        rect.SetPositionAndRotation(Center, Quaternion.identity);
        rect.localScale = new Vector3(Scale, Scale, Scale);
        return go;
    }

    private static Image CreateImage(string name, RectTransform parent, Color color)
    {
        if (!lookedUpAssets)
        {
            lookedUpAssets = true;
            try
            {
                whitePixel = BeatSaberMarkupLanguage.Utilities.ImageResources.WhitePixel;
                noGlow = BeatSaberMarkupLanguage.Utilities.ImageResources.NoGlowMat;
            }
            catch (Exception e)
            {
                Plugin.Log.Debug($"[DifficultySwitch] UI assets not found, using defaults: {e.Message}");
            }
        }

        var go = new GameObject(name, typeof(RectTransform), typeof(Image)) { layer = 5 };
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        if (whitePixel != null)
        {
            image.sprite = whitePixel;
        }

        if (noGlow != null)
        {
            image.material = noGlow;
        }

        image.color = color;
        image.raycastTarget = false;
        return image;
    }
}
