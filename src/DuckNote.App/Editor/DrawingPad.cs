using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;

namespace DuckNote.App.Editor;

/// <summary>
/// Il foglio su cui si disegna col mouse.
///
/// Carta e inchiostro non seguono il tema: un disegno fatto di notte si deve
/// rivedere di giorno, e il colore dei tratti resta scritto dentro l'ISF
/// salvato, dove nessun cambio di tema lo raggiunge piu'.
/// </summary>
public sealed class DrawingPad : Grid
{
    private static readonly Color Paper = Color.FromRgb(0xF7, 0xF8, 0xFA);
    private static readonly Color Ink = Color.FromRgb(0x33, 0x3A, 0x45);

    private static readonly Color[] Palette =
    [
        Ink,
        Color.FromRgb(0x2E, 0x6B, 0xE6),
        Color.FromRgb(0x1F, 0x9D, 0x45),
        Color.FromRgb(0xC0, 0x39, 0x2B),
        Color.FromRgb(0xFF, 0x8C, 0x00),
        Color.FromRgb(0x6D, 0x5F, 0xD6)
    ];

    private static readonly double[] Nibs = [1.6, 3.2, 7];

    private readonly InkCanvas _sheet = new()
    {
        Background = new SolidColorBrush(Paper),
        EditingMode = InkCanvasEditingMode.Ink
    };

    private readonly StackPanel _tools = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 4, 0)
    };

    private int _nib;

    public DrawingPad()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _sheet.DefaultDrawingAttributes.Color = Ink;
        _sheet.DefaultDrawingAttributes.Width = Nibs[0];
        _sheet.DefaultDrawingAttributes.Height = Nibs[0];
        _sheet.DefaultDrawingAttributes.FitToCurve = true;

        Watch();
        BuildTools();

        SetRow(_tools, 0);
        SetRow(_sheet, 1);
        Children.Add(_tools);
        Children.Add(_sheet);
    }

    public event Action? Changed;

    public byte[] Save()
    {
        using MemoryStream stream = new();
        _sheet.Strokes.Save(stream);

        return stream.ToArray();
    }

    public void Load(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            _sheet.Strokes = new StrokeCollection(stream);
        }
        catch (Exception problem) when (problem is ArgumentException or IOException)
        {
            _sheet.Strokes = [];
        }

        Watch();
    }

    private void Watch() => _sheet.Strokes.StrokesChanged += (_, _) => Changed?.Invoke();

    private void BuildTools()
    {
        foreach (Color colour in Palette)
        {
            _tools.Children.Add(Swatch(colour));
        }

        _tools.Children.Add(Chips.Make("spessore", Thicker));
        _tools.Children.Add(Chips.Make("gomma", Erase));
        _tools.Children.Add(Chips.Make("svuota", () => _sheet.Strokes.Clear()));
    }

    private void Thicker()
    {
        _nib = (_nib + 1) % Nibs.Length;
        _sheet.EditingMode = InkCanvasEditingMode.Ink;
        _sheet.DefaultDrawingAttributes.Width = Nibs[_nib];
        _sheet.DefaultDrawingAttributes.Height = Nibs[_nib];
    }

    private void Erase() =>
        _sheet.EditingMode = _sheet.EditingMode == InkCanvasEditingMode.EraseByStroke
            ? InkCanvasEditingMode.Ink
            : InkCanvasEditingMode.EraseByStroke;

    private Button Swatch(Color colour)
    {
        Button pick = new()
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 3, 0),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(colour),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Colore del tratto"
        };

        pick.Click += (_, _) =>
        {
            _sheet.EditingMode = InkCanvasEditingMode.Ink;
            _sheet.DefaultDrawingAttributes.Color = colour;
        };

        return pick;
    }

}
