using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DuckNote.Desktop.Ui;
using DuckNote.Notes.Groups;

namespace DuckNote.Desktop.Views;

/// <summary>Cosa ha scelto la finestra del gruppo: il gruppo salvato, o da eliminare.</summary>
public sealed record GroupChoice(ScanGroup Group, bool Removed);

/// <summary>
/// Un gruppo di scansione: il nome, le reti, gli intervalli e gli indirizzi (uno per riga) e se
/// il monitoraggio ne controlla gli host. Sotto ai bersagli si vede subito quanti indirizzi
/// coprono, o cosa non va nel primo scritto male.
/// </summary>
public static class GroupDialog
{
    /// <summary>Quanti indirizzi prende al massimo un'analisi (come TargetList).</summary>
    private const int MostScanned = 65536;

    public static async Task<GroupChoice?> Ask(Window? owner, ScanGroup? group, IReadOnlyList<ScanGroup> others, string suggested = "")
    {
        if (owner is null)
        {
            return null;
        }

        GroupChoice? choice = null;
        bool editing = group is not null;
        Window window = Dialog.Make(editing ? "Modifica il gruppo" : "Nuovo gruppo", 500);

        TextBox name = new() { Text = group?.Name ?? string.Empty, PlaceholderText = "Ufficio, Server, Laboratorio…", MaxLength = 60 };
        TextBox targets = new()
        {
            Text = group is null ? string.Join('\n', ScanGroups.Split(suggested)) : string.Join('\n', group.Targets),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 150,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(10, 8),
            PlaceholderText = "192.168.10.0/24\n192.168.20.1-50\n10.0.0.5\nnas01.example.local",
            FontFamily = Application.Current?.TryFindResource("Mono", out object? mono) == true && mono is FontFamily family ? family : FontFamily.Default
        };

        TextBlock summary = Dialog.Caption(string.Empty);
        TextBlock problem = Dialog.Tinted(new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(2, 6, 0, 0) }, "Red");

        OnOff monitor = new() { IsChecked = group?.Monitor ?? false, VerticalAlignment = VerticalAlignment.Center };
        Grid monitorRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 16, 0, 0) };
        monitorRow.Children.Add(new StackPanel
        {
            Children =
            {
                Dialog.Tinted(new TextBlock { Text = "Monitora", FontWeight = FontWeight.SemiBold }, "Label"),
                Dialog.Caption("Gli host del gruppo si controllano da soli, come quelli della nota: gli indirizzi singoli subito, quelli delle reti dopo la prima analisi.")
            }
        });
        Grid.SetColumn(monitor, 1);
        monitor.Margin = new Thickness(16, 0, 0, 0);
        monitorRow.Children.Add(monitor);

        void Describe()
        {
            IReadOnlyList<string> list = ScanGroups.Split(targets.Text);
            string? wrong = list.Select(ScanGroups.Problem).FirstOrDefault(text => text is not null);
            problem.Text = wrong ?? string.Empty;
            problem.IsVisible = wrong is not null;
            targets.Classes.Set("error", wrong is not null);
            summary.Text = Summary(list);
        }

        targets.TextChanged += (_, _) => Describe();
        Describe();

        void Save()
        {
            string title = (name.Text ?? string.Empty).Trim();
            IReadOnlyList<string> list = ScanGroups.Split(targets.Text);

            if (title.Length == 0)
            {
                Refuse("Dai un nome al gruppo.", name);
                return;
            }

            if (title.Equals("Nota", StringComparison.OrdinalIgnoreCase) || title.Equals("Ricerca", StringComparison.OrdinalIgnoreCase)
                || title.Equals("Tutti i gruppi", StringComparison.OrdinalIgnoreCase)
                || others.Any(other => other.Id != group?.Id && other.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
            {
                Refuse($"C'e' gia' un gruppo che si chiama «{title}».", name);
                return;
            }

            if (list.Select(ScanGroups.Problem).FirstOrDefault(text => text is not null) is { } wrong)
            {
                Refuse(wrong, targets);
                return;
            }

            choice = new GroupChoice(new ScanGroup(group?.Id ?? ScanGroups.NewId(), title, list, monitor.IsChecked == true), Removed: false);
            window.Close();
        }

        void Refuse(string text, Control field)
        {
            problem.Text = text;
            problem.IsVisible = true;
            field.Focus();
            Kit.Shake(field);
        }

        name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Save();
                e.Handled = true;
            }
        };

        List<Control> buttons = [];

        if (group is not null)
        {
            buttons.Add(Dialog.Button("Elimina il gruppo", "Danger", async () =>
            {
                if (await Message.Ask(window, $"Eliminare «{group.Name}»?",
                        "Il gruppo sparisce dalla nota e dalla mappa; gli host gia' trovati restano in tabella.", "Elimina", danger: true))
                {
                    choice = new GroupChoice(group, Removed: true);
                    window.Close();
                }
            }));
        }

        buttons.Add(Dialog.Button("Annulla", "Quiet", window.Close));
        buttons.Add(Dialog.Button(editing ? "Salva" : "Crea il gruppo", "Primary", Save));

        window.Content = new StackPanel
        {
            Margin = new Thickness(24, 22),
            Children =
            {
                Dialog.Heading(window.Title ?? string.Empty),
                Dialog.Caption("Reti, intervalli e indirizzi da analizzare insieme. Il gruppo si salva nella nota (e nella cassaforte, se la nota e' cifrata)."),
                Dialog.Label("NOME"), name,
                Dialog.Label("RETI E INDIRIZZI, UNO PER RIGA"), targets,
                summary,
                problem,
                monitorRow,
                Dialog.Buttons([.. buttons])
            }
        };

        window.Opened += (_, _) => (editing || name.Text?.Length > 0 ? targets : name).Focus();
        await window.ShowDialog(owner);
        return choice;
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
