using System.Text;
using System.Text.Json.Nodes;
using DuckNote.Notes.Calendar;
using DuckNote.Notes.Notebook;

namespace DuckNote.Notes.Tests;

public class AgendaTests
{
    private static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd");

    private static DateTime T(string iso) => DateTime.ParseExact(iso, "yyyy-MM-dd HH:mm", null);

    private static AgendaItem Item(string date, string? time = null, string repeat = "", int every = 1, string? until = null, string kind = AgendaKinds.Event, params int[] remind)
    {
        AgendaItem item = new()
        {
            Id = Agenda.NewId(),
            Kind = kind,
            Title = "Prova",
            Date = D(date),
            Time = time is null ? null : TimeOnly.ParseExact(time, "HH:mm"),
            Repeat = repeat,
            Every = every,
            Until = until is null ? null : D(until),
            Remind = [.. remind]
        };
        return item;
    }

    private static string[] Days(AgendaItem item, string from, string to) =>
        [.. Agenda.Dates(item, D(from), D(to)).Select(Agenda.Iso)];

    [Fact]
    public void The_calendar_travels_inside_the_note_file()
    {
        Agenda agenda = new();
        AgendaItem renew = Item("2027-04-14", kind: AgendaKinds.Deadline, remind: [43200, 1440]);
        renew.Title = "Rinnovare il certificato di www.example.com";
        renew.Notes = "Ordine sul portale\nPoi installarlo sul proxy";
        renew.Link = new AgendaLink(AgendaLink.HostKind, "www.example.com", Name: "Sito");
        AgendaItem meeting = Item("2027-01-04", "09:30", "week", 2, "2027-06-30", remind: [15]);
        meeting.Skip.Add(D("2027-01-18"));
        meeting.Done.Add(D("2027-01-04"));
        meeting.Link = new AgendaLink(AgendaLink.NodeKind, "192.168.10.2", "mtest", "ncore", "Switch core");
        agenda.Items.AddRange([renew, meeting]);
        agenda.Learn([new CertificateSeen("nas01.example.local", D("2027-02-01"), "CN=nas01", "Example CA")]);
        agenda.IgnoredHosts.Add("printer.example.local");

        NoteArchive archive = new();
        archive.Pages.Add(new ArchivedPage("aaa111", string.Empty, [1]));
        archive.Calendar = agenda.Write();

        Agenda back = Agenda.Read(NoteArchive.Load(archive.Save())!.Calendar);

        Assert.Equal(2, back.Items.Count);
        AgendaItem first = back.Items[0];
        Assert.Equal(renew.Id, first.Id);
        Assert.Equal(AgendaKinds.Deadline, first.Kind);
        Assert.Null(first.Time);
        Assert.Equal("Ordine sul portale\nPoi installarlo sul proxy", first.Notes);
        Assert.Equal([43200, 1440], first.Remind);
        Assert.Equal("www.example.com", first.Link?.Host);

        AgendaItem second = back.Items[1];
        Assert.Equal(new TimeOnly(9, 30), second.Time);
        Assert.Equal(("week", 2, D("2027-06-30")), (second.Repeat, second.Every, second.Until!.Value));
        Assert.Equal([D("2027-01-18")], second.Skip);
        Assert.Equal([D("2027-01-04")], second.Done);
        Assert.Equal(("ncore", "Switch core"), (second.Link!.Node, second.Link.Shown));

        Assert.Single(back.Certificates);
        Assert.Contains("printer.example.local", back.IgnoredHosts);
    }

    [Fact]
    public void An_empty_calendar_adds_nothing_to_the_note() => Assert.Null(new Agenda().Write());

    [Theory]
    [InlineData("")]
    [InlineData("non e' json")]
    [InlineData("""{ "items": 5, "certs": [] }""")]
    [InlineData("""{ "items": [ { "id": "x", "date": "ieri" }, { "date": "2027-02-30" }, null ] }""")]
    public void A_broken_file_is_an_empty_calendar(string json)
    {
        Agenda agenda = Agenda.Read(Encoding.UTF8.GetBytes(json));
        Assert.Empty(agenda.Items);
        Assert.False(agenda.CertificatesOff);
    }

