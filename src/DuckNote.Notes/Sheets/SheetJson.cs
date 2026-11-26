using System.Globalization;
using System.Text.Json.Nodes;
using DuckNote.Notes.Notebook;

namespace DuckNote.Notes.Sheets;

/// <summary>
/// Il foglio nel formato dell'editor (lo JSON di un documento ProseMirror, con lo schema di
/// DuckNote: una riga per nodo, come nel file). Si manda all'editor quando si apre un foglio e
/// torna indietro, a riposo, dopo ogni modifica.
///
///   doc        -> (paragraph | heading{level} | quote | bullet | number | todo{done} | rule | code_block | table)*
///   table      -> table_row+ -> (table_header | table_cell)+ -> riga+
///   in linea   -> text (segni: bold italic underline strike highlight code colour{token} link{href})
///                 | hard_break | inset{id, kind, width, height}
/// </summary>
public static class SheetJson
{
    // ------------------------------------------------------------------ verso l'editor

    public static JsonObject ToJson(SheetDoc doc)
    {
        JsonArray content = [];

        foreach (SheetBlock block in doc.Blocks)
        {
            content.Add(block switch
            {
                SheetTable table => TableJson(table),
                SheetLine line => LineJson(line),
                _ => LineJson(SheetLine.Empty())
            });
        }

        if (content.Count == 0)
        {
            content.Add(LineJson(SheetLine.Empty()));
        }

        return new JsonObject { ["type"] = "doc", ["content"] = content };
    }

