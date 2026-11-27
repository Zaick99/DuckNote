using System.Globalization;

namespace DuckNote.Notes.Calendar;

/// <summary>
/// Le parole degli avvisi del calendario, le stesse sul Mac e su Windows: il promemoria che
/// scatta ("oggi alle 09:30 · fra 15 minuti · Switch core") e il riepilogo all'apertura.
/// </summary>
public static class AgendaText
{
    private static readonly string[] Months = ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"];
    private static readonly string[] Weekdays = ["domenica", "lunedì", "martedì", "mercoledì", "giovedì", "venerdì", "sabato"];

    /// <summary>Il titolo e il testo dell'avviso di un promemoria.</summary>
    public static (string Title, string Text) Alert(AgendaAlert alert, DateTime now)
    {
        Occurrence occurrence = alert.Occurrence;
        AgendaItem item = occurrence.Item;
        string title = item.Title.Length > 0 ? item.Title : "Senza titolo";
        string left = occurrence.AllDay ? string.Empty : Left(occurrence.Start - now);

        string[] parts =
        [
            item.Certificate ? "Certificato" : AgendaKinds.Label(item.Kind),
            When(occurrence, DateOnly.FromDateTime(now)),
            left,
            item.Link?.Shown ?? string.Empty
        ];

        return (title, string.Join(" · ", parts.Where(part => part.Length > 0)));
    }

    /// <summary>"oggi alle 09:30", "domani, tutto il giorno", "mercoledì 21 aprile".</summary>
    public static string When(Occurrence occurrence, DateOnly today)
    {
        int days = occurrence.Date.DayNumber - today.DayNumber;
        string day = days switch
        {
            0 => "oggi",
            1 => "domani",
            -1 => "ieri",
            _ => Day(occurrence.Date, today)
        };

        if (occurrence.Item.Time is { } time)
        {
            return $"{day} alle {time.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        }

        return days is >= -1 and <= 1 ? $"{day}, tutto il giorno" : day;
    }

    /// <summary>"mercoledì 21 aprile", con l'anno se non e' quello di oggi.</summary>
    public static string Day(DateOnly day, DateOnly today) =>
        $"{Weekdays[(int)day.DayOfWeek]} {day.Day} {Months[day.Month - 1]}{(day.Year != today.Year ? $" {day.Year}" : string.Empty)}";

    /// <summary>"fra 15 minuti", "fra 2 ore", "adesso", "iniziato da 5 minuti".</summary>
    public static string Left(TimeSpan span)
    {
        int minutes = (int)Math.Round(span.TotalMinutes);

        if (Math.Abs(minutes) < 1)
        {
            return "adesso";
        }

        string amount = Math.Abs(minutes) switch
        {
            < 60 => Plural(Math.Abs(minutes), "minuto", "minuti"),
            < 1440 => Plural((int)Math.Round(Math.Abs(minutes) / 60.0), "ora", "ore"),
            _ => Plural((int)Math.Round(Math.Abs(minutes) / 1440.0), "giorno", "giorni")
        };

        return minutes > 0 ? $"fra {amount}" : $"iniziato da {amount}";
    }

    /// <summary>Il titolo del riepilogo: corto, i conti stanno nel testo.</summary>
    public static string SummaryTitle(AgendaSummary summary) =>
        summary.Overdue.Count > 0 ? "Calendario: qualcosa e' in ritardo"
        : summary.Today.Count > 0 ? "Calendario: oggi"
        : "Calendario";

    /// <summary>Il testo del riepilogo: le prime voci di oggi e di quelle rimaste indietro, poi i conti.</summary>
    public static string SummaryText(AgendaSummary summary, DateOnly today)
    {
        List<string> lines = [];

        if (summary.Today.Count > 0)
        {
            lines.Add("Oggi: " + Names(summary.Today, withTime: true) + ".");
        }

        if (summary.Overdue.Count > 0)
        {
            lines.Add("In ritardo: " + Names(summary.Overdue, withTime: false) + ".");
        }

        if (summary.Week.Count > 0)
        {
            lines.Add($"Nei prossimi 7 giorni: {Plural(summary.Week.Count, "voce", "voci")}.");
        }

        if (summary.Certificates.FirstOrDefault() is { } first)
        {
            int days = first.Date.DayNumber - today.DayNumber;
            string host = first.Item.Link?.Host ?? first.Item.Title;
            string when = days == 0 ? "scade oggi" : $"scade fra {Plural(days, "giorno", "giorni")}";
            lines.Add(summary.Certificates.Count == 1
                ? $"Il certificato di {host} {when}."
                : $"Certificati in scadenza: {summary.Certificates.Count}, il primo ({host}) {when}.");
        }

        return string.Join(" ", lines);
    }

    private static string Names(IReadOnlyList<Occurrence> list, bool withTime)
    {
        IEnumerable<string> names = list.Take(2).Select(occurrence =>
        {
            string name = occurrence.Item.Title.Length > 0 ? occurrence.Item.Title : "Senza titolo";
            return withTime && occurrence.Item.Time is { } time ? $"{name} ({time.ToString("HH:mm", CultureInfo.InvariantCulture)})" : name;
        });

        string text = string.Join(", ", names);
        return list.Count > 2 ? $"{text} e {Plural(list.Count - 2, "altra", "altre")}" : text;
    }

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
}
