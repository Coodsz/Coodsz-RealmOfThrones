namespace BannerlordCoopLauncher.Core.Modules;

/// <summary>Where the keyboard focus goes after Move up or Move down: the same arrow, the other one, or the row.</summary>
public enum MoveFocusTarget { Up, Down, Row }

/// <summary>
/// A move gives the mod row a new container, so the window puts the focus back on the arrow that was used, and Alt+Up,
/// Alt+Down or the same button keep moving the mod. A move to the edge turns that arrow off (the first movable row has no
/// Move up, the last no Move down), and a disabled button can't hold the focus: it would leave the list. Then the other
/// arrow takes it, or the row itself when both are off.
/// </summary>
public static class MoveFocus
{
    public static MoveFocusTarget After(bool movedUp, bool canMoveUp, bool canMoveDown) =>
        movedUp
            ? canMoveUp ? MoveFocusTarget.Up : canMoveDown ? MoveFocusTarget.Down : MoveFocusTarget.Row
            : canMoveDown ? MoveFocusTarget.Down : canMoveUp ? MoveFocusTarget.Up : MoveFocusTarget.Row;
}
