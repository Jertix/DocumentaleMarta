namespace DocumentaleMarta.App.Servizi;

/// <summary>Cosa decide l'utente quando tra i file da allegare ce ne sono di già archiviati.</summary>
public enum SceltaDuplicati
{
    /// <summary>Allega tutti i file, anche quelli che esistono già.</summary>
    AllegaComunque,

    /// <summary>Allega solo i file nuovi.</summary>
    SaltaDuplicati,

    /// <summary>Non allega niente.</summary>
    Annulla
}
