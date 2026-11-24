using System.Text;
using SwatchSheet.Core;

// Plain test runner: prints each test and exits with 1 if anything failed (build.cmd checks this).
int passed = 0, failed = 0;
var temp = Path.Combine(Path.GetTempPath(), "SwatchSheetTests-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(temp);

void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine($"  ok    {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"  FAIL  {name}\n        {ex.Message}"); }
}
void Eq<T>(T expected, T actual, string what = "value")
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{what}: expected {expected}, got {actual}");
}
void True(bool condition, string what)
{
    if (!condition) throw new Exception(what);
}
Rgb Hex(string h) => Rgb.TryParseHex(h, out var c) ? c : throw new Exception($"bad test color {h}");
string Hexes(IEnumerable<Rgb> colors) => string.Join(" ", colors.Select(c => c.Hex));

// A width x height image filled by a function of (x, y) -> (r, g, b, a).
PixelImage MakeImage(int w, int h, Func<int, int, (byte r, byte g, byte b, byte a)> fill)
{
    int stride = w * 4 + 8; // deliberately padded rows, like real Windows bitmaps
    var px = new byte[stride * h];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var (r, g, b, a) = fill(x, y);
            int i = y * stride + x * 4;
            px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a;
        }
    return new PixelImage(w, h, stride, px);
}

Console.WriteLine("Colors");
Test("hex is upper case and zero padded", () => Eq("#0102FF", new Rgb(1, 2, 255).Hex));
Test("parses #RRGGBB, RRGGBB, #RGB and spaces", () =>
{
    Eq("#A1B2C3", Hex("#a1b2c3").Hex);
    Eq("#A1B2C3", Hex("a1b2c3").Hex);
    Eq("#AABBCC", Hex("#abc").Hex);
    Eq("#A1B2C3", Hex("  #A1B2C3 ").Hex);
});
Test("rejects bad hex", () =>
{
    foreach (var bad in new[] { "", " ", null, "#12", "#12345", "zzzzzz", "#GGGGGG", "#1234567" })
        True(!Rgb.TryParseHex(bad, out _), $"should reject '{bad}'");
});
Test("HSV of pure colors", () =>
{
    var (h, s, v) = new Rgb(255, 0, 0).ToHsv();
    Eq(0.0, h, "red hue"); Eq(1.0, s, "red sat"); Eq(1.0, v, "red value");
    Eq(120.0, new Rgb(0, 255, 0).ToHsv().H, "green hue");
    Eq(240.0, new Rgb(0, 0, 255).ToHsv().H, "blue hue");
    Eq(0.0, new Rgb(128, 128, 128).ToHsv().S, "grey sat");
});
Test("light/dark text choice", () =>
{
    True(new Rgb(255, 255, 255).IsLight, "white is light");
    True(new Rgb(255, 230, 0).IsLight, "yellow is light");
    True(!new Rgb(0, 0, 0).IsLight, "black is dark");
    True(!new Rgb(20, 30, 140).IsLight, "navy is dark");
});
Test("finds colors in pasted text, in order", () =>
{
    var found = ColorText.FindColors("bg: #ff0000; fg #0f0 then rgb(0, 0, 255) and #11223344 and the word cafe #1234567");
    Eq("#FF0000 #00FF00 #0000FF #112233", Hexes(found));
});
Test("ignores impossible rgb() values", () => Eq(0, ColorText.FindColors("rgb(300, 0, 0)").Count));
Test("rgba() works too", () => Eq("#0A141E", Hexes(ColorText.FindColors("rgba(10,20,30,0.5)"))));

Console.WriteLine("Rainbow sort");
Test("orders red, orange, yellow, green, blue, indigo, violet, then pink", () =>
{
    var expected = new[] { "#FF0000", "#FF8000", "#FFFF00", "#00C000", "#0080FF", "#4B00FF", "#A000FF", "#FF69B4" };
    var shuffled = new[] { "#4B00FF", "#FFFF00", "#FF69B4", "#FF0000", "#0080FF", "#A000FF", "#00C000", "#FF8000" };
    Eq(string.Join(" ", expected), Hexes(RainbowSort.Sort(shuffled.Select(Hex), c => c)));
});
Test("greys, white and black go last, light to dark", () =>
{
    var input = new[] { "#000000", "#00C000", "#808080", "#FFFFFF", "#FF0000" }.Select(Hex);
    Eq("#FF0000 #00C000 #FFFFFF #808080 #000000", Hexes(RainbowSort.Sort(input, c => c)));
});
Test("very dark colors count as black", () =>
{
    True(RainbowSort.IsNeutral(new Rgb(16, 0, 0)), "near-black red is neutral");
    True(!RainbowSort.IsNeutral(new Rgb(139, 0, 0)), "dark red is a color");
});
Test("lighter shade first within the same hue", () =>
    Eq("#FF0000 #8B0000", Hexes(RainbowSort.Sort(new[] { Hex("#8B0000"), Hex("#FF0000") }, c => c))));
