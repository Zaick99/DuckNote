using System.Security;
using System.Windows;
using System.Windows.Controls;
using DuckNote.App.Theme;
using DuckNote.App.Vault;
using DuckNote.Notes.Security;

namespace DuckNote.App.Views;

/// <summary>
/// La barra della robustezza sotto la casella della password: lunghezza per alfabeto. Misura la
/// lunghezza, non l'imprevedibilita' (lo dice la riga sotto).
/// </summary>
public sealed class StrengthBar : StackPanel
{
    private readonly Border _track = new() { Height = 5, CornerRadius = new CornerRadius(3) };
    private readonly Border _fill = new() { Height = 5, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly TextBlock _words = new();

    public StrengthBar()
    {
        _track.SetResourceReference(Border.BackgroundProperty, "BgPressed");
        _track.Child = _fill;
        _words.Style = (Style)Application.Current.FindResource("TxtCaption");
        _words.Margin = new Thickness(0, 5, 0, 0);
        _words.Text = "Quattro parole tirate a sorte valgono piu' di una parola con simboli.";

        Children.Add(_track);
        Children.Add(_words);
    }

    public void Show(SecureString password)
    {
        int bits = Passwords.Bits(password);
        (string token, string word) = PasswordStrength.Describe(bits);

        _fill.SetResourceReference(Border.BackgroundProperty, token);
        double full = Math.Max(60, _track.ActualWidth);
        _fill.BeginAnimation(WidthProperty, Motion.Slide(_fill.ActualWidth, full * Math.Min(1, bits / 110.0), 260, Motion.Ease()));
        _words.Text = bits == 0
            ? "Quattro parole tirate a sorte valgono piu' di una parola con simboli."
            : $"{bits} bit, {word}. Misura la lunghezza, non l'imprevedibilita': usa i dadi, non l'ispirazione.";
    }
}
