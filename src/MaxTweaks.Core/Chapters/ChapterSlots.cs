using System;

namespace MaxTweaks.Core.Chapters;

/// <summary>
/// Assigns active chapters to display rows (row 0 = bottom) so a chapter keeps its row while it
/// stays active. Normally rows follow the stacking order from <see cref="ChapterStack.GetActive"/>
/// (longest at the bottom); since that order never changes between two chapters, a chapter only moves
/// when one below it ends (the stack closes the gap) or a longer one starts. While the player is
/// aiming at an arrow (<c>frozen</c>), nothing moves at all: ended chapters leave an empty row and
/// new ones take the lowest free row; the normal order is restored once the aim is released.
/// Allocation-free after construction.
/// </summary>
public sealed class ChapterSlots
{
    private readonly int[] _rows;

    public ChapterSlots(int rows)
    {
        if (rows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "need at least one row");
        }

        _rows = new int[rows];
        for (var i = 0; i < rows; i++)
        {
            _rows[i] = -1;
        }
    }

    public int RowCount => _rows.Length;

    /// <summary>Chapter (index into <see cref="ChapterStack.Chapters"/>) shown on <paramref name="row"/>, or -1.</summary>
    public int ChapterAt(int row) => _rows[row];

    /// <param name="active">Active chapters in stacking order, bottom first.</param>
    /// <param name="count">Number of valid entries in <paramref name="active"/>.</param>
    /// <param name="frozen">True while an arrow is aimed at: keep every row where it is.</param>
    /// <returns>True if any row changed.</returns>
    public bool Update(int[] active, int count, bool frozen)
    {
        if (active == null)
        {
            throw new ArgumentNullException(nameof(active));
        }

        count = Math.Min(count, active.Length);
        var changed = false;
        if (!frozen)
        {
            for (var row = 0; row < _rows.Length; row++)
            {
                var want = row < count ? active[row] : -1;
                if (_rows[row] != want)
                {
                    _rows[row] = want;
                    changed = true;
                }
            }

            return changed;
        }

        // Frozen: drop rows whose chapter ended...
        for (var row = 0; row < _rows.Length; row++)
        {
            if (_rows[row] >= 0 && IndexOf(active, count, _rows[row]) < 0)
            {
                _rows[row] = -1;
                changed = true;
            }
        }

        // ...and put newly active chapters into the lowest free rows (dropped if none is free).
        for (var i = 0; i < count; i++)
        {
            if (IndexOf(_rows, _rows.Length, active[i]) >= 0)
            {
                continue;
            }

            var free = IndexOf(_rows, _rows.Length, -1);
            if (free < 0)
            {
                break;
            }

            _rows[free] = active[i];
            changed = true;
        }

        return changed;
    }

    private static int IndexOf(int[] values, int count, int value)
    {
        for (var i = 0; i < count; i++)
        {
            if (values[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
