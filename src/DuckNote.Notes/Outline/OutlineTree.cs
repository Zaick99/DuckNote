using System.Text.RegularExpressions;

namespace DuckNote.Notes.Outline;

/// <summary>Un titolo del testo: cosa dice, di che grado (1 o 2), in che posizione nella pagina.</summary>
public sealed record Heading(string Text, int Level, int Index);

/// <summary>Quel che serve sapere di una pagina per metterla nella struttura.</summary>
public sealed record OutlinePage(string Id, string Title, IReadOnlyList<Heading> Headings, string Body);

/// <summary>Una riga della struttura, gia' messa in fila per essere mostrata.</summary>
public sealed record OutlineRow
{
    public required string Key { get; init; }
    public required string PageId { get; init; }
    public required bool IsPage { get; init; }
    public required string Title { get; init; }
    public required int Depth { get; init; }

    /// <summary>0 per la pagina, 1 o 2 per i titoli.</summary>
    public int Level { get; init; }

    /// <summary>Indice del titolo nella pagina (-1 per la pagina stessa).</summary>
    public int HeadingIndex { get; init; } = -1;

    public bool HasChildren { get; init; }
    public bool IsOpen { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsLast { get; init; }

    /// <summary>Quante volte il filtro compare nel testo della pagina (0 = non mostrarlo).</summary>
    public int Hits { get; init; }

    /// <summary>Per ogni livello sopra questo: la linea verticale prosegue sotto?</summary>
    public IReadOnlyList<bool> Continues { get; init; } = [];
}

/// <summary>
/// La struttura delle pagine: sotto ogni pagina i suoi titoli di primo grado, sotto ognuno
/// quelli di secondo grado che lo seguono. Un secondo grado senza un primo davanti sta sotto
/// la pagina: nessun titolo sparisce per come e' scritta la nota. Il terzo grado e' dettaglio
/// e resta fuori. Il filtro cerca nei nomi, nei titoli e nel testo; una pagina trovata solo nel
/// testo dice quante volte.
/// </summary>
public static class OutlineTree
{
    private sealed class Node
    {
        public required OutlineRow Row;
        public List<Node> Children { get; } = [];
    }

    public static IReadOnlyList<OutlineRow> Build(
        IEnumerable<OutlinePage> pages, string? currentPageId, string? filter, IReadOnlyDictionary<string, bool> open)
    {
        string wanted = (filter ?? string.Empty).Trim();
        List<Node> roots = [];

        foreach (OutlinePage page in pages)
        {
            if (Branch(page, currentPageId, wanted, open) is { } root)
            {
                roots.Add(root);
            }
        }

        List<OutlineRow> rows = [];
        Flatten(roots, rows, []);

        return rows;
    }

    public static bool Matches(string filter, string text) =>
        filter.Length == 0 || text.Contains(filter, StringComparison.OrdinalIgnoreCase);

    public static int Count(string text, string word) =>
        word.Length == 0 ? 0 : Regex.Matches(text, Regex.Escape(word), RegexOptions.IgnoreCase).Count;

    private static Node? Branch(OutlinePage page, string? current, string filter, IReadOnlyDictionary<string, bool> open)
    {
        string key = $"p:{page.Id}";
        bool named = Matches(filter, page.Title);
        string inner = named ? string.Empty : filter;

        Node root = Make(key, page.Id, true, page.Title, 0, -1, open, page.Id == current);
        Node? head = null;

        foreach (Heading heading in page.Headings.Where(h => h.Level is 1 or 2))
        {
            if (!Matches(inner, heading.Text))
            {
                continue;
            }

            Node node = Make($"h:{page.Id}:{heading.Index}", page.Id, false, heading.Text, 0, heading.Index, open, false);
            node.Row = node.Row with { Level = heading.Level };

            if (heading.Level == 1)
            {
                root.Children.Add(node);
                head = node;
            }
            else
            {
                (head ?? root).Children.Add(node);
            }
        }

        if (filter.Length == 0 || named || root.Children.Count > 0)
        {
            return root;
        }

        int hits = Count(page.Body, filter);

        return hits == 0 ? null : new Node { Row = root.Row with { Hits = hits } };
    }

    private static Node Make(string key, string pageId, bool isPage, string title, int depth, int index,
        IReadOnlyDictionary<string, bool> open, bool current) => new()
    {
        Row = new OutlineRow
        {
            Key = key,
            PageId = pageId,
            IsPage = isPage,
            Title = title.Length > 0 ? title : Notebook.PageIds.Untitled,
            Depth = depth,
            HeadingIndex = index,
            IsOpen = !open.TryGetValue(key, out bool wanted) || wanted,
            IsCurrent = current
        }
    };

    private static void Flatten(List<Node> nodes, List<OutlineRow> into, List<bool> lasts)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            Node node = nodes[i];
            bool last = i == nodes.Count - 1;
            bool unfolds = node.Row.IsOpen && node.Children.Count > 0;

            into.Add(node.Row with
            {
                Depth = lasts.Count,
                IsLast = last,
                HasChildren = node.Children.Count > 0,
                Continues = [.. lasts.Select(l => !l)]
            });

            if (!unfolds)
            {
                continue;
            }

            lasts.Add(last);
            Flatten(node.Children, into, lasts);
            lasts.RemoveAt(lasts.Count - 1);
        }
    }
}