    [Fact]
    public void Written_items_are_tidied_on_reading()
    {
        Agenda agenda = Agenda.Read(Encoding.UTF8.GetBytes(
            """{ "items": [ { "id": "doppio", "date": "2027-03-01", "kind": "boh", "time": "25:00", "remind": [60, 60, -5, 99999999, 0], "repeat": "ogni tanto", "until": "2027-01-01", "done": ["2026-01-01", "2027-03-01"] }, { "id": "doppio", "date": "2027-03-02", "repeat": "month", "every": 500, "until": "2026-01-01" } ] }"""));

        Assert.Equal(2, agenda.Items.Count);
        AgendaItem first = agenda.Items[0];
        Assert.Equal(AgendaKinds.Event, first.Kind);
        Assert.Null(first.Time);
        Assert.Equal([60, 0], first.Remind);
        Assert.Equal(string.Empty, first.Repeat);
        Assert.Null(first.Until);
        Assert.Equal([D("2027-03-01")], first.Done);

        AgendaItem second = agenda.Items[1];
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(99, second.Every);
        Assert.Null(second.Until);
    }

    [Fact]
    public void Monthly_items_keep_the_last_day_of_short_months_and_come_back_to_the_31st() =>
        Assert.Equal(["2027-01-31", "2027-02-28", "2027-03-31", "2027-04-30", "2027-05-31"],
            Days(Item("2027-01-31", repeat: "month"), "2027-01-01", "2027-05-31"));

    [Fact]
    public void Yearly_items_on_february_29_fall_on_the_28th_in_other_years() =>
        Assert.Equal(["2025-02-28", "2026-02-28", "2027-02-28", "2028-02-29"],
            Days(Item("2024-02-29", repeat: "year"), "2025-01-01", "2028-12-31"));

    [Fact]
    public void Every_other_week_until_a_day_without_the_skipped_ones()
    {
        AgendaItem item = Item("2027-01-04", "09:30", "week", 2, "2027-03-01");
        item.Skip.Add(D("2027-02-01"));

        Assert.Equal(["2027-01-04", "2027-01-18", "2027-02-15", "2027-03-01"], Days(item, "2026-12-01", "2027-12-31"));
        Assert.Equal(["2027-02-15"], Days(item, "2027-02-02", "2027-02-20"));
    }

    [Fact]
    public void Daily_items_start_counting_from_the_asked_range()
    {
        AgendaItem item = Item("2020-01-01", repeat: "day", every: 3);

        // dal 2020-01-01 al 2027-04-12 ci sono 2658 giorni, un multiplo di 3
        Assert.Equal(["2027-04-12", "2027-04-15"], Days(item, "2027-04-12", "2027-04-17"));
        Assert.Equal(["2027-04-15"], Days(item, "2027-04-13", "2027-04-17"));
        Assert.Empty(Days(Item("2027-04-14"), "2027-04-15", "2027-05-01"));
        Assert.Equal(["2027-04-14"], Days(Item("2027-04-14"), "2027-04-14", "2027-04-14"));
    }

    [Fact]
    public void Reminders_fire_before_the_start_and_all_day_items_count_from_nine()
    {
        Agenda agenda = new();
        AgendaItem call = Item("2027-04-14", "10:00", remind: [60, 0]);
        AgendaItem deadline = Item("2027-04-14", kind: AgendaKinds.Deadline, remind: [1440]);
        agenda.Items.AddRange([call, deadline]);

        AgendaAlert early = Assert.Single(agenda.Alerts(T("2027-04-14 08:59"), T("2027-04-14 09:00")));
        Assert.Equal((call.Id, 60), (early.Occurrence.Item.Id, early.Before));

        AgendaAlert dayBefore = Assert.Single(agenda.Alerts(T("2027-04-13 08:00"), T("2027-04-13 09:30")));
        Assert.Equal(deadline.Id, dayBefore.Occurrence.Item.Id);
        Assert.Equal(T("2027-04-13 09:00"), dayBefore.At);

        call.Done.Add(D("2027-04-14"));
        Assert.Empty(agenda.Alerts(T("2027-04-14 08:00"), T("2027-04-14 11:00")));
    }

    [Fact]
    public void The_summary_tells_what_is_late_today_this_week_and_which_certificates_expire()
    {
        Agenda agenda = new();
        AgendaItem late = Item("2027-04-12", kind: AgendaKinds.Todo);
        AgendaItem pastMeeting = Item("2027-04-12", "15:00");
        AgendaItem today = Item("2027-04-14", "10:00");
        AgendaItem soon = Item("2027-04-18", kind: AgendaKinds.Deadline);
        agenda.Items.AddRange([late, pastMeeting, today, soon]);
        agenda.Learn([new CertificateSeen("www.example.com", D("2027-05-04"))]);

        AgendaSummary summary = agenda.Summary(T("2027-04-14 08:00"));

        Assert.Equal([late.Id], summary.Overdue.Select(o => o.Item.Id));
        Assert.Equal([today.Id], summary.Today.Select(o => o.Item.Id));
        Assert.Equal([soon.Id], summary.Week.Select(o => o.Item.Id));
        Assert.Equal(["cert:www.example.com"], summary.Certificates.Select(o => o.Item.Id));
        Assert.Equal(2, agenda.Badge(T("2027-04-14 08:00")));

        late.Done.Add(D("2027-04-12"));
        Assert.Equal(1, agenda.Badge(T("2027-04-14 08:00")));
    }

