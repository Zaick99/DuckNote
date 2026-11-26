using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using DuckNote.Desktop.Ui;
using DuckNote.Notes.Sheets;

namespace DuckNote.Desktop.Pages;

/// <summary>
/// La barra di formattazione, nella fascia del titolo della finestra, descritta in una tabella
/// sola (<see cref="Tools"/>): da ogni riga nascono il pulsante, il tooltip con la scorciatoia e
/// la voce nel menu "Altro" (⋯) quando non c'e' posto. Le regole sono quelle della versione WPF:
/// gruppi per tipo di lavoro, interruttori che restano premuti dove vale il formato, comandi
/// spenti (non nascosti) quando non si possono usare, mai a capo.
/// </summary>
public sealed partial class NotesPage
{
    private sealed record Tool(string Group, string Name, Func<Control> Face, Action Run)
    {
        public string? Keys { get; init; }

        public string? Hint { get; init; }

        /// <summary>Interruttore: acceso dove sta il cursore (null = comando semplice).</summary>
        public Func<JsonNode, bool>? IsOn { get; init; }

        public Func<JsonNode, bool>? CanRun { get; init; }

        /// <summary>Comando con sottomenu: le voci, ognuna con la sua condizione.</summary>
        public IReadOnlyList<(string Name, Action Run, Func<JsonNode, bool> CanRun, Func<Control?>? Icon)>? Menu { get; init; }
    }

    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _more = new() { Width = 28, IsVisible = false };
    private readonly List<(Tool Tool, Control Button)> _tools = [];
    private readonly List<(StackPanel Panel, Tool[] Tools)> _groups = [];
    private readonly DockPanel _toolHost = new() { LastChildFill = false, ClipToBounds = true };
    private JsonNode _state = new JsonObject();

    /// <summary>La barra: la finestra la mette nella fascia del titolo.</summary>
    public Control Toolbar => _toolHost;

    private static string Mod => OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";

    private static string Alt => OperatingSystem.IsMacOS() ? "⌥" : "Alt+";

    private static string Shift => OperatingSystem.IsMacOS() ? "⇧" : "Maiusc+";

