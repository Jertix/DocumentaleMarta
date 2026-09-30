using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentaleMarta.Data.Migrazioni
{
    /// <inheritdoc />
    /// <remarks>
    /// L'indice del testo dei documenti passa a una riga per documento con il <c>rowid</c> uguale all'Id del Documento:
    /// cercare o eliminare per Id diventa immediato (la colonna documento_id della prima versione non era indicizzata
    /// e ogni eliminazione avrebbe scorso l'intera tabella). Un trigger toglie il testo dall'indice quando un documento
    /// viene eliminato, anche per cancellazione a cascata di una cartella o di un'area.
    /// </remarks>
    public partial class IndiceTestoPerDocumento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La tabella della prima versione non è mai stata riempita da nessuna funzione dell'app: si può ricreare.
            migrationBuilder.Sql("DROP TABLE IF EXISTS documenti_fts;");
            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE documenti_fts USING fts5(contenuto, tokenize = 'unicode61 remove_diacritics 2');");

            migrationBuilder.Sql(
                """
                CREATE TRIGGER documenti_fts_elimina AFTER DELETE ON Documenti
                BEGIN
                    DELETE FROM documenti_fts WHERE rowid = old.Id;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS documenti_fts_elimina;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS documenti_fts;");
            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE documenti_fts USING fts5(documento_id UNINDEXED, contenuto, tokenize = 'unicode61 remove_diacritics 2');");
        }
    }
}
