using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrewHouse.Migrations
{
    /// <inheritdoc />
    public partial class RenameEquipmentToCoffeeLot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolded as a DropTable/CreateTable pair (data loss) - rewritten as a rename
            // so existing rows survive the deploy, matching this repo's non-destructive
            // migration philosophy (ADR 11/16).
            migrationBuilder.RenameTable(
                name: "EquipmentDirectory",
                newName: "CoffeeLots");

            migrationBuilder.RenameColumn(
                name: "Model",
                table: "CoffeeLots",
                newName: "Origin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Origin",
                table: "CoffeeLots",
                newName: "Model");

            migrationBuilder.RenameTable(
                name: "CoffeeLots",
                newName: "EquipmentDirectory");
        }
    }
}
