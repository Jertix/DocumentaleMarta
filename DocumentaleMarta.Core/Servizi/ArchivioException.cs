namespace DocumentaleMarta.Core.Servizi;

/// <summary>Errore spiegabile all'utente (nome duplicato, elemento non più esistente...). Il messaggio si può mostrare così com'è.</summary>
public class ArchivioException(string messaggio) : Exception(messaggio);