Test("crimson sits with red at the start, not after pink", () =>
{
    var sorted = RainbowSort.Sort(new[] { Hex("#FF69B4"), Hex("#00C000"), Hex("#DC143C") }, c => c);
    Eq("#DC143C", sorted[0].Hex, "first");
});
Test("keeps every item, and equal colors keep their order", () =>
{
    var items = new[] { ("a", Hex("#00FF00")), ("b", Hex("#FF0000")), ("c", Hex("#00FF00")), ("d", Hex("#808080")) };
    Eq("b a c d", string.Join(" ", RainbowSort.Sort(items, i => i.Item2).Select(i => i.Item1)));
});

Console.WriteLine("Picking pixels");
Test("1 px sample reads the exact pixel", () =>
{
    var img = MakeImage(4, 3, (x, y) => ((byte)(x * 10), (byte)(y * 20), 7, 255));
    Eq("#1E2807", img.Sample(3, 2)!.Value.Hex);
});
Test("3x3 sample averages the block", () =>
{
    // Centre pixel white, the rest black: average is 255/9 = 28.3 -> 28.
    var img = MakeImage(3, 3, (x, y) => x == 1 && y == 1 ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)255));
    Eq("#1C1C1C", img.Sample(1, 1, 1)!.Value.Hex);
});
Test("sample at the corner only uses pixels inside the image", () =>
{
    var img = MakeImage(5, 5, (x, y) => x == 0 && y == 0 ? ((byte)200, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)255));
    // 2x2 inside the image around the corner: one red, three black.
    Eq("#320000", img.Sample(0, 0, 1)!.Value.Hex);
});
Test("see-through pixels are ignored", () =>
{
    var img = MakeImage(3, 1, (x, y) => x == 1 ? ((byte)0, (byte)0, (byte)255, (byte)255) : ((byte)255, (byte)0, (byte)0, (byte)0));
    Eq("#0000FF", img.Sample(1, 0, 1)!.Value.Hex);
    Eq(null, img.Sample(0, 0, 0), "fully see-through pixel");
});
Test("outside the image gives nothing", () =>
{
    var img = MakeImage(2, 2, (x, y) => (1, 1, 1, 255));
    Eq(null, img.Sample(-1, 0)); Eq(null, img.Sample(2, 0)); Eq(null, img.Sample(0, 2));
});
Test("all-see-through clipboard images are made solid", () =>
{
    var img = MakeImage(2, 2, (x, y) => (9, 9, 9, 0));
    True(img.FixAllTransparent(), "should fix");
    Eq("#090909", img.Sample(0, 0)!.Value.Hex);
    var normal = MakeImage(2, 2, (x, y) => x == 0 ? ((byte)9, (byte)9, (byte)9, (byte)0) : ((byte)9, (byte)9, (byte)9, (byte)255));
    True(!normal.FixAllTransparent(), "partly see-through images are left alone");
    Eq(null, normal.Sample(0, 0));
});
Test("thumbnail keeps the shape and averages colors", () =>
{
    var img = MakeImage(400, 200, (x, y) => x % 2 == 0 ? ((byte)0, (byte)0, (byte)0, (byte)255) : ((byte)200, (byte)200, (byte)200, (byte)255));
    var t = img.Downscale(100, 100);
    Eq(100, t.Width, "width"); Eq(50, t.Height, "height");
    Eq("#646464", t.Sample(10, 10)!.Value.Hex);
    var small = MakeImage(10, 10, (x, y) => (1, 2, 3, 255)).Downscale(100, 100);
    Eq(10, small.Width, "never enlarges");
});
Test("rejects broken pixel data", () =>
{
    bool threw = false;
    try { _ = new PixelImage(10, 10, 40, new byte[100]); } catch (ArgumentException) { threw = true; }
    True(threw, "should refuse too little data");
});

Console.WriteLine("Copy formats");
var three = new[] { Hex("#FF0000"), Hex("#00FF00"), Hex("#0000FF") };
Test("hex list", () => Eq("#FF0000\r\n#00FF00\r\n#0000FF", CopyFormats.PlainText(CopyFormats.Lines(three, CopyFormat.HexList))));
Test("only Hex, CSS and RGB are offered", () =>
    Eq("HexList,Css,RgbList", string.Join(",", Enum.GetNames<CopyFormat>())));
Test("css variables", () => Eq(":root {\r\n  --color-1: #FF0000;\r\n  --color-2: #00FF00;\r\n  --color-3: #0000FF;\r\n}",
    CopyFormats.PlainText(CopyFormats.Lines(three, CopyFormat.Css))));
Test("rgb list", () => Eq("rgb(255, 0, 0)\r\nrgb(0, 255, 0)\r\nrgb(0, 0, 255)", CopyFormats.PlainText(CopyFormats.Lines(three, CopyFormat.RgbList))));
Test("nothing to copy gives empty text", () =>
{
    foreach (var f in Enum.GetValues<CopyFormat>())
        Eq("", CopyFormats.PlainText(CopyFormats.Lines(Array.Empty<Rgb>(), f)), f.ToString());
});
Test("plain text has no color blocks", () =>
    True(!CopyFormats.PlainText(CopyFormats.Lines(three, CopyFormat.HexList)).Contains('\u2588'), "no blocks"));
