using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DuckNote.App.Pages;
using DuckNote.App.Ui;

namespace DuckNote.App;

/// <summary>
/// La sidebar che si fa da parte, per lasciare al foglio tutta la finestra, e quel che le gira
/// intorno nella fascia del titolo (la barra di formattazione, l'ultimo evento a comparsa, il
/// pallino del salvataggio). La sidebar ha tre stati:
/// - Docked: al suo posto, il contenuto le sta accanto;
/// - Hidden: fuori dalla finestra, il contenuto e' largo quanto la finestra;
/// - Peek: affacciata sopra al contenuto, che non si sposta, finche' la si usa.
///
/// Ogni passaggio e' animato, e si puo' interrompere a meta' senza scatti: si riparte sempre da
/// dove le cose si vedono in quel momento. Il contenuto cambia larghezza una volta sola, subito,
/// e nello stesso istante viene traslato dov'era: poi scivola. Cosi' una nota lunga si
/// reimpagina una volta, non a ogni fotogramma.
/// </summary>
public partial class MainWindow
{
    private enum Side
    {
        Docked,
        Hidden,
        Peek
    }

    private const double SideWidth = 266;

    /// <summary>Dove sta la sidebar nascosta: fuori di tutta la sua larghezza, ombra compresa.</summary>
    private const double SideAway = -(SideWidth + 24);

    private const int SideMs = 260;

    private Side _side = Side.Docked;
    private int _sideRun;

    private bool _toolbarOn;
    private int _toolbarRun;

    private readonly DispatcherTimer _peekIn = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _peekWatch = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private int _peekAway;

    private readonly DispatcherTimer _statusRest = new();
    private DateTime _statusSince;

    private void WireSidebar()
    {
        Color dark = Colors.Black, clear = Colors.Black;
        dark.A = 34;
        clear.A = 0;
        SidebarShadow.Fill = new LinearGradientBrush(dark, clear, 0);

        // il bordo sinistro: fermandoci il mouse (o cliccandoci) la sidebar si affaccia
        EdgeSensor.MouseEnter += (_, _) =>
        {
            _peekIn.Stop();
            _peekIn.Start();
        };
        EdgeSensor.MouseLeave += (_, _) => _peekIn.Stop();
        EdgeSensor.MouseLeftButtonDown += (_, e) =>
        {
            SetSide(Side.Peek);
            e.Handled = true;
        };

        _peekIn.Tick += (_, _) =>
        {
            _peekIn.Stop();

            if (_side == Side.Hidden && CursorOver(EdgeSensor, 0))
            {
                SetSide(Side.Peek);
            }
        };

        _peekWatch.Tick += (_, _) => WatchPeek();

        _statusRest.Tick += (_, _) => HideStatus();

        // la barra della nota sta fra i pulsanti di sinistra e quelli di destra: se cambiano misura si sposta
        TitleLeft.SizeChanged += (_, _) => PlaceToolbar(animate: false);
        TitleRight.SizeChanged += (_, _) => PlaceToolbar(animate: false);

        _ws.Unsaved += () => Dispatcher.BeginInvoke(ShowSaved);
        _ws.Saved += () => Dispatcher.BeginInvoke(ShowSaved);
    }

    // ---------------------------------------------------------------- la sidebar

    private void OnSidebarToggle(object sender, RoutedEventArgs e) => ToggleSidebar();

    /// <summary>F9, Ctrl+\ o il pulsante accanto ai semafori: via la sidebar, o di nuovo al suo posto.</summary>
    private void ToggleSidebar()
    {
        if (_ws.IsLocked)
        {
            return;
        }

        SetSide(_side == Side.Docked ? Side.Hidden : Side.Docked);
    }

    /// <summary>Serve la sidebar (la ricerca nei fogli da tastiera): se e' nascosta si affaccia.</summary>
    private void WantSidebar()
    {
        if (_side == Side.Hidden)
        {
            SetSide(Side.Peek);
        }
    }

