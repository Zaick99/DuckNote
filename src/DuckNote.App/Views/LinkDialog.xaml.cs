using System.Windows;
using System.Windows.Input;
using DuckNote.App.Editor;
using DuckNote.App.Theme;
using DuckNote.App.Ui;
using DuckNote.Notes.Links;

namespace DuckNote.App.Views;

/// <summary>
/// Il collegamento: il testo da un lato, l'indirizzo dall'altro, e il modo di toglierlo che
/// lascia il testo dov'e'. Si accettano solo http, https e mailto.
/// </summary>
public partial class LinkDialog : Window
{
    private LinkChoice? _choice;

    private LinkDialog(string shown, string address)
    {
        InitializeComponent();

        bool editing = address.Length > 0;
        Head.Text = editing ? "Modifica il collegamento" : "Aggiungi un collegamento";
        BtnRemove.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        // con del testo scelto il collegamento si appoggia a quello: cambiarlo qui sarebbe un'altra cosa
        ShownBox.Text = shown;
        ShownBox.IsReadOnly = shown.Length > 0;
        AddressBox.Text = address;

        if (!ShownBox.IsReadOnly)
        {
            Hint.Text = "Niente di selezionato: scrivi il testo da mostrare e il suo indirizzo.";
        }

        Loaded += (_, _) =>
        {
            Motion.OpenDialog(Root);
            (ShownBox.IsReadOnly ? AddressBox : ShownBox).Focus();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }
        };
    }

    public static LinkChoice? Ask(Window? owner, string shown, string address)
    {
        LinkDialog dialog = new(shown, address);

        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog._choice : null;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!ShownBox.IsReadOnly && ShownBox.Text.Trim().Length == 0)
        {
            Complain("Scrivi il testo da mostrare nella nota.", ShownBox);
            return;
        }

        if (WebAddress.Parse(AddressBox.Text) is not { } parsed)
        {
            Complain(AddressBox.Text.Trim().Length == 0
                ? "Scrivi un indirizzo, oppure togli il collegamento."
                : "Indirizzo non valido: servono http, https oppure mailto.", AddressBox);
            return;
        }

        _choice = new LinkChoice(parsed.ToString(), ShownBox.Text.Trim(), Removed: false);
        Motion.CloseDialog(this, Root, true);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        _choice = new LinkChoice(string.Empty, string.Empty, Removed: true);
        Motion.CloseDialog(this, Root, true);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Motion.CloseDialog(this, Root, false);

    private void Complain(string message, FrameworkElement field)
    {
        ProblemText.Text = message;
        Problem.Visibility = Visibility.Visible;
        Fx.Shake(field);
    }
}
