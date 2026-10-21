using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DuckNote.App.Editor;

namespace DuckNote.App.Pages;

/// <summary>
/// La barra di formattazione, descritta in una tabella sola (<see cref="Tools"/>): per
/// aggiungere, togliere o spostare un comando basta toccare una riga. Dalla stessa riga nascono
/// il pulsante, il tooltip con la scorciatoia, la scorciatoia vera e la voce nel menu "Altro".
///
/// I criteri seguiti, comando per comando:
/// - gruppi per tipo di lavoro, separati da una linea (storia, tipo di riga, segni sul testo,
///   elenchi, blocchi, inserimenti, strumenti), nello stesso ordine di Word e Docs;
/// - chi si accende e spegne (grassetto, titoli, elenchi...) e' un interruttore che resta
///   premuto finche' vale dove sta il cursore, con un aspetto diverso dal passaggio del mouse;
/// - ogni pulsante a sola icona ha un tooltip con il nome e la scorciatoia, e ogni comando
///   frequente ha una scorciatoia (quelle di Word e Docs, dove esistono);
/// - quello che non si puo' fare e' spento, non nascosto (Annulla senza nulla da annullare,
///   le righe di una tabella fuori dalla tabella, tutto a nota chiusa);
/// - la barra non va mai a capo: quello che non ci sta finisce nel menu "Altro" (⋯);
/// - da tastiera: Alt+F10 porta sulla barra, le frecce si spostano, Invio/Spazio eseguono,
///   Esc torna al testo.
/// </summary>
public partial class NotesPage
{
    /// <summary>Una riga della tabella della barra.</summary>
    private sealed record Tool(string Group, string Name, Func<FrameworkElement> Face, Action Run)
    {
        public KeyGesture? Keys { get; init; }

        /// <summary>Una riga in piu' nel tooltip, per i comandi che non si spiegano col nome.</summary>
        public string? Hint { get; init; }

        /// <summary>Interruttore: acceso dove sta il cursore? (null = comando semplice)</summary>
        public Func<bool>? IsOn { get; init; }

        public Func<bool>? CanRun { get; init; }

        /// <summary>Comando con sottomenu (la tabella): le voci, ognuna con la sua condizione.</summary>
        public IReadOnlyList<(string Name, Action Run, Func<bool> CanRun, Func<FrameworkElement>? Icon)>? Menu { get; init; }
    }

    private const ModifierKeys Ctrl = ModifierKeys.Control;
    private const ModifierKeys Shift = ModifierKeys.Shift;
    private const ModifierKeys Alt = ModifierKeys.Alt;

    private readonly List<(Tool Tool, ButtonBase Button)> _tools = [];
    private readonly List<(StackPanel Panel, Tool[] Tools)> _groups = [];
    private bool _toolsDirty;