    private void SetSide(Side wanted, bool animate = true)
    {
        if (wanted == _side)
        {
            return;
        }

        Side was = _side;
        _side = wanted;
        int run = ++_sideRun;
        _peekIn.Stop();
        _peekAway = 0;

        bool docked = wanted == Side.Docked;
        bool shown = wanted != Side.Hidden;
        bool over = wanted == Side.Peek;
        int ms = animate ? SideMs : 0;
        IEasingFunction ease = Fx.Smooth();

        // chi stava scrivendo nella sidebar (la ricerca, il nome di un foglio) torna al foglio
        if (!shown && Sidebar.IsKeyboardFocusWithin)
        {
            FocusPage();
        }

        // Quando tutto e' arrivato: nascosta non si disegna piu'; tornata al suo posto non ha
        // piu' niente sotto, e il fondo pieno se ne va piano (col vetro si vedrebbe lo stacco).
        // Se un altro passaggio e' partito nel frattempo, tocca a lui.
        void Arrived()
        {
            if (run != _sideRun)
            {
                return;
            }

            if (!shown)
            {
                Sidebar.Visibility = Visibility.Collapsed;
            }

            if (!over)
            {
                Glide(SidebarBack, OpacityProperty, SidebarBack.Opacity, 0, shown ? 200 : 0);
            }
        }

        // 1. Il contenuto prende la larghezza nuova in un colpo e viene traslato dov'era: poi scivola.
        double left = docked ? SideWidth : 0;
        double jump = ContentArea.Margin.Left - left;
        bool moving = Math.Abs(jump) > 0.5;

        if (moving)
        {
            double from = ContentShift.X + jump;
            ContentArea.Margin = new Thickness(left, 0, 0, 0);
            Glide(ContentShift, TranslateTransform.XProperty, from, from, 0);

            // la reimpaginazione si fa adesso, prima che parta l'orologio dell'animazione
            ContentArea.UpdateLayout();
            Glide(ContentShift, TranslateTransform.XProperty, from, 0, ms, ease, Arrived);
            (PageHost.Content as ISlidingPage)?.Slide(from, ms, ease);
        }

        // 2. La sidebar entra o esce; sopra al foglio ha un'ombra e il fondo pieno.
        if (shown)
        {
            Sidebar.Visibility = Visibility.Visible;
        }

        if (over || was == Side.Peek)
        {
            Glide(SidebarBack, OpacityProperty, 1, 1, 0);
        }

        // contenuto e sidebar durano uguale: se si muove il contenuto e' lui a dire quando si e' arrivati
        // (da affacciata a ferma la sidebar non si sposta affatto, e finirebbe subito)
        Glide(SidebarShadow, OpacityProperty, SidebarShadow.Opacity, over ? 1 : 0, ms, ease);
        Glide(SidebarShift, TranslateTransform.XProperty, SidebarShift.X, shown ? 0 : SideAway, ms, ease, moving ? null : Arrived);

        // 3. La barra della nota fa posto ai semafori, o se lo riprende.
        PlaceToolbar(animate);

        EdgeSensor.Visibility = wanted == Side.Hidden ? Visibility.Visible : Visibility.Collapsed;

        if (over)
        {
            _peekWatch.Start();
        }
        else
        {
            _peekWatch.Stop();
        }

        ShowSidebarButton();

        // affacciarsi non e' una scelta: resta nascosta anche al prossimo avvio
        if (!over && _ws.Settings.SidebarHidden != !docked)
        {
            _ws.Settings.SidebarHidden = !docked;
            _ws.Settings.Save();
        }
    }

    /// <summary>
    /// Mentre e' affacciata: resta finche' la si usa (il mouse ci sta sopra, ci si scrive dentro,
    /// c'e' un suo menu aperto) e rientra poco dopo che la si e' lasciata.
    /// </summary>
    private void WatchPeek()
    {
        if (_side != Side.Peek)
        {
            _peekWatch.Stop();
            return;
        }

        bool writing = Keyboard.FocusedElement is TextBoxBase box && Sidebar.IsAncestorOf(box);

        if (writing || CursorOver(Sidebar, 14) || Mouse.Captured is not null || AccountMenu.IsOpen)
        {
            _peekAway = 0;
            return;
        }

        if (++_peekAway >= 2)
        {
            SetSide(Side.Hidden);
        }
    }

