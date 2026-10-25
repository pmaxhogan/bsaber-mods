using System;
using System.Collections.Generic;
using MaxTweaks.Common.Pointing;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MaxTweaks.Features.ActivePause;

/// <summary>
/// Draws a white square frame ("halo") around every live selected note and a dim one around the
/// hovered note. Each halo is a tiny world-space canvas of four bars (the same UI images as the
/// panels), placed a little in front of the note towards the camera and turned to face it, so the
/// note stays visible through the open middle. Halos are pooled and reused; nothing has a collider.
/// </summary>
internal sealed class NoteHighlighter : IDisposable
{
    private const string Feature = "ActivePause";

    // Canvas units: 1 unit = 1 cm.
    private const float Scale = 0.01f;
    private const float Size = 62f;
    private const float Thickness = 3.2f;
    private const float TowardsCamera = 0.32f;

    private static readonly Color SelectedColor = new(1f, 1f, 1f, 1f);
    private static readonly Color HoverColor = new(1f, 1f, 1f, 0.4f);
    private static readonly Vector3 SelectedHoverScale = new(1.15f, 1.15f, 1f);

    private readonly List<Halo> _pool = new();
    private Camera? _camera;
    private int _used;

    /// <summary>Places the halos for this frame; call every frame while the song runs or is paused.</summary>
    public void Update(IReadOnlyList<IBeatmapObjectController> live, NoteSelection selection)
    {
        _used = 0;
        var hovered = selection.HoveredNote;
        if (selection.Count > 0 || hovered != null)
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            var eye = _camera != null ? _camera.transform.position : new Vector3(0f, 1.5f, 0f);
            for (var i = 0; i < live.Count; i++)
            {
                if (!NoteSelection.IsSelectable(live[i], out var note))
                {
                    continue;
                }

                var isHovered = hovered != null && note == hovered;
                var isSelected = selection.Count > 0 && selection.IsSelected(note);
                if (!isHovered && !isSelected)
                {
                    continue;
                }

                Place(Next(), note.noteTransform.position, eye, isSelected, isHovered);
            }
        }

        for (var i = _used; i < _pool.Count; i++)
        {
            _pool[i].SetVisible(false);
        }
    }

    public void Clear()
    {
        _used = 0;
        foreach (var halo in _pool)
        {
            halo.SetVisible(false);
        }
    }

    public void Dispose()
    {
        foreach (var halo in _pool)
        {
            halo.Destroy();
        }

        _pool.Clear();
    }

    private static void Place(Halo halo, Vector3 note, Vector3 eye, bool selected, bool hovered)
    {
        var toEye = eye - note;
        var direction = toEye.sqrMagnitude > 1e-6f ? toEye.normalized : Vector3.back;
        var position = note + (direction * TowardsCamera);
        halo.Root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-direction, Vector3.up));
        halo.SetColor(selected ? SelectedColor : HoverColor);
        halo.Root.transform.localScale = selected && hovered ? SelectedHoverScale * Scale : Vector3.one * Scale;
        halo.SetVisible(true);
    }

    private Halo Next()
    {
        if (_used == _pool.Count)
        {
            _pool.Add(Halo.Create(_pool.Count));
        }

        return _pool[_used++];
    }

    private sealed class Halo
    {
        private readonly Image[] _bars;
        private Color _color;

        private Halo(GameObject root, Image[] bars)
        {
            Root = root;
            _bars = bars;
        }

        public GameObject Root { get; }

        public static Halo Create(int index)
        {
            var go = new GameObject("MaxTweaksNoteHalo" + index, typeof(RectTransform), typeof(Canvas)) { layer = 5 };
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(Size, Size);
            rect.localScale = Vector3.one * Scale;

            var half = (Size - Thickness) * 0.5f;
            var bars = new[]
            {
                Bar(rect, "Top", new Vector2(0f, half), new Vector2(Size, Thickness)),
                Bar(rect, "Bottom", new Vector2(0f, -half), new Vector2(Size, Thickness)),
                Bar(rect, "Left", new Vector2(-half, 0f), new Vector2(Thickness, Size)),
                Bar(rect, "Right", new Vector2(half, 0f), new Vector2(Thickness, Size)),
            };
            go.SetActive(false);
            return new Halo(go, bars);
        }

        public void SetColor(Color color)
        {
            if (_color == color)
            {
                return;
            }

            _color = color;
            foreach (var bar in _bars)
            {
                bar.color = color;
            }
        }

        public void SetVisible(bool visible)
        {
            if (Root != null && Root.activeSelf != visible)
            {
                Root.SetActive(visible);
            }
        }

        public void Destroy()
        {
            if (Root != null)
            {
                Object.Destroy(Root);
            }
        }

        private static Image Bar(RectTransform parent, string name, Vector2 position, Vector2 size)
        {
            var image = WorldCanvas.CreateImage(name, parent, SelectedColor, Feature);
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            return image;
        }
    }
}