    private IReadOnlyList<Tool> Tools() =>
    [
        new("storia", "Annulla", () => Glyph("\uE7A7"), Do(() => Editor.Undo())) { Keys = new(Key.Z, Ctrl), CanRun = () => Editor.CanUndo },
        new("storia", "Ripeti", () => Glyph("\uE7A6"), Do(() => Editor.Redo())) { Keys = new(Key.Y, Ctrl), CanRun = () => Editor.CanRedo },

        LookTool("Testo normale", () => Letters("T", 13), Look.Text, new(Key.D0, Ctrl | Alt), "Toglie titolo, elenco o citazione dalla riga.", plain: true),
        LookTool("Titolo", () => Letters("H1", 13.5), Look.Heading1, new(Key.D1, Ctrl | Alt), "Oppure scrivi # e uno spazio a inizio riga. Va nell'indice dei fogli."),
        LookTool("Sottotitolo", () => Letters("H2", 12), Look.Heading2, new(Key.D2, Ctrl | Alt), "Oppure scrivi ## e uno spazio a inizio riga. Va nell'indice dei fogli."),
        LookTool("Titolo minore", () => Letters("H3", 10.5), Look.Heading3, new(Key.D3, Ctrl | Alt), "Oppure scrivi ### e uno spazio a inizio riga."),

        MarkTool("Grassetto", () => Glyph("\uE8DD"), Mark.Bold, new(Key.B, Ctrl)),
        MarkTool("Corsivo", () => Glyph("\uE8DB"), Mark.Italic, new(Key.I, Ctrl)),
        MarkTool("Sottolineato", () => Glyph("\uE8DC"), Mark.Underline, new(Key.U, Ctrl)),
        MarkTool("Barrato", () => Struck(), Mark.Strike, new(Key.X, Ctrl | Shift)),
        MarkTool("Evidenziato", () => Marker(), Mark.Highlight, new(Key.H, Ctrl | Alt)),
        new("segni", "Colore del testo", () => Coloured(), () => { })
        {
            Hint = "Sulla selezione, sulla parola sotto il cursore o su quel che scrivi dopo. Segue il tema chiaro o scuro.",
            Menu = [.. Palette.TextColours.Select(colour =>
                (colour.Name, Do(() => _commands.Colour(colour.Token)), (Func<bool>)(() => true), (Func<FrameworkElement>?)(() => Swatch(colour.Token))))]
        },
        MarkTool("Codice nel testo", () => Letters("</>", 10.5, mono: true), Mark.Code, new(Key.E, Ctrl)),

        LookTool("Elenco puntato", () => Glyph("\uE8FD"), Look.Bullet, new(Key.D8, Ctrl | Shift), "Maiusc+Invio aggiunge una voce, Invio torna al testo normale. Oppure scrivi - e uno spazio a inizio riga."),
        LookTool("Elenco numerato", () => Drawn("M1,1 H2 V5 M1,5 H3 M1,8 H3 L1,11 H3 M6,2.5 H14 M6,6.5 H14 M6,10.5 H14"), Look.Number,
            new(Key.D7, Ctrl | Shift), "Maiusc+Invio aggiunge una voce, Invio torna al testo normale. Oppure scrivi 1. e uno spazio."),
        LookTool("Spunte", () => Glyph("\uE73A"), Look.Todo, new(Key.D9, Ctrl | Shift),
            "Clic sul quadratino per segnarla fatta. Maiusc+Invio aggiunge una spunta. Oppure scrivi [] e uno spazio."),

        LookTool("Citazione", () => Drawn("M2,1 V13 M6,3 H14 M6,7 H14 M6,11 H11"), Look.Quote, new(Key.Q, Ctrl | Shift),
            "Oppure scrivi > e uno spazio a inizio riga."),
        new("blocchi", "Blocco di codice", () => Glyph("\uE943"), Do(_commands.ToggleCode))
        {
            Keys = new(Key.C, Ctrl | Alt), IsOn = () => _commands.CurrentLook() == Look.Code,
            Hint = "Maiusc+Invio aggiunge una riga al blocco, Invio torna al testo normale. Oppure scrivi ``` e uno spazio."
        },
        new("blocchi", "Separazione", () => Drawn("M1,7 H15", 1.8), Do(_commands.InsertRule)) { Keys = new(Key.OemMinus, Ctrl | Shift) },
        new("blocchi", "Tabella", () => Drawn("M1,1 H15 V13 H1 Z M1,5 H15 M1,9 H15 M6,1 V13 M11,1 V13"), () => { })
        {
            Menu =
            [
                ("Inserisci tabella", _tables.Insert, () => true, null),
                ("Aggiungi riga sotto", _tables.AddRow, InTable, null),
                ("Togli riga", _tables.RemoveRow, InTable, null),
                ("Aggiungi colonna a destra", _tables.AddColumn, InTable, null),
                ("Togli colonna", _tables.RemoveColumn, InTable, null)
            ]
        },

        new("inserisci", "Collegamento", () => Glyph("\uE71B"), Do(_commands.Link))
        {
            Keys = new(Key.K, Ctrl), IsOn = () => TextCommands.LinkAt(Editor.Selection.Start) is not null,
            Hint = "Sul testo selezionato, o sul collegamento dove sta il cursore per cambiarlo."
        },
        new("inserisci", "Immagine", () => Glyph("\uE91B"), Do(() => OnPicture(this, new RoutedEventArgs()))) { Hint = "Anche incollando con Ctrl+V." },
        new("inserisci", "Disegno a mano libera", () => Drawn("M1,12 C4,4 6,14 9,7 C11,3 13,6 15,3"), Do(() => OnDrawing(this, new RoutedEventArgs()))),
        new("inserisci", "Grafico", () => Drawn("M1,14 H15 M3,14 V9 M7,14 V4 M11,14 V7 M15,14 V2", 2), Do(() => OnChart(this, new RoutedEventArgs()))),

        new("strumenti", "Togli la formattazione", () => Drawn("M3,13 H13 M4,9 L10,2 L14,6 L9,12 M4,9 L7,12 L9,12"), Do(_commands.ClearFormatting))
        {
            Keys = new(Key.Space, Ctrl), Hint = "Toglie segni e tipo di riga dalla selezione."
        },
        new("strumenti", "Cerca e sostituisci", () => Glyph("\uE721"), () => OnFindToggle(this, new RoutedEventArgs()))
        {
            Keys = new(Key.F, Ctrl), IsOn = () => FindBar.Visibility == Visibility.Visible
        }
    ];

