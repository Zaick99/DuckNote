using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using DuckNote.App.Editor;
using DuckNote.App.Models;
using DuckNote.App.Theme;

namespace DuckNote.App;

public partial class MainWindow
{
    private readonly ObservableCollection<SideItem> _sideItems = [];
    private bool _outlineMode;

    private void SetUpSidebar()
    {
        SideList.ItemsSource = _sideItems;

        SideModeHost.Checked += (_, _) => SwitchSideMode(outline: false);
        SideModeOutline.Checked += (_, _) => SwitchSideMode(outline: true);
        SideSearch.TextChanged += (_, _) => RefreshSideList();

        SideSegHost.SizeChanged += (_, _) => MoveSideSegPill(_outlineMode ? 1 : 0, immediate: true);

        SwitchSideMode(outline: false, immediate: true);
    }

    private void SwitchSideMode(bool outline, bool immediate = false)
    {
        _outlineMode = outline;
        SideSearchHint.Text = outline ? "Filtra intestazioni" : "Filtra host";
        MoveSideSegPill(outline ? 1 : 0, immediate);
        RefreshSideList();

        Motion.Enter(SideList, SideListT, from: outline ? 16 : -16);
    }

    private void MoveSideSegPill(int index, bool immediate = false)
    {
        if (SideSegHost.ActualWidth <= 1)
        {
            return;
        }

        double half = SideSegHost.ActualWidth / 2;
        SideSegPill.Width = half;
        SideSegPillS.CenterX = half / 2;
        Motion.MovePill(SideSegPillT, SideSegPillS, index * half, immediate);
    }

    private void RefreshSideList()
    {
        if (_formatter is null)
        {
            return;
        }

        List<SideItem> wanted = _outlineMode ? BuildOutline() : BuildHosts();
        SyncSideItems(wanted);
        UpdateSideFoot(wanted.Count);
    }

    private List<SideItem> BuildHosts()
    {
        string filter = SideSearch.Text.Trim();
        List<SideItem> items = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (ScanRow row in _session.Rows)
        {
            if (row.StatusRank > 2 || !seen.Add(row.IP))
            {
                continue;
            }

            foreach (string name in (string[])[row.Hostname, row.NetBiosName, row.MdnsName])
            {
                if (name.Length > 0)
                {
                    seen.Add(name);
                }
            }

            string subtitle = row.Hostname.Length > 0 ? row.Hostname : row.NetBiosName;
            if (!Matches(filter, $"{row.IP} {subtitle}"))
            {
                continue;
            }

            items.Add(Item($"h:{row.IP}", row.IP, DotHex(row.StatusRank), subtitle, row.IP));
        }

        foreach (string host in NoteHosts())
        {
            if (!seen.Add(host) || !Matches(filter, host))
            {
                continue;
            }

            int rank = _hostStates.TryGetValue(host, out bool up) ? (up ? 0 : 2) : 4;
            items.Add(Item($"h:{host}", host, DotHex(rank), string.Empty, host));
        }

        return items;
    }

    private List<SideItem> BuildOutline()
    {
        string filter = SideSearch.Text.Trim();
        List<SideItem> items = [];

        foreach ((int level, string text, Paragraph paragraph) in EditorOutline())
        {
            if (!Matches(filter, text))
            {
                continue;
            }

            string indent = new(' ', 3 * (level - 1));
            string dot = level switch
            {
                1 => ThemeTokens.Colour(_theme, "Accent"),
                2 => ThemeTokens.Colour(_theme, "LabelSecondary"),
                _ => ThemeTokens.Colour(_theme, "LabelQuaternary")
            };

            items.Add(Item($"o:{items.Count}", indent + text, dot, string.Empty, string.Empty, paragraph));
        }

        return items;
    }

    private void SyncSideItems(List<SideItem> wanted)
    {
        HashSet<string> keys = [.. wanted.Select(item => item.Key)];

        for (int i = _sideItems.Count - 1; i >= 0; i--)
        {
            if (!keys.Contains(_sideItems[i].Key))
            {
                _sideItems.RemoveAt(i);
            }
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            SideItem fresh = wanted[i];

            if (i >= _sideItems.Count || _sideItems[i].Key != fresh.Key)
            {
                _sideItems.Insert(i, fresh);
                continue;
            }

            SideItem existing = _sideItems[i];
            existing.Title = fresh.Title;
            existing.Subtitle = fresh.Subtitle;
            existing.SubVis = fresh.SubVis;
            existing.Dot = fresh.Dot;
            existing.IP = fresh.IP;
            existing.Para = fresh.Para;
        }

        while (_sideItems.Count > wanted.Count)
        {
            _sideItems.RemoveAt(_sideItems.Count - 1);
        }
    }

    private void UpdateSideFoot(int count)
    {
        if (_outlineMode)
        {
            SideFoot.Text = count == 0
                ? "Nessuna intestazione (usa # Titolo)"
                : count == 1 ? "1 intestazione" : $"{count} intestazioni";
            return;
        }

        int up = _session.UpCount;
        SideFoot.Text = count == 0 ? "Nessun host" : $"{count} host, {up} attivi";
    }

    private static SideItem Item(
        string key, string title, string dot, string subtitle, string ip, Paragraph? paragraph = null) =>
        new()
        {
            Key = key,
            Title = title,
            Subtitle = subtitle,
            SubVis = subtitle.Length > 0 ? "Visible" : "Collapsed",
            Dot = dot,
            IP = ip,
            Para = paragraph
        };

    private static bool Matches(string filter, string text) =>
        filter.Length == 0 || Regex.IsMatch(text, Regex.Escape(filter), RegexOptions.IgnoreCase);

    private string DotHex(int rank) => rank switch
    {
        0 => ThemeTokens.Colour(_theme, "Green"),
        1 => ThemeTokens.Colour(_theme, "Teal"),
        2 => ThemeTokens.Colour(_theme, "Red"),
        3 => ThemeTokens.Colour(_theme, "Orange"),
        _ => ThemeTokens.Colour(_theme, "LabelQuaternary")
    };
}
