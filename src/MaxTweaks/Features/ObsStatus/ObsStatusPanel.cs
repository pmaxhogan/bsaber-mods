using System;
using BeatSaberMarkupLanguage;
using MaxTweaks.Common;
using MaxTweaks.Common.Obs;
using MaxTweaks.Configuration;
using MaxTweaks.Core.ObsStatus;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace MaxTweaks.Features.ObsStatus;

/// <summary>
/// One line of text on the song selection screen: whether OBS is connected and how many recordings
/// and clips were saved since the game started (Core <see cref="ObsStatusLine"/> writes it from
/// what the OBS features publish on the <see cref="ObsStatusBoard"/>).
/// - It is a child of the song selection view (LevelSelectionNavigationController: solo, party and
///   multiplayer song lists), so it shows, hides and fades with that screen and is gone with the
///   menu on a soft restart.
/// - Display only: nothing on it takes pointer hits, so it cannot block a button of the game.
/// </summary>
internal sealed class ObsStatusPanel : IInitializable, ITickable, IDisposable
{
    private const string Feature = "ObsStatus";
    private const string ObjectName = "MaxTweaksObsStatus";

    // Where it goes, in the song selection view's own units (the main screen, 160 x 80, 3 cm per
    // unit, its bottom edge at floor level, so nothing below it can be seen). Measured in the solo
    // screen with BetterSongList installed:
    // - the song detail panel covers x 4.5 to 74.5 and ends at y -35, with Practice/Play at y -29;
    // - BetterSongList's sort/filter row runs along the bottom of the left half (y -39) and its
    //   random and settings icons end at about x 15;
    // which leaves the strip under the detail panel free: x 17 to 78, below y -35. The line sits at
    // the height of the sort/filter row.
    private const float FontSize = 3.4f;
    private const float SmallestFontSize = 2.4f;

    private static readonly Vector2 Size = new(61f, 5f);
    private static readonly Vector2 BottomCenter = new(47.5f, -41f);

    private readonly ObsStatusConfig _config;
    private readonly ObsStatusBoard _board;
    private readonly LevelSelectionNavigationController _songSelect;

    private TextMeshProUGUI? _text;
    private string? _shown;
    private bool _appliedEnabled;

    public ObsStatusPanel(ObsStatusConfig config, ObsStatusBoard board, LevelSelectionNavigationController songSelect)
    {
        _config = config;
        _board = board;
        _songSelect = songSelect;
    }

    public void Initialize()
    {
        _appliedEnabled = _config.Enabled;
        _songSelect.didActivateEvent += OnSongSelectActivated;
        _board.Changed += OnBoardChanged;
    }

    /// <summary>The Enabled switch has no event of its own (quick tab, settings page): follow it.</summary>
    public void Tick()
    {
        if (_config.Enabled != _appliedEnabled)
        {
            Guard.Run(Feature, Refresh);
        }
    }

    public void Dispose()
    {
        _songSelect.didActivateEvent -= OnSongSelectActivated;
        _board.Changed -= OnBoardChanged;
        Guard.Run(Feature, () =>
        {
            if (_text != null)
            {
                UnityEngine.Object.Destroy(_text.gameObject);
            }

            _text = null;
        });
    }

    private void OnSongSelectActivated(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) =>
        Guard.Run(Feature, Refresh);

    private void OnBoardChanged() => Guard.Run(Feature, Refresh);

    private void Refresh()
    {
        _appliedEnabled = _config.Enabled;
        var line = _appliedEnabled ? ObsStatusLine.Build(_board.Recording, _board.Clips) : null;
        if (line == null)
        {
            if (_text != null)
            {
                _text.gameObject.SetActive(false);
            }

            return;
        }

        if (_text == null)
        {
            if (!_songSelect.isActivated || !Create())
            {
                return;
            }
        }

        _text!.gameObject.SetActive(true);
        if (line != _shown)
        {
            _text.text = line;
            _shown = line;
            Plugin.Log.Debug($"[{Feature}] {line}");
        }
    }

    private bool Create()
    {
        if (BeatSaberUI.MainTextFont == null || _songSelect.transform is not RectTransform parent)
        {
            return false;
        }

        var text = BeatSaberUI.CreateText(parent, string.Empty, Vector2.zero, Size);
        text.name = ObjectName;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = FontSize;
        text.fontSizeMax = FontSize;
        text.fontSizeMin = SmallestFontSize;
        text.enableAutoSizing = true;
        text.richText = true;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        // Keep it out of any layout group on the parent.
        var layout = text.gameObject.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = text.gameObject.AddComponent<LayoutElement>();
        }

        layout.ignoreLayout = true;

        // Placed from the centre of the view, where the game puts the song list and the detail
        // panel from too, so it keeps its place whatever the view's own rect is.
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = Size;
        rect.anchoredPosition = BottomCenter - parent.rect.center;
        rect.SetAsLastSibling();

        _text = text;
        _shown = null;
        var bounds = parent.rect;
        Plugin.Log.Info($"[{Feature}] shown under the song detail panel (song selection view {bounds.width:0} x {bounds.height:0} units)");
        return true;
    }
}