Test("HTML has a colored block for every color", () =>
{
    var html = CopyFormats.HtmlFragment(CopyFormats.Lines(three, CopyFormat.HexList));
    foreach (var c in three) True(html.Contains($"color:{c.Hex}"), $"block for {c.Hex}");
    True(html.Contains("&nbsp;#00FF00</div>"), "hex text");
});
Test("clipboard HTML header points at the right bytes", () =>
{
    var fragment = CopyFormats.HtmlFragment(CopyFormats.Lines(three, CopyFormat.Css)) + "é"; // non-ASCII on purpose
    var bytes = CopyFormats.ClipboardHtml(fragment);
    var text = Encoding.UTF8.GetString(bytes);
    int Num(string key) => int.Parse(text.Substring(text.IndexOf(key + ":") + key.Length + 1, 10));
    var got = Encoding.UTF8.GetString(bytes, Num("StartFragment"), Num("EndFragment") - Num("StartFragment"));
    Eq(fragment, got, "fragment");
    Eq(bytes.Length, Num("EndHTML"), "EndHTML");
    True(Encoding.UTF8.GetString(bytes, Num("StartHTML"), 6) == "<html>", "StartHTML");
});
Test("RTF lists each color once in its color table", () =>
{
    var rtf = CopyFormats.Rtf(CopyFormats.Lines(new[] { Hex("#FF0000"), Hex("#FF0000"), Hex("#0000FF") }, CopyFormat.HexList));
    True(rtf.StartsWith(@"{\rtf1"), "is RTF");
    Eq(1, rtf.Split(@"\red255\green0\blue0;").Length - 1, "red entries");
    True(rtf.Contains(@"\cf2\u9608?"), "blue block uses entry 2");
    Eq(rtf.Count(ch => ch == '{'), rtf.Count(ch => ch == '}'), "braces balance");
});

Test("HTML puts the title and description above the colors", () =>
{
    var html = CopyFormats.HtmlFragment(CopyFormats.Lines(three, CopyFormat.HexList), " Forest <palette> ", "Line one\r\nLine & two");
    True(html.Contains(">Forest &lt;palette&gt;</div>"), "title is trimmed and made safe");
    True(html.Contains("Line one<br>Line &amp; two"), "description keeps its line breaks");
    True(html.IndexOf("Forest") < html.IndexOf("#FF0000"), "title comes first");
});
Test("no title or description means no heading at all", () =>
{
    var plain = CopyFormats.HtmlFragment(CopyFormats.Lines(three, CopyFormat.HexList));
    Eq(plain, CopyFormats.HtmlFragment(CopyFormats.Lines(three, CopyFormat.HexList), "  ", ""), "html");
    var rtf = CopyFormats.Rtf(CopyFormats.Lines(three, CopyFormat.HexList));
    Eq(rtf, CopyFormats.Rtf(CopyFormats.Lines(three, CopyFormat.HexList), null, " "), "rtf");
});
Test("RTF has a bold title and the description, braces balanced", () =>
{
    var rtf = CopyFormats.Rtf(CopyFormats.Lines(three, CopyFormat.HexList), "My {sheet}", "a\nb");
    True(rtf.Contains(@"\b My \{sheet\}\par}"), "escaped bold title");
    True(rtf.Contains(@"a\line b"), "line break");
    int open = 0;
    for (int i = 0; i < rtf.Length; i++)
    {
        if (rtf[i] == '\\') { i++; continue; } // skip escaped characters
        if (rtf[i] == '{') open++;
        if (rtf[i] == '}') open--;
        True(open >= 0, "never closes more than it opened");
    }
    Eq(0, open, "all groups closed");
});
Test("the date sits between the title and the description", () =>
{
    var lines = CopyFormats.Lines(three, CopyFormat.HexList);
    var html = CopyFormats.HtmlFragment(lines, "Forest", "Greens", "November 24, 2026");
    True(html.IndexOf("Forest") < html.IndexOf("November 24, 2026") && html.IndexOf("November 24, 2026") < html.IndexOf("Greens"), "html order");
    var rtf = CopyFormats.Rtf(lines, "Forest", "Greens", "November 24, 2026");
    True(rtf.IndexOf("Forest") < rtf.IndexOf("November 24, 2026") && rtf.IndexOf("November 24, 2026") < rtf.IndexOf("Greens"), "rtf order");
    Eq(rtf.Count(ch => ch == '{'), rtf.Count(ch => ch == '}'), "rtf braces balance");
});
Test("a hidden or blank date leaves no trace", () =>
{
    var lines = CopyFormats.Lines(three, CopyFormat.HexList);
    Eq(CopyFormats.HtmlFragment(lines, "Forest"), CopyFormats.HtmlFragment(lines, "Forest", null, "  "), "html");
    Eq(CopyFormats.Rtf(lines, "Forest"), CopyFormats.Rtf(lines, "Forest", null, ""), "rtf");
});
Test("a date on its own still gets a heading gap", () =>
{
    var html = CopyFormats.HtmlFragment(CopyFormats.Lines(three, CopyFormat.HexList), null, null, "Today");
    True(html.Contains(">Today</div><div>&nbsp;</div>"), "date then spacer");
});

