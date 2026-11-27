namespace DuckNote.Notes.Calendar;

/// <summary>
/// L'orologio dei promemoria, finche' la nota e' aperta: a ogni battito dice quali promemoria
/// sono scattati dall'ultimo battito. Quelli scattati mentre DuckNote era chiuso (o la nota
/// bloccata) non suonano uno per uno: li racconta il riepilogo all'apertura. "Rimanda" fa
/// tornare un promemoria piu' tardi; se intanto la voce e' fatta o tolta, non torna.
/// </summary>
public sealed class ReminderClock
{
    private readonly Dictionary<string, (Occurrence Occurrence, DateTime Until)> _snoozed = new(StringComparer.Ordinal);
    private DateTime _last;

    public bool IsRunning => _last != default;

    /// <summary>Si parte da adesso: quello che e' gia' scattato e' per il riepilogo.</summary>
    public void Start(DateTime now)
    {
        _last = now;
        _snoozed.Clear();
    }

    /// <summary>La nota si chiude o si blocca: niente piu' promemoria, e si dimenticano quelli rimandati.</summary>
    public void Stop()
    {
        _last = default;
        _snoozed.Clear();
    }

    public IReadOnlyList<AgendaAlert> Tick(Agenda agenda, DateTime now)
    {
        if (!IsRunning)
        {
            return [];
        }

        List<AgendaAlert> due = [];

        if (now > _last)
        {
            // della stessa volta basta l'ultimo promemoria (dopo una pausa lunga ne sono passati piu' d'uno)
            due.AddRange(agenda.Alerts(_last, now).GroupBy(alert => alert.Occurrence.Key).Select(group => group.Last()));
        }

        // un orologio tornato indietro (ora legale, data cambiata a mano) riparte da qui
        _last = now;

        foreach ((string key, (Occurrence occurrence, DateTime until)) in _snoozed.ToList())
        {
            if (until > now)
            {
                continue;
            }

            _snoozed.Remove(key);

            if (Current(agenda, occurrence) is { IsDone: false } fresh && due.All(alert => alert.Occurrence.Key != key))
            {
                due.Add(new AgendaAlert(fresh, 0, until));
            }
        }

        return [.. due.OrderBy(alert => alert.At)];
    }

    /// <summary>Il promemoria di questa volta torna a <paramref name="until"/>.</summary>
    public void Snooze(Occurrence occurrence, DateTime until) => _snoozed[occurrence.Key] = (occurrence, until);

    /// <summary>La volta com'e' adesso nel calendario: la voce puo' essere cambiata, o non esserci piu'.</summary>
    private static Occurrence? Current(Agenda agenda, Occurrence occurrence)
    {
        AgendaItem? item = agenda.AllItems().FirstOrDefault(candidate => candidate.Id == occurrence.Item.Id);
        return item is not null && Agenda.Dates(item, occurrence.Date, occurrence.Date).Any() ? new Occurrence(item, occurrence.Date) : null;
    }
}
