using System.Globalization;

namespace DuckNote.Notes.Notebook;

public enum InsetKind
{
    Drawing,
    Picture,
    Chart
}

/// <summary>
/// Dove sta un riquadro (disegno, immagine, grafico) e quanto e' grande. Nel documento salvato
/// il riquadro e' un collegamento con questo indirizzo, perche' NavigateUri e' l'unica cosa
/// appesa a un elemento di testo che sopravvive al salvataggio in XAML; i byte stanno nello
/// zip della nota sotto <see cref="Entry"/>.
/// </summary>
public sealed record InsetAddress(string Id, InsetKind Kind, double Height, double Width)
{
    public const double LeastHeight = 90;
    public const double MostHeight = 1200;
    public const double LeastWidth = 120;
    public const double MostWidth = 1600;
    public const double BornHeight = 230;
    public const double BornWidth = 360;

    private const string Scheme = "ducknote";
    private const string Host = "riquadro";

    public static InsetAddress Fresh(InsetKind kind) => new(PageIds.Fresh(), kind, BornHeight, BornWidth);

    public string Entry => $"riquadri/{Id}.{Suffix(Kind)}";

    public string Title => Kind switch
    {
        InsetKind.Drawing => "Disegno",
        InsetKind.Picture => "Immagine",
        _ => "Grafico"
    };

    public Uri ToUri() => new(string.Create(CultureInfo.InvariantCulture,
        $"{Scheme}://{Host}/{Id}?t={Suffix(Kind)}&h={Math.Round(Height)}&w={Math.Round(Width)}"));

    /// <summary>Legge un segnaposto; null per qualunque altro collegamento (ci sono anche i link veri).</summary>
    public static InsetAddress? Parse(Uri? address)
    {
        if (address is not { IsAbsoluteUri: true }
            || !address.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)
            || !address.Host.Equals(Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string id = address.AbsolutePath.Trim('/');
        Dictionary<string, string> fields = new(StringComparer.Ordinal);

        foreach (string pair in address.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                fields[parts[0]] = parts[1];
            }
        }

        if (!PageIds.IsValid(id) || !fields.TryGetValue("t", out string? suffix) || KindOf(suffix) is not { } kind)
        {
            return null;
        }

        return new InsetAddress(
            id, kind,
            Size(fields, "h", BornHeight, LeastHeight, MostHeight),
            Size(fields, "w", BornWidth, LeastWidth, MostWidth));
    }

    private static double Size(Dictionary<string, string> fields, string name, double born, double least, double most) =>
        fields.TryGetValue(name, out string? written)
        && double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double size)
            ? Math.Clamp(size, least, most)
            : born;

    private static string Suffix(InsetKind kind) => kind switch
    {
        InsetKind.Drawing => "disegno",
        InsetKind.Picture => "immagine",
        _ => "grafico"
    };

    private static InsetKind? KindOf(string suffix) => suffix switch
    {
        "disegno" => InsetKind.Drawing,
        "immagine" => InsetKind.Picture,
        "grafico" => InsetKind.Chart,
        _ => null
    };
}