    private static JsonObject LineJson(SheetLine line)
    {
        JsonObject node = new() { ["type"] = TypeOf(line.Kind) };

        switch (line.Kind)
        {
            case LineKind.Heading1 or LineKind.Heading2 or LineKind.Heading3:
                node["attrs"] = new JsonObject { ["level"] = line.Kind - LineKind.Heading1 + 1 };
                break;

            case LineKind.Todo:
                node["attrs"] = new JsonObject { ["done"] = line.Done };
                break;

            case LineKind.Rule:
                return node;

            case LineKind.Code:
                string code = line.Text;

                if (code.Length > 0)
                {
                    node["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = code });
                }

                return node;
        }

        JsonArray inline = [];

        foreach (Piece piece in line.Pieces)
        {
            switch (piece)
            {
                case TextPiece { Text.Length: > 0 } text:
                    JsonObject run = new() { ["type"] = "text", ["text"] = text.Text };

                    if (MarksJson(text) is { Count: > 0 } marks)
                    {
                        run["marks"] = marks;
                    }

                    inline.Add(run);
                    break;

                case BreakPiece:
                    inline.Add(new JsonObject { ["type"] = "hard_break" });
                    break;

                case InsetPiece inset:
                    inline.Add(new JsonObject
                    {
                        ["type"] = "inset",
                        ["attrs"] = new JsonObject
                        {
                            ["id"] = inset.Address.Id,
                            ["kind"] = KindName(inset.Address.Kind),
                            ["width"] = Math.Round(inset.Address.Width),
                            ["height"] = Math.Round(inset.Address.Height)
                        }
                    });
                    break;
            }
        }

        if (inline.Count > 0)
        {
            node["content"] = inline;
        }

        return node;
    }

    private static JsonArray MarksJson(TextPiece text)
    {
        JsonArray marks = [];

        foreach ((TextMarks flag, string name) in Simple)
        {
            if (text.Marks.HasFlag(flag))
            {
                marks.Add(new JsonObject { ["type"] = name });
            }
        }

        if (text.Colour is { } token)
        {
            marks.Add(new JsonObject { ["type"] = "colour", ["attrs"] = new JsonObject { ["token"] = token } });
        }

        if (text.Link is { } href)
        {
            marks.Add(new JsonObject { ["type"] = "link", ["attrs"] = new JsonObject { ["href"] = href } });
        }

        return marks;
    }

    private static JsonObject TableJson(SheetTable table)
    {
        JsonArray rows = [];
        int columns = Math.Max(1, table.Columns);

        for (int r = 0; r < table.Rows.Count; r++)
        {
            JsonArray cells = [];

            for (int c = 0; c < columns; c++)
            {
                IReadOnlyList<SheetLine> lines = c < table.Rows[r].Count && table.Rows[r][c].Count > 0 ? table.Rows[r][c] : [SheetLine.Empty()];
                JsonArray inner = [];

                foreach (SheetLine line in lines)
                {
                    // in una cella stanno righe di testo: niente codice, separazioni o tabelle
                    inner.Add(LineJson(line.Kind is LineKind.Code or LineKind.Rule ? SheetLine.Of(LineKind.Text, line.Text) : line));
                }

                cells.Add(new JsonObject { ["type"] = r == 0 ? "table_header" : "table_cell", ["content"] = inner });
            }

            rows.Add(new JsonObject { ["type"] = "table_row", ["content"] = cells });
        }

        return new JsonObject { ["type"] = "table", ["content"] = rows };
    }

    // ------------------------------------------------------------------ dall'editor

    /// <summary>Legge il documento dell'editor; quello che non conosce lo salta, non lo inventa.</summary>
    public static SheetDoc FromJson(JsonNode? json)
    {
        SheetDoc doc = new();

        if (json?["content"] is JsonArray blocks)
        {
            foreach (JsonNode? block in blocks)
            {
                if (block is null)
                {
                    continue;
                }

                if (Str(block["type"]) == "table")
                {
                    doc.Blocks.Add(TableFrom(block));
                }
                else if (LineFrom(block) is { } line)
                {
                    doc.Blocks.Add(line);
                }
            }
        }

        if (doc.Blocks.Count == 0)
        {
            doc.Blocks.Add(SheetLine.Empty());
        }

        return doc;
    }

    private static SheetLine? LineFrom(JsonNode node)
    {
        LineKind? kind = Str(node["type"]) switch
        {
            "paragraph" => LineKind.Text,
            "heading" => (Int(node["attrs"]?["level"]) ?? 1) switch
            {
                <= 1 => LineKind.Heading1,
                2 => LineKind.Heading2,
                _ => LineKind.Heading3
            },
            "quote" => LineKind.Quote,
            "bullet" => LineKind.Bullet,
            "number" => LineKind.Number,
            "todo" => LineKind.Todo,
            "rule" => LineKind.Rule,
            "code_block" => LineKind.Code,
            _ => null
        };

        if (kind is not { } known)
        {
            return null;
        }

        if (known == LineKind.Rule)
        {
            return SheetLine.Empty(LineKind.Rule);
        }

        List<Piece> pieces = [];

        if (node["content"] is JsonArray inline)
        {
            foreach (JsonNode? item in inline)
            {
                switch (Str(item?["type"]))
                {
                    case "text":
                        pieces.Add(TextFrom(item!));
                        break;

                    case "hard_break":
                        pieces.Add(new BreakPiece());
                        break;

                    case "inset" when InsetFrom(item!["attrs"]) is { } address:
                        pieces.Add(new InsetPiece(address));
                        break;
                }
            }
        }

        if (known == LineKind.Code)
        {
            string code = string.Concat(pieces.Select(piece => piece switch
            {
                TextPiece text => text.Text,
                BreakPiece => "\n",
                _ => string.Empty
            }));

            return new SheetLine(LineKind.Code, code.Length == 0 ? [] : [new TextPiece(code)]);
        }

        bool done = known == LineKind.Todo && node["attrs"]?["done"] is JsonValue flag && flag.TryGetValue(out bool value) && value;

        return new SheetLine(known, SheetXaml.Merge(pieces), done);
    }

    private static TextPiece TextFrom(JsonNode node)
    {
        TextMarks marks = TextMarks.None;
        string? colour = null;
        string? link = null;

        if (node["marks"] is JsonArray all)
        {
            foreach (JsonNode? mark in all)
            {
                string? type = Str(mark?["type"]);

                if (Array.FindIndex(Simple, pair => pair.Name == type) is var at and >= 0)
                {
                    marks |= Simple[at].Flag;
                }
                else if (type == "colour")
                {
                    colour = Str(mark!["attrs"]?["token"]);
                }
                else if (type == "link")
                {
                    link = Str(mark!["attrs"]?["href"]);
                }
            }
        }

        return new TextPiece(Str(node["text"]) ?? string.Empty, marks, colour, link);
    }

    private static SheetTable TableFrom(JsonNode table)
    {
        List<IReadOnlyList<IReadOnlyList<SheetLine>>> rows = [];

        foreach (JsonNode? row in table["content"] as JsonArray ?? [])
        {
            List<IReadOnlyList<SheetLine>> cells = [];

            foreach (JsonNode? cell in row?["content"] as JsonArray ?? [])
            {
                List<SheetLine> lines = [];

                foreach (JsonNode? inner in cell?["content"] as JsonArray ?? [])
                {
                    if (inner is not null && LineFrom(inner) is { } line)
                    {
                        lines.Add(line);
                    }
                }

                cells.Add(lines.Count > 0 ? lines : [SheetLine.Empty()]);
            }

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }

        return new SheetTable(rows);
    }

    private static InsetAddress? InsetFrom(JsonNode? attrs)
    {
        if (Str(attrs?["id"]) is not { } id || !PageIds.IsValid(id))
        {
            return null;
        }

        InsetKind kind = Str(attrs!["kind"]) switch
        {
            "drawing" => InsetKind.Drawing,
            "picture" => InsetKind.Picture,
            _ => InsetKind.Chart
        };

        double width = Dbl(attrs["width"]) ?? InsetAddress.BornWidth;
        double height = Dbl(attrs["height"]) ?? InsetAddress.BornHeight;

        return new InsetAddress(id, kind,
            Math.Clamp(height, InsetAddress.LeastHeight, InsetAddress.MostHeight),
            Math.Clamp(width, InsetAddress.LeastWidth, InsetAddress.MostWidth));
    }

    // ------------------------------------------------------------------ nomi

    private static readonly (TextMarks Flag, string Name)[] Simple =
    [
        (TextMarks.Bold, "bold"), (TextMarks.Italic, "italic"), (TextMarks.Underline, "underline"),
        (TextMarks.Strike, "strike"), (TextMarks.Highlight, "highlight"), (TextMarks.Code, "code")
    ];

    public static string KindName(InsetKind kind) => kind switch
    {
        InsetKind.Drawing => "drawing",
        InsetKind.Picture => "picture",
        _ => "chart"
    };

    private static string TypeOf(LineKind kind) => kind switch
    {
        LineKind.Heading1 or LineKind.Heading2 or LineKind.Heading3 => "heading",
        LineKind.Quote => "quote",
        LineKind.Bullet => "bullet",
        LineKind.Number => "number",
        LineKind.Todo => "todo",
        LineKind.Rule => "rule",
        LineKind.Code => "code_block",
        _ => "paragraph"
    };

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static int? Int(JsonNode? node) => Dbl(node) is { } number ? (int)Math.Round(number) : null;

    private static double? Dbl(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue(out double number))
        {
            return number;
        }

        if (value.TryGetValue(out int whole))
        {
            return whole;
        }

        return value.TryGetValue(out string? text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
    }
}
