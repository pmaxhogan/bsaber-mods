using System;
using MaxTweaks.Core.Blink;
using UnityEngine;
using UnityEngine.UI;

namespace MaxTweaks.Features.Blink;

/// <summary>
/// The head-locked blink overlay: a soft black vignette (clear centre, darkest towards the corners
/// of the view) and a small closed-eye icon a little below eye level. Two world-space canvases
/// parented to the main (HMD) camera, on the UI layer the HMD camera renders. Camera2 cameras show
/// it too when their "UI" visibility is on (same layer as the game HUD).
/// </summary>
internal class BlinkOverlay : IDisposable
{
    /// <summary>The game HUD's layer; the HMD camera renders it and Camera2 exposes it as "UI".</summary>
    private const int UiLayer = 5;

    private const float VignetteDistance = 0.6f;
    private const float EyeDistance = 1.5f;
    private const float EyeDegreesBelow = 10f;
    private const float EyeWidth = 0.2f;
    private const float EyeMaxAlpha = 0.85f;
    private const float CanvasUnits = 100f;

    private readonly BlinkAssets _assets;
    private readonly float _strength;
    private GameObject? _root;
    private RawImage? _vignette;
    private RawImage? _eye;
    private Transform? _parent;
    private bool _visible;

    public BlinkOverlay(BlinkAssets assets, float vignetteStrength)
    {
        _assets = assets;
        _strength = Mathf.Clamp01(vignetteStrength);
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
            Build();
        }

        _parent = camera.transform;
        _root!.transform.SetParent(_parent, false);
        _root.transform.localPosition = Vector3.zero;
        _root.transform.localRotation = Quaternion.identity;
        _root.transform.localScale = Vector3.one;
        return true;
    }

    private void Build()
    {
        _root = new GameObject("MaxTweaksBlinkOverlay") { layer = UiLayer };
        _root.SetActive(false);
        _visible = false;

        // Wide enough to cover the whole HMD field of view (BlinkArt.QuadHalfAngleDegrees each way).
        var vignetteSize = 2f * VignetteDistance * Mathf.Tan(BlinkArt.QuadHalfAngleDegrees * Mathf.Deg2Rad);
        _vignette = CreateImage("Vignette", new Vector3(0f, 0f, VignetteDistance), vignetteSize, vignetteSize, _assets.Vignette, 0);

        var eyeY = -EyeDistance * Mathf.Tan(EyeDegreesBelow * Mathf.Deg2Rad);
        _eye = CreateImage("ClosedEye", new Vector3(0f, eyeY, EyeDistance), EyeWidth, EyeWidth, _assets.Eye, 1);
    }

    private RawImage CreateImage(string name, Vector3 localPosition, float width, float height, Texture texture, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
        go.transform.SetParent(_root!.transform, false);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 32000 + sortingOrder;

        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(CanvasUnits, CanvasUnits * (height / width));
        rect.localPosition = localPosition;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * (width / CanvasUnits);

        var imageGo = new GameObject("Image", typeof(RectTransform)) { layer = UiLayer };
        imageGo.transform.SetParent(go.transform, false);
        var imageRect = (RectTransform)imageGo.transform;
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;

        var image = imageGo.AddComponent<RawImage>();
        image.texture = texture;
        image.material = _assets.Material;
        image.raycastTarget = false;
        image.color = Color.clear;
        return image;
    }
}
