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
internal class HideOstButton : IInitializable, IDisposable
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
        var strip = _categoryView._levelFilterCategoryIconSegmentedControl.transform;
        _parser.Parse(Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), Resource), strip.gameObject, this);
        if (Image == null)
        {
            return;
        }

        // ignore-layout='true' in the BSML keeps the icon out of the strip's own cell layout (BSML
        // already adds a LayoutElement, so adding a second one here would be ignored).
        Image.name = "MaxTweaksHideOstButton";

        var rect = Image.rectTransform;
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-Gap, 0f);
        rect.sizeDelta = new Vector2(IconSize, IconSize);

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
