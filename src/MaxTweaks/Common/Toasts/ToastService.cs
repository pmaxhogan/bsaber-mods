using System;
using BeatSaberMarkupLanguage;
using HMUI;
using IPA.Utilities;
using IPA.Utilities.Async;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MaxTweaks.Common.Toasts;

/// <summary>Implementation of <see cref="IToastService"/>; see that interface for the contract.</summary>
internal class ToastService : IToastService, IDisposable
{
    public const float DefaultSeconds = 4f;

    private const string Feature = "Toasts";
    private const string GameplaySceneName = "GameCore";

    // Canvas units are centimetres (scale 0.01) before the per-placement scale factor.
    private static readonly Vector2 MaxTextSize = new(160f, 12f);
    private static readonly Vector2 Padding = new(6f, 2.5f);

    // Menu: just above the Enhancements menu clock, in the clock's own plane. Measured positions:
    // - Enhancements 3.0.18 clock (UserData/Enhancements.json): (0, 3.0, 3.9), pitch -35 deg, a BSML
    //   FloatingScreen (scale 0.02) with font size 10, so its text is about 0.2 m tall: seen from the
    //   head (about 1.7 m) it spans roughly 17-20 deg up.
    // - The main screens' top (Solo header) sits just below the clock; the song list and
    //   BetterSongList's sort/filter/random buttons are below eye level (the old 0.8 m spot overlapped
    //   them); SongCore's banner is at (0, 2.5, 2.5), about 17-22 deg up, and is silenced during our reloads.
    // Here: 3.9 m ahead at 3.45 m = about 24 deg up; the box is about 0.18 m tall (+-1.3 deg), so it
    // clears the clock text and everything below it, and only overlaps the background logo.
    private static readonly Placement MenuPlacement = new(distance: 3.9f, height: 3.45f, scale: 0.016f);
    private static readonly Placement GameplayPlacement = new(distance: 3.5f, height: 2.6f, scale: 0.014f);

    private GameObject? _root;
    private ToastView? _view;

    public void Show(string message, float seconds = DefaultSeconds)
    {
        if (message == null)
        {
            return;
        }

        if (!UnityGame.OnMainThread)
        {
            UnityMainThreadTaskScheduler.Factory.StartNew(() => Show(message, seconds));
            return;
        }

        Guard.Run(Feature, () =>
        {
            if (!EnsureView())
            {
                Plugin.Log.Info($"[{Feature}] (not shown, UI not ready) {message}");
                return;
            }

            var placement = SceneManager.GetActiveScene().name == GameplaySceneName ? GameplayPlacement : MenuPlacement;
            Place(_root!.transform, placement);
            _view!.Show(message, seconds);
        });
    }

    public void Hide()
    {
        if (!UnityGame.OnMainThread)
        {
            UnityMainThreadTaskScheduler.Factory.StartNew(Hide);
            return;
        }

        Guard.Run(Feature, () =>
        {
            if (_view != null)
            {
                _view.FadeOut();
            }
        });
    }

    /// <summary>
    /// Builds the toast objects now. Call from the menu (where the game's UI font can be found) so
    /// that the first toast during gameplay does not depend on menu objects.
    /// </summary>
    public void Prewarm()
    {
        if (UnityGame.OnMainThread)
        {
            Guard.Run(Feature, () => EnsureView());
        }
    }

    public void Dispose()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
        }

        _root = null;
        _view = null;
    }

    private static void Place(Transform transform, Placement placement)
    {
        var camera = Camera.main;
        var head = camera != null ? camera.transform.position : new Vector3(0f, 1.7f, 0f);
        var forward = camera != null ? camera.transform.forward : Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        var position = new Vector3(head.x, 0f, head.z) + (forward * placement.Distance);
        position.y = placement.Height;
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head));
        transform.localScale = Vector3.one * placement.Scale;
    }

    private bool EnsureView()
    {
        if (_view != null)
        {
            return true;
        }

        if (BeatSaberUI.MainTextFont == null)
        {
            return false;
        }

        var root = new GameObject("MaxTweaksToast") { layer = 5 };
        Object.DontDestroyOnLoad(root);
        root.SetActive(false);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        // BSML text uses the game's curved-UI shader; give it a flat curve like BSML's floating screens.
        root.AddComponent<CurvedCanvasSettings>().SetRadius(0f);
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = MaxTextSize + (Padding * 2f);
        var group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var background = new GameObject("Background") { layer = 5 }.AddComponent<Image>();
        background.rectTransform.SetParent(rect, false);
        background.rectTransform.sizeDelta = rect.sizeDelta;
        background.color = new Color(0f, 0f, 0f, 0.7f);
        background.raycastTarget = false;
        var noGlow = BeatSaberMarkupLanguage.Utilities.ImageResources.NoGlowMat;
        if (noGlow != null)
        {
            background.material = noGlow;
        }

        var text = BeatSaberUI.CreateText(rect, string.Empty, Vector2.zero, MaxTextSize);
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 6f;
        text.richText = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        _view = root.AddComponent<ToastView>();
        _view.Init(group, background.rectTransform, text, MaxTextSize, Padding);
        _root = root;
        return true;
    }

    private readonly struct Placement
    {
        public Placement(float distance, float height, float scale)
        {
            Distance = distance;
            Height = height;
            Scale = scale;
        }

        public float Distance { get; }

        public float Height { get; }

        public float Scale { get; }
    }
}