    private IReadOnlyList<Tool> Tools() =>
    [
        new("storia", "Annulla", () => Icon("undo"), () => Command("undo")) { Keys = $"{Mod}Z", CanRun = s => Flag(s, "canUndo") },
        new("storia", "Ripeti", () => Icon("redo"), () => Command("redo")) { Keys = $"{Mod}Y", CanRun = s => Flag(s, "canRedo") },

        LookTool("Testo normale", "T", 13, "text", $"{Mod}{Alt}0", "Toglie titolo, elenco o citazione dalla riga."),
        LookTool("Titolo", "H1", 13, "h1", $"{Mod}{Alt}1", "Oppure scrivi # e uno spazio a inizio riga. Va nell'indice dei fogli."),
        LookTool("Sottotitolo", "H2", 11.5, "h2", $"{Mod}{Alt}2", "Oppure scrivi ## e uno spazio a inizio riga. Va nell'indice dei fogli."),
        LookTool("Titolo minore", "H3", 10, "h3", $"{Mod}{Alt}3", "Oppure scrivi ### e uno spazio a inizio riga."),

        MarkTool("Grassetto", () => Letters("B", 13.5, FontWeight.Bold), "bold", $"{Mod}B"),
        MarkTool("Corsivo", () => Letters("I", 13.5, FontWeight.SemiBold, FontStyle.Italic), "italic", $"{Mod}I"),
        MarkTool("Sottolineato", () => Letters("U", 13, FontWeight.SemiBold, decorations: TextDecorations.Underline), "underline", $"{Mod}U"),
        MarkTool("Barrato", () => Letters("S", 13, FontWeight.SemiBold, decorations: TextDecorations.Strikethrough), "strike", $"{Mod}{Shift}X"),
        MarkTool("Evidenziato", () => Marker(), "highlight", $"{Mod}{Alt}H"),
        new("segni", "Colore del testo", () => Coloured(), () => { })
        {
            Hint = "Sulla selezione, sulla parola sotto il cursore o su quel che scrivi dopo. Segue il tema chiaro o scuro.",
            Menu = [.. ThemeColours.TextColours.Select(colour =>
                (colour.Name, (Action)(() => Command("colour", colour.Token)), (Func<JsonNode, bool>)(_ => true), (Func<Control?>?)(() => Swatch(colour.Token))))]
        },
        MarkTool("Codice nel testo", () => Letters("</>", 10, FontWeight.SemiBold, mono: true), "code", $"{Mod}E"),

        LookTool("Elenco puntato", "bullet", 0, "bullet", $"{Mod}{Shift}8", "Maiusc+Invio aggiunge una voce, Invio torna al testo normale. Oppure scrivi - e uno spazio a inizio riga."),
        LookTool("Elenco numerato", "number", 0, "number", $"{Mod}{Shift}7", "Maiusc+Invio aggiunge una voce, Invio torna al testo normale. Oppure scrivi 1. e uno spazio."),
        LookTool("Spunte", "todo", 0, "todo", $"{Mod}{Shift}9", "Clic sul quadratino per segnarla fatta. Maiusc+Invio aggiunge una spunta. Oppure scrivi [] e uno spazio."),

        LookTool("Citazione", "quote", 0, "quote", $"{Mod}{Shift}Q", "Oppure scrivi > e uno spazio a inizio riga."),
        new("blocchi", "Blocco di codice", () => Icon("code"), () => Command("codeBlock"))
        {
            Keys = $"{Mod}{Alt}C", IsOn = s => Text(s, "look") == "code",
            Hint = "Maiusc+Invio aggiunge una riga al blocco, Invio torna al testo normale. Oppure scrivi ``` e uno spazio."
        },
        new("blocchi", "Separazione", () => Icon("rule"), () => Command("rule")) { Keys = $"{Mod}{Shift}-", Hint = "Oppure scrivi --- e premi Invio." },
        new("blocchi", "Tabella", () => Icon("table"), () => { })
        {
            Menu =
            [
                ("Inserisci tabella", () => Command("table"), s => !Flag(s, "inTable"), null),
                ("Aggiungi riga sotto", () => Command("addRow"), s => Flag(s, "inTable"), null),
                ("Togli riga", () => Command("removeRow"), s => Flag(s, "inTable"), null),
                ("Aggiungi colonna a destra", () => Command("addColumn"), s => Flag(s, "inTable"), null),
                ("Togli colonna", () => Command("removeColumn"), s => Flag(s, "inTable"), null)
            ]
        },

        new("inserisci", "Collegamento", () => Icon("link"), () => Command("askLink"))
        {
            Keys = $"{Mod}K", IsOn = s => s["link"] is JsonValue,
            Hint = "Sul testo selezionato, o sul collegamento dove sta il cursore per cambiarlo."
        },
        new("inserisci", "Immagine", () => Icon("image"), PickPicture) { Hint = "Anche incollando o trascinando un'immagine nel foglio." },
        new("inserisci", "Disegno a mano libera", () => Icon("drawing"), NewDrawing),
        new("inserisci", "Grafico", () => Icon("chart"), NewChart),

        new("strumenti", "Togli la formattazione", () => Icon("clear"), () => Command("clear")) { Keys = $"{Mod}Spazio", Hint = "Toglie segni e tipo di riga dalla selezione." },
        new("strumenti", "Cerca e sostituisci", () => Icon("search"), () => Command("find")) { Keys = $"{Mod}F" }
    ];

    private Tool LookTool(string name, string face, double size, string look, string keys, string hint) =>
        new(Group(look), name, () => size > 0 ? Letters(face, size, FontWeight.SemiBold) : Icon(face), () => Command("look", look))
        {
            Keys = keys, Hint = hint, IsOn = s => Text(s, "look") == look
        };

    private Tool MarkTool(string name, Func<Control> face, string mark, string keys) =>
        new("segni", name, face, () => Command(mark))
        {
            Keys = keys, IsOn = s => s["marks"]?[mark] is JsonValue on && on.GetValue<bool>(),
            Hint = "Senza selezione vale per la parola sotto il cursore, o per quel che scrivi dopo."
        };

    private static string Group(string look) => look switch
    {
        "bullet" or "number" or "todo" => "elenchi",
        "quote" => "blocchi",
        _ => "riga"
    };

    private static bool Flag(JsonNode state, string name) => state[name] is JsonValue value && value.GetValue<bool>();

    private static string? Text(JsonNode state, string name) => state[name] is JsonValue value ? value.GetValue<string>() : null;

    private void Command(string name, string? argument = null) =>
        Call(argument is null ? $"dn.cmd({Js(name)})" : $"dn.cmd({Js(name)}, {Js(argument)})");

