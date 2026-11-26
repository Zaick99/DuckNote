using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DuckNote.Desktop.Ui;

/// <summary>Le illustrazioni della versione WPF (Assets), caricate una volta sola.</summary>
public static class Art
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public static Bitmap? Of(string path)
    {
        if (Cache.TryGetValue(path, out Bitmap? known))
        {
            return known;
        }

        Bitmap? loaded = null;

        try
        {
            using Stream stream = AssetLoader.Open(new Uri($"avares://DuckNote/Assets/{path}"));
            loaded = new Bitmap(stream);
        }
        catch (Exception problem) when (problem is IOException or ArgumentException or FileNotFoundException)
        {
            // un'illustrazione mancante non ferma la finestra
        }

        Cache[path] = loaded;
        return loaded;
    }

    public static Image Picture(string path, double size)
    {
        Image image = new() { Source = Of(path), Width = size, Height = size };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        return image;
    }
}

/// <summary>Da un token del tema ("Green", "Red"...) al pennello che lo dipinge adesso.</summary>
public sealed class TokenBrush : IValueConverter
{
    public static readonly TokenBrush Instance = new();

    public static IBrush Of(string? token) =>
        token is { Length: > 0 } && Application.Current?.TryFindResource(token, out object? brush) == true && brush is IBrush paint
            ? paint
            : Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Of(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
