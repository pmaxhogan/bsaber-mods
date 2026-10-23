using System;
using BeatSaberMarkupLanguage;
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

    private static readonly Placement MenuPlacement = new(distance: 2.3f, height: 0.8f, scale: 0.01f);
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
