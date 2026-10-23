using System.Windows.Documents;

namespace DuckNote.App.Editor;

/// <summary>Le note che DuckNote scrive da se': quella di benvenuto e l'avviso di contenitore rovinato.</summary>
public static class Welcome
{
    public static FlowDocument Note()
    {
        FlowDocument note = Doc.Empty();

        Add(note, Look.Heading1, "DuckNote");
        Add(note, Look.Text, "Note tecniche e scanner di rete nella stessa finestra.");
        Add(note, Look.Text, string.Empty);
        Add(note, Look.Text,
            "Scrivi un indirizzo e DuckNote lo controlla: il gateway 192.168.1.1, il NAS nas.casa.lan, " +
            "i DNS 1.1.1.1 e 1.0.0.1. Ogni indirizzo riconosciuto prende il colore del suo stato: verde " +
            "raggiungibile, rosso spento, grigio mai provato.");
        Add(note, Look.Text, string.Empty);
        Add(note, Look.Heading2, "Come si scrive");
        Add(note, Look.Text,
            "Si scrive e si preme un tasto della barra: niente marcatori da ricordare. Grassetto, corsivo, " +
            "sottolineato, barrato, evidenziato, codice, titoli, elenchi, spunte, citazioni, tabelle, collegamenti.");
        Add(note, Look.Text, string.Empty);
        Add(note, Look.Todo, "provare una spunta, cliccandoci sopra");
        Add(note, Look.Bullet, "una voce di elenco");
        Add(note, Look.Quote, "Una citazione, per le cose dette da altri.");
        Add(note, Look.Text, string.Empty);
        Add(note, Look.Heading2, "Disegni, immagini e grafici");
        Add(note, Look.Text,
            "Gli ultimi tasti della barra mettono nel testo un riquadro da disegno, un'immagine o un grafico. " +
            "Il testo gli gira attorno; i bordi si trascinano per cambiarne la misura.");
        Add(note, Look.Text, string.Empty);

        return note;
    }

    public static FlowDocument Damaged()
    {
        FlowDocument note = Doc.Empty();

        Add(note, Look.Heading1, "Il contenitore non si e' lasciato leggere");
        Add(note, Look.Text,
            "La verifica di integrita' non torna: il file e' stato troncato, alterato, o scritto da una versione diversa.");
        Add(note, Look.Text,
            "La nota NON viene sovrascritta e il salvataggio resta sospeso, cosi' quel che c'e' dentro non va perso.");
        Add(note, Look.Text, "Accanto al contenitore c'e' una copia di poco precedente:");
        Add(note, Look.Code, @"%APPDATA%\duck\DuckNote\store.bin.bak");
        Add(note, Look.Text, "Chiudi DuckNote, rinominala in store.bin e riapri.");

        return note;
    }

    private static void Add(FlowDocument note, Look look, string line)
    {
        Paragraph row = Doc.Row(line);
        note.Blocks.Add(row);

        if (look != Look.Text)
        {
            Styler.Dress(row, look);
        }
    }
}
