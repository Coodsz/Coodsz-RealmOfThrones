using System.Windows;
using RealmOfThrones.Core;

namespace RealmOfThronesHost;

public partial class MainWindow
{
    private void MoveHostModUp_Click(object sender, RoutedEventArgs e) => MoveHostMod(sender, -1);

    private void MoveHostModDown_Click(object sender, RoutedEventArgs e) => MoveHostMod(sender, 1);

    private void MoveHostMod(object sender, int delta)
    {
        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;
        if (sender is not FrameworkElement { DataContext: SealedModuleRow { CanMove: true, ModuleId: { } id } }
            || EditingSetup.Locked)
            return;
        var editing = EditingSetup;
        var original = editing.Clone();
        if (!RotLoadOrder.Move(editing, id, delta))
            return;
        if (RotLoadOrder.MoveProblem(original, editing, HostModFacts()) is { } problem)
        {
            editing.Extras = original.Extras;
            ModStatus(problem, failed: true);
            return;
        }
        ShowModSetups();
    }
}