    [Fact]
    public void Certificates_seen_by_scans_become_deadlines_that_can_be_renewed_or_ignored()
    {
        Agenda agenda = new();
        Assert.True(agenda.Learn([new CertificateSeen("www.example.com", D("2027-04-14"), "CN=www.example.com", "Example CA")]));
        Assert.False(agenda.Learn([new CertificateSeen("WWW.example.com", D("2027-04-14"), "CN=www.example.com", "Example CA")]));

        AgendaItem cert = Assert.Single(agenda.CertificateItems());
        Assert.Equal(("cert:www.example.com", AgendaKinds.Deadline, D("2027-04-14")), (cert.Id, cert.Kind, cert.Date));
        Assert.Equal(Agenda.CertificateDefaults, cert.Remind);
        Assert.Equal("www.example.com", cert.Link?.Host);

        Assert.True(agenda.SetDone(cert.Id, D("2027-04-14"), true));
        Assert.Contains(D("2027-04-14"), Assert.Single(agenda.CertificateItems()).Done);

        // rinnovato: la scadenza nuova non e' fatta
        Assert.True(agenda.Learn([new CertificateSeen("www.example.com", D("2028-04-14"))]));
        Assert.Empty(Assert.Single(agenda.CertificateItems()).Done);

        Assert.True(agenda.Apply(new JsonObject { ["op"] = "ignore", ["host"] = "www.example.com", ["on"] = true }));
        Assert.Empty(agenda.CertificateItems());

        Assert.True(agenda.Apply(new JsonObject { ["op"] = "ignore", ["host"] = "www.example.com", ["on"] = false }));
        Assert.True(agenda.Apply(new JsonObject { ["op"] = "certs", ["off"] = true }));
        Assert.Empty(agenda.AllItems());
    }

    [Fact]
    public void Changes_from_the_page_add_replace_skip_mark_and_remove()
    {
        Agenda agenda = new();
        JsonObject written = new() { ["kind"] = "todo", ["title"] = "Backup", ["date"] = "2027-04-14", ["repeat"] = "week" };

        Assert.True(agenda.Apply(new JsonObject { ["op"] = "put", ["item"] = written }));
        AgendaItem item = Assert.Single(agenda.Items);
        Assert.Matches("^[a-z0-9]{2,24}$", item.Id);

        JsonObject changed = Agenda.WriteItem(item);
        changed["title"] = "Backup del NAS";
        Assert.True(agenda.Apply(new JsonObject { ["op"] = "put", ["item"] = changed }));
        Assert.Equal("Backup del NAS", Assert.Single(agenda.Items).Title);

        Assert.True(agenda.Apply(new JsonObject { ["op"] = "done", ["id"] = item.Id, ["date"] = "2027-04-21", ["on"] = true }));
        Assert.True(agenda.Apply(new JsonObject { ["op"] = "skip", ["id"] = item.Id, ["date"] = "2027-04-28" }));
        Assert.Equal(["2027-04-14", "2027-04-21", "2027-05-05"], Days(agenda.Items[0], "2027-04-01", "2027-05-06"));
        Assert.Contains(D("2027-04-21"), agenda.Items[0].Done);

        Assert.False(agenda.Apply(new JsonObject { ["op"] = "put", ["item"] = new JsonObject { ["id"] = "cert:x", ["date"] = "2027-01-01" } }));
        Assert.False(agenda.Apply(new JsonObject { ["op"] = "boh" }));

        Assert.True(agenda.Apply(new JsonObject { ["op"] = "remove", ["id"] = item.Id }));
        Assert.Empty(agenda.Items);
    }

