using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DuckNote.Notes.Charts;
using DuckNote.Notes.Notebook;

namespace DuckNote.App.Editor;

/// <summary>
/// La cornice di un riquadro nel testo: il nome, i suoi attrezzi, la croce per toglierlo, e i
/// bordi in basso e a destra da trascinare per altezza e larghezza. Dentro: un foglio da
/// disegno, un'immagine o un grafico.
/// </summary>
public sealed class InsetFrame : Border
{
    /// <summary>Lo stacco fra una cornice e la parola o la cornice che segue.</summary>
    public const double Apart = 8;

    private const double Grip = 7;

    private readonly SketchPad? _pad;
    private readonly ChartPainter? _chart;
    private bool _chosen;

    public InsetFrame(Inset inset)
    {
        Inset = inset;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        SnapsToDevicePixels = true;
        Height = inset.Address.Height;
        SetResourceReference(BorderBrushProperty, "TableBorder");
        SetResourceReference(BackgroundProperty, "BgContent");

        Grid frame = new();
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        frame.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement body = inset.Kind switch
        {
            InsetKind.Drawing => _pad = new SketchPad(inset.Bytes),
            InsetKind.Chart => _chart = new ChartPainter { Spec = ChartSpec.Read(ChartText(inset)) },
            _ => Picture(inset.Bytes)
        };

        if (_pad is not null)
        {
            _pad.Changed += () => Changed?.Invoke();
        }

        Grid.SetRow(body, 1);
        frame.Children.Add(Header());
        frame.Children.Add(body);
        frame.Children.Add(Taller());
        frame.Children.Add(Wider());

        Child = frame;
    }

    /// <summary>
    /// Nascosta alla serializzazione: togliere un oggetto da un RichTextBox vivo fa passare
    /// l'oggetto a XamlWriter per l'annullamento, e una proprieta' piena di un tipo qualsiasi lo
    /// farebbe saltare a meta' modifica.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Inset Inset { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, string?>? AskForChart { get; set; }

    public event Action? Changed;

    public event Action<InsetFrame>? Removed;

    /// <summary>La zattera che tiene la cornice nel testo, se e' in pagina.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Floater? Raft => Parent is BlockUIContainer holder ? holder.Parent as Floater : null;

    /// <summary>
    /// Dentro la selezione: lo dice con un anello invece di finire sotto il velo della
    /// selezione, che sta sopra a tutto e coprirebbe proprio il disegno che si guarda.
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
            SetResourceReference(BorderBrushProperty, value ? "Accent" : "TableBorder");
            BorderThickness = new Thickness(value ? 2 : 1);
        }
    }

    /// <summary>Porta nel riquadro i byte e la misura che ha adesso.</summary>
    public void Harvest()
    {
        if (_pad is not null)
        {
            Inset.Bytes = _pad.Save();
        }

        Inset.Address = Inset.Address with
        {
            Height = Math.Round(Height),
            Width = Raft is { Width: var wide } && !double.IsNaN(wide) ? Math.Round(wide) : Inset.Address.Width
        };
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
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 6, 0, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "LabelTertiary");

        StackPanel tools = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 4, 0)
        };

        if (_chart is not null)
        {
            tools.Children.Add(Chips.Icon("", "Modifica i numeri del grafico", EditChart));
        }

        tools.Children.Add(Chips.Icon("", "Togli il riquadro dalla nota", () => Removed?.Invoke(this), "Red"));

        Grid.SetColumn(tools, 1);
        bar.Children.Add(name);
        bar.Children.Add(tools);

        return bar;
    }

    private static Image Picture(byte[] bytes)
    {
        Image shown = new() { Stretch = Stretch.Uniform, Margin = new Thickness(6, 0, 6, 6) };

        if (bytes.Length == 0)
        {
            return shown;
        }

        try
        {
            BitmapImage picture = new();
            picture.BeginInit();
            picture.CacheOption = BitmapCacheOption.OnLoad;
            picture.StreamSource = new MemoryStream(bytes, writable: false);
            picture.EndInit();
            picture.Freeze();
            shown.Source = picture;
        }
        catch (Exception problem) when (problem is NotSupportedException or IOException or ArgumentException)
        {
            shown.ToolTip = "Immagine non leggibile";
        }

        return shown;
    }

    private Thumb Taller()
    {
        Thumb grip = Handle(Cursors.SizeNS, "Trascina per cambiare l'altezza", wide: false);
        grip.Height = Grip;

        grip.DragDelta += (_, drag) =>
        {
            double wanted = Math.Clamp(Height + drag.VerticalChange, InsetAddress.LeastHeight, InsetAddress.MostHeight);

            if (Math.Abs(wanted - Height) >= 0.5)
            {
                Height = wanted;
                Changed?.Invoke();
            }
        };

        Grid.SetRow(grip, 2);
        Grid.SetColumnSpan(grip, 2);

        return grip;
    }

    /// <summary>La larghezza e' della zattera, non della cornice: e' lei che decide il posto per le parole accanto.</summary>
    private Thumb Wider()
    {
        Thumb grip = Handle(Cursors.SizeWE, "Trascina per cambiare la larghezza", wide: true);
        grip.Width = Grip;

        grip.DragDelta += (_, drag) =>
        {
            if (Raft is not { } raft)
            {
                return;
            }

            double now = double.IsNaN(raft.Width) ? ActualWidth : raft.Width;
            double most = Page() is { } page ? Math.Min(InsetAddress.MostWidth, Room(page)) : InsetAddress.MostWidth;
            double wanted = Math.Clamp(now + drag.HorizontalChange, InsetAddress.LeastWidth, most);

            if (Math.Abs(wanted - now) >= 0.5)
            {
                raft.Width = wanted;
                Changed?.Invoke();
            }
        };

        Grid.SetRow(grip, 1);
        Grid.SetColumn(grip, 1);

        return grip;
    }

    /// <summary>Quanto e' larga una riga nel controllo che mostra la nota: oltre, la cornice sborderebbe.</summary>
    public static double Room(FrameworkElement page) =>
        Math.Max(InsetAddress.LeastWidth * 2, (Doc.Column(page) ?? page.ActualWidth - 46) - 14);

    private FrameworkElement? Page()
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

    private static Thumb Handle(Cursor pointer, string tip, bool wide)
    {
        FrameworkElementFactory line = new(typeof(Rectangle));
        line.SetValue(Rectangle.WidthProperty, wide ? 2d : 34d);
        line.SetValue(Rectangle.HeightProperty, wide ? 34d : 2d);
        line.SetValue(Rectangle.RadiusXProperty, 1d);
        line.SetValue(Rectangle.RadiusYProperty, 1d);
        line.SetResourceReference(Shape.FillProperty, "LabelQuaternary");
        line.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        line.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

        FrameworkElementFactory band = new(typeof(Border));
        band.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        band.AppendChild(line);

        return new Thumb
        {
            Cursor = pointer,
            ToolTip = tip,
            Template = new ControlTemplate(typeof(Thumb)) { VisualTree = band }
        };
    }

    private void EditChart()
    {
        if (AskForChart?.Invoke(ChartText(Inset)) is not { } written)
        {
            return;
        }

        Inset.Bytes = Encoding.UTF8.GetBytes(written);
        _chart!.Spec = ChartSpec.Read(written);
        Changed?.Invoke();
    }

    private static string ChartText(Inset inset) =>
        inset.Bytes.Length == 0 ? ChartSpec.Example : Encoding.UTF8.GetString(inset.Bytes);
}
