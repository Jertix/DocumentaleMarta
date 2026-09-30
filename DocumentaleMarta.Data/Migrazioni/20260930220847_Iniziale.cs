using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentaleMarta.Data.Migrazioni
{
    /// <inheritdoc />
    public partial class Iniziale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Aree",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false, collation: "NOCASE"),
                    PercorsoRelativo = table.Column<string>(type: "TEXT", nullable: false),
                    Ordine = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aree", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cartelle",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AreaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Titolo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Descrizione = table.Column<string>(type: "TEXT", nullable: true),
                    DataScadenza = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Ricorrenza = table.Column<int>(type: "INTEGER", nullable: false),
                    Completato = table.Column<bool>(type: "INTEGER", nullable: false),
                    DataCompletamento = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PercorsoRelativo = table.Column<string>(type: "TEXT", nullable: false),
                    DataCreazione = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cartelle", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cartelle_Aree_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Aree",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Documenti",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CartellaId = table.Column<int>(type: "INTEGER", nullable: false),
                    NomeFile = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    PercorsoRelativo = table.Column<string>(type: "TEXT", nullable: false),
                    Estensione = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Dimensione = table.Column<long>(type: "INTEGER", nullable: false),
                    DataCaricamento = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    StatoIndicizzazione = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documenti", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documenti_Cartelle_CartellaId",
                        column: x => x.CartellaId,
                        principalTable: "Cartelle",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Aree_Nome",
                table: "Aree",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Aree_PercorsoRelativo",
                table: "Aree",
                column: "PercorsoRelativo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cartelle_AreaId",
                table: "Cartelle",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Cartelle_DataScadenza",
                table: "Cartelle",
                column: "DataScadenza");

            migrationBuilder.CreateIndex(
                name: "IX_Cartelle_PercorsoRelativo",
                table: "Cartelle",
                column: "PercorsoRelativo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documenti_CartellaId",
                table: "Documenti",
                column: "CartellaId");

            migrationBuilder.CreateIndex(
                name: "IX_Documenti_Hash",
                table: "Documenti",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "IX_Documenti_PercorsoRelativo",
                table: "Documenti",
                column: "PercorsoRelativo",
                unique: true);

            // Indice full-text del contenuto dei documenti (riempito dall'indicizzazione, fase 6).
            // documento_id è l'Id del Documento e non è indicizzato; unicode61 ignora accenti e maiuscole.
            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE documenti_fts USING fts5(documento_id UNINDEXED, contenuto, tokenize = 'unicode61 remove_diacritics 2');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS documenti_fts;");

            migrationBuilder.DropTable(
                name: "Documenti");

            migrationBuilder.DropTable(
                name: "Cartelle");

            migrationBuilder.DropTable(
                name: "Aree");
        }
    }
}
