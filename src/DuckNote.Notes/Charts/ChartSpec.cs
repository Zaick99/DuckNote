using System.Globalization;

namespace DuckNote.Notes.Charts;

public enum ChartShape
{
    Bars,
    Line,
    Pie
}

public sealed record ChartPoint(string Label, double Value);

/// <summary>
/// Un grafico scritto a mano: "tipo:" e "titolo:" in testa, poi una coppia "etichetta numero"
/// per riga. Resta testo anche nel file, cosi' si corregge un numero invece di ridisegnare.
/// Il numero si cerca in fondo alla riga, perche' le etichette hanno gli spazi dentro.
/// </summary>
public sealed class ChartSpec
{
    public const string Example =
        """
        tipo: barre
        titolo: Tempo di risposta (ms)

        gateway 3
        nas 12
        dns pubblico 27
        """;

    public ChartShape Shape { get; private init; }

    public string Title { get; private init; } = string.Empty;

    public IReadOnlyList<ChartPoint> Points { get; private init; } = [];

    public double Tallest => Points.Count == 0 ? 0 : Points.Max(point => point.Value);

    public double Total => Points.Sum(point => Math.Max(0, point.Value));

    public static ChartSpec Read(string? text)
    {
        ChartShape shape = ChartShape.Bars;
        string title = string.Empty;
        List<ChartPoint> points = [];

        foreach (string raw in (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (Field(line, "tipo") is { } kind)
            {
                shape = kind.ToLowerInvariant() switch
                {
                    "linea" or "linee" => ChartShape.Line,
                    "torta" => ChartShape.Pie,
                    _ => ChartShape.Bars
                };
                continue;
            }

            if (Field(line, "titolo") is { } name)
            {
                title = name;
                continue;
            }

            int gap = line.LastIndexOf(' ');

            if (gap > 0 && double.TryParse(line[(gap + 1)..].Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value))
            {
                points.Add(new ChartPoint(line[..gap].Trim(), value));
            }
        }

        return new ChartSpec { Shape = shape, Title = title, Points = points };
    }

    /// <summary>Un'intestazione si riconosce solo per nome intero, prima dei due punti.</summary>
    private static string? Field(string line, string name)
    {
        int colon = line.IndexOf(':');

        return colon > 0 && line[..colon].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)
            ? line[(colon + 1)..].Trim()
            : null;
    }
}
