using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DuckNote.App.Editor;

/// <summary>
/// Disegna un <see cref="ChartPlan"/>. Non tiene stato: cambia il piano, si
/// ridipinge, e i pennelli sono gli stessi dell'editor cosi' il grafico segue
/// il tema senza saperne niente.
/// </summary>
public sealed class ChartView(EditorBrushes brushes) : FrameworkElement
{
    private const double Edge = 14;
    private const double Label = 18;
    private const double Caption = 22;
    private const double Dot = 3.5;
    private const double Legend = 112;

    private ChartPlan _plan = ChartPlan.Read(string.Empty);

    public ChartPlan Plan
    {
        get => _plan;
        set
        {
            _plan = value;
            InvalidateVisual();
        }
    }

    public void Repaint() => InvalidateVisual();

    protected override void OnRender(DrawingContext paint)
    {
        Rect room = new(Edge, Edge, Math.Max(0, ActualWidth - 2 * Edge), Math.Max(0, ActualHeight - 2 * Edge));

        if (room.Width < 40 || room.Height < 40)
        {
            return;
        }

        if (_plan.Title.Length > 0)
        {
            paint.DrawText(Written(_plan.Title, brushes.Text, 12.5), new Point(room.X, room.Y));
            room = new Rect(room.X, room.Y + Caption, room.Width, room.Height - Caption);
        }

        if (_plan.Points.Count == 0)
        {
            paint.DrawText(
                Written("Scrivi una riga per ogni valore: etichetta e numero.", brushes.Tertiary, 11.5),
                new Point(room.X, room.Y));
            return;
        }

        switch (_plan.Shape)
        {
            case ChartShape.Pie:
                Pie(paint, room);
                break;
            case ChartShape.Line:
                Line(paint, room);
                break;
            default:
                Bars(paint, room);
                break;
        }
    }

    // --- barre --------------------------------------------------------------

    private void Bars(DrawingContext paint, Rect room)
    {
        double floor = Floor(paint, room);
        double step = room.Width / _plan.Points.Count;
        double thick = Math.Min(54, step * 0.62);

        for (int i = 0; i < _plan.Points.Count; i++)
        {
            ChartPoint point = _plan.Points[i];
            double tall = Share(point.Value) * (floor - room.Y);
            double middle = room.X + step * (i + 0.5);

            paint.DrawRoundedRectangle(
                brushes.Series[i % brushes.Series.Length], null,
                new Rect(middle - thick / 2, floor - tall, thick, tall), 3, 3);

            Under(paint, point.Label, middle, floor, step);
            Above(paint, point.Value, middle, floor - tall);
        }
    }

    // --- linea --------------------------------------------------------------