    // ------------------------------------------------------------------ costruzione

    private void BuildToolbar()
    {
        string? group = null;
        StackPanel? panel = null;
        List<Tool> members = [];

        foreach (Tool tool in Tools())
        {
            if (tool.Group != group)
            {
                if (panel is not null)
                {
                    _groups.Add((panel, [.. members]));
                }

                panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };

                if (group is not null)
                {
                    Rectangle sep = new() { Width = 1, Height = 18, Margin = new Thickness(5, 0), VerticalAlignment = VerticalAlignment.Center };
                    sep.Bind(Shape.FillProperty, sep.GetResourceObservable("Separator"));
                    panel.Children.Add(sep);
                }

                _strip.Children.Add(panel);
                group = tool.Group;
                members = [];
            }

            Control button;

            if (tool.IsOn is not null)
            {
                ToggleButton toggle = new() { Content = tool.Face(), Theme = Find("ToolToggle") };
                toggle.Click += (_, _) => Fire(tool, toggle);
                button = toggle;
            }
            else
            {
                Button plain = new() { Content = tool.Face(), Width = 28, Theme = Find("Tool") };
                plain.Click += (_, _) => Fire(tool, plain);
                button = plain;
            }

            ToolTip.SetTip(button, Tip(tool));
            ToolTip.SetShowDelay(button, 450);
            Avalonia.Automation.AutomationProperties.SetName(button, tool.Keys is null ? tool.Name : $"{tool.Name} ({tool.Keys})");
            panel!.Children.Add(button);
            _tools.Add((tool, button));
            members.Add(tool);
        }

        if (panel is not null)
        {
            _groups.Add((panel, [.. members]));
        }

