using Kanawanagasaki.Blazor.MarkdownEditor.Services;
using Kanawanagasaki.Blazor.MarkdownEditor.Extensions;
using Xunit;

namespace Kanawanagasaki.Blazor.MarkdownEditor.bUnitTests;

public class DiagTest
{
    [Fact]
    public void TraceFailingTest()
    {
        string text = "One two three four five";
        int start = 4, end = 13;
        var result = MarkdownTextExtensions.ToggleBold(text, start, end);
        text = result.Text;
        Console.WriteLine($"After Bold: [{text}]");
        
        int threeStart = text.IndexOf("three");
        int fourEnd = text.IndexOf("four") + "four".Length;
        Console.WriteLine($"Selection: [{threeStart}..{fourEnd}]");
        Console.WriteLine($"Selected: [{text.Substring(threeStart, fourEnd - threeStart)}]");
        
        // Parse and trace
        var doc = MarkdownDocument.Parse(text);
        foreach (var line in doc.Lines)
        {
            Console.WriteLine($"  PlainText: [{line.PlainText}]");
            foreach (var span in line.Spans)
                Console.WriteLine($"    Span [{span.Start}..{span.End}) Styles={span.Styles}");
        }
        
        // Toggle italic
        result = MarkdownTextExtensions.ToggleItalic(text, threeStart, fourEnd);
        Console.WriteLine($"After Italic: [{result.Text}]");
        Console.WriteLine($"Expected:      [One **two *three**** *four* five]");
        Console.WriteLine($"Match: {result.Text == "One **two *three**** *four* five"}");
    }
}
