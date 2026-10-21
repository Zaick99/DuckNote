using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using DuckNote.App.Ui;
using DuckNote.Notes.Outline;

namespace DuckNote.App.Pages;

/// <summary>Una riga della struttura come la mostra la lista: binari, triangolino, titolo, conteggio.</summary>
public sealed class OutlineItem : Observable
{
    private const double Step = 14;
    private const double Half = 6;

    private bool _editing;

    public OutlineItem(OutlineRow row) => Row = row;

    public OutlineRow Row { get; private set; }

    public string Key => Row.Key;

    public string Title => Row.Title;

    public double RowHeight => Row.IsPage ? 30 : 26;

    /// <summary>Le pagine hanno il foglietto davanti, i titoli la loro sigla (H1, H2).</summary>
    public Visibility IconVis => Row.IsPage ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Il colore della tessera del foglio: scelto dal suo identificativo, quindi diverso da un
    /// foglio all'altro ma sempre lo stesso per lo stesso foglio, anche dopo un riavvio.
    /// </summary>
    public Brush? Tile => Application.Current?.TryFindResource(TileTokens[Stable(Row.PageId) % TileTokens.Length]) as Brush;

    private static readonly string[] TileTokens = ["Orange", "Blue", "Green", "Pink", "Purple", "Teal", "Red", "Indigo", "Yellow"];

    private static int Stable(string text)
    {
        uint hash = 2166136261;

        foreach (char ch in text)
        {
            hash = (hash ^ ch) * 16777619;
        }

        return (int)(hash % int.MaxValue);
    }

    public Visibility LevelVis => Row.IsPage ? Visibility.Collapsed : Visibility.Visible;

    public string LevelText => Row.Level == 1 ? "H1" : "H2";

    public double Size => Row.IsPage ? 13 : 12.5;

    public FontWeight Weight => Row.IsPage ? FontWeights.SemiBold : FontWeights.Normal;

    /// <summary>
    /// Aprire una pagina e aprire una sezione non sono lo stesso gesto: la pagina ha il
    /// triangolino pieno, la sezione la freccina sottile.
    /// </summary>
    public string Toggle => Row.IsPage
        ? Row.IsOpen ? "" : ""
        : Row.IsOpen ? "" : "";

    public Visibility ToggleVis => Row.HasChildren ? Visibility.Visible : Row.IsPage ? Visibility.Hidden : Visibility.Collapsed;

    public string Badge => Row.Hits.ToString(CultureInfo.CurrentCulture);

    public Visibility BadgeVis => Row.Hits > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HereVis => Row.IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public bool Editing
    {
        get => _editing;
        set
        {
            _editing = value;
            Raise(nameof(EditVis));
            Raise(nameof(LabelVis));
        }
    }

    public Visibility EditVis => _editing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LabelVis => _editing ? Visibility.Collapsed : Visibility.Visible;

    public double RailWidth => Row.Depth * Step;

    /// <summary>
    /// I binari: una verticale per ogni ramo che continua sotto, un gomito verso questa riga.
    /// </summary>
    public string Rails
    {
        get
        {
            double height = RowHeight;
            double middle = Math.Round(height / 2);
            StringBuilder path = new();

            for (int level = 1; level < Row.Depth; level++)
            {
                if (Row.Continues[level])
                {
                    Line(path, (level - 1) * Step + Half, 0, height);
                }
            }

            if (Row.Depth > 0)
            {
                double x = (Row.Depth - 1) * Step + Half;
                path.Append(string.Format(CultureInfo.InvariantCulture, "M {0} 0 V {1} H {2} ", x, middle, Row.Depth * Step - 2));

                if (!Row.IsLast)
                {
                    Line(path, x, middle, height);
                }
            }

            return path.Length == 0 ? "M 0 0" : path.ToString().TrimEnd();
        }
    }

    public void Update(OutlineRow row)
    {
        Row = row;
        RaiseAll();
    }

    private static void Line(StringBuilder path, double x, double from, double to) =>
        path.Append(string.Format(CultureInfo.InvariantCulture, "M {0} {1} V {2} ", x, from, to));
}
