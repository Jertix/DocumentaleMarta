using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentaleMarta.Data.Migrazioni
{
    /// <inheritdoc />
    public partial class CartelleArchiviate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Archiviata",
                table: "Cartelle",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Archiviata",
                table: "Cartelle");
        }
    }
}