Console.WriteLine("Average color");
Test("average of two colors is halfway", () =>
    Eq("#808080", Rgb.Average(new[] { Hex("#000000"), Hex("#FFFFFF") })!.Value.Hex)); // 127.5 rounds up
Test("average mixes each channel separately", () =>
    Eq("#552B0E", Rgb.Average(new[] { Hex("#FF0000"), Hex("#008000"), Hex("#00002A") })!.Value.Hex));
Test("average of one color is that color, of none is nothing", () =>
{
    Eq("#123456", Rgb.Average(new[] { Hex("#123456") })!.Value.Hex);
    Eq(null, Rgb.Average(Array.Empty<Rgb>()));
});
Test("averaging hundreds of bright colors doesn't overflow", () =>
    Eq("#FFFFFF", Rgb.Average(Enumerable.Repeat(Hex("#FFFFFF"), 100_000))!.Value.Hex));

Console.WriteLine("Saving and loading");
Test("missing file gives an empty sheet with no warning", () =>
{
    var r = SheetStore.Load(Path.Combine(temp, "nope.json"));
    Eq(0, r.Sheet.Colors.Count); Eq(null, r.Problem); Eq(false, r.SaveBlocked);
});
Test("save then load gives the same colors and images", () =>
{
    var path = Path.Combine(temp, "a", "sheet.json");
    SheetStore.Save(path, new SavedSheet
    {
        Colors = { new() { Hex = "#112233", Source = "cat.png" }, new() { Hex = "#ABCDEF" } },
        Images = { @"C:\pics\cat.png" },
    });
    var r = SheetStore.Load(path);
    Eq("#112233,#ABCDEF", string.Join(",", r.Sheet.Colors.Select(c => c.Hex)));
    Eq("cat.png", r.Sheet.Colors[0].Source);
    Eq(@"C:\pics\cat.png", r.Sheet.Images.Single());
    True(!File.Exists(path + ".tmp"), "no temp file left behind");
});
Test("the date and whether it's shown are saved and loaded", () =>
{
    var path = Path.Combine(temp, "date", "sheet.json");
    SheetStore.Save(path, new SavedSheet { Date = "Spring 2026", ShowDate = false });
    var r = SheetStore.Load(path);
    Eq("Spring 2026", r.Sheet.Date, "date");
    Eq(false, r.Sheet.ShowDate, "hidden");
});
Test("saves from 1.1.2 (sample size, no date) still load, with the date shown", () =>
{
    var path = Path.Combine(temp, "v112.json");
    File.WriteAllText(path, """{"formatVersion":1,"title":"Forest","description":"","colors":[{"hex":"#112233"}],"images":[],"sampleRadius":2}""");
    var r = SheetStore.Load(path);
    Eq(null, r.Problem, "no problem");
    Eq("Forest", r.Sheet.Title);
    Eq("", r.Sheet.Date, "no date yet (the app fills in today)");
    Eq(true, r.Sheet.ShowDate, "date shown");
    Eq("#112233", r.Sheet.Colors.Single().Hex);
});
Test("today's date reads like November 24, 2026", () =>
    True(System.Text.RegularExpressions.Regex.IsMatch(SavedSheet.Today(), @"^[A-Z][a-z]+ \d{1,2}, \d{4}$"), SavedSheet.Today()));
Test("title and description are saved and loaded", () =>
{
    var path = Path.Combine(temp, "t", "sheet.json");
    SheetStore.Save(path, new SavedSheet { Title = "Forest", Description = "Greens\nfor the logo", Colors = { new() { Hex = "#00FF00" } } });
    var r = SheetStore.Load(path);
    Eq("Forest", r.Sheet.Title);
    Eq("Greens\nfor the logo", r.Sheet.Description);
});
Test("saves from version 1.1.1 (no title) still load", () =>
{
    var path = Path.Combine(temp, "old.json");
    File.WriteAllText(path, """{"formatVersion":1,"colors":[{"hex":"#112233"}],"images":[],"sampleRadius":1}""");
    var r = SheetStore.Load(path);
    Eq("", r.Sheet.Title, "title"); Eq("", r.Sheet.Description, "description");
    Eq("#112233", r.Sheet.Colors.Single().Hex); Eq(null, r.Problem);
});
Test("a null or huge title is tidied on load", () =>
{
    var path = Path.Combine(temp, "big.json");
    File.WriteAllText(path, $$"""{"title":null,"description":"{{new string('x', 2000)}}"}""");
    var r = SheetStore.Load(path);
    Eq("", r.Sheet.Title, "null title");
    Eq(SavedSheet.MaxDescriptionLength, r.Sheet.Description.Length, "description cut");
});
Test("saving keeps the previous version as .bak", () =>
{
    var path = Path.Combine(temp, "b", "sheet.json");
    SheetStore.Save(path, new SavedSheet { Colors = { new() { Hex = "#111111" } } });
    SheetStore.Save(path, new SavedSheet { Colors = { new() { Hex = "#222222" } } });
    Eq("#111111", SheetStore.Load(path + ".bak").Sheet.Colors.Single().Hex, "backup");
    Eq("#222222", SheetStore.Load(path).Sheet.Colors.Single().Hex, "current");
});
Test("unreadable entries are skipped, the rest are kept and tidied", () =>
{
    var path = Path.Combine(temp, "c.json");
    File.WriteAllText(path, $$"""{"colors":[{"hex":"#abc"},{"hex":"nope"},null,{"hex":"#00ff00"}],"date":"{{new string('9', 300)}}"}""");
    var r = SheetStore.Load(path);
    Eq("#AABBCC,#00FF00", string.Join(",", r.Sheet.Colors.Select(c => c.Hex)));
    Eq(SavedSheet.MaxDateLength, r.Sheet.Date.Length, "date cut to fit");
    Eq(null, r.Problem);
});
Test("a corrupt file is kept, not lost, and a fresh sheet starts", () =>
{
    var path = Path.Combine(temp, "d.json");
    File.WriteAllText(path, "{ this is not json");
    var r = SheetStore.Load(path);
    Eq(0, r.Sheet.Colors.Count);
    True(r.Problem is not null, "user is told");
    Eq(false, r.SaveBlocked);
    var kept = Directory.GetFiles(temp, "d.json.unreadable-*");
    Eq(1, kept.Length, "kept copies");
    Eq("{ this is not json", File.ReadAllText(kept[0]), "kept copy content");
});
Test("if the file can't be read or backed up, saving is blocked", () =>
{
    var path = Path.Combine(temp, "e.json");
    File.WriteAllText(path, """{"colors":[{"hex":"#123456"}]}""");
    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var r = SheetStore.Load(path);
        Eq(true, r.SaveBlocked, "blocked");
        True(r.Problem is not null, "user is told");
    }
    Eq("#123456", SheetStore.Load(path).Sheet.Colors.Single().Hex, "original still there");
});

