using System;
using MaxTweaks.Core.Blink;
using UnityEngine;
using UnityEngine.UI;

namespace MaxTweaks.Features.Blink;

/// <summary>
/// The head-locked blink overlay: a soft black vignette (clear centre, darkest towards the corners
/// of the view) and a closed-eye icon a little below eye level. Two modes, chosen by the Blink
/// setting "Screen-space overlay":
/// <list type="bullet">
/// <item>World (default): two world-space canvases parented to the main (HMD) camera, on the UI
/// layer the HMD camera renders. Camera2 cameras show it too when their "UI" visibility is on.</item>
/// <item>Screen-space: one Screen Space - Camera canvas on the main camera (Unity's VR UI mode),
/// which the stereo renderer places in front of each eye itself.</item>
/// </list>
/// The owner reported the world mode darkening one eye only in the headset (2026-10-25); the
/// mode switch and the settings page's "Test blink" button exist to compare them in VR.
/// </summary>
internal class BlinkOverlay : IDisposable
{
    /// <summary>The game HUD's layer; the HMD camera renders it and Camera2 exposes it as "UI".</summary>
    private const int UiLayer = 5;

    private const float VignetteDistance = 0.6f;
    private const float EyeDistance = 1.5f;
    private const float EyeDegreesBelow = 10f;
    private const float EyeWidth = 0.32f;
    private const float EyeMaxAlpha = 0.85f;
    private const float CanvasUnits = 100f;

    // Screen-space mode: fractions of the canvas height.
    private const float ScreenEyeSize = 0.14f;
    private const float ScreenEyeBelow = 0.2f;

    private readonly BlinkAssets _assets;
    private readonly float _strength;
    private readonly bool _screenSpace;
    private GameObject? _root;
    private RawImage? _vignette;
    private RawImage? _eye;
    private Transform? _parent;
    private bool _visible;
    private bool _loggedAttach;

    public BlinkOverlay(BlinkAssets assets, float vignetteStrength, bool screenSpace = false)
    {
        _assets = assets;
        _strength = Mathf.Clamp01(vignetteStrength);
        _screenSpace = screenSpace;
    }

    /// <summary>Shows the overlay at the given opacity (0 hides it). Call from Tick only.</summary>
    public void SetAlpha(float alpha)
    {
        if (alpha <= 0f)
        {
            if (_visible && _root != null)
            {
                _root.SetActive(false);
            }

            _visible = false;
            return;
        }

        if (!EnsureAttached())
        {
            return;
        }

        if (!_visible)
        {
            _root!.SetActive(true);
            _visible = true;
        }

        _vignette!.color = new Color(0f, 0f, 0f, _strength * alpha);
        _eye!.color = new Color(1f, 1f, 1f, EyeMaxAlpha * alpha);
    }

    public void Dispose()
    {
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
        }

