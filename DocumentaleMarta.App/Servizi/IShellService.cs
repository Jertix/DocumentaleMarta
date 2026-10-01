namespace DocumentaleMarta.App.Servizi;

/// <summary>Interazioni con Windows (Esplora file, programma associato al tipo di file).</summary>
public interface IShellService
{
    void ApriCartella(string percorso);

    /// <summary>Apre il file con il programma predefinito di Windows per quel tipo.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">Nessun programma associato al tipo di file.</exception>
    void ApriFile(string percorso);

    /// <summary>Apre Esplora file nella cartella del file, con il file evidenziato.</summary>
    void MostraFileInEsplora(string percorso);

    /// <summary>Chiude Documentale e lo riapre (serve a usare un archivio diverso: le impostazioni si leggono all'avvio).</summary>
    void RiavviaApplicazione();
}
