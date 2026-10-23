using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.FloatingScreen;
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
    private const float Scale = 0.02f;
    private const float RowHeight = 18f;
    private const float Width = 220f;
    private const float LabelMaxWidth = 160f;
    private const float LabelMinWidth = 24f;
    private const float ArrowWidth = 18f;
    private const float Gap = 4f;
    private const float BarHeight = 0.9f;
    private const float LabelFontSize = 11f;
    private const float ArrowFontSize = 15f;

    private static readonly Vector3 BottomCenter = new(0f, 2.8f, 7f);
    private static readonly Color LabelColor = new(1f, 1f, 1f, 0.9f);
    private static readonly Color BarBackColor = new(1f, 1f, 1f, 0.15f);
    private static readonly Color BarFillColor = new(1f, 1f, 1f, 0.7f);

    private readonly GameObject _root;
    private readonly RowView[] _rows;
    private readonly bool _showBars;
    private ChapterArrowButton? _rayHovered;

    public ChaptersDisplay(int rowCount, bool showBars, bool showArrows, Action<int, bool> onArrowClicked)
    {
        _showBars = showBars;
        var size = new Vector2(Width, rowCount * RowHeight);
        var center = BottomCenter + new Vector3(0f, size.y * Scale * 0.5f, 0f);
        _root = CreateRoot(size, center);
        _root.name = "MaxTweaksChapters";

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

    /// <summary>Hides rows from <paramref name="firstHidden"/> upwards.</summary>
    public void HideRowsFrom(int firstHidden)
    {
        for (var i = firstHidden; i < _rows.Length; i++)
        {
            var view = _rows[i];
            if (view.Active)
            {
                if (_rayHovered != null && _rayHovered.Row == i)
                {
                    SetRayHover(null);
                }

                view.Active = false;
                view.ChapterIndex = -1;
                view.Root.SetActive(false);
            }
        }
    }

    /// <summary>Chapter index currently shown on <paramref name="row"/>, or -1.</summary>
    public int ChapterAt(int row) => row >= 0 && row < _rows.Length && _rows[row].Active ? _rows[row].ChapterIndex : -1;

    /// <summary>Finds the arrow hit by <paramref name="ray"/> among the first <paramref name="activeRows"/> rows.</summary>
    public ChapterArrowButton? HitTest(Ray ray, int activeRows)
    {
        var screen = _root.transform;
        var plane = new Plane(screen.forward, screen.position);
        if (!plane.Raycast(ray, out var distance) || distance <= 0f || distance > 60f)
        {
            return null;
        }

        var point = ray.GetPoint(distance);
        var rows = Mathf.Min(activeRows, _rows.Length);
        for (var i = 0; i < rows; i++)
        {
            var view = _rows[i];
            if (view.Left != null && Contains(view.Left.Rect, point))
            {
                return view.Left;
            }

            if (view.Right != null && Contains(view.Right.Rect, point))
            {
                return view.Right;
            }
        }

        return null;
    }

    /// <summary>Highlights the arrow the saber ray points at (null clears it).</summary>
    public void SetRayHover(ChapterArrowButton? arrow)
    {
        // The arrow may have cleared its own ray hover (OnDisable when its row hid), so compare the
        // arrow's actual state, not just the cached reference.
        if (_rayHovered == arrow && (arrow == null || arrow.RayHovered))
        {
            return;
        }

        if (_rayHovered != null)
        {
            _rayHovered.SetRayHover(false);
        }

        _rayHovered = arrow;
        if (arrow != null)
        {
            arrow.SetRayHover(true);
        }
    }

    public void Dispose()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
        }
    }

    private static bool Contains(RectTransform rect, Vector3 worldPoint)
    {
        Vector2 local = rect.InverseTransformPoint(worldPoint);
        return rect.rect.Contains(local);
    }

    private static GameObject CreateRoot(Vector2 size, Vector3 center)
    {
        try
        {
            var screen = FloatingScreen.CreateFloatingScreen(size, false, center, Quaternion.identity);
            return screen.gameObject;
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"[Chapters] floating screen unavailable, using a plain canvas (no trigger clicks): {e.Message}");
            // CurvedCanvasSettings is required by the CurvedTextMeshPro that BeatSaberUI.CreateText makes.
            var go = new GameObject("MaxTweaksChapters", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(HMUI.CurvedCanvasSettings)) { layer = 5 };
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.GetComponent<HMUI.CurvedCanvasSettings>().SetRadius(0f);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.SetPositionAndRotation(center, Quaternion.identity);
            rect.localScale = new Vector3(Scale, Scale, Scale);
            return go;
        }
    }

    private static Sprite? whitePixel;
    private static Material? noGlow;
    private static bool lookedUpAssets;

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
                Plugin.Log.Debug($"[Chapters] UI assets not found, using defaults: {e.Message}");
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

            Label = BeatSaberUI.CreateText(rect, string.Empty, new Vector2(0f, 1.5f), new Vector2(LabelMaxWidth, RowHeight - 4f));
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
            var width = Mathf.Clamp(Label.GetPreferredValues(text).x, LabelMinWidth, LabelMaxWidth);
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
