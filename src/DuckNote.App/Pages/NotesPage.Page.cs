using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DuckNote.App.Editor;
using DuckNote.App.Ui;

namespace DuckNote.App.Pages;

/// <summary>
/// L'impaginazione del foglio, pensata per lasciare al testo tutto lo spazio che c'e':
/// - il testo occupa il foglio intero; i margini della pagina stanno nel documento
///   (PagePadding) e non intorno all'editor, cosi' scorrono via insieme al testo;
/// - il titolo della pagina e' scritto in cima al foglio, sopra al margine alto del documento,
///   e scorre anche lui: dopo due righe di rotellina tutta l'altezza e' del testo;
/// - quando la sidebar entra o esce, la carta tiene fermo il bordo destro mentre il contenuto
///   scivola (vedi <see cref="Slide"/>);
/// - quando il foglio cambia larghezza (la sidebar, la finestra, lo zoom) le righe sopra vanno
///   a capo in un altro modo: la vista tiene il segno sulla prima riga che si stava leggendo.
/// </summary>
public partial class NotesPage : ISlidingPage
{
    /// <summary>I margini della pagina in pixel dello schermo, qualunque sia lo zoom.</summary>
    private const double PageLeft = 44;
    private const double PageRight = 26;
    private const double PageBottom = 36;

    private ScrollViewer? _scroller;
    private int _slideRun;

    /// <summary>La riga in cima alla vista, e a che altezza stava: il segno da tenere se il foglio si allarga o si stringe.</summary>
    private TextPointer? _anchor;
    private double _anchorTop;

