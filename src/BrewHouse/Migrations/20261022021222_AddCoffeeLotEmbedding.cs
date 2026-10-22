using System.Globalization;
using BrewHouse.Data;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace BrewHouse.Migrations
{
    /// <inheritdoc />
    public partial class AddCoffeeLotEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "CoffeeLots",
                type: "vector(32)",
                nullable: true);

            // The five seeded lots already exist when this migration runs. New rows inserted by
            // the app set their own embedding; these five are the ones a camera will search.
            foreach (var origin in SeededOrigins)
            {
                var literal = string.Join(
                    ',',
                    CoffeeLotEmbedding.Compute(origin).Select(value => value.ToString("G9", CultureInfo.InvariantCulture)));
                migrationBuilder.Sql($"""UPDATE "CoffeeLots" SET "Embedding" = '[{literal}]' WHERE "Origin" = '{origin}';""");
            }
        }

        private static readonly string[] SeededOrigins =
        [
            "Terraform Roast",
            "Proxmox Pour-Over",
            "Wolverine Dark Roast",
            "Rabbit Hole Espresso",
            "Garnet Reserve",
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "CoffeeLots");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