    private Tool LookTool(string name, Func<FrameworkElement> face, Look look, KeyGesture keys, string? hint = null, bool plain = false) =>
        new(Group(look), name, face, Do(plain ? _commands.Plain : () => _commands.SetLook(look)))
        {
            Keys = keys, Hint = hint, IsOn = () => _commands.CurrentLook() == look
        };

    private Tool MarkTool(string name, Func<FrameworkElement> face, Mark mark, KeyGesture keys) =>
        new("segni", name, face, Do(() => _commands.Toggle(mark)))
        {
            Keys = keys, IsOn = () => _commands.Has(mark),
            Hint = "Senza selezione vale per la parola sotto il cursore, o per quel che scrivi dopo."
        };

    private static string Group(Look look) => look switch
    {
        Look.Bullet or Look.Number or Look.Todo => "elenchi",
        Look.Quote or Look.Code => "blocchi",
        _ => "riga"
    };

    /// <summary>Ogni comando lavora sul testo: il fuoco torna sempre nel foglio.</summary>
    private Action Do(Action work) => () =>
    {
        work();
        Editor.Focus();
    };

    private bool InTable() => Editor.CaretPosition?.Paragraph?.Parent is TableCell;

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

                panel = new StackPanel { Orientation = Orientation.Horizontal };

                if (group is not null)
                {
                    panel.Children.Add(new Rectangle { Style = (Style)FindResource("ToolSep") });
                }

