using System.Net;
using System.Text;

namespace SwatchSheet.Core;

public enum CopyFormat
{
    HexList,
    Css,
    RgbList,
}

/// <summary>A piece of a copied line. If it has a swatch, a colored block is shown before the text.</summary>
public sealed record CopySegment(string Text, Rgb? Swatch = null);

public sealed record CopyLine(IReadOnlyList<CopySegment> Segments)
{
    public CopyLine(string text, Rgb? swatch = null) : this(new[] { new CopySegment(text, swatch) }) { }
    public string PlainText => string.Concat(Segments.Select(s => s.Text));
}

/// <summary>
/// Turns the sheet's colors into text to copy. Every copy carries three versions so each app
/// pastes the best one it understands:
///  - plain text (Notepad, Discord, code editors): just the codes
///  - HTML (OneNote, Google Docs, email): a colored block next to every code
///  - RTF (Word, WordPad): the same colored blocks
/// </summary>
public static class CopyFormats
{
    /// <summary>The block shown in front of each color when pasted somewhere that shows colors.</summary>
    public const string Block = "███";

    // RTF writes the block character as a numbered code (9608) followed by a "?" fallback.
    private static readonly string RtfBlock = string.Concat(Enumerable.Repeat("\\" + "u9608?", 3));

    public static string Label(CopyFormat format) => format switch
    {
        CopyFormat.HexList => "Hex list",
        CopyFormat.Css => "CSS",
        CopyFormat.RgbList => "RGB",
        _ => format.ToString(),
    };

    public static List<CopyLine> Lines(IReadOnlyList<Rgb> colors, CopyFormat format)
    {
        var lines = new List<CopyLine>();
        switch (format)
        {
            case CopyFormat.HexList:
                lines.AddRange(colors.Select(c => new CopyLine(c.Hex, c)));
                break;
            case CopyFormat.RgbList:
                lines.AddRange(colors.Select(c => new CopyLine(c.RgbText, c)));
                break;
            case CopyFormat.Css:
                if (colors.Count > 0)
                {
                    lines.Add(new CopyLine(":root {"));
                    lines.AddRange(colors.Select((c, i) => new CopyLine($"  --color-{i + 1}: {c.Hex};", c)));
                    lines.Add(new CopyLine("}"));
                }
                break;
        }
        return lines;
    }

    public static string PlainText(IEnumerable<CopyLine> lines) =>
        string.Join("\r\n", lines.Select(l => l.PlainText));

    /// <summary>An HTML snippet with a colored block in front of every color.</summary>
    public static string HtmlFragment(IEnumerable<CopyLine> lines, string? title = null, string? description = null, string? date = null)
    {
        var sb = new StringBuilder();
        title = title?.Trim();
        date = date?.Trim();
        description = NormalizeNewlines(description?.Trim());
        if (!string.IsNullOrEmpty(title))
            sb.Append("<div style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:16pt;font-weight:bold\">")
              .Append(WebUtility.HtmlEncode(title)).Append("</div>");
        if (!string.IsNullOrEmpty(date))
            sb.Append("<div style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:10pt;color:#888888\">")
              .Append(WebUtility.HtmlEncode(date)).Append("</div>");
        if (!string.IsNullOrEmpty(description))
            sb.Append("<div style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:11pt;color:#666666\">")
              .Append(WebUtility.HtmlEncode(description).Replace("\n", "<br>")).Append("</div>");
        if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(date) || !string.IsNullOrEmpty(description))
            sb.Append("<div>&nbsp;</div>");
        sb.Append("<div style=\"font-family:Consolas,'Courier New',monospace;font-size:11pt\">");
        foreach (var line in lines)
        {
            sb.Append("<div>");
            foreach (var seg in line.Segments)
            {
                if (seg.Swatch is Rgb c)
                    sb.Append($"<span style=\"color:{c.Hex};background-color:{c.Hex}\">")
                      .Append("&#9608;&#9608;&#9608;</span>&nbsp;");
                sb.Append(WebUtility.HtmlEncode(seg.Text).Replace("  ", "&nbsp;&nbsp;"));
            }
            sb.Append("</div>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// Wraps an HTML snippet in the header Windows expects on the clipboard ("CF_HTML").
    /// The numbers in the header are byte positions in the UTF-8 text.
    /// </summary>
    public static byte[] ClipboardHtml(string fragment)
    {
        const string header =
            "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string before = "<html><body>\r\n<!--StartFragment-->";
        const string after = "<!--EndFragment-->\r\n</body></html>";

        var utf8 = new UTF8Encoding(false);
        int headerLength = utf8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        int startHtml = headerLength;
        int startFragment = startHtml + utf8.GetByteCount(before);
        int endFragment = startFragment + utf8.GetByteCount(fragment);
        int endHtml = endFragment + utf8.GetByteCount(after);

        var text = string.Format(header, startHtml, endHtml, startFragment, endFragment) + before + fragment + after;
        return utf8.GetBytes(text);
    }

    /// <summary>The same thing as rich text (RTF) for Word and WordPad.</summary>
    public static string Rtf(IEnumerable<CopyLine> lines, string? title = null, string? description = null, string? date = null)
    {
        title = title?.Trim();
        date = date?.Trim();
        description = NormalizeNewlines(description?.Trim());
        var lineList = lines.ToList();
        var palette = lineList.SelectMany(l => l.Segments).Where(s => s.Swatch is not null)
                              .Select(s => s.Swatch!.Value).Distinct().ToList();
        var index = palette.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i + 1);

        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fmodern Consolas;}{\f1\fswiss Segoe UI;}}{\colortbl ;");
        foreach (var c in palette) sb.Append($@"\red{c.R}\green{c.G}\blue{c.B};");
        sb.Append(@"}");
        if (!string.IsNullOrEmpty(title))
            sb.Append(@"{\f1\fs32\b ").Append(RtfEscape(title)).Append(@"\par}");
        if (!string.IsNullOrEmpty(date))
            sb.Append(@"{\f1\fs20 ").Append(RtfEscape(date)).Append(@"\par}");
        if (!string.IsNullOrEmpty(description))
            sb.Append(@"{\f1\fs22 ").Append(RtfEscape(description).Replace("\n", @"\line ")).Append(@"\par}");
        if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(date) || !string.IsNullOrEmpty(description))
            sb.Append(@"\par ");
        sb.Append(@"\f0\fs22 ");
        foreach (var line in lineList)
        {
            foreach (var seg in line.Segments)
            {
                if (seg.Swatch is Rgb c)
                    sb.Append(@"\cf").Append(index[c]).Append(RtfBlock).Append(@"\cf0  ");
                sb.Append(RtfEscape(seg.Text));
            }
            sb.Append(@"\par ");
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string? NormalizeNewlines(string? text) => text?.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string RtfEscape(string text)
    {
        var sb = new StringBuilder();
        foreach (char ch in text)
        {
            if (ch is '\\' or '{' or '}') sb.Append('\\').Append(ch);
            else if (ch > 127) sb.Append($@"\u{(short)ch}?");
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}
