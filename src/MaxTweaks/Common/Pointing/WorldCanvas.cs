using System;
using BeatSaberMarkupLanguage.FloatingScreen;
using UnityEngine;
using UnityEngine.UI;

namespace MaxTweaks.Common.Pointing;

/// <summary>
/// World-space canvases for in-game panels with <see cref="RayHoverButton"/>s (Chapters, Active pause).
/// </summary>
internal static class WorldCanvas
{
    /// <summary>World size of one canvas unit (BSML FloatingScreen's scale): 1 unit = 2 cm.</summary>
    public const float Scale = 0.02f;

    private static Sprite? whitePixel;
    private static Material? noGlow;
    private static bool lookedUpAssets;

    /// <summary>
    /// A BSML FloatingScreen (it brings a VRGraphicRaycaster, so UI pointers can hover/click its
    /// buttons); falls back to a plain world-space canvas (no pointer clicks, ray + A only) if that fails.
    /// </summary>
    public static GameObject Create(string name, string feature, Vector2 size, Vector3 center, Quaternion rotation)
    {
        GameObject go;
        try
        {
            go = FloatingScreen.CreateFloatingScreen(size, false, center, rotation).gameObject;
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"[{feature}] floating screen unavailable, using a plain canvas (no trigger clicks): {e.Message}");
            go = CreatePlain(name, size, center, rotation);
        }

        go.name = name;
        return go;
    }

    /// <summary>
    /// A bare world-space canvas for display-only panels (no raycaster, so nothing on it can be
    /// hovered or clicked). <paramref name="size"/> is in canvas units.
    /// </summary>
    public static GameObject CreatePlain(string name, Vector2 size, Vector3 center, Quaternion rotation)
    {
        // CurvedCanvasSettings is required by the CurvedTextMeshPro that BeatSaberUI.CreateText makes.
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(HMUI.CurvedCanvasSettings)) { layer = 5 };
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        go.GetComponent<HMUI.CurvedCanvasSettings>().SetRadius(0f);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = size;
        rect.SetPositionAndRotation(center, rotation);
        rect.localScale = new Vector3(Scale, Scale, Scale);
        return go;
    }

    /// <summary>A flat-coloured, non-raycast image (BSML's white pixel and no-glow material when available).</summary>
    public static Image CreateImage(string name, RectTransform parent, Color color, string feature)
    {
        LookUpAssets(feature);
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

    /// <summary>BSML's UI material that leaves the bloom (alpha) channel alone, or null when it cannot be found.</summary>
    public static Material? NoGlowMaterial(string feature)
    {
        LookUpAssets(feature);
        return noGlow;
    }

    private static void LookUpAssets(string feature)
    {
        if (lookedUpAssets)
        {
            return;
        }

        lookedUpAssets = true;
        try
        {
            whitePixel = BeatSaberMarkupLanguage.Utilities.ImageResources.WhitePixel;
            noGlow = BeatSaberMarkupLanguage.Utilities.ImageResources.NoGlowMat;
        }
        catch (Exception e)
        {
            Plugin.Log.Debug($"[{feature}] UI assets not found, using defaults: {e.Message}");
        }
    }
}
