using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DuckNote.App.Editor;

/// <summary>
/// La cornice attorno a un riquadro: il nome di cosa c'e' dentro, i suoi
/// attrezzi, il tasto per toglierlo e il bordo in basso da trascinare per
/// cambiare l'altezza. Dentro ci sta un foglio da disegno, un'immagine o un
/// grafico, e la cornice sa solo chiedere al contenuto i byte da salvare.
/// </summary>
public sealed class InsetBox : Border
{
    private const double GripHeight = 7;

    /// <summary>Lo stacco che una cornice tiene dalla parola o dalla cornice dopo.</summary>
    public const double Apart = 8;

    /// <summary>
    /// Quanto spazio ha una riga di testo nel controllo che la mostra, tolto
    /// quel poco che una riga non riempie mai: una cornice piu' larga di questo
    /// sborderebbe dalla pagina.
    /// </summary>
    public static double Room(FrameworkElement shown) =>
        Math.Max(
            Inset.LeastWidth * 2,
            (NoteDocument.Column(shown) ?? shown.ActualWidth - Edges) - Spare);

    private const double Spare = 14;

    private const double Edges = 46;

    private readonly EditorBrushes _brushes;
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal };

    private readonly DrawingPad? _pad;
    private readonly ChartView? _chart;

    private bool _chosen;

    public InsetBox(Inset inset, EditorBrushes brushes)
    {
        Inset = inset;
        _brushes = brushes;

        BorderBrush = brushes.TableLine;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Margin = NoteDocument.Flush;
        SnapsToDevicePixels = true;
        Height = inset.Height;

        Grid frame = new();
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement body = inset.Kind switch
        {
            InsetKind.Drawing => _pad = Pad(inset),
            InsetKind.Chart => _chart = Chart(inset),
            _ => Picture(inset)
        };

        Grid.SetRow(body, 1);
        frame.Children.Add(Header());
        frame.Children.Add(body);
        frame.Children.Add(Taller());
        frame.Children.Add(Wider());

        Child = frame;
    }

    /// <summary>
    /// Nascoste alla serializzazione, e non per gusto: togliere un oggetto
    /// incastonato da un RichTextBox vivo fa costruire a WPF il passo di
    /// annullamento, e per farlo passa l'oggetto a XamlWriter. Una proprieta'
    /// piena di tipo generico lo fa saltare, l'eccezione scappa a metà
    /// modifica, e da quel momento ogni passata di layout rilancia: finestre
    /// d'errore a catena. L'annullamento non ha bisogno di queste, tiene
    /// l'oggetto vero.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Inset Inset { get; }

    public event Action? Changed;

    public event Action<InsetBox>? Removed;

    /// <summary>Chi vuole modificare un grafico chiede il testo nuovo a questo.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, string?>? AskForChart { get; set; }

    /// <summary>Porta nel riquadro i byte che il contenuto ha adesso.</summary>
    public void Harvest()
    {
        if (_pad is not null)
        {
            Inset.Bytes = _pad.Save();
        }

        Inset.Height = Math.Round(Height);
    }

    /// <summary>
    /// Il riquadro e' dentro la selezione. Lo dice da se' con un anello, invece
    /// di finire sotto il velo della selezione: quel velo e' un adorner, sta
    /// sopra a tutto per costruzione, e coprirebbe il disegno proprio mentre lo
    /// si vuole guardare.
    /// </summary>
    public bool Chosen
    {
        get => _chosen;
        set
        {
            if (_chosen == value)
            {
                return;
            }

            _chosen = value;
            Recolour();
        }
    }

    public void Recolour()
    {
        BorderBrush = _chosen ? _brushes.Accent : _brushes.TableLine;
        BorderThickness = new Thickness(_chosen ? 2 : 1);
        _chart?.Repaint();
    }

    private Grid Header()
    {
        Grid bar = new();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        TextBlock name = new()
        {
            Text = Inset.Title,
            FontSize = 10.5,
            Foreground = _brushes.Tertiary,
            Margin = new Thickness(9, 5, 0, 3),
            VerticalAlignment = VerticalAlignment.Center
        };

        _strip.HorizontalAlignment = HorizontalAlignment.Right;
        _strip.Margin = new Thickness(0, 3, 6, 2);

        if (_chart is not null)
        {
            _strip.Children.Add(Chips.Numbers(EditChart));
        }

        _strip.Children.Add(Chips.Close(() => Removed?.Invoke(this)));

        Grid.SetColumn(name, 0);
        Grid.SetColumn(_strip, 1);
        bar.Children.Add(name);
        bar.Children.Add(_strip);
        Grid.SetRow(bar, 0);

        return bar;
    }

    private DrawingPad Pad(Inset inset)
    {
        DrawingPad pad = new();
        pad.Load(inset.Bytes);
        pad.Changed += () => Changed?.Invoke();

        return pad;
    }

    private ChartView Chart(Inset inset) => new(_brushes) { Plan = ChartPlan.Read(Text(inset)) };

    private static FrameworkElement Picture(Inset inset)
    {
        Image shown = new() { Stretch = Stretch.Uniform, Margin = new Thickness(6, 0, 6, 6) };

        if (Framed(inset.Bytes) is { } picture)
        {
            shown.Source = picture;
        }

        return shown;
    }

    private static BitmapImage? Framed(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        try
        {
            BitmapImage picture = new();
            picture.BeginInit();
            picture.CacheOption = BitmapCacheOption.OnLoad;
            picture.StreamSource = new MemoryStream(bytes, writable: false);
            picture.EndInit();
            picture.Freeze();

            return picture;
        }
        catch (Exception problem) when (problem is NotSupportedException or IOException or ArgumentException)
        {
            return null;
        }
    }

    private Thumb Taller()
    {
        Thumb grip = Grip(Cursors.SizeNS, "Trascina per cambiare l'altezza", wide: false);
        grip.Height = GripHeight;

        grip.DragDelta += (_, drag) =>
        {
            double wanted = Math.Clamp(Height + drag.VerticalChange, Inset.LeastHeight, Inset.MostHeight);

            if (Math.Abs(wanted - Height) < 0.5)
            {
                return;
            }

            Height = wanted;
            Inset.Height = Math.Round(wanted);
            Changed?.Invoke();
        };

        Grid.SetRow(grip, 2);
        Grid.SetColumnSpan(grip, 2);

        return grip;
    }

    /// <summary>
    /// La larghezza non e' della cornice ma della zattera che la tiene a galla:
    /// e' quella che decide quanto posto resta alle parole di fianco.
    /// </summary>
    private Thumb Wider()
    {
        Thumb grip = Grip(Cursors.SizeWE, "Trascina per cambiare la larghezza", wide: true);
        grip.Width = GripHeight;

        grip.DragDelta += (_, drag) =>
        {
            if (Raft is not { } raft)
            {
                return;
            }

            double wanted = Widened(drag.HorizontalChange);

            if (Math.Abs(wanted - raft.Width) < 0.5)
            {
                return;
            }

            raft.Width = wanted;
            Inset.Width = Math.Round(wanted);
            Changed?.Invoke();
        };

        Grid.SetRow(grip, 1);
        Grid.SetColumn(grip, 1);

        return grip;
    }

    /// <summary>La zattera che tiene questa cornice, se e' in pagina.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Floater? Raft => Parent is BlockUIContainer holder ? holder.Parent as Floater : null;

    /// <summary>
    /// La larghezza che avrebbe trascinando il bordo di tanto. Non oltre la
    /// riga: una cornice piu' larga della pagina ci sborderebbe, e il testo non
    /// avrebbe piu' dove girarle attorno.
    /// </summary>
    public double Widened(double by)
    {
        double now = Raft is { Width: var wide } && !double.IsNaN(wide) ? wide : ActualWidth;
        double most = Shown() is { } page ? Math.Min(Inset.MostWidth, Room(page)) : Inset.MostWidth;

        return Math.Clamp(now + by, Inset.LeastWidth, most);
    }

    /// <summary>Il controllo che mostra la nota, risalendo dalla cornice.</summary>
    private FrameworkElement? Shown()
    {
        for (DependencyObject? step = this; step is not null; step = VisualTreeHelper.GetParent(step))
        {
            if (step is RichTextBox page)
            {
                return page;
            }
        }

        return null;
    }

    private Thumb Grip(Cursor pointer, string tip, bool wide) => new()
    {
        Cursor = pointer,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Template = GripLook(wide),
        ToolTip = tip
    };

    private ControlTemplate GripLook(bool wide)
    {
        FrameworkElementFactory handle = new(typeof(Rectangle));
        handle.SetValue(Rectangle.WidthProperty, wide ? 2d : 34d);
        handle.SetValue(Rectangle.HeightProperty, wide ? 34d : 2d);
        handle.SetValue(Rectangle.RadiusXProperty, 1d);
        handle.SetValue(Rectangle.RadiusYProperty, 1d);
        handle.SetValue(Rectangle.FillProperty, _brushes.TableLine);
        handle.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        handle.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

        FrameworkElementFactory band = new(typeof(Border));
        band.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        band.AppendChild(handle);

        return new ControlTemplate(typeof(Thumb)) { VisualTree = band };
    }

    private void EditChart()
    {
        if (AskForChart?.Invoke(Text(Inset)) is not { } written)
        {
            return;
        }

        Inset.Bytes = Encoding.UTF8.GetBytes(written);
        _chart!.Plan = ChartPlan.Read(written);
        Changed?.Invoke();
    }

    private static string Text(Inset inset) =>
        inset.Bytes.Length == 0 ? ChartPlan.Example : Encoding.UTF8.GetString(inset.Bytes);
}
