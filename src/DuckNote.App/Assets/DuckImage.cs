using System.IO;
using System.Windows.Media.Imaging;

namespace DuckNote.App.Assets;

public static class DuckImage
{
    private const string Pack = "pack://application:,,,/DuckNote;component/Assets/duck.png";

    private static readonly Lazy<BitmapImage?> Cached = new(Load);

    public static BitmapImage? Bitmap => Cached.Value;

    private static BitmapImage? Load()
    {
        try
        {
            BitmapImage duck = new();
            duck.BeginInit();
            duck.UriSource = new Uri(Pack);
            duck.CacheOption = BitmapCacheOption.OnLoad;
            duck.EndInit();
            duck.Freeze();
            return duck;
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException)
        {
            return null;
        }
    }
}
