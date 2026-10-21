using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using DuckNote.App.Theme;

namespace DuckNote.App.Editor;

/// <summary>
/// Il foglio su cui si disegna col mouse. Carta e inchiostro non seguono il tema: un disegno
/// fatto di notte si deve rivedere di giorno, e il colore dei tratti resta scritto nell'ISF.
/// Per questo le tinte si prendono sempre dalla tavolozza chiara.
/// </summary>
public sealed class SketchPad : Grid
{
    private static readonly string[] Inks = ["Label", "Blue", "Green", "Red", "Orange", "Purple"];
    private static readonly double[] Nibs = [1.6, 3.2, 7];

    private readonly InkCanvas _sheet = new() { EditingMode = InkCanvasEditingMode.Ink };
    private int _nib;

    public SketchPad(byte[] bytes)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _sheet.Background = new SolidColorBrush(Fixed("Paper"));
        _sheet.DefaultDrawingAttributes.Color = Fixed(Inks[0]);
        _sheet.DefaultDrawingAttributes.Width = Nibs[0];
        _sheet.DefaultDrawingAttributes.Height = Nibs[0];
        _sheet.DefaultDrawingAttributes.FitToCurve = true;

        Load(bytes);

        StackPanel tools = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 4) };

        foreach (string ink in Inks)
        {
            tools.Children.Add(Swatch(Fixed(ink)));
        }

        tools.Children.Add(Chips.Icon("", "Spessore del tratto", Thicker));
        tools.Children.Add(Chips.Icon("", "Gomma: tocca un tratto per toglierlo", Erase));
        tools.Children.Add(Chips.Icon("", "Svuota il foglio", () => _sheet.Strokes.Clear()));

        SetRow(_sheet, 1);
        Children.Add(tools);
        Children.Add(_sheet);
    }

    public event Action? Changed;

    public byte[] Save()
    {
        using MemoryStream stream = new();
        _sheet.Strokes.Save(stream);

        return stream.ToArray();
    }

    private void Load(byte[] bytes)
    {
        if (bytes.Length > 0)
        {
            try
            {
                using MemoryStream stream = new(bytes, writable: false);
                _sheet.Strokes = new StrokeCollection(stream);
            }
            catch (Exception problem) when (problem is ArgumentException or IOException)
            {
                _sheet.Strokes = [];
            }
        }

        _sheet.Strokes.StrokesChanged += (_, _) => Changed?.Invoke();
    }

    private static Color Fixed(string token) => ThemeTokens.Parse(ThemeTokens.Colour("light", token));

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

    private Border Swatch(Color colour)
    {
        Border dot = new()
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 5, 0),
            Background = new SolidColorBrush(colour),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Colore del tratto",
            VerticalAlignment = VerticalAlignment.Center
        };

        dot.SetResourceReference(Border.BorderBrushProperty, "BorderControl");
        dot.BorderThickness = new Thickness(1);
        dot.MouseLeftButtonUp += (_, _) =>
        {
            _sheet.EditingMode = InkCanvasEditingMode.Ink;
            _sheet.DefaultDrawingAttributes.Color = colour;
        };

        return dot;
    }
}
