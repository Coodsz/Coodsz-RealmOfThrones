using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace RealmOfThronesHost;

static class ColoredConsole
{
    private static readonly SolidColorBrush NormalBrush = Freeze(Color.FromRgb(155, 196, 154));
    private static readonly SolidColorBrush FailedBrush = Freeze(Color.FromRgb(255, 77, 77));
    private static readonly SolidColorBrush InGameBrush = Freeze(Color.FromRgb(255, 180, 180));

    public static void Clear(RichTextBox box) => box.Document.Blocks.Clear();

    public static string Read(RichTextBox box) =>
        Coodsz.Ui.UiLanguage.EnglishText(box);

    public static void Append(RichTextBox box, string text, bool scroll)
    {
        if (!box.Dispatcher.CheckAccess())
        {
            var copy = text;
            box.Dispatcher.BeginInvoke(() => Append(box, copy, scroll));
            return;
        }

        var lines = text.Replace("\r", "").Split('\n');
        var tellThem = false;
        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;
            if (line.Contains("only available on the client", StringComparison.OrdinalIgnoreCase))
                tellThem = true;
            AddLine(box, line, CommandReply.Tone(line));
        }

        if (tellThem)
            AddLine(box, CommandReply.InGameNotice, CommandReplyTone.InGame);

        while (box.Document.Blocks.Count > 500)
            box.Document.Blocks.Remove(box.Document.Blocks.FirstBlock);

        if (scroll)
            box.ScrollToEnd();
    }

    private static void AddLine(RichTextBox box, string line, CommandReplyTone tone)
    {
        var brush = tone switch
        {
            CommandReplyTone.Failed => FailedBrush,
            CommandReplyTone.InGame => InGameBrush,
            _ => NormalBrush
        };
        var paragraph = new Paragraph(Coodsz.Ui.UiLanguage.LogRun(line))
        {
            Margin = new System.Windows.Thickness(0),
            Foreground = brush
        };
        box.Document.Blocks.Add(paragraph);
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
