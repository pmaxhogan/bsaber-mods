using TMPro;
using UnityEngine;

namespace MaxTweaks.Common.Toasts;

/// <summary>
/// Drives one toast's fade in, hold and fade out on unscaled time (so it also runs while paused).
/// The component disables itself when idle, so it costs nothing per frame between toasts.
/// </summary>
internal class ToastView : MonoBehaviour
{
    private const float FadeInSeconds = 0.2f;
    private const float FadeOutSeconds = 0.6f;

    private CanvasGroup _group = null!;
    private RectTransform _background = null!;
    private TMP_Text _text = null!;
    private Vector2 _maxSize;
    private Vector2 _padding;
    private float _holdUntil;
    private bool _fadingOut;

    public void Init(CanvasGroup group, RectTransform background, TMP_Text text, Vector2 maxSize, Vector2 padding)
    {
        _group = group;
        _background = background;
        _text = text;
        _maxSize = maxSize;
        _padding = padding;
        enabled = false;
    }

    public void Show(string message, float seconds)
    {
        // Activate first: TMP initialises its text info in Awake, which only runs once the object is
        // active, and measuring before that throws.
        gameObject.SetActive(true);
        _text.text = message;
        _text.ForceMeshUpdate();
        var size = new Vector2(Mathf.Min(_text.preferredWidth, _maxSize.x), Mathf.Min(_text.preferredHeight, _maxSize.y));
        _background.sizeDelta = size + (_padding * 2f);

        _holdUntil = Time.unscaledTime + FadeInSeconds + Mathf.Max(0.5f, seconds);
        _fadingOut = false;
        enabled = true;
    }

    public void FadeOut()
    {
        if (gameObject.activeSelf)
        {
            _holdUntil = Time.unscaledTime;
        }
    }

    protected void Update()
    {
        var delta = Time.unscaledDeltaTime;
        if (!_fadingOut && Time.unscaledTime >= _holdUntil)
        {
            _fadingOut = true;
        }

        if (_fadingOut)
        {
            _group.alpha = Mathf.Max(0f, _group.alpha - (delta / FadeOutSeconds));
            if (_group.alpha <= 0f)
            {
                enabled = false;
                gameObject.SetActive(false);
            }
        }
        else if (_group.alpha < 1f)
        {
            _group.alpha = Mathf.Min(1f, _group.alpha + (delta / FadeInSeconds));
        }
    }
}
