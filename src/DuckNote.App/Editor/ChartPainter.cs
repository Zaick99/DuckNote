using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DuckNote.Notes.Charts;

namespace DuckNote.App.Editor;

/// <summary>
/// Disegna un <see cref="ChartSpec"/>: barre, linea o torta, con i colori delle serie del tema.
/// I pennelli sono quelli vivi del tema, quindi il grafico cambia colore col tema da solo.
/// </summary>
public sealed class ChartPainter : FrameworkElement
{
    private const double Edge = 14;
    private const double Label = 18;
    private const double Caption = 22;
    private const double Dot = 3.5;
    private const double Legend = 112;

    private ChartSpec _spec = ChartSpec.Read(string.Empty);

    public ChartSpec Spec
    {
        get => _spec;
        set
        {
            _spec = value;
            InvalidateVisual();
        }
    }

    private static Brush Series(int i) => Palette.Of(Palette.Series[i % Palette.Series.Length]);

    protected override void OnRender(DrawingContext paint)
    {
        Rect room = new(Edge, Edge, Math.Max(0, ActualWidth - 2 * Edge), Math.Max(0, ActualHeight - 2 * Edge));

        if (room.Width < 40 || room.Height < 40)
        {
            return;
        }

        paint.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (_spec.Title.Length > 0)
        {
            paint.DrawText(Text(_spec.Title, Palette.Text, 12.5, FontWeights.SemiBold), room.TopLeft);
            room = new Rect(room.X, room.Y + Caption, room.Width, Math.Max(0, room.Height - Caption));
        }

        if (_spec.Points.Count == 0)
        {
            paint.DrawText(Text("Scrivi una riga per ogni valore: etichetta e numero.", Palette.Tertiary, 11.5), room.TopLeft);
            return;
        }

        switch (_spec.Shape)
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

    private void Bars(DrawingContext paint, Rect room)
    {
        double floor = Floor(paint, room);
        double step = room.Width / _spec.Points.Count;
        double thick = Math.Min(54, step * 0.62);

        for (int i = 0; i < _spec.Points.Count; i++)
        {
            ChartPoint point = _spec.Points[i];
            double tall = Share(point.Value) * (floor - room.Y);
            double middle = room.X + step * (i + 0.5);

            paint.DrawRoundedRectangle(Series(i), null, new Rect(middle - thick / 2, floor - tall, thick, tall), 3, 3);
            Under(paint, point.Label, middle, floor, step);
            Above(paint, point.Value, middle, floor - tall);
        }
    }

    private void Line(DrawingContext paint, Rect room)
    {
        double floor = Floor(paint, room);
        int count = _spec.Points.Count;
        double step = count == 1 ? 0 : room.Width / (count - 1);
        Point[] places = new Point[count];

        for (int i = 0; i < count; i++)
        {
            double x = count == 1 ? room.X + room.Width / 2 : room.X + step * i;
            places[i] = new Point(x, floor - Share(_spec.Points[i].Value) * (floor - room.Y));
        }

        Pen stroke = new(Series(0), 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        for (int i = 1; i < count; i++)
        {
            paint.DrawLine(stroke, places[i - 1], places[i]);
        }

        for (int i = 0; i < count; i++)
        {
            paint.DrawEllipse(Series(0), null, places[i], Dot, Dot);
            Under(paint, _spec.Points[i].Label, places[i].X, floor, room.Width / count);
            Above(paint, _spec.Points[i].Value, places[i].X, places[i].Y - Dot);
        }
    }

    private void Pie(DrawingContext paint, Rect room)
    {
        double total = _spec.Total;

        if (total <= 0)
        {
            return;
        }

        double span = Math.Max(40, Math.Min(room.Height, room.Width - Legend));
        Point heart = new(room.X + span / 2, room.Y + room.Height / 2);
        double radius = span / 2 - 2;
        double from = -Math.PI / 2;

        // la legenda sta in colonna accanto alla torta: sulle fette nessun grigio si leggerebbe
        double top = Math.Max(room.Y, room.Y + (room.Height - _spec.Points.Count * Label) / 2);

        for (int i = 0; i < _spec.Points.Count; i++)
        {
            double sweep = Math.Max(0, _spec.Points[i].Value) / total * Math.PI * 2;
            paint.DrawGeometry(Series(i), null, Slice(heart, radius, from, sweep));
            from += sweep;

            Point where = new(room.X + span + 12, top + i * Label);
            paint.DrawRoundedRectangle(Series(i), null, new Rect(where.X, where.Y + 4, 8, 8), 2, 2);

            FormattedText written = Text($"{_spec.Points[i].Label}  {Number(_spec.Points[i].Value)}", Palette.Text, 11);
            written.MaxTextWidth = Math.Max(24, room.Right - where.X - 13);
            written.MaxLineCount = 1;
            written.Trimming = TextTrimming.CharacterEllipsis;
            paint.DrawText(written, new Point(where.X + 13, where.Y));
        }
    }

    private static Geometry Slice(Point heart, double radius, double from, double sweep)
    {
        if (sweep >= Math.PI * 2 - 0.0001)
        {
            return new EllipseGeometry(heart, radius, radius);
        }

        StreamGeometry slice = new();

        using (StreamGeometryContext draw = slice.Open())
        {
            draw.BeginFigure(heart, isFilled: true, isClosed: true);
            draw.LineTo(On(heart, radius, from), isStroked: false, isSmoothJoin: false);
            draw.ArcTo(On(heart, radius, from + sweep), new Size(radius, radius), 0, sweep > Math.PI,
                SweepDirection.Clockwise, isStroked: false, isSmoothJoin: false);
        }

        slice.Freeze();

        return slice;
    }

    private static Point On(Point heart, double radius, double angle) =>
        new(heart.X + radius * Math.Cos(angle), heart.Y + radius * Math.Sin(angle));

    /// <summary>La riga su cui poggia tutto: barre e punti sopra, etichette sotto.</summary>
    private static double Floor(DrawingContext paint, Rect room)
    {
        double floor = room.Bottom - Label;
        paint.DrawLine(new Pen(Palette.TableLine, 1), new Point(room.X, floor), new Point(room.Right, floor));

        return floor;
    }

    private double Share(double value) => _spec.Tallest <= 0 ? 0 : Math.Max(0, value) / _spec.Tallest * 0.86;

    private void Under(DrawingContext paint, string label, double middle, double floor, double room)
    {
        FormattedText written = Text(label, Palette.Text, 10.5);
        written.MaxTextWidth = Math.Max(18, room - 4);
        written.MaxLineCount = 1;
        written.Trimming = TextTrimming.CharacterEllipsis;
        paint.DrawText(written, new Point(middle - written.Width / 2, floor + 3));
    }

    private void Above(DrawingContext paint, double value, double middle, double top)
    {
        FormattedText written = Text(Number(value), Palette.Secondary, 10);
        paint.DrawText(written, new Point(middle - written.Width / 2, top - written.Height - 2));
    }

    private static string Number(double value) =>
        value.ToString(value == Math.Floor(value) ? "0" : "0.##", CultureInfo.CurrentCulture);

    private FormattedText Text(string text, Brush ink, double size, FontWeight? weight = null) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(Doc.TextFonts), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
