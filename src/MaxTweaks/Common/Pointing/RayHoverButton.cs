using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MaxTweaks.Common.Pointing;

/// <summary>
/// A world-space text button with two hover sources: the game's UI pointer (pause-menu controllers,
/// trigger to click, via the canvas raycaster) and a feature's own ray test (sabers, camera; the
/// feature calls <see cref="SetRayHover"/> and handles A itself). Hover shows as colour + scale.
/// Shared by Chapters (skip arrows) and Active pause (seek buttons).
/// </summary>
internal class RayHoverButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private static readonly Color NormalColor = new(1f, 1f, 1f, 0.75f);
    private static readonly Color HoverColor = new(1f, 0.82f, 0.2f, 1f);
    private static readonly Color LitColor = new(1f, 0.72f, 0.05f, 1f);
    private static readonly Color DimmedColor = new(1f, 1f, 1f, 0.3f);
    private static readonly Vector3 HoverScale = new(1.35f, 1.35f, 1f);

    private TextMeshProUGUI _text = null!;
    private Action? _onClick;
    private string _feature = "MaxTweaks";
    private bool _pointerHover;
    private bool _rayHover;
    private bool _lit;
    private bool _dimmed;
    private Vector3 _hoverScale = HoverScale;

    public RectTransform Rect { get; private set; } = null!;

    /// <summary>Whether the ray hover is currently applied (cleared by OnDisable).</summary>
    public bool RayHovered => _rayHover;

    /// <summary>Hovered by either source (feature ray or UI pointer).</summary>
    public bool IsHovered => _rayHover || _pointerHover;

    /// <param name="feature">Log prefix for exceptions thrown by <paramref name="onClick"/>.</param>
    public void Init(TextMeshProUGUI text, string feature, Action onClick)
    {
        _text = text;
        Rect = (RectTransform)transform;
        _feature = feature;
        _onClick = onClick;
        Apply();
    }

    /// <summary>Whether the toggle-style "on" look is applied (see <see cref="SetLit"/>).</summary>
    public bool Lit => _lit;

    public void SetRayHover(bool hover)
    {
        if (_rayHover != hover)
        {
            _rayHover = hover;
            Apply();
        }
    }

    /// <summary>Toggle-style "on" look: gold text that stays when the hover leaves.</summary>
    public void SetLit(bool lit)
    {
        if (_lit != lit)
        {
            _lit = lit;
            Apply();
        }
    }

    /// <summary>Faded look for a button that currently does nothing (still clickable).</summary>
    public void SetDimmed(bool dimmed)
    {
        if (_dimmed != dimmed)
        {
            _dimmed = dimmed;
            Apply();
        }
    }

    /// <summary>Hover scale (default 1.35); wide buttons look better with less.</summary>
    public void SetHoverScale(float scale)
    {
        _hoverScale = new Vector3(scale, scale, 1f);
        Apply();
    }

    public void SetLabel(string label)
    {
        if (_text != null && _text.text != label)
        {
            _text.text = label;
        }
    }

    /// <summary>True when <paramref name="worldPoint"/> (on the button's plane) lies inside its rect.</summary>
    public bool Contains(Vector3 worldPoint)
    {
        Vector2 local = Rect.InverseTransformPoint(worldPoint);
        return Rect.rect.Contains(local);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _pointerHover = true;
        Apply();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerHover = false;
        Apply();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Guard.Run(_feature, () => _onClick?.Invoke());
    }

    protected void OnDisable()
    {
        _pointerHover = false;
        _rayHover = false;
        Apply();
    }

    private void Apply()
    {
        if (_text == null)
        {
            return;
        }

        // Only called when a hover source changes, never per frame.
        var hover = _pointerHover || _rayHover;
        _text.color = _lit ? LitColor : hover ? HoverColor : _dimmed ? DimmedColor : NormalColor;
        transform.localScale = hover ? _hoverScale : Vector3.one;
    }
}

/// <summary>
/// Remembers which <see cref="RayHoverButton"/> the feature's ray hovers and moves the ray hover
/// between buttons (one at a time).
/// </summary>
internal sealed class RayHoverTracker
{
    public RayHoverButton? Current { get; private set; }

    /// <summary>Highlights <paramref name="button"/> as ray-hovered (null clears it).</summary>
    public void Set(RayHoverButton? button)
    {
        // The button may have cleared its own ray hover (OnDisable when it hid), so compare the
        // button's actual state, not just the cached reference.
        if (Current == button && (button == null || button.RayHovered))
        {
            return;
        }

        if (Current != null)
        {
            Current.SetRayHover(false);
        }

        Current = button;
        if (button != null)
        {
            button.SetRayHover(true);
        }
    }
}
