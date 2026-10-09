using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentaleMarta.Data.Migrazioni
{
    /// <inheritdoc />
    public partial class IconaArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Icona",
                table: "Aree",
                type: "TEXT",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Icona",
                table: "Aree");
        }
    }
}
