using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrewHouse.Migrations
{
    /// <inheritdoc />
    public partial class SeedCoffeeLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Demo/dev inventory so a fresh database has lots to bid on - the app previously
            // shipped with an empty CoffeeLots table and nothing to demo. Real growing regions,
            // developer/homelab-flavored names - the mock photo upload in Home.razor matches the
            // first one ("Segfault-Yirgacheffe-Front.txt").
            migrationBuilder.InsertData(
                table: "CoffeeLots",
                columns: new[] { "Origin", "CurrentBid" },
                values: new object[,]
                {
                    { "Segfault Yirgacheffe", 450m },
                    { "Cron Job Huila", 375m },
                    { "Kernel Panic Kenya AA", 525m },
                    { "Stack Overflow Antigua", 300m },
                    { "sudo Panama Geisha", 950m },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CoffeeLots",
                keyColumn: "Origin",
                keyValues: new object[]
                {
                    "Segfault Yirgacheffe",
                    "Cron Job Huila",
                    "Kernel Panic Kenya AA",
                    "Stack Overflow Antigua",
                    "sudo Panama Geisha",
                });
        }
    }
}
