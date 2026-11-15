using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DuckNote.Notes.Links;

namespace DuckNote.App.Editor;

public enum Mark
{
    Bold,
    Italic,
    Underline,
    Strike,
    Highlight,
    Code
}

/// <summary>Cosa ha scelto la finestra del collegamento.</summary>
public sealed record LinkChoice(string Address, string Text, bool Removed);

/// <summary>
/// I comandi della barra. Ognuno lavora sulla selezione: tre righe selezionate cambiano tutte
/// e tre. Uno stile sta sul testo, quindi non c'e' sintassi da aprire e chiudere.
/// </summary>
public sealed class TextCommands(RichTextBox editor, RowWatcher watcher)
{
    /// <summary>Un comando che non si puo' fare, detto in una riga.</summary>
    public event Action<string>? Refused;

    public Func<string, string, LinkChoice?>? AskForLink { get; set; }

    // ------------------------------------------------------------------ segni sul testo

    /// <summary>
    /// Accende o spegne un segno. Senza selezione, come in Word e Docs: col cursore dentro una
    /// parola vale per tutta la parola; altrove vale per quello che si scrive da li' in poi.
    /// </summary>
    public void Toggle(Mark mark)
    {
        TextRange target = Target();

        switch (mark)
        {
            case Mark.Bold:
                Flip(target, TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal);
                break;

            case Mark.Italic:
                Flip(target, TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal);
                break;

            case Mark.Underline:
                Decorate(target, TextDecorations.Underline[0]);
                break;

            case Mark.Strike:
                Decorate(target, TextDecorations.Strikethrough[0]);
                break;

            case Mark.Highlight:
                bool marked = Brushed(target.GetPropertyValue(TextElement.BackgroundProperty), Palette.MarkBack);
                target.ApplyPropertyValue(TextElement.BackgroundProperty, marked ? null : Palette.MarkBack);
                target.ApplyPropertyValue(TextElement.ForegroundProperty, marked ? Palette.Text : Palette.MarkFore);
                break;

            case Mark.Code:
                bool coded = Brushed(target.GetPropertyValue(TextElement.BackgroundProperty), Palette.CodeBack);
                target.ApplyPropertyValue(TextElement.BackgroundProperty, coded ? null : Palette.CodeBack);
                target.ApplyPropertyValue(TextElement.ForegroundProperty, coded ? Palette.Text : Palette.CodeFore);
                target.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(coded ? Doc.TextFonts : Doc.MonoFonts));
                break;
        }
    }

    /// <summary>
    /// Il colore del testo, come i segni: sulla selezione, o sulla parola sotto il cursore, o su
    /// quel che si scrive dopo. E' un token del tema: cambia da solo con chiaro e scuro.
    /// </summary>
    public void Colour(string token)
    {
        TextRange target = Target();
        target.ApplyPropertyValue(TextElement.ForegroundProperty, Palette.Of(token));
    }

    /// <summary>Il segno e' acceso sulla selezione (o sul cursore)? Serve alla barra per mostrarlo premuto.</summary>
    public bool Has(Mark mark)
    {
        // lo stesso testo su cui lavorerebbe il comando: cosi' il pulsante dice il vero anche
        // quando la selezione si porta dietro lo spazio dopo la parola
        TextRange selection = editor.Selection.IsEmpty ? editor.Selection : Target();

        return mark switch
        {
            Mark.Bold => Equals(selection.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold),
            Mark.Italic => Equals(selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic),
            Mark.Underline => Decorated(TextDecorationLocation.Underline),
            Mark.Strike => Decorated(TextDecorationLocation.Strikethrough),
            Mark.Highlight => Brushed(selection.GetPropertyValue(TextElement.BackgroundProperty), Palette.MarkBack),
            Mark.Code => Brushed(selection.GetPropertyValue(TextElement.BackgroundProperty), Palette.CodeBack),
            _ => false
        };

        bool Decorated(TextDecorationLocation where) =>
            selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection now
            && now.Any(decoration => decoration.Location == where);
    }

    /// <summary>Il tipo delle righe scelte, se e' lo stesso per tutte; altrimenti Text.</summary>
    public Look CurrentLook()
    {
        Paragraph[] rows = Chosen();

        if (rows.Length == 0)
        {
            return Look.Text;
        }

        Look first = Looks.Of(rows[0]);
        return rows.All(row => Looks.Of(row) == first) ? first : Look.Text;
    }

    /// <summary>
    /// Il testo su cui lavora un segno. Senza selezione: la parola sotto il cursore, o quel che
    /// si scrive dopo. Con una selezione: la selezione senza gli spazi in coda. Un doppio clic
    /// (o Ctrl+Maiusc+freccia) prende la parola e lo spazio che la segue: sottolineare, barrare o
    /// evidenziare anche quello spazio e' il difetto che Word evita allo stesso modo.
    /// </summary>
    private TextRange Target()
    {
        TextSelection selection = editor.Selection;

        if (selection.IsEmpty)
        {
            return WordAt(editor.CaretPosition) is { } word ? word : selection;
        }

        return Trimmed(selection);
    }

    private static TextRange Trimmed(TextRange range)
    {
        string text = range.Text;
        int trailing = text.Length - text.TrimEnd(' ', '\t').Length;

        if (trailing == 0 || trailing == text.Length)
        {
            return range;
        }

        // gli spazi in coda stanno quasi sempre nello stesso pezzo di testo: se non e' cosi'
        // (un confine di formato in mezzo) si lascia la selezione com'e'
        if (range.End.GetPositionAtOffset(-trailing) is { } end
            && end.CompareTo(range.Start) > 0
            && new TextRange(end, range.End).Text == text[^trailing..])
        {
            return new TextRange(range.Start, end);
        }

        return range;
    }

    /// <summary>La parola sotto il cursore, se il cursore sta proprio dentro (non ai bordi).</summary>
    private static TextRange? WordAt(TextPointer? caret)
    {
        if (caret is null)
        {
            return null;
        }

        string before = caret.GetTextInRun(LogicalDirection.Backward);
        string after = caret.GetTextInRun(LogicalDirection.Forward);
        int back = before.Length - before.Reverse().TakeWhile(char.IsLetterOrDigit).Count();
        int ahead = after.TakeWhile(char.IsLetterOrDigit).Count();

        if (back == before.Length || ahead == 0)
        {
            return null;
        }

        return caret.GetPositionAtOffset(-(before.Length - back)) is { } start && caret.GetPositionAtOffset(ahead) is { } end
            ? new TextRange(start, end)
            : null;
    }

    /// <summary>
    /// Lo stesso colore, anche se il pennello non e' lo stesso oggetto: la selezione puo' portare
    /// una copia del pennello del tema.
    /// </summary>
    private static bool Brushed(object? value, Brush wanted) =>
        value is SolidColorBrush have && wanted is SolidColorBrush want && have.Color == want.Color;

    private static void Flip(TextRange target, DependencyProperty property, object on, object off) =>
        target.ApplyPropertyValue(property, Equals(target.GetPropertyValue(property), on) ? off : on);

    /// <summary>Sottolineato e barrato convivono: si toglie o si aggiunge solo quello chiesto.</summary>
    private static void Decorate(TextRange target, TextDecoration wanted)
    {
        TextDecorationCollection now = target.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection ?? [];
        bool already = now.Any(decoration => decoration.Location == wanted.Location);
        TextDecorationCollection next = [.. now.Where(decoration => decoration.Location != wanted.Location)];

        if (!already)
        {
            next.Add(wanted);
        }

        target.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
    }

    /// <summary>Le righe scelte tornano testo normale (titoli, elenchi, citazioni tolti).</summary>
    public void Plain()
    {
        Paragraph[] rows = Chosen();

        if (rows.Length == 0)
        {
            return;
        }

        Change(() =>
        {
            foreach (Paragraph row in rows)
            {
                Styler.Undress(row);
            }
        });

        watcher.Touch(editor.CaretPosition?.Paragraph);
        watcher.Flush();
    }

    // ------------------------------------------------------------------ tipi di riga

    /// <summary>
    /// Il tipo di riga sulle righe scelte. Se lo hanno gia' tutte lo tolgono; un elenco numerato
    /// conta, e le righe vuote in mezzo a una selezione restano vuote.
    /// </summary>
    public void SetLook(Look look)
    {
        Paragraph[] rows = Chosen();

        if (rows.Length == 0)
        {
            return;
        }

        bool strip = rows.All(row => Looks.Is(row, look));
        int number = 0;

        Change(() =>
        {
            foreach (Paragraph row in rows)
            {
                if (strip)
                {
                    Styler.Undress(row);
                    continue;
                }

                if (rows.Length > 1 && Doc.LineOf(row).Trim().Length == 0)
                {
                    continue;
                }

                Styler.Dress(row, look, ++number);
            }
        });

        if (rows.Length == 1)
        {
            editor.CaretPosition = rows[0].ContentEnd;
            WritePlain(rows[0]);
        }
        else
        {
            editor.Selection.Select(rows[0].ContentStart, rows[^1].ContentEnd);
        }
    }

    /// <summary>Le righe scelte diventano un blocco di codice; un blocco scelto torna righe.</summary>
    public void ToggleCode()
    {
        Paragraph[] rows = Chosen();

        if (rows.Length == 0 || Doc.Siblings(rows[0]) is not { } where)
        {
            return;
        }

        TextPointer? land = null;

        Change(() =>
        {
            if (rows.Length == 1 && Looks.Is(rows[0], Look.Code))
            {
                Paragraph after = rows[0];

                foreach (string line in Styler.CodeLines(rows[0]))
                {
                    Paragraph row = Doc.Row(line);
                    where.InsertAfter(after, row);
                    land ??= row.ContentStart;
                    after = row;
                }

                where.Remove(rows[0]);
                return;
            }

            Paragraph block = Styler.CodeBlock(rows.Select(Doc.LineOf));
            where.InsertBefore(rows[0], block);

            foreach (Paragraph row in rows)
            {
                where.Remove(row);
            }

            land = block.ContentEnd;
        });

        if (land is not null)
        {
            editor.CaretPosition = land;
        }

        watcher.Touch(editor.CaretPosition?.Paragraph);
        watcher.Flush();
    }

    /// <summary>
    /// La separazione va sulla riga subito sotto a quella che si scrive, e si continua su una
    /// riga nuova sotto di lei: quel che c'era sotto resta dov'era.
    /// </summary>
    public void InsertRule()
    {
        Paragraph[] rows = Chosen();

        if (rows.Length == 0 || Doc.Siblings(rows[^1]) is not { } where)
        {
            return;
        }

        Paragraph after = Doc.Row();

        Change(() =>
        {
            Paragraph rule = Doc.Row();
            Styler.Dress(rule, Look.Rule);
            where.InsertAfter(rows[^1], rule);
            where.InsertAfter(rule, after);
        });

        editor.CaretPosition = after.ContentStart;
    }

    public void ClearFormatting()
    {
        Paragraph[] rows = Chosen();

        // senza selezione vale per la parola sotto il cursore (come Ctrl+Spazio in Word)
        TextRange? letters = editor.Selection.IsEmpty ? WordAt(editor.CaretPosition) : editor.Selection;

        Change(() =>
        {
            letters?.ClearAllProperties();

            foreach (Paragraph row in rows)
            {
                Styler.Undress(row);
            }
        });

        // e per quel che si scrive da qui in poi
        if (editor.Selection.IsEmpty)
        {
            editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            editor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
            editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            editor.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
            editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Palette.Text);
            editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(Doc.TextFonts));
        }

        watcher.PaintAll();
    }

    // ------------------------------------------------------------------ invio

    /// <summary>
    /// Invio: a capo su una riga pulita, sempre. Il testo dopo il cursore scende sulla riga nuova
    /// con i suoi segni, ma la riga non e' piu' titolo, elenco, citazione o codice; se e' vuota
    /// non porta nemmeno grassetto, colore o altro: si riparte da testo normale.
    /// </summary>
    public bool PlainEnter()
    {
        if (editor.Selection.Start is not { Paragraph: { } } || editor.IsReadOnly)
        {
            return false;
        }

        Paragraph? clean = null;
        Paragraph? left = null;

        Change(() =>
        {
            if (!editor.Selection.IsEmpty)
            {
                editor.Selection.Text = string.Empty;
            }

            left = editor.Selection.Start.Paragraph;
            TextPointer at = editor.Selection.Start.InsertParagraphBreak();

            if (at.Paragraph is not { } fresh || Doc.Siblings(fresh) is not { } where)
            {
                return;
            }

            clean = Doc.Row();

            if (Doc.LineOf(fresh).Length > 0)
            {
                foreach (Inline inline in fresh.Inlines.ToList())
                {
                    fresh.Inlines.Remove(inline);
                    clean.Inlines.Add(inline);
                }
            }
            else
            {
                clean.Inlines.Add(new Run());
            }

            where.InsertAfter(fresh, clean);
            where.Remove(fresh);
        });

        if (clean is null)
        {
            return false;
        }

        editor.CaretPosition = clean.ContentStart.GetInsertionPosition(LogicalDirection.Forward);

        // la riga lasciata e' finita: i suoi indirizzi si colorano adesso, non al prossimo giro
        watcher.Touch(left);
        watcher.Touch(clean);
        watcher.Flush();
        return true;
    }

    /// <summary>
    /// Maiusc+Invio: a capo tenendo la formattazione. Un elenco continua con la voce dopo, un
    /// blocco di codice con la riga dopo; altrove la riga nuova ha lo stesso tipo (titolo,
    /// citazione...) e gli stessi segni di quella da cui viene.
    /// </summary>
    public bool KeepEnter()
    {
        if (watcher.CaretRow is { } row && (Looks.IsListed(Looks.Of(row)) || Looks.Of(row) == Look.Code) && Enter())
        {
            return true;
        }

        // a meta' di una voce d'elenco: quel che segue il cursore diventa la voce successiva
        if (watcher.CaretRow is { } item && Looks.HasMarkRun(item) && editor.Selection.IsEmpty && !editor.IsReadOnly
            && editor.CaretPosition is { } middle && Doc.OffsetOf(item, middle) >= Looks.MarkLength(item))
        {
            Look look = Looks.Of(item);
            Paragraph? fresh = null;

            Change(() =>
            {
                fresh = middle.InsertParagraphBreak().Paragraph;

                if (fresh is not null)
                {
                    Styler.Dress(fresh, look, Looks.Counter(item) + 1);
                }
            });

            if (fresh is not null && Doc.PointAt(fresh, Looks.MarkLength(fresh)) is { } start)
            {
                editor.CaretPosition = start;
                WritePlain(fresh);
                watcher.Touch(item);
                watcher.Touch(fresh);
                return true;
            }
        }

        if (editor.CaretPosition is not { Paragraph: { } } caret || editor.IsReadOnly)
        {
            return false;
        }

        Change(() =>
        {
            if (!editor.Selection.IsEmpty)
            {
                editor.Selection.Text = string.Empty;
            }

            editor.CaretPosition = editor.Selection.Start.InsertParagraphBreak();
        });

        return true;
    }

    /// <summary>
    /// Invio sa dove si trova. In un blocco di codice aggiunge una riga al blocco, e su una riga
    /// vuota in fondo lo chiude. Su una voce d'elenco ne apre un'altra, su una voce vuota chiude
    /// l'elenco; dopo un titolo si torna a scrivere normale. Torna false se Invio fa da se'.
    /// </summary>
    public bool Enter()
    {
        if (watcher.CaretRow is not { } row || editor.CaretPosition is not { } caret || Doc.Siblings(row) is not { } where)
        {
            return false;
        }

        Look look = Looks.Of(row);

        if (look == Look.Code)
        {
            return CodeEnter(row, caret, where);
        }

        if (look == Look.Text || Rest(caret, row).Length > 0)
        {
            return false;
        }

        bool listed = Looks.IsListed(look);

        if (listed && Doc.LineOf(row).Length <= Looks.MarkLength(row))
        {
            Change(() => Styler.Undress(row));
            return true;
        }

        Paragraph next = Doc.Row();

        Change(() =>
        {
            where.InsertAfter(row, next);

            if (listed)
            {
                Styler.Dress(next, look, Looks.Counter(row) + 1);
            }
        });

        editor.CaretPosition = next.ContentEnd;
        WritePlain(next);

        return true;
    }

    private bool CodeEnter(Paragraph block, TextPointer caret, BlockCollection where)
    {
        string[] lines = [.. Styler.CodeLines(block)];
        bool atEnd = Rest(caret, block).Length == 0;

        if (atEnd && lines.Length > 0 && lines[^1].Trim().Length == 0)
        {
            Paragraph after = Doc.Row();

            Change(() =>
            {
                while (block.Inlines.LastInline is Run { Text.Length: 0 } empty)
                {
                    block.Inlines.Remove(empty);
                }

                if (block.Inlines.LastInline is LineBreak tail)
                {
                    block.Inlines.Remove(tail);
                }

                where.InsertAfter(block, after);

                // un blocco svuotato non resta a fare da cornice vuota
                if (Doc.LineOf(block).Trim().Length == 0)
                {
                    where.Remove(block);
                }
            });

            editor.CaretPosition = after.ContentStart;
            return true;
        }

        editor.CaretPosition = caret.InsertLineBreak();
        return true;
    }

    /// <summary>
    /// Il cursore appena dopo un segno d'elenco ne erediterebbe il grigio monospazio: si dice
    /// alla selezione vuota come scrivere il prossimo carattere.
    /// </summary>
    public void WritePlain(Paragraph row)
    {
        if (Looks.MarkLength(row) == 0 || !editor.Selection.IsEmpty)
        {
            return;
        }

        editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Palette.Text);
        editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(Doc.TextFonts));
        editor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
    }

    // ------------------------------------------------------------------ i bordi delle righe

    /// <summary>
    /// Il cursore non sta mai dentro il segno di una voce (pallino, numero, casella): se ci
    /// finisce (Home, freccia su, un clic) va all'inizio del testo, e quel che si scrive da li'
    /// e' testo normale, non il grigio del segno. Una selezione dentro una sola riga non prende
    /// il segno: scrivendoci sopra la voce resta una voce.
    /// </summary>
    public void KeepOutOfMark()
    {
        TextSelection selection = editor.Selection;

        if (selection.Start.Paragraph is not { } row || !Looks.HasMarkRun(row))
        {
            return;
        }

        int mark = Looks.MarkLength(row);
        int at = Doc.OffsetOf(row, selection.Start);

        if (at < 0 || at > mark || Doc.PointAt(row, mark) is not { } start)
        {
            return;
        }

        if (selection.IsEmpty)
        {
            if (at < mark)
            {
                editor.CaretPosition = start;
            }

            WritePlain(row);
        }
        else if (at < mark && ReferenceEquals(selection.End.Paragraph, row))
        {
            if (selection.End.CompareTo(start) > 0)
            {
                selection.Select(start, selection.End);
            }
            else
            {
                editor.CaretPosition = start;
            }
        }
    }

    /// <summary>
    /// Backspace all'inizio di una riga che ha un formato lo toglie, invece di fondere la riga
    /// con quella sopra portandosi dietro il pallino: una voce torna testo, un titolo o una
    /// citazione tornano testo, un blocco di codice torna righe. Il Backspace dopo fonde.
    /// Torna true se ha fatto lui.
    /// </summary>
    public bool Backspace()
    {
        if (!editor.Selection.IsEmpty || editor.IsReadOnly || watcher.CaretRow is not { } row || editor.CaretPosition is not { } caret)
        {
            return false;
        }

        int at = Doc.OffsetOf(row, caret);
        Look look = Looks.Of(row);

        if (Looks.HasMarkRun(row) && at <= Looks.MarkLength(row))
        {
            Change(() => Styler.Undress(row));
            editor.CaretPosition = row.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            watcher.Touch(row);
            return true;
        }

        if (at != 0)
        {
            return false;
        }

        if (look is Look.Heading1 or Look.Heading2 or Look.Heading3 or Look.Quote)
        {
            Change(() => Styler.Undress(row));
            watcher.Touch(row);
            return true;
        }

        if (look == Look.Code)
        {
            ToggleCode();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Canc in fondo a una riga, quando sotto c'e' una voce d'elenco: la voce sale senza il suo
    /// segno. Qui si toglie solo il segno; a fondere le righe ci pensa l'editor (torna sempre
    /// false: il tasto prosegue).
    /// </summary>
    public bool DeleteForward()
    {
        if (!editor.Selection.IsEmpty || editor.IsReadOnly || watcher.CaretRow is not { } row || editor.CaretPosition is not { } caret
            || Rest(caret, row).Length > 0)
        {
            return false;
        }

        if (row.ElementEnd.GetNextInsertionPosition(LogicalDirection.Forward)?.Paragraph is { } below
            && !ReferenceEquals(below, row) && Looks.HasMarkRun(below))
        {
            Change(() => Styler.Undress(below));
        }

        return false;
    }

    /// <summary>
    /// Freccia a sinistra dall'inizio del testo di una voce: si va in fondo alla riga sopra,
    /// scavalcando il segno (con Maiusc si estende la selezione fin li').
    /// </summary>
    public bool Left(bool extend)
    {
        TextSelection selection = editor.Selection;

        if (watcher.CaretRow is not { } row || !Looks.HasMarkRun(row) || (!selection.IsEmpty && !extend))
        {
            return false;
        }

        if (Doc.OffsetOf(row, selection.Start) != Looks.MarkLength(row)
            || (extend && !selection.IsEmpty && !ReferenceEquals(selection.End.Paragraph, row)))
        {
            return false;
        }

        if (row.ElementStart.GetNextInsertionPosition(LogicalDirection.Backward) is not { } above)
        {
            return true;
        }

        if (extend)
        {
            selection.Select(above, selection.End);
        }
        else
        {
            editor.CaretPosition = above;
        }

        return true;
    }

    /// <summary>
    /// Tre trattini (o asterischi, o trattini bassi) da soli su una riga e Invio: la riga diventa
    /// una separazione, come in Markdown. Torna true se l'ha fatto.
    /// </summary>
    public bool RuleFromDashes()
    {
        if (!editor.Selection.IsEmpty || editor.IsReadOnly || watcher.CaretRow is not { } row || Looks.Of(row) != Look.Text
            || row.Parent is TableCell || Doc.LineOf(row).Trim() is not ("---" or "***" or "___") || Doc.Siblings(row) is not { } where)
        {
            return false;
        }

        Paragraph after = Doc.Row();

        Change(() =>
        {
            Styler.Dress(row, Look.Rule);
            where.InsertAfter(row, after);
        });

        editor.CaretPosition = after.ContentStart;
        return true;
    }

    // ------------------------------------------------------------------ collegamenti

    public void Link()
    {
        if (AskForLink is null)
        {
            return;
        }

        Hyperlink? here = editor.Selection.IsEmpty ? LinkAt(editor.Selection.Start) : Covering(editor.Selection);

        if (here is null && editor.Selection.Start.Paragraph != editor.Selection.End.Paragraph)
        {
            Refused?.Invoke("Un collegamento sta su una riga sola: restringi la selezione.");
            return;
        }

        string shown = here is null ? Trimmed(editor.Selection).Text : Doc.TextOf(here);

        if (AskForLink(shown, here?.NavigateUri?.ToString() ?? string.Empty) is not { } choice)
        {
            return;
        }

        try
        {
            Change(() => Relink(here, choice));
        }
        catch (Exception problem) when (problem is ArgumentException or InvalidOperationException)
        {
            Refused?.Invoke("Il collegamento non si e' potuto applicare a questa selezione.");
        }
    }

    private void Relink(Hyperlink? here, LinkChoice choice)
    {
        if (choice.Removed)
        {
            Unlink(here);
            return;
        }

        if (here is not null)
        {
            here.NavigateUri = new Uri(choice.Address);
            here.ToolTip = choice.Address;
            return;
        }

        if (!editor.Selection.IsEmpty)
        {
            TextRange words = Trimmed(editor.Selection);
            Dress(new Hyperlink(words.Start, words.End), choice);
            return;
        }

        // niente di selezionato: il testo arriva dalla finestra e si mette dov'e' il cursore
        if (choice.Text.Length == 0 || editor.CaretPosition is not { Paragraph: { } row } caret)
        {
            return;
        }

        Run word = new(choice.Text);

        if (caret.GetAdjacentElement(LogicalDirection.Backward) is Inline before && before.Parent == row)
        {
            row.Inlines.InsertAfter(before, word);
        }
        else if (row.Inlines.FirstInline is { } first)
        {
            row.Inlines.InsertBefore(first, word);
        }
        else
        {
            row.Inlines.Add(word);
        }

        Dress(new Hyperlink(word.ContentStart, word.ContentEnd), choice);
    }

    private void Dress(Hyperlink link, LinkChoice choice)
    {
        link.NavigateUri = new Uri(choice.Address);
        link.ToolTip = choice.Address + "  (Ctrl+clic per aprire)";
        link.Foreground = Palette.Link;
        editor.Selection.Select(link.ContentStart, link.ContentEnd);
    }

    private static void Unlink(Hyperlink? link)
    {
        InlineCollection? around = link?.Parent switch
        {
            Paragraph paragraph => paragraph.Inlines,
            Span span => span.Inlines,
            _ => null
        };

        if (link is null || around is null)
        {
            return;
        }

        foreach (Inline kept in link.Inlines.ToArray())
        {
            link.Inlines.Remove(kept);
            around.InsertBefore(link, kept);
        }

        around.Remove(link);
    }

    public static Hyperlink? LinkAt(TextPointer? point)
    {
        for (DependencyObject? up = point?.Parent; up is not null; up = LogicalTreeHelper.GetParent(up))
        {
            if (up is Hyperlink inside)
            {
                return inside;
            }
        }

        return point?.Paragraph?.Inlines.OfType<Hyperlink>().FirstOrDefault(link =>
            link.ElementStart.CompareTo(point) <= 0 && link.ElementEnd.CompareTo(point) >= 0);
    }

    private static Hyperlink? Covering(TextSelection selection) =>
        LinkAt(selection.Start) is { } link
        && link.ElementStart.CompareTo(selection.Start) <= 0
        && link.ElementEnd.CompareTo(selection.End) >= 0
            ? link
            : null;

    public static bool Opens(Hyperlink link) => WebAddress.Safe(link.NavigateUri) && Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

    // ------------------------------------------------------------------ attrezzi

    /// <summary>Le righe toccate dalla selezione (o quella del cursore).</summary>
    public Paragraph[] Chosen()
    {
        TextSelection selection = editor.Selection;

        if (selection.IsEmpty)
        {
            return watcher.CaretRow is { } here ? [here] : [];
        }

        if (selection.Start.Paragraph is not { } first || selection.End.Paragraph is not { } last)
        {
            return [];
        }

        Paragraph[] all = Doc.Rows(editor.Document);
        int from = Array.IndexOf(all, first);
        int to = Array.IndexOf(all, last);

        // una selezione che finisce all'inizio di una riga non la comprende
        if (to > from && selection.End.CompareTo(last.ContentStart) <= 0)
        {
            to--;
        }

        return from < 0 || to < from ? [] : all[from..(to + 1)];
    }

    public void Change(Action change)
    {
        Typing.Flush(editor);
        watcher.Suspended = true;

        try
        {
            editor.BeginChange();

            try
            {
                change();
            }
            finally
            {
                editor.EndChange();
            }
        }
        catch (InvalidOperationException)
        {
            Refused?.Invoke("Questa parte del documento non si lascia cambiare cosi'.");
        }
        finally
        {
            watcher.Suspended = false;
        }
    }

    private static string Rest(TextPointer caret, Paragraph row)
    {
        try
        {
            return new TextRange(caret, row.ContentEnd).Text.Trim('\r', '\n');
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