    private void WirePage()
    {
        Header.SizeChanged += (_, _) => PlacePage();
        Header.PreviewMouseWheel += OnHeaderWheel;
        Editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnPageScroll));
        Editor.SizeChanged += OnPageResized;
    }

    private double ZoomScale => Math.Max(0.1, _ws.Settings.EditorZoom / 100.0);

    private ScrollViewer? Scroller => _scroller ??= Doc.Scroller(Editor);

    /// <summary>
    /// Mette i margini della pagina in ogni foglio. Si misurano in pixel dello schermo e si
    /// dividono per lo zoom (l'editor e' ingrandito per intero): ingrandendo cresce il testo, non
    /// il bianco intorno. Il margine alto e' l'altezza del titolo, che sta li' sopra.
    /// </summary>
    private void PlacePage()
    {
        double zoom = ZoomScale;
        double top = Header.ActualHeight + Header.Margin.Top + Header.Margin.Bottom;
        Thickness padding = new(PageLeft / zoom, top / zoom, PageRight / zoom, PageBottom / zoom);

        foreach (Sheet sheet in _ws.Book.Sheets)
        {
            if (sheet.Document.PagePadding != padding)
            {
                sheet.Document.PagePadding = padding;
            }
        }

        PlaceHeader();
    }

    /// <summary>Il titolo segue lo scorrimento del testo; uscito dal foglio non si disegna (ne' prende il fuoco).</summary>
    private void PlaceHeader()
    {
        double away = (Scroller?.VerticalOffset ?? 0) * ZoomScale;
        HeaderShift.Y = -away;

        bool gone = away > Header.ActualHeight + Header.Margin.Top + 2;
        Header.Visibility = gone ? Visibility.Hidden : Visibility.Visible;
    }

    private void OnPageScroll(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Scroller))
        {
            return;
        }

        PlaceHeader();

        if (e.VerticalChange != 0)
        {
            KeepPlace();
        }
    }

    /// <summary>Ricorda la riga in cima alla vista. In cima al foglio non serve: si resta in cima.</summary>
    private void KeepPlace()
    {
        _anchor = null;

        if (Scroller is not { VerticalOffset: > 0.5 })
        {
            return;
        }

        if (Editor.GetPositionFromPoint(new Point((PageLeft / ZoomScale) + 6, 3), snapToText: true) is { } top)
        {
            _anchor = top;
            _anchorTop = top.GetCharacterRect(LogicalDirection.Forward).Top;
        }
    }

    /// <summary>
    /// Il foglio ha cambiato larghezza: i paragrafi sopra la vista sono andati a capo in un altro
    /// modo e tutto quel che si leggeva e' scivolato su o giu'. Si riporta la riga ricordata
    /// all'altezza a cui stava, cosi' si continua a leggere (e a scrivere) dallo stesso punto.
    /// </summary>
    private void OnPageResized(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || _anchor is not { } anchor || Scroller is not { VerticalOffset: > 0.5 } view
            || !anchor.IsInSameDocument(Editor.Document.ContentStart))
        {
            return;
        }

        Rect now = anchor.GetCharacterRect(LogicalDirection.Forward);

        if (now.IsEmpty || double.IsInfinity(now.Top))
        {
            return;
        }

        // niente limite in fondo: qui ScrollableHeight e' ancora quella di prima della nuova
        // larghezza (si aggiorna dopo), e il ScrollViewer riporta da se' l'offset nel suo intervallo
        double wanted = Math.Max(0, view.VerticalOffset + now.Top - _anchorTop);

        if (Math.Abs(wanted - view.VerticalOffset) >= 1)
        {
            view.ScrollToVerticalOffset(wanted);
        }
    }

    /// <summary>La rotellina sopra al titolo scorre il foglio, come su tutto il resto della pagina.</summary>
    private void OnHeaderWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnEditorWheel(sender, e);
            return;
        }

        if (Scroller is not { } view)
        {
            return;
        }

        e.Handled = true;
        view.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = view });
    }

    /// <summary>Il cursore nel testo: dopo un comando fatto altrove, o quando la sidebar si nasconde.</summary>
    public void FocusText() => Editor.Focus();

    private bool _airPending;

    /// <summary>
    /// Il cursore si e' mosso: appena il testo e' impaginato si controlla che la sua riga abbia
    /// aria intorno. Subito dopo l'impaginazione, nello stesso fotogramma: un Invio sull'ultima
    /// riga visibile fa scorrere la vista una volta sola, non prima un po' e poi il resto. Se non
    /// c'e' niente da impaginare (le frecce), ci pensa il giro a riposo.
    /// </summary>
    private void AirLater()
    {
        if (_airPending)
        {
            return;
        }

        _airPending = true;
        Editor.LayoutUpdated += OnAirLayout;
        Dispatcher.BeginInvoke(KeepAir, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void OnAirLayout(object? sender, EventArgs e) => KeepAir();

    /// <summary>
    /// Il testo arriva fino ai bordi del foglio, ma la riga dove si scrive no: l'editor porta il
    /// cursore appena dentro la vista, qui gli si lascia sotto il margine basso della pagina (e
    /// un filo sopra), cosi' non si scrive mai attaccati al bordo. Sulla prima riga si torna in
    /// cima, dove c'e' il titolo della pagina. Non mentre si trascina col mouse, e non se a
    /// muovere la vista e' stata la rotellina (il cursore non si e' mosso: non si arriva qui).
    /// </summary>
    private void KeepAir()
    {
        Editor.LayoutUpdated -= OnAirLayout;

        if (!_airPending)
        {
            return;
        }

        _airPending = false;

        if (Scroller is not { } view || !Editor.IsKeyboardFocusWithin || Mouse.LeftButton == MouseButtonState.Pressed
            || Editor.CaretPosition is not { } caret)
        {
            return;
        }

        Rect spot = caret.GetCharacterRect(LogicalDirection.Forward);

        if (spot.IsEmpty || double.IsInfinity(spot.Top))
        {
            return;
        }

        double zoom = ZoomScale;
        double above = 10 / zoom, below = PageBottom / zoom;
        double offset = view.VerticalOffset;
        double wanted = offset;

        if (offset + spot.Top < Editor.Document.PagePadding.Top + spot.Height)
        {
            wanted = 0;
        }
        else if (spot.Top < above)
        {
            wanted = offset + spot.Top - above;
        }
        else if (spot.Bottom > view.ViewportHeight - below)
        {
            // mai tanto da mandare fuori, in alto, l'inizio di una riga piu' alta della vista
            wanted = Math.Min(offset + spot.Bottom - (view.ViewportHeight - below), offset + spot.Top - above);
        }

        wanted = Math.Clamp(wanted, 0, Math.Max(0, view.ScrollableHeight));

        if (Math.Abs(wanted - offset) >= 1)
        {
            view.ScrollToVerticalOffset(wanted);
        }
    }

    /// <summary>
    /// La sidebar entra o esce. La finestra ha gia' dato alla pagina la larghezza nuova e la fa
    /// scivolare da <paramref name="from"/> a zero; qui, con la stessa durata e la stessa curva:
    /// - la carta si allunga (o si accorcia) a destra di quanto il contenuto e' spostato, cosi' il
    ///   suo bordo destro non si muove e a muoversi e' solo quello accanto alla sidebar;
    /// - il contenuto si vede solo dentro la carta;
    /// - la ricerca (a destra) e l'invito della pagina vuota (al centro) restano dove sono;
    /// - la barra di scorrimento torna in dissolvenza quando e' quasi al suo posto.
    /// </summary>
    public void Slide(double from, int ms, IEasingFunction ease)
    {
        if (ms <= 0 || Math.Abs(from) < 0.5)
        {
            return;
        }

        Duration time = TimeSpan.FromMilliseconds(ms);
        int run = ++_slideRun;

        Paper.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0, 0, from, 0), new Thickness(0), time)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        });

        double width = Sheet.ActualWidth, height = Sheet.ActualHeight;
        Rect whole = new(0, 0, width, height);
        RectangleGeometry clip = new(whole);
        RectAnimation open = new(new Rect(0, 0, Math.Max(0, width - from), height), whole, time)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        };

        open.Completed += (_, _) =>
        {
            if (run == _slideRun)
            {
                Sheet.Clip = null;
                Sheet.ClipToBounds = true;
            }
        };

        Sheet.ClipToBounds = false;
        Sheet.Clip = clip;
        clip.BeginAnimation(RectangleGeometry.RectProperty, open);

        Hold(FindShift, -from, time, ease);
        Hold(EmptyShift, -from / 2, time, ease);

        if (Scroller is { } view && view.Template?.FindName("PART_VerticalScrollBar", view) is ScrollBar bar)
        {
            DoubleAnimationUsingKeyFrames fade = new() { Duration = time, FillBehavior = FillBehavior.Stop };
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.6)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(1)));
            bar.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>Tiene fermo un elemento mentre il foglio scivola: parte spostato al contrario e torna a zero.</summary>
    private static void Hold(TranslateTransform shift, double from, Duration time, IEasingFunction ease) =>
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, time)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop
        });
}
