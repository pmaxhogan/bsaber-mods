using System;
using MaxTweaks.Core.Blink;
using UnityEngine;
using UnityEngine.Rendering;

namespace MaxTweaks.Features.Blink;

/// <summary>
/// Procedurally generated overlay textures and the UI material, created lazily on the main thread
/// the first time a song needs them and kept for the whole session (no shipped image assets).
/// </summary>
internal class BlinkAssets
{
    private const int VignetteSize = 256;
    private const int EyeSize = 128;

    private Texture2D? _vignette;
    private Texture2D? _eye;
    private Material? _material;

    /// <summary>Black, alpha = vignette mask (strength is applied through the image colour).</summary>
    public Texture2D Vignette => _vignette != null ? _vignette : (_vignette = BuildVignette());

    /// <summary>White closed-eye icon on transparent.</summary>
    public Texture2D Eye => _eye != null ? _eye : (_eye = BuildEye());

    /// <summary>
    /// A UI material that draws on top of the scene: a copy of the game's no-glow UI material
    /// (via BSML) so the overlay does not bloom, falling back to Unity's default UI material.
    /// </summary>
    public Material Material => _material != null ? _material : (_material = BuildMaterial());

    private static Texture2D BuildVignette()
    {
        var pixels = new Color32[VignetteSize * VignetteSize];
        for (var y = 0; y < VignetteSize; y++)
        {
            var ny = (((y + 0.5f) / VignetteSize) * 2f) - 1f;
            for (var x = 0; x < VignetteSize; x++)
            {
                var nx = (((x + 0.5f) / VignetteSize) * 2f) - 1f;
                var a = BlinkArt.VignetteMask(nx, ny);
                pixels[(y * VignetteSize) + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
            }
        }

        return MakeTexture("MaxTweaksBlinkVignette", VignetteSize, pixels, mipmaps: false);
    }

    private static Texture2D BuildEye()
    {
        var pixels = new Color32[EyeSize * EyeSize];
        var pixel = 2f / EyeSize;
        for (var y = 0; y < EyeSize; y++)
        {
            // Shift the drawing up a little: the lid sits high and the lashes hang below it.
            var ny = ((((y + 0.5f) / EyeSize) * 2f) - 1f) - 0.1f;
            for (var x = 0; x < EyeSize; x++)
            {
                var nx = (((x + 0.5f) / EyeSize) * 2f) - 1f;
                var a = BlinkArt.ClosedEye(nx, ny, pixel);
                pixels[(y * EyeSize) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }

        return MakeTexture("MaxTweaksBlinkEye", EyeSize, pixels, mipmaps: true);
    }

    private static Texture2D MakeTexture(string name, int size, Color32[] pixels, bool mipmaps)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipmaps)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };
        texture.SetPixels32(pixels);
        texture.Apply(mipmaps, makeNoLongerReadable: true);
        return texture;
    }

    private static Material BuildMaterial()
    {
        Material? source = null;
        try
        {
            source = BeatSaberMarkupLanguage.Utilities.ImageResources.NoGlowMat;
        }
        catch (Exception e)
        {
            Plugin.Log.Debug($"[Blink] no-glow material unavailable: {e.Message}");
        }

        var usedFallback = source == null;
        if (source == null)
        {
            source = Canvas.GetDefaultCanvasMaterial();
        }

        var material = new Material(source)
        {
            name = "MaxTweaksBlinkOverlay",
            renderQueue = (int)RenderQueue.Overlay,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };

        // Draw over everything (walls passing close to the head) when the shader supports it.
        if (material.HasProperty("unity_GUIZTestMode"))
        {
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        }

        if (material.HasProperty("_ZTest"))
        {
            material.SetInt("_ZTest", (int)CompareFunction.Always);
        }

        Plugin.Log.Debug($"[Blink] overlay material from '{source.name}' (shader '{material.shader.name}', fallback={usedFallback})");
        return material;
    }
}
