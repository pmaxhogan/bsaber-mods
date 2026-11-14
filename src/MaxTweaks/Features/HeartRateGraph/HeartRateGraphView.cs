using System;
using MaxTweaks.Common;
using MaxTweaks.Common.Pointing;
using MaxTweaks.Core.HeartRateGraph;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MaxTweaks.Features.HeartRateGraph;

/// <summary>
/// The graph's world-space canvas: three faint guide lines (bottom, middle and top of the heart
/// rate range) and one <see cref="HeartRateGraphGraphic"/> that draws <see cref="Mesh"/>. Display
/// only: no raycaster and no raycast targets, so it never gets in the way of a pointer.
/// </summary>
internal sealed class HeartRateGraphView : IDisposable
{
    private const string Feature = "HeartRateGraph";
    private const int RampPixels = 256;

    // Canvas units (1 unit = 2 cm). Thinner lines shimmer at 7 m in a headset.
    private const float GuideThickness = 0.4f;
    private static readonly Color BaselineColor = new(1f, 1f, 1f, 0.3f);
    private static readonly Color GuideColor = new(1f, 1f, 1f, 0.1f);

    private readonly GameObject _root;
    private readonly HeartRateGraphGraphic _graphic;
    private readonly Texture2D _ramp;

    /// <param name="sizeMetres">Width (the whole song) and height (the heart rate range) in metres.</param>
    public HeartRateGraphView(Vector2 sizeMetres, Vector3 center, Quaternion rotation)
    {
        Size = sizeMetres / WorldCanvas.Scale;
        _root = WorldCanvas.CreatePlain("MaxTweaksHeartRateGraph", Size, center, rotation);
        var parent = (RectTransform)_root.transform;

        AddGuide(parent, "Baseline", 0f, BaselineColor);
        AddGuide(parent, "Middle", 0.5f, GuideColor);
        AddGuide(parent, "Top", 1f, GuideColor);

        // The colour ramp, one pixel per step from the lowest heart rate (bottom) to the highest.
        _ramp = new Texture2D(1, RampPixels, TextureFormat.RGBA32, false)
        {
            name = "MaxTweaksHeartRateRamp",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        _ramp.LoadRawTextureData(Plasma.Lut(RampPixels));
        _ramp.Apply(false, true);

        var go = new GameObject("Graph", typeof(RectTransform), typeof(CanvasRenderer), typeof(HeartRateGraphGraphic)) { layer = 5 };
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        _graphic = go.GetComponent<HeartRateGraphGraphic>();
        var material = WorldCanvas.NoGlowMaterial(Feature);
        if (material != null)
        {
            _graphic.material = material;
        }

        _graphic.raycastTarget = false;
        _graphic.Init(_ramp, Mesh, RampPixels);
    }

    /// <summary>Graph size in canvas units: build the mesh in these.</summary>
    public Vector2 Size { get; }

    public HrGraphMesh Mesh { get; } = new();

    public Vector3 Position => _root.transform.position;

    /// <summary>Call after <see cref="Mesh"/> changed.</summary>
    public void Refresh() => _graphic.SetVerticesDirty();

    /// <summary>Moves the graph; with a parent it then follows that transform (the rotating HUD of 360 levels).</summary>
    public void Place(Vector3 center, Quaternion rotation, Transform? parent)
    {
        var transform = _root.transform;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(center, rotation);
        transform.localScale = new Vector3(WorldCanvas.Scale, WorldCanvas.Scale, WorldCanvas.Scale);
        if (parent != null)
        {
            transform.SetParent(parent, true);
        }
    }

    public void Dispose()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
        }

        if (_ramp != null)
        {
            Object.Destroy(_ramp);
        }
    }

    private static void AddGuide(RectTransform parent, string name, float height, Color color)
    {
        var rect = WorldCanvas.CreateImage(name, parent, color, Feature).rectTransform;
        rect.anchorMin = new Vector2(0f, height);
        rect.anchorMax = new Vector2(1f, height);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, GuideThickness);
    }
}

/// <summary>
/// Draws an <see cref="HrGraphMesh"/> as one UI mesh. Every corner looks its colour up in the ramp
/// texture by its heart rate (uv.y), so the fill shades through the ramp from the bottom edge up to
/// the line, and the line takes the colour of its own height.
/// </summary>
internal sealed class HeartRateGraphGraphic : Graphic
{
    private Texture? _ramp;
    private HrGraphMesh? _mesh;
    private int _rampPixels = 2;

    public override Texture mainTexture => _ramp != null ? _ramp : s_WhiteTexture;

    public void Init(Texture ramp, HrGraphMesh mesh, int rampPixels)
    {
        _ramp = ramp;
        _mesh = mesh;
        _rampPixels = Mathf.Max(2, rampPixels);
        SetMaterialDirty();
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var mesh = _mesh;
        if (mesh == null || mesh.Vertices.Count == 0)
        {
            return;
        }

        Guard.Run("HeartRateGraph", () => Populate(vh, mesh));
    }

    private void Populate(VertexHelper vh, HrGraphMesh mesh)
    {
        // A UI mesh holds 65000 vertices; four per reading is hours of song, so just refuse beyond it.
        if (mesh.Vertices.Count > 64000)
        {
            return;
        }

        var origin = rectTransform.rect.min;
        var colour = (Color32)color;
        // Sample pixel centres: value 0 is the middle of the first ramp pixel, 1 of the last.
        var first = 0.5f / _rampPixels;
        var span = (_rampPixels - 1f) / _rampPixels;
        foreach (var vertex in mesh.Vertices)
        {
            var tint = colour;
            tint.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(vertex.Alpha) * colour.a);
            vh.AddVert(
                new Vector3(origin.x + vertex.X, origin.y + vertex.Y, 0f),
                tint,
                new Vector4(0.5f, first + (vertex.Value * span), 0f, 0f));
        }

        var triangles = mesh.Triangles;
        for (var i = 0; i + 2 < triangles.Count; i += 3)
        {
            vh.AddTriangle(triangles[i], triangles[i + 1], triangles[i + 2]);
        }
    }
}
