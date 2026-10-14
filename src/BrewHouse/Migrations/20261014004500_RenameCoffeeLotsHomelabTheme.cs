using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrewHouse.Migrations
{
    /// <inheritdoc />
    public partial class RenameCoffeeLotsHomelabTheme : Migration
    {
        private static readonly (string Old, string New)[] Renames =
        [
            ("Segfault Yirgacheffe", "Terraform Roast"),
            ("Cron Job Huila", "Proxmox Pour-Over"),
            ("Kernel Panic Kenya AA", "Wolverine Dark Roast"),
            ("Stack Overflow Antigua", "Rabbit Hole Espresso"),
            ("sudo Panama Geisha", "Garnet Reserve"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The seeded lots get names that belong to this project and are easy to say on camera.
            // Renamed in place (current bids and ids untouched) so an existing database keeps its auction state.
            foreach (var (oldName, newName) in Renames)
                migrationBuilder.Sql($"UPDATE \"CoffeeLots\" SET \"Origin\" = '{newName}' WHERE \"Origin\" = '{oldName}';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (oldName, newName) in Renames)
                migrationBuilder.Sql($"UPDATE \"CoffeeLots\" SET \"Origin\" = '{oldName}' WHERE \"Origin\" = '{newName}';");
        }
    }
}
