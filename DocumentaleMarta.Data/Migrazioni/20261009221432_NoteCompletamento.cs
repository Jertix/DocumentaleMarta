using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentaleMarta.Data.Migrazioni
{
    /// <inheritdoc />
    public partial class NoteCompletamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NoteCompletamento",
                table: "Cartelle",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoteCompletamento",
                table: "Cartelle");
        }
    }
}
