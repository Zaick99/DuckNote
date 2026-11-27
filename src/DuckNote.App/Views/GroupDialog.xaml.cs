using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DuckNote.App.Dialogs;
using DuckNote.App.Theme;
using DuckNote.App.Ui;
using DuckNote.Notes.Groups;

namespace DuckNote.App.Views;

/// <summary>Cosa ha scelto la finestra del gruppo: il gruppo salvato, o da eliminare.</summary>
public sealed record GroupChoice(ScanGroup Group, bool Removed);

/// <summary>
/// Un gruppo di scansione: il nome, le reti, gli intervalli e gli indirizzi (uno per riga) e se
/// il monitoraggio ne controlla gli host. Sotto ai bersagli si vede subito quanti indirizzi
/// coprono, o cosa non va nel primo scritto male.
/// </summary>
public partial class GroupDialog : Window
{
    /// <summary>Quanti indirizzi prende al massimo un'analisi (come TargetList).</summary>
    private const int MostScanned = 65536;

    private readonly ScanGroup? _group;
    private readonly IReadOnlyList<ScanGroup> _others;
    private GroupChoice? _choice;

    private GroupDialog(ScanGroup? group, IReadOnlyList<ScanGroup> others, string suggested)
    {
        InitializeComponent();
        _group = group;
        _others = others;

        bool editing = group is not null;
        Head.Text = editing ? "Modifica il gruppo" : "Nuovo gruppo";
        BtnRemove.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        BtnSave.Content = editing ? "Salva" : "Crea il gruppo";
        NameBox.Text = group?.Name ?? string.Empty;
        TargetsBox.Text = group is null ? string.Join(Environment.NewLine, ScanGroups.Split(suggested)) : string.Join(Environment.NewLine, group.Targets);
        MonitorBox.IsChecked = group?.Monitor ?? false;
        Describe();

        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                OnSave(this, new RoutedEventArgs());
            }
        };

        Loaded += (_, _) =>
        {
            Motion.OpenDialog(Root);
            (editing || NameBox.Text.Length > 0 ? TargetsBox : NameBox).Focus();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox)
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

    public static GroupChoice? Ask(Window? owner, ScanGroup? group, IReadOnlyList<ScanGroup> others, string suggested = "")
    {
        GroupDialog dialog = new(group, others, suggested);

        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog._choice : null;
    }

    private void OnTargets(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            Describe();
        }
    }

    private void Describe()
    {
        IReadOnlyList<string> list = ScanGroups.Split(TargetsBox.Text);
        string? wrong = list.Select(ScanGroups.Problem).FirstOrDefault(text => text is not null);
        ProblemText.Text = wrong ?? string.Empty;
        Problem.Visibility = wrong is null ? Visibility.Collapsed : Visibility.Visible;
        SummaryText.Text = Summary(list);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string title = NameBox.Text.Trim();
        IReadOnlyList<string> list = ScanGroups.Split(TargetsBox.Text);

        if (title.Length == 0)
        {
            Complain("Dai un nome al gruppo.", NameBox);
            return;
        }

        if (title.Equals("Nota", StringComparison.OrdinalIgnoreCase) || title.Equals("Ricerca", StringComparison.OrdinalIgnoreCase)
            || title.Equals("Tutti i gruppi", StringComparison.OrdinalIgnoreCase)
            || _others.Any(other => other.Id != _group?.Id && other.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
        {
            Complain($"C'e' gia' un gruppo che si chiama «{title}».", NameBox);
            return;
        }

        if (list.Select(ScanGroups.Problem).FirstOrDefault(text => text is not null) is { } wrong)
        {
            Complain(wrong, TargetsBox);
            return;
        }

        _choice = new GroupChoice(new ScanGroup(_group?.Id ?? ScanGroups.NewId(), title, list, MonitorBox.IsChecked == true), Removed: false);
        Motion.CloseDialog(this, Root, true);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (_group is null || !DuckDialog.Confirm(this, $"Eliminare «{_group.Name}»?",
                "Il gruppo sparisce dalla nota e dalla mappa; gli host gia' trovati restano in tabella.", "Elimina", danger: true))
        {
            return;
        }

        _choice = new GroupChoice(_group, Removed: true);
        Motion.CloseDialog(this, Root, true);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Motion.CloseDialog(this, Root, false);

    private void Complain(string message, FrameworkElement field)
    {
        ProblemText.Text = message;
        Problem.Visibility = Visibility.Visible;
        field.Focus();
        Fx.Shake(field);
    }

    /// <summary>"2 reti e intervalli (306 indirizzi) · 3 indirizzi singoli", e un avviso se un'analisi non li prende tutti.</summary>
    public static string Summary(IReadOnlyList<string> targets)
    {
        if (targets.Count == 0)
        {
            return "Ancora vuoto: si puo' creare e riempire dopo.";
        }

        CultureInfo culture = CultureInfo.CurrentCulture;
        List<string> good = [.. targets.Where(target => ScanGroups.Problem(target) is null)];
        TargetMatcher matcher = new(good);
        int ranges = good.Count(target => TargetMatcher.Range(target) is not null);
        List<string> parts = [];

        if (ranges > 0)
        {
            parts.Add(string.Create(culture, $"{ranges} {(ranges == 1 ? "rete o intervallo" : "reti e intervalli")} ({matcher.Addresses:N0} indirizzi)"));
        }

        if (matcher.Singles.Count > 0)
        {
            parts.Add(string.Create(culture, $"{matcher.Singles.Count} {(matcher.Singles.Count == 1 ? "indirizzo singolo" : "indirizzi singoli")}"));
        }

        string text = string.Join(" · ", parts);

        return matcher.Addresses + matcher.Singles.Count > MostScanned
            ? text + string.Create(culture, $". Troppi per un'analisi sola (al massimo {MostScanned:N0}): il gruppo raccoglie gli host trovati, Analizza va su reti piu' strette.")
            : text + ".";
    }
}
