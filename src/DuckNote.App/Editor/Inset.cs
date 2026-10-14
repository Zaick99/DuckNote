using System.Globalization;

namespace DuckNote.App.Editor;

public enum InsetKind
{
    Drawing,
    Picture,
    Chart
}

/// <summary>
/// Un riquadro dentro il testo: un disegno, un'immagine o un grafico.
///
/// I byte stanno nello zip della nota e nel documento resta un segnaposto che
/// porta l'indirizzo costruito qui. Il giro passa da NavigateUri perche' di
/// tutto quello che si puo' appendere a un TextElement e' l'unica cosa che
/// sopravvive al salvataggio in XAML: Tag e ToolTip si perdono, e un
/// BlockUIContainer torna indietro come paragrafo vuoto.
/// </summary>
public sealed class Inset
{
    public const double LeastHeight = 90;
    public const double MostHeight = 1200;
    public const double LeastWidth = 120;
    public const double MostWidth = 1600;

    private const double BornHeight = 230;
    private const double BornWidth = 360;
    private const string Scheme = "ducknote";
    private const string Host = "riquadro";

    public required string Id { get; init; }

    public required InsetKind Kind { get; init; }

    public byte[] Bytes { get; set; } = [];

    public double Height { get; set; } = BornHeight;

    /// <summary>
    /// Quanto e' larga la cornice. Non tutta la riga: un riquadro grande quanto
    /// basta lascia posto al testo di fianco.
    /// </summary>
    public double Width { get; set; } = BornWidth;

    public static Inset Fresh(InsetKind kind) => new() { Id = NotePage.FreshId(), Kind = kind };

    public string Entry => $"riquadri/{Id}.{Suffix(Kind)}";

    public string Title => Kind switch
    {
        InsetKind.Drawing => "Disegno",
        InsetKind.Picture => "Immagine",
        _ => "Grafico"
    };

    public Uri Address => new(string.Create(
        CultureInfo.InvariantCulture,
        $"{Scheme}://{Host}/{Id}?t={Suffix(Kind)}&h={Math.Round(Height)}&w={Math.Round(Width)}"));

    /// <summary>
    /// Legge l'indirizzo di un segnaposto. Torna null per qualunque altro
    /// collegamento: nel testo ci sono anche i link veri.
    /// </summary>
    public static Inset? At(Uri? address)
    {
        if (address is null || !address.IsAbsoluteUri
            || !string.Equals(address.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(address.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string id = address.AbsolutePath.Trim('/');
        if (!NotePage.IsId(id))
        {
            return null;
        }

        Dictionary<string, string> fields = Fields(address.Query);

        if (!fields.TryGetValue("t", out string? suffix) || Kinded(suffix) is not { } kind)
        {
            return null;
        }

        return new Inset
        {
            Id = id,
            Kind = kind,
            Height = Sized(fields, "h", BornHeight, LeastHeight, MostHeight),
            Width = Sized(fields, "w", BornWidth, LeastWidth, MostWidth)
        };
    }

    private static double Sized(
        Dictionary<string, string> fields, string field, double born, double least, double most) =>
        fields.TryGetValue(field, out string? written)
        && double.TryParse(written, CultureInfo.InvariantCulture, out double size)
            ? Math.Clamp(size, least, most)
            : born;

    private static Dictionary<string, string> Fields(string query)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);

        foreach (string couple in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = couple.Split('=', 2);

            if (parts.Length == 2)
            {
                fields[parts[0]] = parts[1];
            }
        }

        return fields;
    }

    private static string Suffix(InsetKind kind) => kind switch
    {
        InsetKind.Drawing => "disegno",
        InsetKind.Picture => "immagine",
        _ => "grafico"
    };

    private static InsetKind? Kinded(string suffix) => suffix switch
    {
        "disegno" => InsetKind.Drawing,
        "immagine" => InsetKind.Picture,
        "grafico" => InsetKind.Chart,
        _ => null
    };
}
