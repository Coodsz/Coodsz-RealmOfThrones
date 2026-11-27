using System.Windows;
using System.Windows.Controls;

namespace Coodsz.Ui;

/// <summary>
/// The one layout of a mod row in the mod list of every tool (the Coop and AD1259 Server Tools' ModuleList, the TAOM and
/// Burning Empires Server Tools' OrderList and the four client launchers' rows). Every column on the right has a fixed
/// width, kept even when a row has nothing in it (no Delete, no switch, no chip), so the chips, the arrows and the
/// switch sit at the same place on every row. Every row has the same height with its content centred, so a row with a
/// Report link under its chips looks like a row without one. XAML reads these with x:Static, the code built rows directly.
/// </summary>
public static class ModRowLayout
{
    /// <summary>The height of every row: room for two lines (name and id, or chips and the Report link) and the padding. A
    /// minimum, not a cap, so a long translated chip that wraps to a second line is never cut off.</summary>
    public const double RowHeight = 62;

    /// <summary>The chip column of the server tools: the chips wrap or trim inside it, its left edge never moves.</summary>
    public const double ChipColumn = 300;

    /// <summary>The chip column of the client launchers, narrower since their rows also carry a status chip column.</summary>
    public const double ClientChipColumn = 232;

    /// <summary>The on and off switch slot, kept empty on a row that has none.</summary>
    public const double SwitchColumn = 56;

    /// <summary>The TAOM and Burning Empires Server Tools' move up, move down and remove buttons, kept empty on a core row.</summary>
    public const double EditButtonsColumn = 132;

    /// <summary>The client launchers' Move up and Move down pair and the remove button, kept empty on a core row.</summary>
    public const double ClientActionColumn = 120;

    /// <summary>An empty fixed width slot for a row that has nothing in that column.</summary>
    public static FrameworkElement Slot(double width, UIElement? content = null)
    {
        var slot = new Border { Width = width, VerticalAlignment = VerticalAlignment.Center };
        if (content != null)
            slot.Child = content;
        return slot;
    }

    /// <summary>A row height that stays the same for one line and two line rows.</summary>
    public static void FixHeight(FrameworkElement row)
    {
        row.MinHeight = RowHeight;
        if (row is Border border && border.Child is FrameworkElement child)
            child.VerticalAlignment = VerticalAlignment.Center;
        else if (row is Panel)
            row.VerticalAlignment = VerticalAlignment.Center;
    }
}
