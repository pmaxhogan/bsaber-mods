using System;
using MaxTweaks.Common.Pointing;
using TMPro;

namespace MaxTweaks.Features.Chapters;

/// <summary>
/// One "&lt;" or "&gt;" skip arrow. Hover comes from two sources: the game's UI pointer (pause menu,
/// trigger to click) and the feature's own saber-ray test (during play, A to click); see
/// <see cref="RayHoverButton"/>.
/// </summary>
internal class ChapterArrowButton : RayHoverButton
{
    public int Row { get; private set; }

    public bool ToEnd { get; private set; }

    public void Init(TextMeshProUGUI text, int row, bool toEnd, Action<int, bool> onClick)
    {
        Row = row;
        ToEnd = toEnd;
        Init(text, "Chapters", () => onClick(Row, ToEnd));
    }
}
