using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DuckNote.Notes.Hosts;
using DuckNote.Notes.Notebook;

namespace DuckNote.Notes.Sheets;

/// <summary>
/// Il foglio nel formato del file della nota: lo XAML di un FlowDocument, come lo salva la
/// versione WPF (TextRange.Save). Il tipo di una riga non e' scritto: si deduce da come e'
/// vestita, con le stesse regole della versione WPF (Looks): sfondo = codice, bordo sotto =
/// separazione, bordo a sinistra = citazione, 21/17/15 punti = titoli, e il segno in testa per
/// elenchi, numeri e spunte. Scrivendo si veste ogni riga nello stesso modo (Styler), cosi' le
/// due versioni leggono e scrivono la stessa nota.
///
/// I colori che la versione WPF dipinge da se' (gli indirizzi col loro stato, il codice, i
/// collegamenti, le spunte fatte) non sono colori dell'utente: leggendo si tolgono.
/// </summary>
public static partial class SheetXaml
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    public const string MonoFonts = "SF Mono, Cascadia Mono, Consolas, Menlo, Courier New";

    private static readonly double[] HeadingSizes = [21, 17, 15];

    private const string Gap = " ";
    private const string Dot = "●" + Gap;
    private const string Unchecked = "☐" + Gap;
    private const string Checked = "☑" + Gap;

    // ------------------------------------------------------------------ lettura

    /// <summary>Legge un foglio; un XAML illeggibile da' un foglio vuoto, mai un'eccezione.</summary>
    public static SheetDoc Read(byte[] xaml)
    {
        SheetDoc doc = new();

        try
        {
            string text = Encoding.UTF8.GetString(xaml).TrimStart('﻿');
            XDocument xml = XDocument.Parse(text, LoadOptions.PreserveWhitespace);

            if (xml.Root is { } root)
            {
                ReadBlocks(root.Name.LocalName is "Paragraph" or "Table" or "List" ? [root] : root.Elements(), doc.Blocks);
            }
        }
        catch (XmlException)
        {
            // un foglio rovinato non ferma la nota: resta vuoto
        }

        if (doc.Blocks.Count == 0)
        {
            doc.Blocks.Add(SheetLine.Empty());
        }

        return doc;
    }

    private static void ReadBlocks(IEnumerable<XElement> elements, List<SheetBlock> into)
    {
        foreach (XElement element in elements)
        {
            switch (element.Name.LocalName)
            {
                case "Paragraph":
                    into.Add(ReadParagraph(element));
                    break;

                case "Table":
                    into.Add(ReadTable(element));
                    break;

                case "Section":
                case "FlowDocument":
                    ReadBlocks(element.Elements(), into);
                    break;

                case "List":
                    ReadList(element, into);
                    break;

                // contenitori di controlli: nel file non sopravvivono, si saltano
                default:
                    break;
            }
        }
    }

    /// <summary>Gli elenchi veri di WPF (note scritte da versioni molto vecchie): una riga per voce.</summary>
    private static void ReadList(XElement list, List<SheetBlock> into)
    {
        bool numbered = Attr(list, "MarkerStyle") is { } style
            && (style.Contains("Decimal", StringComparison.OrdinalIgnoreCase) || style.Contains("Roman", StringComparison.OrdinalIgnoreCase)
                || style.Contains("Latin", StringComparison.OrdinalIgnoreCase));

        foreach (XElement item in list.Elements().Where(e => e.Name.LocalName == "ListItem"))
        {
            foreach (XElement paragraph in item.Elements().Where(e => e.Name.LocalName == "Paragraph"))
            {
                SheetLine line = ReadParagraph(paragraph);
                into.Add(line with { Kind = numbered ? LineKind.Number : LineKind.Bullet });
            }
        }
    }

    private static SheetTable ReadTable(XElement table)
    {
        List<IReadOnlyList<IReadOnlyList<SheetLine>>> rows = [];

        foreach (XElement row in table.Descendants().Where(e => e.Name.LocalName == "TableRow"))
        {
            List<IReadOnlyList<SheetLine>> cells = [];

            foreach (XElement cell in row.Elements().Where(e => e.Name.LocalName == "TableCell"))
            {
                List<SheetBlock> inner = [];
                ReadBlocks(cell.Elements(), inner);
                List<SheetLine> lines = [.. inner.OfType<SheetLine>()];
                cells.Add(lines.Count > 0 ? lines : [SheetLine.Empty()]);
            }

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }

        return new SheetTable(rows);
    }

    /// <summary>Il formato che un pezzo di testo eredita mentre si scende nei suoi Span.</summary>
    private sealed record Style(TextMarks Marks, string? Foreground, string? Background, bool Mono, string? Link);

    private static SheetLine ReadParagraph(XElement paragraph)
    {
        LineKind kind = KindOf(paragraph);

        if (kind == LineKind.Rule)
        {
            return SheetLine.Empty(LineKind.Rule);
        }

        List<Piece> pieces = [];
        ReadInlines(paragraph.Nodes(), new Style(TextMarks.None, null, null, false, null), pieces, kind);

        if (kind == LineKind.Code)
        {
            // il codice e' testo e basta: i colori li mette l'editor
            string code = string.Concat(pieces.Select(piece => piece switch
            {
                TextPiece text => text.Text,
                BreakPiece => "\n",
                _ => string.Empty
            }));

            return new SheetLine(LineKind.Code, code.Length == 0 ? [] : [new TextPiece(code)]);
        }

        bool done = false;

        if (kind is LineKind.Text)
        {
            string lead = string.Concat(pieces.TakeWhile(piece => piece is TextPiece).Cast<TextPiece>().Select(piece => piece.Text));
            int mark = 0;

            if (lead.StartsWith(Unchecked, StringComparison.Ordinal) || lead.StartsWith(Checked, StringComparison.Ordinal))
            {
                kind = LineKind.Todo;
                done = lead.StartsWith(Checked, StringComparison.Ordinal);
                mark = Unchecked.Length;
            }
            else if (lead.StartsWith(Dot, StringComparison.Ordinal))
            {
                kind = LineKind.Bullet;
                mark = Dot.Length;
            }
            else if (NumberMark().Match(lead) is { Success: true } number)
            {
                kind = LineKind.Number;
                mark = number.Length;
            }

            if (mark > 0)
            {
                pieces = Strip(pieces, mark);
            }

            // una spunta fatta barra e ingrigisce il testo: e' il suo aspetto, non un formato
            if (done)
            {
                pieces = [.. pieces.Select(piece => piece is TextPiece text
                    ? text with { Marks = text.Marks & ~TextMarks.Strike, Colour = text.Colour is "LabelTertiary" ? null : text.Colour }
                    : piece)];
            }
        }

        return new SheetLine(kind, Merge(pieces), done);
    }

    private static LineKind KindOf(XElement paragraph)
    {
        if (Attr(paragraph, "Background") is { } background && !ThemeColours.IsClear(background))
        {
            return LineKind.Code;
        }

        Thickness border = ThicknessOf(Attr(paragraph, "BorderThickness"));

        if (border.Bottom > 0)
        {
            return LineKind.Rule;
        }

        if (border.Left > 0)
        {
            return LineKind.Quote;
        }

        if (Attr(paragraph, "FontSize") is { } size && double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out double points))
        {
            for (int i = 0; i < HeadingSizes.Length; i++)
            {
                if (Math.Abs(points - HeadingSizes[i]) < 0.6)
                {
                    return LineKind.Heading1 + i;
                }
            }
        }

        return LineKind.Text;
    }

    private static void ReadInlines(IEnumerable<XNode> nodes, Style style, List<Piece> into, LineKind kind)
    {
        foreach (XNode node in nodes)
        {
            switch (node)
            {
                // testo dentro un Run o uno Span, o direttamente nel paragrafo (un Run implicito);
                // gli a capo con rientro sono la forma di un file scritto a mano, non testo
                case XText text when !(text.Parent?.Name.LocalName == "Paragraph" && string.IsNullOrWhiteSpace(text.Value) && text.Value.Contains('\n')):
                    Add(into, text.Value, style, kind);
                    break;

                case XElement { Name.LocalName: "LineBreak" }:
                    into.Add(new BreakPiece());
                    break;

                case XElement { Name.LocalName: "Hyperlink" } link
                    when Attr(link, "NavigateUri") is { } uri && Uri.TryCreate(uri, UriKind.Absolute, out Uri? address)
                         && InsetAddress.Parse(address) is { } inset:
                    into.Add(new InsetPiece(inset));
                    break;

                case XElement element when element.Name.LocalName is "Run" or "Span" or "Bold" or "Italic" or "Underline" or "Hyperlink":
                    Style inner = Dress(element, style);

                    // il testo di un Run puo' stare anche nell'attributo Text
                    if (element.Name.LocalName == "Run" && Attr(element, "Text") is { } attributeText && !element.Nodes().Any())
                    {
                        Add(into, attributeText, inner, kind);
                    }
                    else
                    {
                        ReadInlines(element.Nodes(), inner, into, kind);
                    }

                    break;

                default:
                    break;
            }
        }
    }

    private static Style Dress(XElement element, Style outer)
    {
        TextMarks marks = outer.Marks;

        switch (element.Name.LocalName)
        {
            case "Bold":
                marks |= TextMarks.Bold;
                break;
            case "Italic":
                marks |= TextMarks.Italic;
                break;
            case "Underline":
                marks |= TextMarks.Underline;
                break;
        }

        if (Attr(element, "FontWeight") is { } weight)
        {
            marks = IsBold(weight) ? marks | TextMarks.Bold : marks & ~TextMarks.Bold;
        }

        if (Attr(element, "FontStyle") is { } fontStyle)
        {
            marks = fontStyle.Contains("Italic", StringComparison.OrdinalIgnoreCase) || fontStyle.Contains("Oblique", StringComparison.OrdinalIgnoreCase)
                ? marks | TextMarks.Italic
                : marks & ~TextMarks.Italic;
        }

        if (Attr(element, "TextDecorations") is { } decorations)
        {
            marks = decorations.Contains("Underline", StringComparison.OrdinalIgnoreCase) ? marks | TextMarks.Underline : marks & ~TextMarks.Underline;
            marks = decorations.Contains("Strikethrough", StringComparison.OrdinalIgnoreCase) ? marks | TextMarks.Strike : marks & ~TextMarks.Strike;
        }

        string? link = outer.Link;

        if (element.Name.LocalName == "Hyperlink" && Attr(element, "NavigateUri") is { } uri)
        {
            link = uri;
        }

        return new Style(
            marks,
            Attr(element, "Foreground") ?? outer.Foreground,
            Attr(element, "Background") ?? outer.Background,
            Attr(element, "FontFamily") is { } family ? IsMono(family) : outer.Mono,
            link);
    }

    private static void Add(List<Piece> into, string text, Style style, LineKind kind)
    {
        if (text.Length == 0)
        {
            return;
        }

        TextMarks marks = style.Marks;
        string? background = ThemeColours.IsClear(style.Background) ? null : ThemeColours.TokenOf(style.Background);
        string? colour = ThemeColours.TokenOf(style.Foreground);

        if (background == "Highlight")
        {
            marks |= TextMarks.Highlight;
            colour = colour is "HighlightFg" or "Label" ? null : colour;
        }
        else if (background == "CodeBg" || (style.Background is not null && style.Mono && !ThemeColours.IsClear(style.Background)))
        {
            marks |= TextMarks.Code;
            colour = colour is "CodeFg" or "Label" ? null : colour;
        }

        // colori che non sono dell'utente: il collegamento blu, gli indirizzi dipinti col loro stato
        if (style.Link is not null && colour is "Blue")
        {
            colour = null;
        }

        if (colour is "Green" or "Red" or "LabelQuaternary" or "LabelTertiary" && style.Mono && HostTokens.Only(text) is not null)
        {
            colour = null;
        }

        if (colour is "Label" || (kind == LineKind.Quote && colour is "LabelSecondary"))
        {
            colour = null;
        }

        // restano solo i colori che si danno dalla barra
        if (colour is not null && !ThemeColours.TextColours.Any(pair => pair.Token == colour))
        {
            colour = null;
        }

        into.Add(new TextPiece(text, marks, colour, style.Link));
    }

    /// <summary>Toglie i primi caratteri (il segno dell'elenco) anche se stanno su piu' pezzi.</summary>
    private static List<Piece> Strip(List<Piece> pieces, int length)
    {
        List<Piece> left = [];
        int skip = length;

        foreach (Piece piece in pieces)
        {
            if (skip > 0 && piece is TextPiece text)
            {
                if (text.Text.Length <= skip)
                {
                    skip -= text.Text.Length;
                    continue;
                }

                left.Add(text with { Text = text.Text[skip..] });
                skip = 0;
                continue;
            }

            left.Add(piece);
        }

        return left;
    }

    /// <summary>Pezzi vicini con lo stesso formato diventano uno.</summary>
    internal static List<Piece> Merge(IEnumerable<Piece> pieces)
    {
        List<Piece> merged = [];

        foreach (Piece piece in pieces)
        {
            if (piece is TextPiece { Text.Length: 0 })
            {
                continue;
            }

            if (piece is TextPiece text && merged.Count > 0 && merged[^1] is TextPiece last
                && last.Marks == text.Marks && last.Colour == text.Colour && last.Link == text.Link)
            {
                merged[^1] = last with { Text = last.Text + text.Text };
                continue;
            }

            merged.Add(piece);
        }

        return merged;
    }

    private static bool IsBold(string weight) =>
        weight.Trim() switch
        {
            "Normal" or "Regular" or "Thin" or "Light" or "ExtraLight" or "UltraLight" or "Medium" => false,
            var number when int.TryParse(number, out int value) => value >= 600,
            _ => true
        };

    private static bool IsMono(string family) =>
        family.Contains("Mono", StringComparison.OrdinalIgnoreCase)
        || family.Contains("Consolas", StringComparison.OrdinalIgnoreCase)
        || family.Contains("Courier", StringComparison.OrdinalIgnoreCase)
        || family.Contains("Menlo", StringComparison.OrdinalIgnoreCase);

    private static string? Attr(XElement element, string name) =>
        element.Attribute(name)?.Value ?? element.Attribute(Ns + name)?.Value;

    private readonly record struct Thickness(double Left, double Top, double Right, double Bottom);

    private static Thickness ThicknessOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        double[] parts = [.. value.Split(',', ' ')
            .Where(part => part.Length > 0)
            .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0)];

        return parts.Length switch
        {
            1 => new Thickness(parts[0], parts[0], parts[0], parts[0]),
            2 => new Thickness(parts[0], parts[1], parts[0], parts[1]),
            >= 4 => new Thickness(parts[0], parts[1], parts[2], parts[3]),
            _ => default
        };
    }

    [GeneratedRegex(@"^\d+\. ")]
    private static partial Regex NumberMark();

    // ------------------------------------------------------------------ scrittura

    /// <summary>Il foglio nel formato del file, vestito come lo veste la versione WPF.</summary>
    public static byte[] Write(SheetDoc doc)
    {
        StringBuilder xaml = new();
        xaml.Append("<Section xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xml:space=\"preserve\">");

        int number = 0;

        foreach (SheetBlock block in doc.Blocks)
        {
            number = block is SheetLine { Kind: LineKind.Number } ? number + 1 : 0;

            switch (block)
            {
                case SheetLine line:
                    WriteLine(xaml, line, number);
                    break;

                case SheetTable table:
                    WriteTable(xaml, table);
                    break;
            }
        }

        xaml.Append("</Section>");

        return Encoding.UTF8.GetBytes(xaml.ToString());
    }

    private static void WriteLine(StringBuilder xaml, SheetLine line, int number)
    {
        string tertiary = ThemeColours.Written("LabelTertiary");

        switch (line.Kind)
        {
            case LineKind.Rule:
                xaml.Append("<Paragraph Margin=\"0,6.5,0,6.5\" BorderBrush=\"").Append(ThemeColours.Written("Separator"))
                    .Append("\" BorderThickness=\"0,0,0,1\" LineHeight=\"1\" LineStackingStrategy=\"BlockLineHeight\" ClearFloaters=\"Both\" />");
                return;

            case LineKind.Code:
                xaml.Append("<Paragraph Margin=\"0,0,0,0\" Background=\"").Append(ThemeColours.Written("CodeBg"))
                    .Append("\" Foreground=\"").Append(ThemeColours.Written("CodeFg"))
                    .Append("\" FontFamily=\"").Append(MonoFonts).Append("\" Padding=\"12,8,12,8\">");
                string[] rows = line.Text.Replace("\r\n", "\n").Split('\n');

                for (int i = 0; i < rows.Length; i++)
                {
                    if (i > 0)
                    {
                        xaml.Append("<LineBreak />");
                    }

                    xaml.Append("<Run>").Append(Escape(rows[i])).Append("</Run>");
                }

                xaml.Append("</Paragraph>");
                return;
        }

        xaml.Append("<Paragraph Margin=\"0,0,0,0\"");

        switch (line.Kind)
        {
            case LineKind.Heading1 or LineKind.Heading2 or LineKind.Heading3:
                xaml.Append(" FontSize=\"").Append(HeadingSizes[line.Kind - LineKind.Heading1].ToString(CultureInfo.InvariantCulture))
                    .Append("\" FontWeight=\"SemiBold\"");
                break;

            case LineKind.Quote:
                xaml.Append(" BorderBrush=\"").Append(tertiary).Append("\" BorderThickness=\"2,0,0,0\" Padding=\"12,0,0,0\" FontStyle=\"Italic\" Foreground=\"")
                    .Append(ThemeColours.Written("LabelSecondary")).Append('"');
                break;

            case LineKind.Bullet or LineKind.Number or LineKind.Todo:
                xaml.Append(" Padding=\"14,0,0,0\" TextIndent=\"-14\"");
                break;
        }

        xaml.Append('>');

        string? mark = line.Kind switch
        {
            LineKind.Bullet => Dot,
            LineKind.Number => Math.Max(1, number).ToString(CultureInfo.InvariantCulture) + "." + Gap,
            LineKind.Todo => line.Done ? Checked : Unchecked,
            _ => null
        };

        if (mark is not null)
        {
            xaml.Append("<Run FontFamily=\"").Append(MonoFonts).Append("\" Foreground=\"")
                .Append(line.Kind == LineKind.Todo && line.Done ? ThemeColours.Written("Green") : tertiary)
                .Append("\">").Append(Escape(mark)).Append("</Run>");
        }

        WritePieces(xaml, line.Pieces, done: line.Kind == LineKind.Todo && line.Done);
        xaml.Append("</Paragraph>");
    }

    private static void WritePieces(StringBuilder xaml, IReadOnlyList<Piece> pieces, bool done)
    {
        int i = 0;

        while (i < pieces.Count)
        {
            switch (pieces[i])
            {
                case BreakPiece:
                    xaml.Append("<LineBreak />");
                    i++;
                    break;

                case InsetPiece inset:
                    xaml.Append("<Hyperlink NavigateUri=\"").Append(Escape(inset.Address.ToUri().ToString())).Append("\"><Run>")
                        .Append(Escape(inset.Address.Title)).Append("</Run></Hyperlink>");
                    i++;
                    break;

                case TextPiece { Link: { } link }:
                    // i pezzi vicini con lo stesso collegamento stanno nello stesso Hyperlink
                    xaml.Append("<Hyperlink NavigateUri=\"").Append(Escape(link)).Append("\" Foreground=\"")
                        .Append(ThemeColours.Written("Blue")).Append("\">");

                    while (i < pieces.Count && pieces[i] is TextPiece { Link: { } same } linked && same == link)
                    {
                        WriteRun(xaml, linked with { Link = null }, done);
                        i++;
                    }

                    xaml.Append("</Hyperlink>");
                    break;

                case TextPiece text:
                    WriteRun(xaml, text, done);
                    i++;
                    break;

                default:
                    i++;
                    break;
            }
        }
    }

    private static void WriteRun(StringBuilder xaml, TextPiece piece, bool done)
    {
        TextMarks marks = piece.Marks | (done ? TextMarks.Strike : TextMarks.None);
        xaml.Append("<Run");

        if (marks.HasFlag(TextMarks.Bold))
        {
            xaml.Append(" FontWeight=\"Bold\"");
        }

        if (marks.HasFlag(TextMarks.Italic))
        {
            xaml.Append(" FontStyle=\"Italic\"");
        }

        List<string> decorations = [];

        if (marks.HasFlag(TextMarks.Underline))
        {
            decorations.Add("Underline");
        }

        if (marks.HasFlag(TextMarks.Strike))
        {
            decorations.Add("Strikethrough");
        }

        if (decorations.Count > 0)
        {
            xaml.Append(" TextDecorations=\"").Append(string.Join(",", decorations)).Append('"');
        }

        string? foreground = done ? "LabelTertiary" : piece.Colour;

        if (marks.HasFlag(TextMarks.Code))
        {
            xaml.Append(" Background=\"").Append(ThemeColours.Written("CodeBg")).Append("\" FontFamily=\"").Append(MonoFonts).Append('"');
            foreground ??= "CodeFg";
        }
        else if (marks.HasFlag(TextMarks.Highlight))
        {
            xaml.Append(" Background=\"").Append(ThemeColours.Written("Highlight")).Append('"');
            foreground ??= "HighlightFg";
        }

        if (foreground is not null)
        {
            xaml.Append(" Foreground=\"").Append(ThemeColours.Written(foreground)).Append('"');
        }

        xaml.Append('>').Append(Escape(piece.Text)).Append("</Run>");
    }

    private static void WriteTable(StringBuilder xaml, SheetTable table)
    {
        string line = ThemeColours.Written("TableBorder");
        int columns = Math.Max(1, table.Columns);

        xaml.Append("<Table CellSpacing=\"0\" Margin=\"0,6,0,8\" BorderBrush=\"").Append(line).Append("\" BorderThickness=\"1,1,0,0\"><Table.Columns>");

        for (int i = 0; i < columns; i++)
        {
            xaml.Append("<TableColumn Width=\"*\" />");
        }

        xaml.Append("</Table.Columns><TableRowGroup>");

        for (int r = 0; r < table.Rows.Count; r++)
        {
            xaml.Append("<TableRow>");

            for (int c = 0; c < columns; c++)
            {
                IReadOnlyList<SheetLine> cell = c < table.Rows[r].Count ? table.Rows[r][c] : [SheetLine.Empty()];
                xaml.Append("<TableCell Padding=\"8,4,8,4\" BorderBrush=\"").Append(line).Append("\" BorderThickness=\"0,0,1,1\"");

                if (r == 0)
                {
                    xaml.Append(" Background=\"").Append(ThemeColours.Written("TableHeaderBg")).Append("\" FontWeight=\"SemiBold\"");
                }

                xaml.Append('>');

                foreach (SheetLine inner in cell.Count > 0 ? cell : [SheetLine.Empty()])
                {
                    WriteLine(xaml, inner, inner.Kind == LineKind.Number ? 1 : 0);
                }

                xaml.Append("</TableCell>");
            }

            xaml.Append("</TableRow>");
        }

        xaml.Append("</TableRowGroup></Table>");
    }

    private static string Escape(string text) => SecurityElement.Escape(text) ?? string.Empty;
}