    [Fact]
    public void The_clock_rings_once_skips_what_happened_while_closed_and_brings_back_snoozed_reminders()
    {
        Agenda agenda = new();
        AgendaItem call = Item("2027-04-14", "10:00", remind: [15]);
        AgendaItem missed = Item("2027-04-14", "08:00", remind: [0]);
        agenda.Items.AddRange([call, missed]);

        ReminderClock clock = new();
        clock.Start(T("2027-04-14 09:00"));

        Assert.Empty(clock.Tick(agenda, T("2027-04-14 09:30")));
        AgendaAlert ring = Assert.Single(clock.Tick(agenda, T("2027-04-14 09:45")));
        Assert.Equal(call.Id, ring.Occurrence.Item.Id);
        Assert.Empty(clock.Tick(agenda, T("2027-04-14 09:46")));

        clock.Snooze(ring.Occurrence, T("2027-04-14 09:55"));
        Assert.Empty(clock.Tick(agenda, T("2027-04-14 09:50")));
        Assert.Equal(call.Id, Assert.Single(clock.Tick(agenda, T("2027-04-14 09:56"))).Occurrence.Item.Id);

        // fatto mentre era rimandato: non torna
        clock.Snooze(ring.Occurrence, T("2027-04-14 10:05"));
        call.Done.Add(D("2027-04-14"));
        Assert.Empty(clock.Tick(agenda, T("2027-04-14 10:06")));

        clock.Stop();
        Assert.Empty(clock.Tick(agenda, T("2027-04-15 10:00")));
    }
}

public class AgendaTextTests
{
    private static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd");

    private static DateTime T(string iso) => DateTime.ParseExact(iso, "yyyy-MM-dd HH:mm", null);

    [Fact]
    public void An_alert_says_what_when_and_with_what()
    {
        AgendaItem call = new()
        {
            Id = "e1",
            Title = "Riunione con il fornitore",
            Date = D("2027-04-14"),
            Time = new TimeOnly(9, 30),
            Link = new AgendaLink(AgendaLink.NodeKind, "192.168.10.2", "m1", "ncore", "Switch core")
        };

        (string title, string text) = AgendaText.Alert(new AgendaAlert(new Occurrence(call, D("2027-04-14")), 15, T("2027-04-14 09:15")), T("2027-04-14 09:15"));

        Assert.Equal("Riunione con il fornitore", title);
        Assert.Equal("Appuntamento · oggi alle 09:30 · fra 15 minuti · Switch core", text);
    }

    [Fact]
    public void All_day_items_and_far_days_read_naturally()
    {
        AgendaItem deadline = new() { Id = "e2", Kind = AgendaKinds.Deadline, Title = "Dominio", Date = D("2027-04-15") };
        Assert.Equal("domani, tutto il giorno", AgendaText.When(new Occurrence(deadline, D("2027-04-15")), D("2027-04-14")));
        Assert.Equal("mercoledì 21 aprile", AgendaText.When(new Occurrence(deadline, D("2027-04-21")), D("2027-04-14")));
        Assert.Equal("venerdì 7 gennaio 2028", AgendaText.Day(D("2028-01-07"), D("2027-04-14")));
        Assert.Equal("iniziato da 5 minuti", AgendaText.Left(TimeSpan.FromMinutes(-5)));
        Assert.Equal("fra 2 ore", AgendaText.Left(TimeSpan.FromMinutes(118)));
    }

    [Fact]
    public void The_summary_names_the_first_items_and_counts_the_rest()
    {
        Agenda agenda = new();
        agenda.Items.AddRange([
            new AgendaItem { Id = "a1", Title = "Riunione", Date = D("2027-04-14"), Time = new TimeOnly(9, 30) },
            new AgendaItem { Id = "a2", Title = "Corso", Date = D("2027-04-14"), Time = new TimeOnly(14, 0) },
            new AgendaItem { Id = "a3", Title = "Pranzo", Date = D("2027-04-14"), Time = new TimeOnly(13, 0) },
            new AgendaItem { Id = "a4", Kind = AgendaKinds.Todo, Title = "Toner", Date = D("2027-04-12") },
            new AgendaItem { Id = "a5", Title = "Dentista", Date = D("2027-04-18") }
        ]);
        agenda.Learn([new CertificateSeen("www.example.com", D("2027-04-20"))]);

        AgendaSummary summary = agenda.Summary(T("2027-04-14 08:00"));

        Assert.Equal("Calendario: qualcosa e' in ritardo", AgendaText.SummaryTitle(summary));
        Assert.Equal("Oggi: Riunione (09:30), Pranzo (13:00) e 1 altra. In ritardo: Toner. Nei prossimi 7 giorni: 2 voci. Il certificato di www.example.com scade fra 6 giorni.",
            AgendaText.SummaryText(summary, D("2027-04-14")));
    }
}
