using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using DuckNote.App.Editor;
using DuckNote.App.Models;

namespace DuckNote.App;

public partial class MainWindow
{
    private static readonly char[] IgnoredSeparators = [',', ';', ' ', '\t', '\n', '\r'];

    private void SetUpHostMenus()
    {
        HostMenu.Opened += (_, _) => DressHostMenu();
        HostMenuIgnore.Click += (_, _) => StopWatching(SelectedHost());

        EdMenu.Opened += (_, _) => DressEditorMenu();
        EdMenuPing.Click += (_, _) => AnalyseNow(_hostInSight);
        EdMenuDetails.Click += (_, _) => ShowDetails(_hostInSight);
        EdMenuWatch.Click += (_, _) => ToggleWatching(_hostInSight);
        EdMenuCopyHost.Click += (_, _) => CopyHost(_hostInSight);

        EdMenuCut.Click += (_, _) => Editor.Cut();
        EdMenuCopy.Click += (_, _) => Editor.Copy();
        EdMenuPaste.Click += (_, _) => Editor.Paste();
        EdMenuPasteRaw.Click += (_, _) => PastePlain();
        EdMenuAll.Click += (_, _) => Editor.SelectAll();

        Editor.ContextMenuOpening += (_, _) => _hostInSight = HostAt(Mouse.GetPosition(Editor));
    }

    private string? _hostInSight;

    private void DressHostMenu()
    {
        string? host = SelectedHost();

        HostMenuIgnore.IsEnabled = host is not null && !_session.IsIgnored(host);
        HostMenuIgnore.Header = host is null ? "Non analizzare" : $"Non analizzare {host}";

        HostMenuIgnored.Items.Clear();
        HostMenuIgnored.IsEnabled = _session.Ignored.Count > 0;

        foreach (string ignored in _session.Ignored)
        {
            MenuItem entry = new() { Header = ignored, ToolTip = "Torna ad analizzarlo" };
            entry.Click += (_, _) => ResumeWatching(ignored);
            HostMenuIgnored.Items.Add(entry);
        }
    }

    private void DressEditorMenu()
    {
        bool found = _hostInSight is not null;

        EdMenuPing.Visibility = Show(found);
        EdMenuDetails.Visibility = Show(found);
        EdMenuWatch.Visibility = Show(found);
        EdMenuCopyHost.Visibility = Show(found);
        EdMenuSep.Visibility = Show(found);

        if (_hostInSight is not { } host)
        {
            return;
        }

        EdMenuPing.Header = $"Analizza adesso {host}";
        EdMenuDetails.Header = $"Mostra dettagli di {host}";
        EdMenuCopyHost.Header = $"Copia {host}";
        EdMenuWatch.Header = _session.IsIgnored(host)
            ? $"Analizza di nuovo {host}"
            : $"Non analizzare piu' {host}";
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private string? SelectedHost() => SideList.SelectedItem switch
    {
        SideItem { IP.Length: > 0 } item => item.IP,
        _ => (NetGrid.SelectedItem as ScanRow)?.IP
    };

    private string? HostAt(Point spot)
    {
        if (!Editor.Selection.IsEmpty && Only(Editor.Selection.Text) is { } picked)
        {
            return picked;
        }

        if (Editor.GetPositionFromPoint(spot, snapToText: true) is not { Paragraph: { } row } point)
        {
            return null;
        }

        string line = LiveFormatter.TextOf(row);
        int at = Offset(row, point);

        foreach (HostToken token in HostTokens.Find(line))
        {
            if (at >= token.Start && at <= token.Start + token.Length)
            {
                return token.Value;
            }
        }

        return null;
    }

    private static int Offset(Paragraph row, TextPointer point)
    {
        try
        {
            return new TextRange(row.ContentStart, point).Text.Length;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static string? Only(string text)
    {
        IReadOnlyList<HostToken> found = HostTokens.Find(text.Trim());

        return found.Count == 1 && found[0].Length == text.Trim().Length ? found[0].Value : null;
    }

    private async void AnalyseNow(string? host)
    {
        if (host is null)
        {
            return;
        }

        if (_session.Watch(host))
        {
            KeepIgnored();
        }

        _session.EnsurePending([host]);
        RefreshCounts();

        await ScanAsync(host, keepExisting: true);
    }

    private void ShowDetails(string? host)
    {
        if (host is null)
        {
            return;
        }

        if (_session.IsIgnored(host))
        {
            StatusText.Text = $"{host} e' fra gli host da non analizzare: riprendilo per vederne i dettagli.";
            return;
        }

        _session.EnsurePending([host]);
        RefreshCounts();

        if (Known(host) is not { } row)
        {
            StatusText.Text = $"Di {host} non c'e' ancora nulla da mostrare.";
            return;
        }

        _syncingSelection = true;
        try
        {
            NetGrid.SelectedItem = row;
            NetGrid.ScrollIntoView(row);
        }
        finally
        {
            _syncingSelection = false;
        }

        Reveal(row);
    }

    private ScanRow? Known(string host) =>
        _session.Rows.FirstOrDefault(row =>
            string.Equals(row.IP, host, StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.NoteKey, host, StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Hostname, host, StringComparison.OrdinalIgnoreCase));

    private void ToggleWatching(string? host)
    {
        if (host is null)
        {
            return;
        }

        if (_session.IsIgnored(host))
        {
            ResumeWatching(host);
            return;
        }

        StopWatching(host);
    }

    private void StopWatching(string? host)
    {
        if (host is null || !_session.Ignore(host))
        {
            return;
        }

        KeepIgnored();
        _formatter.PaintAll();
        RefreshCounts();
    }

    private void ResumeWatching(string host)
    {
        if (!_session.Watch(host))
        {
            return;
        }

        KeepIgnored();
        SyncNoteHosts();
        _formatter.PaintAll();
        RefreshCounts();
    }

    private void KeepIgnored()
    {
        _settings.IgnoredHosts = string.Join(',', _session.Ignored);
        _settings.Save();
    }

    private void CopyHost(string? host)
    {
        if (host is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(host);
            StatusText.Text = $"{host} copiato.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusText.Text = "Gli appunti sono occupati da un altro programma: riprova.";
        }
    }

    private void PastePlain()
    {
        try
        {
            if (Clipboard.GetText() is not { Length: > 0 } text)
            {
                return;
            }

            Editor.Selection.Text = text;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusText.Text = "Gli appunti sono occupati da un altro programma: riprova.";
        }
    }
}