    private void Line(DrawingContext paint, Rect room)
    {
        double floor = Floor(paint, room);
        double step = _plan.Points.Count == 1 ? 0 : room.Width / (_plan.Points.Count - 1);
        Point[] places = new Point[_plan.Points.Count];

        for (int i = 0; i < places.Length; i++)
        {
            double x = places.Length == 1 ? room.X + room.Width / 2 : room.X + step * i;
            places[i] = new Point(x, floor - Share(_plan.Points[i].Value) * (floor - room.Y));
        }

        Pen stroke = new(brushes.Series[0], 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        for (int i = 1; i < places.Length; i++)
        {
            paint.DrawLine(stroke, places[i - 1], places[i]);
        }

        for (int i = 0; i < places.Length; i++)
        {
            paint.DrawEllipse(brushes.Series[0], null, places[i], Dot, Dot);
            Under(paint, _plan.Points[i].Label, places[i].X, floor, room.Width / places.Length);
            Above(paint, _plan.Points[i].Value, places[i].X, places[i].Y - Dot);
        }
    }

    // --- torta --------------------------------------------------------------

    private void Pie(DrawingContext paint, Rect room)
    {
        double total = _plan.Total;
        if (total <= 0)
        {
            return;
        }

        double span = Math.Max(40, Math.Min(room.Height, room.Width - Legend));
        Point heart = new(room.X + span / 2, room.Y + room.Height / 2);
        double radius = span / 2 - 2;
        double from = -Math.PI / 2;

        // La legenda sta in colonna accanto alla torta, centrata con lei: le
        // etichette non vanno sopra le fette, dove nessun grigio si leggerebbe.
        double top = Math.Max(room.Y, room.Y + (room.Height - _plan.Points.Count * Label) / 2);

        for (int i = 0; i < _plan.Points.Count; i++)
        {
            double sweep = _plan.Points[i].Value / total * Math.PI * 2;
            paint.DrawGeometry(brushes.Series[i % brushes.Series.Length], null, Slice(heart, radius, from, sweep));
            from += sweep;

            Keyed(paint, i, new Point(room.X + span + 12, top + i * Label), room.Right);
        }
    }

    private static Geometry Slice(Point heart, double radius, double from, double sweep)
    {
        if (sweep >= Math.PI * 2 - 0.0001)
        {
            return new EllipseGeometry(heart, radius, radius);
        }

        Point start = On(heart, radius, from);
        Point end = On(heart, radius, from + sweep);

        StreamGeometry slice = new();
        using (StreamGeometryContext draw = slice.Open())
        {
            draw.BeginFigure(heart, isFilled: true, isClosed: true);
            draw.LineTo(start, isStroked: false, isSmoothJoin: false);
            draw.ArcTo(end, new Size(radius, radius), 0, sweep > Math.PI,
                SweepDirection.Clockwise, isStroked: false, isSmoothJoin: false);
        }

        slice.Freeze();

        return slice;
    }

    private static Point On(Point heart, double radius, double angle) =>
        new(heart.X + radius * Math.Cos(angle), heart.Y + radius * Math.Sin(angle));

    private void Keyed(DrawingContext paint, int place, Point where, double edge)
    {
        ChartPoint point = _plan.Points[place];

        paint.DrawRoundedRectangle(
            brushes.Series[place % brushes.Series.Length], null,
            new Rect(where.X, where.Y + 4, 8, 8), 2, 2);

        FormattedText written = Written($"{point.Label}  {Number(point.Value)}", brushes.Text, 11);
        written.MaxTextWidth = Math.Max(24, edge - where.X - 13);
        written.MaxLineCount = 1;
        written.Trimming = TextTrimming.CharacterEllipsis;

        paint.DrawText(written, new Point(where.X + 13, where.Y));
    }

    // --- pezzi condivisi ----------------------------------------------------

    /// <summary>
    /// La riga su cui poggiano barre e punti, e sotto la quale stanno le
    /// etichette. Torna la sua quota: tutto il resto si misura da li', cosi'
    /// niente finisce sotto la linea ne' sopra il colore.
    /// </summary>
    private double Floor(DrawingContext paint, Rect room)
    {
        double floor = room.Bottom - Label;

        paint.DrawLine(new Pen(brushes.TableLine, 1), new Point(room.X, floor), new Point(room.Right, floor));

        return floor;
    }

    private double Share(double value) => _plan.Tallest <= 0 ? 0 : Math.Max(0, value) / _plan.Tallest * 0.86;

    private void Under(DrawingContext paint, string label, double middle, double floor, double room)
    {
        FormattedText written = Written(label, brushes.Text, 10.5);
        written.MaxTextWidth = Math.Max(18, room - 4);
        written.MaxLineCount = 1;
        written.Trimming = TextTrimming.CharacterEllipsis;

        paint.DrawText(written, new Point(middle - written.Width / 2, floor + 3));
    }

    private void Above(DrawingContext paint, double value, double middle, double top)
    {
        FormattedText written = Written(Number(value), brushes.Secondary, 10);
        paint.DrawText(written, new Point(middle - written.Width / 2, top - written.Height - 2));
    }

    private static string Number(double value) =>
        value == Math.Floor(value)
            ? value.ToString("0", CultureInfo.CurrentCulture)
            : value.ToString("0.##", CultureInfo.CurrentCulture);

    private FormattedText Written(string text, Brush ink, double size) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(NoteDocument.UiFonts), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