        _more.Content = Icon("more");
        _more.Theme = Find("Tool");
        ToolTip.SetTip(_more, "Altri comandi");
        _more.Click += (_, _) => ShowMore();
        DockPanel.SetDock(_strip, Dock.Left);
        DockPanel.SetDock(_more, Dock.Left);
        _toolHost.Children.Add(_strip);
        _toolHost.Children.Add(_more);
        _toolHost.SizeChanged += (_, _) => Fit();
        ShowState(_state);
    }

    private static ControlTheme? Find(string key) => Themes.Of(key);

    private void Fire(Tool tool, Control button)
    {
        if (tool.Menu is { } items)
        {
            if (button is ToggleButton toggle)
            {
                toggle.IsChecked = false;
            }

            ContextMenu menu = new() { Placement = PlacementMode.Bottom };

            foreach ((string name, Action run, Func<JsonNode, bool> canRun, Func<Control?>? icon) in items)
            {
                MenuItem item = new() { Header = name, IsEnabled = !ReadOnly && canRun(_state), Icon = icon?.Invoke() };
                item.Click += (_, _) => run();
                menu.Items.Add(item);

                if (name == "Inserisci tabella")
                {
                    menu.Items.Add(new Separator());
                }
            }

            menu.Open(button);
            return;
        }

        tool.Run();
    }

    private bool ReadOnly => _ws.IsLocked || _ws.IsDamaged || _sheet is null;

    private static object Tip(Tool tool)
    {
        StackPanel tip = new() { MaxWidth = 280, Spacing = 2 };
        tip.Children.Add(new TextBlock { Text = tool.Name, FontWeight = FontWeight.SemiBold });

        if (tool.Keys is { } keys)
        {
            tip.Children.Add(new TextBlock { Text = keys, Opacity = 0.7 });
        }

        if (tool.Hint is { } hint)
        {
            tip.Children.Add(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 3, 0, 0) });
        }

        return tip;
    }

    // ------------------------------------------------------------------ stato

    /// <summary>Lo stato che manda l'editor (segni e tipo di riga dove sta il cursore, Annulla possibile...).</summary>
    private void ShowState(JsonNode state)
    {
        _state = state;
        bool writable = !ReadOnly;

        foreach ((Tool tool, Control button) in _tools)
        {
            button.IsEnabled = writable && (tool.CanRun?.Invoke(state) ?? true);

            if (button is ToggleButton toggle)
            {
                toggle.IsChecked = writable && tool.Menu is null && (tool.IsOn?.Invoke(state) ?? false);
            }
        }
    }

    // ------------------------------------------------------------------ niente a capo: il resto in "Altro"

    private void Fit()
    {
        double[] widths = new double[_groups.Count];

        for (int i = 0; i < _groups.Count; i++)
        {
            StackPanel panel = _groups[i].Panel;
            bool shown = panel.IsVisible;
            panel.IsVisible = true;
            panel.Measure(Size.Infinity);
            widths[i] = panel.DesiredSize.Width;
            panel.IsVisible = shown;
        }

        double room = _toolHost.Bounds.Width;
        bool all = widths.Sum() <= room;

        if (!all)
        {
            room -= 32;
        }

        double used = 0;
        bool full = false;

        for (int i = 0; i < _groups.Count; i++)
        {
            full |= used + widths[i] > room;
            _groups[i].Panel.IsVisible = !full;
            used += full ? 0 : widths[i];
        }

        _more.IsVisible = !all;
    }

    private void ShowMore()
    {
        ContextMenu menu = new() { Placement = PlacementMode.Bottom };
        bool writable = !ReadOnly;

        foreach ((StackPanel panel, Tool[] tools) in _groups.Where(group => !group.Panel.IsVisible))
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            foreach (Tool tool in tools)
            {
                MenuItem item = new()
                {
                    Header = tool.Name,
                    Icon = tool.Face(),
                    InputGesture = null,
                    IsEnabled = writable && (tool.CanRun?.Invoke(_state) ?? true)
                };

                if (tool.Keys is { } keys)
                {
                    item.Header = $"{tool.Name}    {keys}";
                }

                if (tool.Menu is { } items)
                {
                    foreach ((string name, Action run, Func<JsonNode, bool> canRun, Func<Control?>? icon) in items)
                    {
                        MenuItem sub = new() { Header = name, IsEnabled = writable && canRun(_state), Icon = icon?.Invoke() };
                        sub.Click += (_, _) => run();
                        item.Items.Add(sub);
                    }
                }
                else
                {
                    item.Click += (_, _) => tool.Run();
                }

                menu.Items.Add(item);
            }
        }

        menu.Open(_more);
    }

    /// <summary>Alt+F10, come in Word: il primo pulsante attivo della barra prende il fuoco.</summary>
    private void FocusToolbar() =>
        _tools.Select(pair => pair.Button).FirstOrDefault(button => button.IsEnabled && button.IsEffectivelyVisible)?.Focus();

    // ------------------------------------------------------------------ facce dei pulsanti

    private static Control Icon(string name) => new Glyph { Kind = name, Width = 14, Height = 14 };

    private static Control Letters(string text, double size, FontWeight weight, FontStyle style = FontStyle.Normal,
        TextDecorationCollection? decorations = null, bool mono = false)
    {
        TextBlock letters = new()
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            FontStyle = style,
            TextDecorations = decorations,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (mono && Application.Current?.TryFindResource("Mono", out object? family) == true && family is FontFamily font)
        {
            letters.FontFamily = font;
        }

        return letters;
    }

    /// <summary>La "A" del colore del testo, con sotto una barretta a colori.</summary>
    private static Control Coloured()
    {
        StackPanel face = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        face.Children.Add(new TextBlock { Text = "A", FontSize = 12.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) });
        face.Children.Add(new Border
        {
            Height = 3,
            Width = 14,
            CornerRadius = new CornerRadius(1.5),
            Margin = new Thickness(0, 1, 0, 0),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Colors.OrangeRed, 0), new GradientStop(Colors.Gold, 0.35),
                    new GradientStop(Colors.MediumSeaGreen, 0.65), new GradientStop(Colors.DodgerBlue, 1)
                }
            }
        });
        return face;
    }

    private static Control Swatch(string token)
    {
        Ellipse dot = new() { Width = 12, Height = 12, StrokeThickness = 1 };
        dot.Bind(Shape.FillProperty, dot.GetResourceObservable(token));
        dot.Bind(Shape.StrokeProperty, dot.GetResourceObservable("Separator"));
        return dot;
    }

    private static Control Marker()
    {
        TextBlock letter = new() { Text = "A", FontSize = 12.5, FontWeight = FontWeight.SemiBold };
        letter.Bind(TextBlock.ForegroundProperty, letter.GetResourceObservable("HighlightFg"));
        Border chip = new() { CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0), Child = letter, VerticalAlignment = VerticalAlignment.Center };
        chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable("Highlight"));
        return chip;
    }
}
