using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoadrunnerAuction.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentCurrentBid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrentBid",
                table: "EquipmentDirectory",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentBid",
                table: "EquipmentDirectory");
        }
    }
}