Console.WriteLine("Palettes");
Test("palette file names are safe for Windows", () =>
{
    Eq("Forest palette.json", PaletteStore.FileNameFor("  Forest   palette "), "tidied");
    Eq("a b c d.json", PaletteStore.FileNameFor("a/b:c?d"), "bad characters");
    Eq("Untitled palette.json", PaletteStore.FileNameFor(""), "empty");
    Eq("Untitled palette.json", PaletteStore.FileNameFor("???"), "only bad characters");
    Eq("con palette.json", PaletteStore.FileNameFor("con"), "reserved name");
    Eq("LPT1 palette.json", PaletteStore.FileNameFor("LPT1"), "reserved name, upper case");
    Eq("Dots.json", PaletteStore.FileNameFor("Dots..."), "no trailing dots");
    True(PaletteStore.FileNameFor(new string('x', 300)).Length <= 65, "kept short");
});
Test("a palette saves and opens with its colors, title, date and description", () =>
{
    var folder = Path.Combine(temp, "pal1");
    var path = PaletteStore.Save(folder, new SavedSheet
    {
        Title = "Sunset", Date = "June 1, 2026", ShowDate = false, Description = "warm",
        Colors = { new() { Hex = "#FF5A5F" }, new() { Hex = "#FFB400" } },
    });
    Eq(Path.Combine(folder, "Sunset.json"), path, "file");
    var p = PaletteStore.Load(path);
    Eq("Sunset", p.Title); Eq("June 1, 2026", p.Date); Eq(false, p.ShowDate); Eq("warm", p.Description);
    Eq("#FF5A5F,#FFB400", string.Join(",", p.Colors.Select(c => c.Hex)));
    True(!File.Exists(path + ".tmp"), "no temp file left behind");
});
Test("palettes never store the image list (no file paths from your PC)", () =>
{
    var folder = Path.Combine(temp, "pal2");
    var path = PaletteStore.Save(folder, new SavedSheet
    {
        Title = "Private", Colors = { new() { Hex = "#010203" } }, Images = { @"C:\Users\someone\secret.png" },
    });
    True(!File.ReadAllText(path).Contains("secret"), "image path not written");
});
Test("saving the same title again replaces that palette", () =>
{
    var folder = Path.Combine(temp, "pal3");
    PaletteStore.Save(folder, new SavedSheet { Title = "Mix", Colors = { new() { Hex = "#111111" } } });
    PaletteStore.Save(folder, new SavedSheet { Title = "Mix", Colors = { new() { Hex = "#222222" } } });
    Eq(1, Directory.GetFiles(folder).Length, "one file");
    Eq("#222222", PaletteStore.Load(Path.Combine(folder, "Mix.json")).Colors.Single().Hex);
});
Test("the list shows newest first and skips broken files without touching them", () =>
{
    var folder = Path.Combine(temp, "pal4");
    var older = PaletteStore.Save(folder, new SavedSheet { Title = "Older", Date = "Jan 1", Colors = { new() { Hex = "#111111" } } });
    var newer = PaletteStore.Save(folder, new SavedSheet { Title = "Newer", Colors = { new() { Hex = "#222222" }, new() { Hex = "#333333" } } });
    File.SetLastWriteTime(older, DateTime.Now.AddDays(-2));
    File.SetLastWriteTime(newer, DateTime.Now.AddDays(-1));
    var broken = Path.Combine(folder, "Broken.json");
    File.WriteAllText(broken, "{ nope");
    File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a palette");

    var list = PaletteStore.List(folder);
    Eq("Newer,Older", string.Join(",", list.Palettes.Select(p => p.Title)), "order");
    Eq(2, list.Palettes[0].Colors.Count, "color count");
    Eq("Jan 1", list.Palettes[1].Date, "date");
    Eq(1, list.Unreadable, "broken counted");
    Eq("{ nope", File.ReadAllText(broken), "broken file left alone");
    Eq(0, Directory.GetFiles(folder, "*.unreadable-*").Length, "no extra files made");
});
Test("a missing Palettes folder just means no palettes", () =>
    Eq(0, PaletteStore.List(Path.Combine(temp, "nothing-here")).Palettes.Count));