                ToolStrip.Children.Add(panel);
                group = tool.Group;
                members = [];
            }

            ButtonBase button = tool.IsOn is null
                ? new Button { Style = (Style)FindResource("FmtBtn") }
                : new ToggleButton { Style = (Style)FindResource("FmtToggle") };
            button.Content = tool.Face();
            button.ToolTip = Tip(tool);
            AutomationName(button, tool);
            button.Click += (_, _) => Fire(tool, button);
            panel!.Children.Add(button);
            _tools.Add((tool, button));
            members.Add(tool);
        }

        if (panel is not null)
        {
            _groups.Add((panel, [.. members]));
        }

        ToolHost.SizeChanged += (_, _) => FitToolbar();
        Editor.SelectionChanged += (_, _) => ToolsLater();
        Editor.TextChanged += (_, _) => ToolsLater();
        ToolStrip.PreviewKeyDown += OnToolKey;
        RefreshTools();
    }

    private void Fire(Tool tool, ButtonBase button)
    {
        if (tool.Menu is { } items)
        {
            ContextMenu menu = new() { PlacementTarget = button, Placement = PlacementMode.Bottom };

            foreach ((string name, Action run, Func<bool> canRun, Func<FrameworkElement>? icon) in items)
            {
                MenuItem item = new() { Header = name, IsEnabled = !Editor.IsReadOnly && canRun(), Icon = icon?.Invoke() };
                item.Click += (_, _) => Do(run)();
                menu.Items.Add(item);

                if (name == "Inserisci tabella")
                {
                    menu.Items.Add(new Separator());
                }
            }

            menu.IsOpen = true;
            return;
        }

        tool.Run();
        RefreshTools();
    }

    private static object Tip(Tool tool)
    {
        StackPanel tip = new() { MaxWidth = 280 };
        tip.Children.Add(new TextBlock { Text = tool.Name, FontWeight = FontWeights.SemiBold });

        if (tool.Keys is { } keys)
        {
            tip.Children.Add(new TextBlock { Text = Shortcut(keys), Opacity = 0.75, Margin = new Thickness(0, 1, 0, 0) });
        }

        if (tool.Hint is { } hint)
        {
            tip.Children.Add(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 0) });
        }

        return tip;
    }

    private static void AutomationName(ButtonBase button, Tool tool) =>
        System.Windows.Automation.AutomationProperties.SetName(button, tool.Keys is { } keys ? $"{tool.Name} ({Shortcut(keys)})" : tool.Name);

    /// <summary>La scorciatoia come la si legge su una tastiera italiana: Ctrl+Maiusc+8.</summary>
    private static string Shortcut(KeyGesture keys)
    {
        List<string> parts = [];

        if (keys.Modifiers.HasFlag(Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (keys.Modifiers.HasFlag(Alt))
        {
            parts.Add("Alt");
        }

        if (keys.Modifiers.HasFlag(Shift))
        {
            parts.Add("Maiusc");
        }

        parts.Add(keys.Key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(keys.Key - Key.D0)).ToString(CultureInfo.InvariantCulture),
            Key.Space => "Spazio",
            Key.OemMinus => "-",
            _ => keys.Key.ToString()
        });

        return string.Join("+", parts);
    }

    // ------------------------------------------------------------------ stato

    /// <summary>Selezione e testo cambiano spesso: lo stato si aggiorna una volta, a riposo.</summary>
    private void ToolsLater()
    {
        if (_toolsDirty)
        {
            return;
        }

        _toolsDirty = true;
        Dispatcher.BeginInvoke(RefreshTools, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RefreshTools()
    {
        _toolsDirty = false;
        bool writable = !Editor.IsReadOnly;

        foreach ((Tool tool, ButtonBase button) in _tools)
        {
            button.IsEnabled = writable && (tool.CanRun?.Invoke() ?? true);

            if (button is ToggleButton toggle)
            {
                toggle.IsChecked = writable && tool.IsOn!();
            }
        }
    }

    // ------------------------------------------------------------------ niente a capo: il resto in "Altro"

    private void FitToolbar()
    {
        const double more = 36;
        double room = ToolHost.ActualWidth - more;
        double used = 0;
        bool full = false;

        foreach ((StackPanel panel, _) in _groups)
        {
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            full |= used + panel.DesiredSize.Width > room;
            panel.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            used += full ? 0 : panel.DesiredSize.Width;
        }

        BtnMore.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMoreTools(object sender, RoutedEventArgs e)
    {
        ContextMenu menu = new() { PlacementTarget = BtnMore, Placement = PlacementMode.Bottom };
        bool writable = !Editor.IsReadOnly;

        foreach ((StackPanel panel, Tool[] tools) in _groups.Where(group => group.Panel.Visibility != Visibility.Visible))
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
                    InputGestureText = tool.Keys is { } keys ? Shortcut(keys) : string.Empty,
                    IsEnabled = writable && (tool.CanRun?.Invoke() ?? true),
                    IsCheckable = false,
                    IsChecked = writable && (tool.IsOn?.Invoke() ?? false)
                };

                if (tool.Menu is { } items)
                {
                    foreach ((string name, Action run, Func<bool> canRun, Func<FrameworkElement>? icon) in items)
                    {
                        MenuItem sub = new() { Header = name, IsEnabled = writable && canRun(), Icon = icon?.Invoke() };
                        sub.Click += (_, _) => Do(run)();
                        item.Items.Add(sub);
                    }
                }
                else
                {
                    item.Click += (_, _) =>
                    {
                        tool.Run();
                        RefreshTools();
                    };
                }

                menu.Items.Add(item);
            }
        }

        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ tastiera

    /// <summary>Le scorciatoie della tabella, nel foglio. Torna true se ne ha eseguita una.</summary>
    private bool RunShortcut(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys mods = Keyboard.Modifiers;

        if (key == Key.F10 && mods == Alt)
        {
            FocusToolbar();
            return true;
        }

        foreach ((Tool tool, ButtonBase button) in _tools)
        {
            if (tool.Keys is { } keys && keys.Key == key && keys.Modifiers == mods && tool.Menu is null)
            {
                if (button.IsEnabled)
                {
                    tool.Run();
                    RefreshTools();
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>I segni che si scrivono a inizio riga, seguiti da uno spazio, come in Docs e Notion.</summary>
    private static readonly (string Mark, Look Look)[] Typed =
    [
        ("#", Look.Heading1), ("##", Look.Heading2), ("###", Look.Heading3),
        ("-", Look.Bullet), ("*", Look.Bullet), ("1.", Look.Number), ("[]", Look.Todo),
        (">", Look.Quote), ("```", Look.Code)
    ];

    /// <summary>
    /// Spazio dopo un segno a inizio di una riga normale: il segno sparisce e la riga cambia tipo.
    /// Annulla (Ctrl+Z) riporta il segno scritto.
    /// </summary>
    private bool AutoFormat(KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None || Editor.IsReadOnly || !Editor.Selection.IsEmpty
            || Editor.CaretPosition is not { Paragraph: { } row } caret || Looks.Of(row) != Look.Text || row.Parent is TableCell)
        {
            return false;
        }

        string typed = new TextRange(row.ContentStart, caret).Text;

        if (Typed.FirstOrDefault(pair => pair.Mark == typed) is not { Mark: not null } hit)
        {
            return false;
        }

        new TextRange(row.ContentStart, caret).Text = string.Empty;

        if (hit.Look == Look.Code)
        {
            _commands.ToggleCode();
        }
        else
        {
            _commands.SetLook(hit.Look);
        }

        RefreshTools();
        return true;
    }

    /// <summary>Alt+F10, come in Word e negli editor web: il primo pulsante attivo prende il fuoco.</summary>
    private void FocusToolbar() =>
        _tools.Select(pair => pair.Button).FirstOrDefault(button => button.IsEnabled && button.IsVisible)?.Focus();

    private void OnToolKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Editor.Focus();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ facce dei pulsanti

    private FrameworkElement Glyph(string glyph) => new TextBlock { Text = glyph, Style = (Style)FindResource("FmtGlyph") };

    private FrameworkElement Letters(string text, double size, bool mono = false)
    {
        TextBlock letters = new() { Text = text, Style = (Style)FindResource("FmtText"), FontSize = size };

        if (mono)
        {
            letters.FontFamily = (FontFamily)FindResource("FMono");
        }

        return letters;
    }

    private FrameworkElement Drawn(string data, double thickness = 1.4) =>
        new Path { Style = (Style)FindResource("FmtPath"), Data = Geometry.Parse(data), StrokeThickness = thickness };

    private FrameworkElement Struck() =>
        new TextBlock { Text = "S", Style = (Style)FindResource("FmtText"), TextDecorations = System.Windows.TextDecorations.Strikethrough };

    /// <summary>La "A" del colore del testo, con sotto una barretta a colori.</summary>
    private FrameworkElement Coloured()
    {
        StackPanel face = new() { VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(new TextBlock { Text = "A", Style = (Style)FindResource("FmtText"), Margin = new Thickness(0, -2, 0, 0) });
        Border bar = new() { Height = 3, Width = 14, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 1, 0, 0) };
        bar.Background = new LinearGradientBrush(
            [new GradientStop(Colors.OrangeRed, 0), new GradientStop(Colors.Gold, 0.35), new GradientStop(Colors.MediumSeaGreen, 0.65), new GradientStop(Colors.DodgerBlue, 1)],
            0);
        face.Children.Add(bar);
        return face;
    }

    private static FrameworkElement Swatch(string token)
    {
        Ellipse dot = new() { Width = 12, Height = 12, StrokeThickness = 1 };
        dot.SetResourceReference(Shape.FillProperty, token);
        dot.SetResourceReference(Shape.StrokeProperty, "Separator");
        return dot;
    }

    private FrameworkElement Marker()
    {
        TextBlock letter = new() { Text = "A", Style = (Style)FindResource("FmtText") };
        letter.SetResourceReference(TextBlock.ForegroundProperty, "HighlightFg");
        Border chip = new() { CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 0), Child = letter };
        chip.SetResourceReference(Border.BackgroundProperty, "Highlight");
        return chip;
    }
}
