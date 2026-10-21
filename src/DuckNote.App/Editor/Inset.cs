using DuckNote.Notes.Notebook;

namespace DuckNote.App.Editor;

/// <summary>Un riquadro nel testo: dove sta e quanto e' grande (<see cref="InsetAddress"/>) piu' i suoi byte.</summary>
public sealed class Inset(InsetAddress address, byte[] bytes)
{
    public InsetAddress Address { get; set; } = address;

    /// <summary>Tratti in ISF per un disegno, file originale per un'immagine, testo UTF-8 per un grafico.</summary>
    public byte[] Bytes { get; set; } = bytes;

    public InsetKind Kind => Address.Kind;

    public string Title => Address.Title;

    public static Inset Fresh(InsetKind kind, byte[]? bytes = null) => new(InsetAddress.Fresh(kind), bytes ?? []);
}