Console.WriteLine("Where files go");
Test("the log lives in the data folder, palettes in their own folder", () =>
{
    Eq(AppPaths.DataFolder, Log.Folder, "log folder");
    Eq("Palettes", Path.GetFileName(AppPaths.PalettesFolder), "palettes folder name");
    True(Directory.Exists(AppPaths.PalettesFolder), "made automatically");
});

Console.WriteLine("Undo");
Test("peeking shows the next undo and redo without doing them", () =>
{
    var h = new UndoHistory<string>();
    True(!h.TryPeekUndo(out _), "empty");
    h.Record("a");
    True(h.TryPeekUndo(out var u) && u == "a", "peek undo");
    True(h.CanUndo, "still there");
    h.TryUndo("b", out _);
    True(h.TryPeekRedo(out var r) && r == "b", "peek redo");
});
Test("undo and redo walk back and forward", () =>
{
    var h = new UndoHistory<string>();
    h.Record("a"); // a -> b
    h.Record("b"); // b -> c
    True(h.TryUndo("c", out var s1) && s1 == "b", "undo 1");
    True(h.TryUndo("b", out var s2) && s2 == "a", "undo 2");
    True(!h.TryUndo("a", out _), "nothing left");
    True(h.TryRedo("a", out var s3) && s3 == "b", "redo");
});
Test("a new change clears redo", () =>
{
    var h = new UndoHistory<string>();
    h.Record("a");
    h.TryUndo("b", out _);
    h.Record("a");
    True(!h.CanRedo, "redo cleared");
});
Test("history is capped", () =>
{
    var h = new UndoHistory<int>(3);
    for (int i = 0; i < 10; i++) h.Record(i);
    int n = 0;
    while (h.TryUndo(-1, out _)) n++;
    Eq(3, n);
});

Console.WriteLine("Sheet image layout");
Test("column counts make tidy grids", () =>
{
    Eq(1, SheetLayout.AutoColumns(1)); Eq(2, SheetLayout.AutoColumns(2)); Eq(4, SheetLayout.AutoColumns(7));
    Eq(4, SheetLayout.AutoColumns(12)); Eq(8, SheetLayout.AutoColumns(64));
    for (int n = 1; n <= 200; n++) True(SheetLayout.AutoColumns(n) is >= 1 and <= 8, $"columns for {n}");
});
Test("swatches never overlap and stay inside the picture", () =>
{
    for (int n = 1; n <= 40; n++)
    {
        var layout = SheetLayout.Compute(n);
        Eq(n, layout.Cells.Count, "cells");
        foreach (var c in layout.Cells)
            True(c.X + SheetLayout.SwatchWidth <= layout.Width - SheetLayout.Margin + 0.01 &&
                 c.Y + SheetLayout.SwatchHeight + SheetLayout.LabelHeight <= layout.Height - SheetLayout.Margin + 0.01, $"inside for {n}");
        Eq(n, layout.Cells.Select(c => (c.X, c.Y)).Distinct().Count(), $"distinct spots for {n}");
    }
});

Test("a taller header and a minimum width make room for the title", () =>
{
    var plain = SheetLayout.Compute(2);
    var roomy = SheetLayout.Compute(2, headerHeight: SheetLayout.HeaderHeight + 50, minWidth: 600);
    Eq(600.0, roomy.Width, "width");
    Eq(plain.Height + 50, roomy.Height, "height");
    Eq(plain.Cells[0].Y + 50, roomy.Cells[0].Y, "grid moves down");
    Eq(plain.Width, SheetLayout.Compute(2, minWidth: 10).Width, "min width never shrinks it");
});

Console.WriteLine("Updates");
// A fake GitHub: answers from a dictionary of url -> (status, bytes). Nothing goes online.
var fakeFiles = new Dictionary<string, (int Status, byte[] Body)>();
var http = Updater.CreateClient(new FakeWeb(fakeFiles));
string ReleaseJson(string tag, bool withSha = true, bool draft = false) => $$"""
    {"tag_name":"{{tag}}","html_url":"https://github.com/ItsHazardd/SwatchSheet/releases/tag/{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":false,
     "assets":[{"name":"SwatchSheet-{{tag}}.zip","browser_download_url":"https://example.test/app.zip"}
               {{(withSha ? ",{\"name\":\"SwatchSheet-" + tag + ".zip.sha256\",\"browser_download_url\":\"https://example.test/app.zip.sha256\"}" : "")}}]}
    """;
