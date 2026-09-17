using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace iBarter.View;

public static class LogAppearance {
    public static void Append(RichTextBox box, string time, string message, Brush color) {
        if (box.Document.Blocks.Count == 1 && string.IsNullOrWhiteSpace(
            new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text))
            box.Document.Blocks.Clear();
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        var stamp = new Run(time);
        stamp.SetResourceReference(TextElement.ForegroundProperty, "AppMutedTextBrush");
        var text = new Run(message);
        string key = "AppTextBrush";
        if (color is SolidColorBrush solid) {
            var c = solid.Color;
            if (c.R > c.G * 1.25 && c.R > c.B * 1.25) key = "AppLogWarningBrush";
            else if (c.G > c.R && c.G > c.B) key = "AppLogSuccessBrush";
            else if (c.B > c.R * 1.25 || c.G > c.R * 1.25) key = "AppLogInfoBrush";
        }
        text.SetResourceReference(TextElement.ForegroundProperty, key);
        paragraph.Inlines.Add(stamp); paragraph.Inlines.Add(text);
        box.Document.Blocks.Add(paragraph);
    }
}
