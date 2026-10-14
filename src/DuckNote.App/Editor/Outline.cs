using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using DuckNote.App.Models;

namespace DuckNote.App.Editor;

public static class Outline
{
    public const double Step = 28;

    public const double Half = 7;

    public const double PageRow = 25;

    public const double HeadRow = 18;

    public static List<SideNode> Build(
        NoteBook book, NotePage? current, string filter, IReadOnlyDictionary<string, bool> open)
    {
        return [.. book.Pages.Select(page => Branch(page, current, filter.Trim(), open)).OfType<SideNode>()];
    }

    private static SideNode? Branch(
        NotePage page, NotePage? current, string filter, IReadOnlyDictionary<string, bool> open)
    {
        SideNode root = Node($"p:{page.Id}", page.Id, isPage: true, page.Title, open);
        root.HereVis = ReferenceEquals(page, current) ? "Visible" : "Collapsed";

        bool named = Matches(filter, page.Title);
        Nest(root, Headings(page), named ? string.Empty : filter, open);

        if (filter.Length == 0 || named || root.Children.Count > 0)
        {
            return Dressed(root);
        }

        int inside = Occurrences(page.Text, filter);

        if (inside == 0)
        {
            return null;
        }

        root.Badge = inside.ToString();
        root.BadgeVis = "Visible";

        return Dressed(root);
    }

    /// <summary>
    /// Appende i titoli sotto la pagina, mettendo ogni h2 sotto l'h1 che lo
    /// precede. Un h2 senza nessun h1 davanti sta sotto la pagina: nessun
    /// titolo sparisce per colpa di come e' scritta la nota.
    /// </summary>
    private static void Nest(
        SideNode root,
        List<(string Text, Paragraph Row, int Level)> headings,
        string filter,
        IReadOnlyDictionary<string, bool> open)
    {
        SideNode? head = null;

        for (int i = 0; i < headings.Count; i++)
        {
            (string text, Paragraph row, int level) = headings[i];

            if (!Matches(filter, text))
            {
                continue;
            }

            SideNode node = Node($"h:{root.PageId}:{i}", root.PageId, isPage: false, text, open);
            node.Heading = row;

            if (level == 1)
            {
                root.Children.Add(node);
                head = node;
                continue;
            }

            (head ?? root).Children.Add(node);
        }
    }

    private static SideNode Node(
        string key, string pageId, bool isPage, string title, IReadOnlyDictionary<string, bool> open)
    {
        SideNode node = new() { Key = key, PageId = pageId, IsPage = isPage };

        node.Title = title.Length > 0 ? title : NotePage.Untitled;
        node.IsOpen = !open.TryGetValue(key, out bool wanted) || wanted;
        node.RowHeight = isPage ? PageRow : HeadRow;

        return node;
    }

    private static SideNode Dressed(SideNode node)
    {
        foreach (SideNode child in node.Children)
        {
            Dressed(child);
        }

        node.ToggleVis = node.Children.Count > 0 ? "Visible" : node.IsPage ? "Hidden" : "Collapsed";
        node.Toggle = node.IsPage
            ? node.IsOpen ? "▾" : "▸"
            : node.IsOpen ? "⌄" : "›";

        return node;
    }

    public static List<SideNode> Flat(List<SideNode> roots)
    {
        List<SideNode> shown = [];
        Walk(roots, shown, []);

        return shown;

        static void Walk(List<SideNode> nodes, List<SideNode> into, List<bool> lasts)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                SideNode node = nodes[i];
                bool last = i == nodes.Count - 1;
                bool unfolds = node.IsOpen && node.Children.Count > 0;

                node.RailWidth = lasts.Count * Step;
                node.Rails = Rails(lasts, last, unfolds, node.RowHeight);
                into.Add(node);

                if (!unfolds)
                {
                    continue;
                }

                lasts.Add(last);
                Walk(node.Children, into, lasts);
                lasts.RemoveAt(lasts.Count - 1);
            }
        }
    }

    private static string Rails(List<bool> lasts, bool last, bool unfolds, double height)
    {
        int depth = lasts.Count;
        StringBuilder path = new();
        double middle = Math.Round(height / 2);

        for (int level = 1; level < depth; level++)
        {
            if (!lasts[level])
            {
                Line(path, (level - 1) * Step + Half, 0, height);
            }
        }

        if (depth > 0)
        {
            double x = (depth - 1) * Step + Half;

            path.Append(Figure("M {0} 0 V {1} H {2} ", x, middle, depth * Step - 2));

            if (!last)
            {
                Line(path, x, middle, height);
            }
        }

        if (unfolds)
        {
            Line(path, depth * Step + Half, middle, height);
        }

        return path.Length == 0 ? "M 0 0" : path.ToString().TrimEnd();
    }

    private static void Line(StringBuilder path, double x, double from, double to) =>
        path.Append(Figure("M {0} {1} V {2} ", x, from, to));

    private static string Figure(string shape, params object[] parts) =>
        string.Format(CultureInfo.InvariantCulture, shape, parts);

    /// <summary>
    /// I titoli del testo, in ordine di lettura e col loro grado: il primo
    /// livello sono gli h1, e gli h2 che li seguono gli stanno sotto.
    /// </summary>
    public static List<(string Text, Paragraph Row, int Level)> Headings(NotePage page)
    {
        List<(string, Paragraph, int)> found = [];

        foreach (Paragraph row in LiveFormatter.RowsOf(page.Document))
        {
            if (Grade(row) is not { } level)
            {
                continue;
            }

            found.Add((LiveFormatter.TextOf(row).Trim(), row, level));
        }

        return found;
    }

    private static int? Grade(Paragraph row) => Looks.Of(row) switch
    {
        Look.Heading1 => 1,
        Look.Heading2 => 2,
        _ => null
    };

    public static bool Matches(string filter, string text) =>
        filter.Length == 0 || Regex.IsMatch(text, Regex.Escape(filter), RegexOptions.IgnoreCase);

    private static int Occurrences(string text, string word) =>
        word.Length == 0 ? 0 : Regex.Matches(text, Regex.Escape(word), RegexOptions.IgnoreCase).Count;
}
