using System;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using HMUI;
using MaxTweaks.Common;
using UnityEngine;
using Zenject;
using LevelCategory = SelectLevelCategoryViewController.LevelCategory;

namespace MaxTweaks.Features.HideOst;

/// <summary>
/// The one-click toggle in the song list header: a small icon just left of the category tabs (Music
/// Packs / Custom / Favorites / All), on the same row as the search field / pack carousel. It reuses the
/// game's own Music Packs tab icon: white = official songs shown, red = official songs hidden.
/// Only visible while the solo song list is open (the category view is shared with party and
/// multiplayer, so visibility is re-evaluated on every activation).
/// </summary>
internal class HideOstButton : IInitializable, ITickable, IDisposable
{
    private const string Resource = "MaxTweaks.Features.HideOst.HideOstButton.bsml";

    // Placement relative to the category tab strip: just outside its LEFT edge, centred vertically.
    // The right side is taken: the All/Favorites search field and the Music Packs/Custom pack
    // carousel start immediately right of the tabs (FPFC QA: a right-side icon overlapped them).
    private const float Gap = 2.5f;
    private const float IconSize = 6f;

    private static readonly Color ShownColor = Color.white;
    private static readonly Color HiddenColor = new(1f, 0.38f, 0.38f);
    private static readonly Color HoverColor = new(0.6f, 0.8f, 1f);

    private readonly HideOstController _controller;
    private readonly BSMLParser _parser;
    private readonly SelectLevelCategoryViewController _categoryView;

    private HoverHint? _hoverHint;
    private Vector3 _placedForCategoryPosition = new(float.NaN, 0f, 0f);

    public HideOstButton(HideOstController controller, BSMLParser parser, SelectLevelCategoryViewController categoryView)
    {
        _controller = controller;
        _parser = parser;
        _categoryView = categoryView;
    }

    [UIComponent("toggle-button")]
    private ClickableImage? Image { get; set; }

    public void Initialize()
    {
        _categoryView.didActivateEvent += OnCategoryViewActivated;
        _controller.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// The category view is positioned by the header's layout AFTER it activates (at activation it
    /// still sits at the header's left edge), so a placement made on activation is stale. Follow it:
    /// re-place whenever its local position changes (cheap compare, no allocation).
    /// </summary>
    public void Tick()
    {
        if (Image == null || !Image.gameObject.activeInHierarchy)
        {
            return;
        }

        var categoryPosition = _categoryView.transform.localPosition;
        if (categoryPosition != _placedForCategoryPosition && _categoryView.transform.parent is RectTransform header)
        {
            Guard.Run(HideOstController.Feature, () => Place(Image.rectTransform, (RectTransform)_categoryView.transform, header));
            _placedForCategoryPosition = categoryPosition;
        }
    }

    public void Dispose()
    {
        _categoryView.didActivateEvent -= OnCategoryViewActivated;
        _controller.StateChanged -= OnStateChanged;
        Guard.Run(HideOstController.Feature, () =>
        {
            if (Image != null)
            {
                UnityEngine.Object.Destroy(Image.gameObject);
            }

            Image = null;
            _hoverHint = null;
        });
    }

    [UIAction("toggle-clicked")]
    private void OnClicked()
    {
        Guard.Run(HideOstController.Feature, () =>
        {
            // The controller raises StateChanged, which refreshes the icon.
            _controller.ToggleHidden();
        });
    }

    private void OnCategoryViewActivated(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) =>
        Guard.Run(HideOstController.Feature, EnsureAndRefresh);

    /// <summary>Enabled/Hidden changed (icon, settings page or quick tab): update in place.</summary>
    private void OnStateChanged() => Guard.Run(HideOstController.Feature, EnsureAndRefresh);

    private void EnsureAndRefresh()
    {
        if (Image == null && _controller.ButtonVisible && _categoryView.isActivated)
        {
            Create();
        }

        Refresh();
    }

    private void Create()
    {
        // The icon lives one level ABOVE the category view (in the level filtering header), not
        // inside it: the game's VRGraphicRaycaster rejects a hit on a curved canvas when the point is
        // outside that canvas's own rect, and the category view's canvas is exactly as wide as its
        // tab strip (30 units). Inside it the icon rendered but the VR pointer could never hover or
        // click it. The header's canvas is 160 wide, so the same spot on screen is inside its rect.
        var categoryRect = (RectTransform)_categoryView.transform;
        var header = categoryRect.parent as RectTransform;
        if (header == null)
        {
            Plugin.Log.Warn($"[{HideOstController.Feature}] category view has no parent rect; button not created");
            return;
        }

        _parser.Parse(Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), Resource), header.gameObject, this);
        if (Image == null)
        {
            return;
        }

        // ignore-layout='true' in the BSML keeps the icon out of the header's own layout (BSML
        // already adds a LayoutElement, so adding a second one here would be ignored).
        Image.name = "MaxTweaksHideOstButton";

        var rect = Image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(IconSize, IconSize);
        rect.SetAsLastSibling();
        Place(rect, categoryRect, header);

        foreach (var info in _categoryView._allLevelCategoryInfos)
        {
            if (info.levelCategory == LevelCategory.MusicPacks && info.categoryIcon != null)
            {
                Image.sprite = info.categoryIcon;
                break;
            }
        }

        Image.HighlightColor = HoverColor;
        _hoverHint = Image.GetComponent<HoverHint>();
    }

    /// <summary>
    /// Just outside the category view's LEFT edge, vertically centred on it, expressed in the
    /// header's space (anchored to the header's centre). The category view's rect is fixed (the
    /// tab strip is centred inside it), so this is stable across 3 and 4 tabs.
    /// </summary>
    private static void Place(RectTransform icon, RectTransform categoryRect, RectTransform header)
    {
        var categoryBounds = categoryRect.rect;
        var leftMiddleWorld = categoryRect.TransformPoint(new Vector3(categoryBounds.xMin, categoryBounds.center.y, 0f));
        var local = header.InverseTransformPoint(leftMiddleWorld);
        var headerCenter = header.rect.center;
        icon.anchoredPosition = new Vector2(local.x - headerCenter.x - Gap, local.y - headerCenter.y);
        Plugin.Log.Debug($"[{HideOstController.Feature}] placed at header x {icon.anchoredPosition.x:0.0} (category view at {categoryRect.localPosition.x:0.0})");
    }

    private void Refresh()
    {
        if (Image == null)
        {
            return;
        }

        var visible = _controller.ButtonVisible;
        Image.gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        if (_categoryView.transform.parent is RectTransform header)
        {
            Place(Image.rectTransform, (RectTransform)_categoryView.transform, header);
            _placedForCategoryPosition = _categoryView.transform.localPosition;
        }

        var hidden = _controller.Hidden;
        Image.DefaultColor = hidden ? HiddenColor : ShownColor;
        if (_hoverHint != null)
        {
            _hoverHint.text = hidden
                ? "Official songs hidden (OST, Extras, DLC). Click to show them."
                : "Hide official songs (OST, Extras, DLC) and show only custom maps";
        }
    }
}