        _root = null;
        _parent = null;
        _visible = false;
    }

    private bool EnsureAttached()
    {
        if (_root != null && _parent != null)
        {
            return true;
        }

        // The camera can be replaced (FPFC toggle, scene changes); re-resolve when it is gone.
        var camera = Camera.main;
        if (camera == null)
        {
            return false;
        }

        if (_root == null)
        {
            if (_screenSpace)
            {
                BuildScreenSpace(camera);
            }
            else
            {
                BuildWorld();
            }
        }

        _parent = camera.transform;
        _root!.transform.SetParent(_parent, false);
        _root.transform.localPosition = Vector3.zero;
        _root.transform.localRotation = Quaternion.identity;
        _root.transform.localScale = Vector3.one;
        if (_screenSpace)
        {
            foreach (var canvas in _root.GetComponentsInChildren<Canvas>(true))
            {
                canvas.worldCamera = camera;
            }
        }

        if (!_loggedAttach)
        {
            _loggedAttach = true;
            Plugin.Log.Info($"[Blink] overlay ({(_screenSpace ? "screen-space" : "world")}) attached to camera '{camera.name}' stereo={camera.stereoEnabled} targetEye={camera.stereoTargetEye} parent={(camera.transform.parent != null ? camera.transform.parent.name : "none")}");
        }

        return true;
    }

    /// <summary>
    /// Builds the world-space hierarchy; fields are assigned only once everything exists, so a
    /// failure part way leaves no half-built overlay behind (EnsureAttached then keeps returning false).
    /// </summary>
    private void BuildWorld()
    {
        var root = new GameObject("MaxTweaksBlinkOverlay") { layer = UiLayer };
        root.SetActive(false);
        _visible = false;
        try
        {
            // Wide enough to cover the whole HMD field of view (BlinkArt.QuadHalfAngleDegrees each way).
            var vignetteSize = 2f * VignetteDistance * Mathf.Tan(BlinkArt.QuadHalfAngleDegrees * Mathf.Deg2Rad);
            var vignette = CreateWorldImage(root, "Vignette", new Vector3(0f, 0f, VignetteDistance), vignetteSize, vignetteSize, _assets.Vignette, 0);

            var eyeY = -EyeDistance * Mathf.Tan(EyeDegreesBelow * Mathf.Deg2Rad);
            var eye = CreateWorldImage(root, "ClosedEye", new Vector3(0f, eyeY, EyeDistance), EyeWidth, EyeWidth, _assets.Eye, 1);

            _vignette = vignette;
            _eye = eye;
            _root = root;
        }
        catch
        {
            UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    private void BuildScreenSpace(Camera camera)
    {
        var root = new GameObject("MaxTweaksBlinkOverlay", typeof(RectTransform)) { layer = UiLayer };
        root.SetActive(false);
        _visible = false;
        try
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = VignetteDistance;
            canvas.sortingOrder = 32000;

            // The vignette texture is square: size it to the longer side, centred, so the mask stays round.
            var vignette = CreateScreenImage(root, "Vignette", _assets.Vignette);
            var vRect = vignette.rectTransform;
            vRect.anchorMin = new Vector2(0.5f, 0.5f);
            vRect.anchorMax = new Vector2(0.5f, 0.5f);
            vRect.pivot = new Vector2(0.5f, 0.5f);
            var side = Mathf.Max(camera.pixelWidth, camera.pixelHeight) * 1.1f;
            vRect.sizeDelta = new Vector2(side, side);
            vRect.anchoredPosition = Vector2.zero;

            var eye = CreateScreenImage(root, "ClosedEye", _assets.Eye);
            var eRect = eye.rectTransform;
            eRect.anchorMin = new Vector2(0.5f, 0.5f);
            eRect.anchorMax = new Vector2(0.5f, 0.5f);
            eRect.pivot = new Vector2(0.5f, 0.5f);
            var height = camera.pixelHeight;
            eRect.sizeDelta = new Vector2(height * ScreenEyeSize, height * ScreenEyeSize);
            eRect.anchoredPosition = new Vector2(0f, -height * ScreenEyeBelow);

            _vignette = vignette;
            _eye = eye;
            _root = root;
        }
        catch
        {
            UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    private RawImage CreateWorldImage(GameObject root, string name, Vector3 localPosition, float width, float height, Texture texture, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
        go.transform.SetParent(root.transform, false);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 32000 + sortingOrder;

        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(CanvasUnits, CanvasUnits * (height / width));
        rect.localPosition = localPosition;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * (width / CanvasUnits);

        return CreateScreenImage(go, "Image", texture, stretch: true);
    }

    private RawImage CreateScreenImage(GameObject parent, string name, Texture texture, bool stretch = false)
    {
        var imageGo = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
        imageGo.transform.SetParent(parent.transform, false);
        if (stretch)
        {
            var imageRect = (RectTransform)imageGo.transform;
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
        }

        var image = imageGo.AddComponent<RawImage>();
        image.texture = texture;
        image.material = _assets.Material;
        image.raycastTarget = false;
        image.color = Color.clear;
        return image;
    }
}
