using System;
using MaxTweaks.Common;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MaxTweaks.Features.Chapters;

/// <summary>
/// One "&lt;" or "&gt;" skip arrow. Hover comes from two sources: the game's UI pointer (pause menu,
/// trigger to click) and the feature's own saber-ray test (during play, A to click).
/// </summary>
internal class ChapterArrowButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private static readonly Color NormalColor = new(1f, 1f, 1f, 0.75f);
    private static readonly Color HoverColor = new(1f, 0.82f, 0.2f, 1f);
    private static readonly Vector3 HoverScale = new(1.35f, 1.35f, 1f);

    private TextMeshProUGUI _text = null!;
    private Action<int, bool>? _onClick;
    private bool _pointerHover;
    private bool _rayHover;

    public int Row { get; private set; }

    public bool ToEnd { get; private set; }

    public RectTransform Rect { get; private set; } = null!;

    /// <summary>Whether the saber-ray hover is currently applied (cleared by OnDisable).</summary>
    public bool RayHovered => _rayHover;

    /// <summary>Hovered by either source (saber ray or pause-menu pointer).</summary>
    public bool IsHovered => _rayHover || _pointerHover;

    public void Init(TextMeshProUGUI text, int row, bool toEnd, Action<int, bool> onClick)
    {
        _text = text;
        Rect = (RectTransform)transform;
        Row = row;
        ToEnd = toEnd;
        _onClick = onClick;
        Apply();
    }

    public void SetRayHover(bool hover)
    {
        if (_rayHover != hover)
        {
            _rayHover = hover;
            Apply();
        }
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
        Guard.Run("Chapters", () => _onClick?.Invoke(Row, ToEnd));
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
        _text.color = hover ? HoverColor : NormalColor;
        transform.localScale = hover ? HoverScale : Vector3.one;
    }
}
