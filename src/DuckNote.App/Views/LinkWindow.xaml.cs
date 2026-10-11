using System.Windows;
using System.Windows.Input;
using DuckNote.App.Theme;

namespace DuckNote.App.Views;

public partial class LinkWindow : Window
{
    public LinkWindow(string text, string address, string theme, ResourceDictionary ownerResources)
    {
        InitializeComponent();

        Resources.MergedDictionaries.Add(ownerResources);
        ThemeTokens.Apply(Resources, theme);

        LText.Text = text.Length > 0 ? text : "(nessun testo selezionato)";
        LUrl.Text = address;

        bool editing = address.Length > 0;
        LHead.Text = editing ? "Modifica il collegamento" : "Aggiungi un collegamento";
        LDrop.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        ContentRendered += (_, _) =>
        {
            Motion.OpenDialog(LRoot);
            LUrl.Focus();
            LUrl.SelectAll();
        };

        LTitleBar.MouseLeftButtonDown += (_, _) =>
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };

        LClose.Click += (_, _) => Dismiss();
        LCancel.Click += (_, _) => Dismiss();
        LOk.Click += (_, _) => Accept();
        LDrop.Click += (_, _) => Erase();

        LUrl.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Dismiss();
            }
        };
    }

    public string Address { get; private set; } = string.Empty;

    public bool Removed { get; private set; }

    private void Accept()
    {
        if (LUrl.Text.Trim().Length == 0)
        {
            Complain("Scrivi un indirizzo, oppure togli il collegamento.");
            return;
        }

        if (Editor.WebAddress.Parse(LUrl.Text) is not { } parsed)
        {
            Complain("Indirizzo non valido: servono http, https oppure mailto.");
            return;
        }

        Address = parsed.ToString();
        Motion.CloseDialog(this, LRoot, true);
    }

    private void Erase()
    {
        Removed = true;
        Motion.CloseDialog(this, LRoot, true);
    }

    private void Dismiss() => Motion.CloseDialog(this, LRoot, false);

    private void Complain(string message)
    {
        LError.Text = message;
        LError.Visibility = Visibility.Visible;
        Motion.Shake(LMove);
        LUrl.Focus();
    }
}
