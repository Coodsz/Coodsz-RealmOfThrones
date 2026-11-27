using RealmOfThrones.Core;
using TaomCoop.Core;

namespace System.Windows
{
    // The linked click handler reads only DataContext from the WPF sender.
    internal class FrameworkElement
    {
        public object? DataContext { get; init; }
    }

    internal class RoutedEventArgs { }
}

namespace RealmOfThronesHost
{
    public partial class MainWindow
    {
        private sealed record SealedModuleRow(string? ModuleId, bool Editable, bool CanMove);

        private TaomSetup EditingSetup { get; } = new() { Name = "Mine" };
        private IReadOnlyDictionary<string, TaomModFact> HostModFacts() => new Dictionary<string, TaomModFact>();
        private bool RefuseWhileModListRuns() => false;
        private void ModStatus(string message, bool failed) => throw new InvalidOperationException(message);
        private int Refreshes { get; set; }
        private void ShowModSetups() => Refreshes++;

        [Fact]
        public void The_sealed_core_rows_arrow_moves_the_group()
        {
            Assert.Null(RotLoadOrder.Add(EditingSetup, "Extra", "Extra", null));
            var coreRow = new SealedModuleRow(RotStack.CoreModuleId, Editable: false, CanMove: true);
            var button = new System.Windows.FrameworkElement { DataContext = coreRow };

            MoveHostModDown_Click(button, new System.Windows.RoutedEventArgs());

            Assert.Equal(RotLoadOrder.SlotBeforeRot, Assert.Single(EditingSetup.Extras).Slot);
            Assert.Equal(1, Refreshes);

            MoveHostModUp_Click(button, new System.Windows.RoutedEventArgs());

            Assert.Equal(RotLoadOrder.SlotAfterRot, Assert.Single(EditingSetup.Extras).Slot);
            Assert.Equal(2, Refreshes);
        }
    }
}
