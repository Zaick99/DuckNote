using System.Globalization;

namespace DuckNote.App.Editor;

public enum ChartShape
{
    Bars,
    Line,
    Pie
}

public sealed record ChartPoint(string Label, double Value);

/// <summary>
/// Un grafico scritto a mano: due righe di intestazione e poi una coppia
/// "etichetta numero" per riga. Resta testo anche nel file, cosi' si rilegge e
/// si corregge senza strumenti, e il numero si riconosce dalla fine della riga
/// perche' le etichette hanno gli spazi dentro.
/// </summary>
public sealed class ChartPlan
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

    public double Total => Points.Sum(point => point.Value);

    public static ChartPlan Read(string text)
    {
        ChartShape shape = ChartShape.Bars;
        string title = string.Empty;
        List<ChartPoint> points = [];

        foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (Said(line, "tipo") is { } written)
            {
                shape = Shaped(written);
                continue;
            }

            if (Said(line, "titolo") is { } named)
            {
                title = named;
                continue;
            }

            if (Point(line) is { } point)
            {
                points.Add(point);
            }
        }

        return new ChartPlan { Shape = shape, Title = title, Points = points };
    }

    /// <summary>
    /// Un'intestazione si riconosce solo per nome intero: "titolo: Latenza 2024"
    /// altrimenti passerebbe per una coppia etichetta-numero.
    /// </summary>
    private static string? Said(string line, string field)
    {
        int colon = line.IndexOf(':');

        return colon > 0 && line[..colon].Trim().Equals(field, StringComparison.OrdinalIgnoreCase)
            ? line[(colon + 1)..].Trim()
            : null;
    }

    private static ChartPoint? Point(string line)
    {
        int gap = line.LastIndexOf(' ');
        if (gap <= 0)
        {
            return null;
        }

        string written = line[(gap + 1)..].Replace(',', '.');

        return double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? new ChartPoint(line[..gap].Trim(), value)
            : null;
    }

    private static ChartShape Shaped(string written) => written.ToLowerInvariant() switch
    {
        "linea" or "linee" => ChartShape.Line,
        "torta" => ChartShape.Pie,
        _ => ChartShape.Bars
    };
}