byte[] MakeZip(string entryName, int size)
{
    using var ms = new MemoryStream();
    using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
    {
        using (var s = zip.CreateEntry(entryName).Open())
            s.Write(Enumerable.Range(0, size).Select(i => (byte)(i % 251)).ToArray());
        using (var r = new StreamWriter(zip.CreateEntry("README.txt").Open()))
            r.Write("readme");
    }
    return ms.ToArray();
}
void ServeRelease(byte[] zip, string? shaOverride = null)
{
    fakeFiles["https://example.test/app.zip"] = (200, zip);
    var sha = shaOverride ?? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zip));
    fakeFiles["https://example.test/app.zip.sha256"] = (200, Encoding.UTF8.GetBytes(sha.ToLowerInvariant() + "  SwatchSheet-v9.9.9.zip\n"));
}
var goodInfo = new UpdateInfo(new Version(9, 9, 9), "v9.9.9", "https://example.test/app.zip", "https://example.test/app.zip.sha256", Updater.ReleasesPage);

Test("versions read from tags", () =>
{
    Eq(new Version(1, 1, 4), Updater.ParseVersion("v1.1.4"), "v-tag");
    Eq(new Version(1, 2, 0), Updater.ParseVersion("1.2"), "short");
    Eq(new Version(1, 1, 4), Updater.ParseVersion(" V1.1.4.7 "), "extra part ignored");
    Eq(null, Updater.ParseVersion("latest"), "not a version");
    Eq(null, Updater.ParseVersion(null), "nothing");
});
Test("only a higher version counts as newer", () =>
{
    True(Updater.IsNewer(new Version(1, 1, 10), "1.1.9"), "10 beats 9 (not alphabetical)");
    True(Updater.IsNewer(new Version(1, 2, 0), "1.1.9"), "batch beats day");
    True(!Updater.IsNewer(new Version(1, 1, 4), "1.1.4"), "same");
    True(!Updater.IsNewer(new Version(1, 1, 3), "1.1.4"), "older");
});
Test("a release needs both the zip and its fingerprint", () =>
{
    var info = Updater.ParseLatestRelease(ReleaseJson("v9.9.9"))!;
    Eq(new Version(9, 9, 9), info.Version, "version");
    Eq("https://example.test/app.zip", info.ZipUrl, "zip");
    Eq("https://example.test/app.zip.sha256", info.ShaUrl, "fingerprint");
    Eq(null, Updater.ParseLatestRelease(ReleaseJson("v9.9.9", withSha: false)), "no fingerprint, not offered");
    Eq(null, Updater.ParseLatestRelease(ReleaseJson("v9.9.9", draft: true)), "drafts never offered");
    Eq(null, Updater.ParseLatestRelease(ReleaseJson("nightly")), "tag that isn't a version");
});
Test("downloads must come over https", () =>
{
    Eq(null, Updater.ParseLatestRelease(ReleaseJson("v9.9.9").Replace("https://example.test", "http://example.test")), "plain http refused");
    True(Updater.IsSafeUrl("http://localhost:5000/x") && Updater.IsSafeUrl("http://127.0.0.1/x"), "this PC is allowed (testing)");
    True(!Updater.IsSafeUrl("ftp://example.test/x") && !Updater.IsSafeUrl("not a url"), "other junk refused");
});
Test("fingerprint files are read in either style", () =>
{
    var h = new string('a', 64);
    Eq(h.ToUpperInvariant(), Updater.ParseSha256(h + "  SwatchSheet-1.1.4.zip\n"), "with name");
    Eq(h.ToUpperInvariant(), Updater.ParseSha256("﻿" + h.ToUpperInvariant()), "just the hash");
    Eq(null, Updater.ParseSha256("not a hash"), "junk");
    Eq(null, Updater.ParseSha256(new string('a', 63)), "too short");
});
Test("check: no releases yet, same version, and newer version", () =>
{
    fakeFiles["https://example.test/latest"] = (404, Array.Empty<byte>());
    Eq(null, Updater.CheckAsync(http, "1.1.4", "https://example.test/latest").Result, "404");
    fakeFiles["https://example.test/latest"] = (200, Encoding.UTF8.GetBytes(ReleaseJson("v1.1.4")));
    Eq(null, Updater.CheckAsync(http, "1.1.4", "https://example.test/latest").Result, "same");
    fakeFiles["https://example.test/latest"] = (200, Encoding.UTF8.GetBytes(ReleaseJson("v1.1.5")));
    Eq("v1.1.5", Updater.CheckAsync(http, "1.1.4", "https://example.test/latest").Result?.Tag, "newer");
});
Test("a good download gives the new program file", () =>
{
    ServeRelease(MakeZip("SwatchSheet.exe", 5000));
    var exe = Updater.DownloadAsync(http, goodInfo, Path.Combine(temp, "upd1")).Result;
    Eq(5000L, new FileInfo(exe).Length, "size");
});
Test("a download that doesn't match its fingerprint is refused", () =>
{
    ServeRelease(MakeZip("SwatchSheet.exe", 5000), shaOverride: new string('0', 64));
    var folder = Path.Combine(temp, "upd2");
    bool refused = false;
    try { Updater.DownloadAsync(http, goodInfo, folder).Wait(); }
    catch (AggregateException ex) when (ex.InnerException is InvalidDataException) { refused = true; }
    True(refused, "refused");
    Eq(0, Directory.GetFiles(folder, "*.new").Length, "no program file left behind");
});
Test("a zip without the program, or with it hidden in a subfolder, is refused", () =>
{
    foreach (var name in new[] { "Other.exe", "../SwatchSheet.exe", "sub/SwatchSheet.exe" })
    {
        ServeRelease(MakeZip(name, 5000));
        bool refused = false;
        try { Updater.DownloadAsync(http, goodInfo, Path.Combine(temp, "upd3")).Wait(); }
        catch (AggregateException ex) when (ex.InnerException is InvalidDataException) { refused = true; }
        True(refused, $"refused {name}");
    }
});
Test("a tiny fake program file is refused", () =>
{
    ServeRelease(MakeZip("SwatchSheet.exe", 10));
    bool refused = false;
    try { Updater.DownloadAsync(http, goodInfo, Path.Combine(temp, "upd4")).Wait(); }
    catch (AggregateException ex) when (ex.InnerException is InvalidDataException) { refused = true; }
    True(refused, "refused");
});
Test("install swaps the program and keeps the old one until cleanup", () =>
{
    var app = Path.Combine(temp, "app1");
    Directory.CreateDirectory(Path.Combine(app, "data"));
    File.WriteAllText(Path.Combine(app, "SwatchSheet.exe"), "old");
    File.WriteAllText(Path.Combine(app, "data", "sheet.json"), "my colors");
    File.WriteAllText(Path.Combine(app, "new.bin"), "new");
    Updater.Install(Path.Combine(app, "SwatchSheet.exe"), Path.Combine(app, "new.bin"));
    Eq("new", File.ReadAllText(Path.Combine(app, "SwatchSheet.exe")), "new program in place");
    Eq("old", File.ReadAllText(Path.Combine(app, "SwatchSheet.exe.old")), "old one kept");
    Eq("my colors", File.ReadAllText(Path.Combine(app, "data", "sheet.json")), "data untouched");
    Updater.Cleanup(Path.Combine(app, "SwatchSheet.exe"), Path.Combine(app, "data", "update"));
    True(!File.Exists(Path.Combine(app, "SwatchSheet.exe.old")), "old removed after a good start");
    Eq("my colors", File.ReadAllText(Path.Combine(app, "data", "sheet.json")), "data still untouched");
});
Test("if installing fails, the original program is put back", () =>
{
    var app = Path.Combine(temp, "app2");
    Directory.CreateDirectory(app);
    File.WriteAllText(Path.Combine(app, "SwatchSheet.exe"), "old");
    bool threw = false;
    try { Updater.Install(Path.Combine(app, "SwatchSheet.exe"), Path.Combine(app, "missing.bin")); }
    catch (IOException) { threw = true; }
    True(threw, "reported");
    Eq("old", File.ReadAllText(Path.Combine(app, "SwatchSheet.exe")), "original back in place");
});
Test("settings: on by default, once a day, saved and loaded", () =>
{
    var s = new AppSettings();
    True(s.CheckForUpdates && s.DueForCheck(DateTime.UtcNow), "first time");
    s.LastUpdateCheckUtc = DateTime.UtcNow.AddHours(-2);
    True(!s.DueForCheck(DateTime.UtcNow), "checked 2 hours ago");
    s.LastUpdateCheckUtc = DateTime.UtcNow.AddDays(-1);
    True(s.DueForCheck(DateTime.UtcNow), "checked yesterday");
    s.LastUpdateCheckUtc = DateTime.UtcNow.AddDays(5);
    True(s.DueForCheck(DateTime.UtcNow), "clock went backwards");
    s.CheckForUpdates = false;
    True(!s.DueForCheck(DateTime.UtcNow), "switched off");

    var path = Path.Combine(temp, "settings", "settings.json");
    SettingsStore.Save(path, s);
    Eq(false, SettingsStore.Load(path).CheckForUpdates, "off stays off");
    File.WriteAllText(path, "{ broken");
    Eq(true, SettingsStore.Load(path).CheckForUpdates, "broken file means defaults");
});

try { Directory.Delete(temp, recursive: true); } catch { /* temp folder, not important */ }
Console.WriteLine();
Console.WriteLine(failed == 0 ? $"All {passed} tests passed." : $"{failed} FAILED, {passed} passed.");
return failed == 0 ? 0 : 1;

/// <summary>Stands in for the internet in the update tests.</summary>
sealed class FakeWeb(Dictionary<string, (int Status, byte[] Body)> files) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        var url = request.RequestUri!.ToString();
        var response = files.TryGetValue(url, out var f)
            ? new HttpResponseMessage((System.Net.HttpStatusCode)f.Status) { Content = new ByteArrayContent(f.Body) }
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { Content = new ByteArrayContent(Array.Empty<byte>()) };
        return Task.FromResult(response);
    }
}