    /// <summary>Un clic fuori dalla sidebar affacciata la fa rientrare subito.</summary>
    private void DismissPeek(MouseButtonEventArgs e)
    {
        if (_side == Side.Peek && !CursorOver(Sidebar, 0) && !BtnSidebar.IsMouseOver && e.OriginalSource is not Popup)
        {
            SetSide(Side.Hidden);
        }
    }

    private void ShowSidebarButton()
    {
        BtnSidebar.ToolTip = _side == Side.Docked
            ? "Nascondi la barra laterale: tutto lo spazio al foglio (F9)"
            : "Mostra la barra laterale (F9). Si affaccia anche portando il mouse sul bordo sinistro.";
    }

    /// <summary>
    /// Il fuoco della tastiera passa alla pagina: nel testo della nota, o sul primo comando delle
    /// altre. Mai a nessuno: senza un elemento a fuoco la finestra non riceve piu' i tasti, e le
    /// scorciatoie (F9 compreso) smetterebbero di rispondere.
    /// </summary>
    private void FocusPage()
    {
        if (PageHost.Content is NotesPage notes)
        {
            notes.FocusText();
            return;
        }

        if (MapHost.Visibility == Visibility.Visible && MapHost.Content is MapPage map)
        {
            map.OnShown();
            return;
        }

        if (PageHost.Content is FrameworkElement page && page.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)))
        {
            return;
        }

        Focusable = true;
        Focus();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out ScreenPoint point);

    /// <summary>
    /// Il mouse sta sopra l'elemento? Lo si chiede a Windows: a mouse fermo, o gia' fuori dalla
    /// finestra, WPF non riceve movimenti e il suo IsMouseOver resta quello di prima.
    /// </summary>
    private static bool CursorOver(FrameworkElement element, double slack)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) is null || !GetCursorPos(out ScreenPoint at))
        {
            return false;
        }

        Point local = element.PointFromScreen(new Point(at.X, at.Y));

        return local.X >= -slack && local.Y >= -slack
            && local.X <= element.ActualWidth + slack && local.Y <= element.ActualHeight + slack;
    }

    // ---------------------------------------------------------------- la barra della nota

    /// <summary>La barra di formattazione si vede solo con la nota in primo piano: entra ed esce in dissolvenza.</summary>
    private void ShowToolbar(bool on)
    {
        if (on == _toolbarOn)
        {
            return;
        }

        _toolbarOn = on;
        int run = ++_toolbarRun;

        if (on)
        {
            ToolbarHost.Visibility = Visibility.Visible;
            ToolbarHost.IsHitTestVisible = true;
            Glide(ToolbarHost, OpacityProperty, ToolbarHost.Opacity, 1, 240);
            Glide(ToolbarShift, TranslateTransform.YProperty, -8, 0, 300);
            return;
        }

        ToolbarHost.IsHitTestVisible = false;
        Glide(ToolbarShift, TranslateTransform.YProperty, ToolbarShift.Y, -8, 160);
        Glide(ToolbarHost, OpacityProperty, ToolbarHost.Opacity, 0, 160, null, () =>
        {
            if (run == _toolbarRun)
            {
                ToolbarHost.Visibility = Visibility.Collapsed;
            }
        });
    }

    /// <summary>
    /// Dove sta la barra della nota: dopo la sidebar, oppure (a sidebar nascosta) dopo i semafori;
    /// a destra si ferma prima di versione, lucchetto, Impostazioni e giorno/notte.
    /// </summary>
    private void PlaceToolbar(bool animate)
    {
        double left = _side == Side.Docked ? SideWidth + 10 : 16 + TitleLeft.ActualWidth + 8;
        Thickness to = new(left, 0, 12 + TitleRight.ActualWidth + 10, 0);
        Thickness from = ToolbarHost.Margin;

        ToolbarHost.Margin = to;

        if (!animate || from == to)
        {
            ToolbarHost.BeginAnimation(MarginProperty, null);
            return;
        }

        ToolbarHost.BeginAnimation(MarginProperty, new ThicknessAnimation(from, to, TimeSpan.FromMilliseconds(SideMs))
        {
            EasingFunction = Fx.Smooth(),
            FillBehavior = FillBehavior.Stop
        });
    }

    // ---------------------------------------------------------------- l'ultimo evento

    /// <summary>
    /// L'ultimo evento, in basso a sinistra: compare, resta il tempo di leggerlo (di piu' se e'
    /// lungo) e se ne va. Chi riprende a scrivere lo manda via prima.
    /// </summary>
    public void SetStatus(string text, string? right = null)
    {
        if (right is not null)
        {
            StatusRight.Text = right;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            HideStatus();
            return;
        }

        StatusText.Text = text;
        _statusSince = DateTime.UtcNow;
        Fx.SlideVisible(StatusPill, true, 14);

        _statusRest.Stop();
        _statusRest.Interval = TimeSpan.FromMilliseconds(Math.Clamp(2200 + (text.Length * 45), 3200, 9000));
        _statusRest.Start();
    }

    private void HideStatus()
    {
        _statusRest.Stop();
        Fx.SlideVisible(StatusPill, false, 14);
    }

    /// <summary>Si riprende a scrivere: l'avviso, se lo si e' avuto davanti abbastanza, lascia libero il foglio.</summary>
    private void StatusOnKey(KeyEventArgs e)
    {
        if (StatusPill.Visibility == Visibility.Visible && e.Key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.System)
            && (DateTime.UtcNow - _statusSince).TotalMilliseconds > 1500)
        {
            HideStatus();
        }
    }

    // ---------------------------------------------------------------- salvata?

    /// <summary>
    /// Il pallino accanto alla versione: arancio finche' ci sono modifiche da scrivere, verde
    /// quando sono su disco. Il salvataggio lavora in secondo piano: questo e' il suo segno.
    /// </summary>
    private void ShowSaved()
    {
        if (_ws.IsLocked)
        {
            SaveDot.Visibility = Visibility.Collapsed;
            return;
        }

        bool pending = _ws.HasUnsaved;
        string token = pending ? "Orange" : "Green";

        SaveDot.ToolTip = pending
            ? (_ws.Settings.AutosaveEnabled ? "Modifiche da salvare: si scrivono da sole appena ti fermi" : "Modifiche da salvare")
            : _ws.LastSaved is { } at ? $"Nota salvata alle {at:HH:mm:ss}" : "Nota salvata";

        if (SaveDot.Visibility == Visibility.Visible && Equals(SaveDot.Tag, token))
        {
            return;
        }

        SaveDot.Tag = token;
        SaveDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, token);
        SaveDot.Visibility = Visibility.Visible;
        Glide(SaveDot, OpacityProperty, 0.25, 1, 320);
    }

    // ---------------------------------------------------------------- movimento

    /// <summary>
    /// Porta una proprieta' da un valore a un altro. Il valore d'arrivo viene scritto subito e
    /// l'animazione, finita, si toglie: cosi' un movimento nuovo puo' partire in qualunque
    /// momento da dove si vede, e niente resta appeso all'ultimo fotogramma.
    /// </summary>
    private static void Glide(DependencyObject target, DependencyProperty property, double from, double to, int ms,
        IEasingFunction? ease = null, Action? done = null)
    {
        IAnimatable mover = (IAnimatable)target;
        target.SetValue(property, to);

        if (ms <= 0 || Math.Abs(from - to) < 0.01)
        {
            mover.BeginAnimation(property, null);
            done?.Invoke();
            return;
        }

        DoubleAnimation move = new(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease ?? Fx.Smooth(),
            FillBehavior = FillBehavior.Stop
        };

        if (done is not null)
        {
            move.Completed += (_, _) => done();
        }

        mover.BeginAnimation(property, move);
    }
}
