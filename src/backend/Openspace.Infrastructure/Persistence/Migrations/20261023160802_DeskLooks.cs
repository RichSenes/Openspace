using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Openspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeskLooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LookAccessory",
                table: "Desks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LookCloth",
                table: "Desks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LookCostume",
                table: "Desks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LookHair",
                table: "Desks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LookHairStyle",
                table: "Desks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LookOutfit",
                table: "Desks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LookSkin",
                table: "Desks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LookAccessory",
                table: "Desks");

            migrationBuilder.DropColumn(
                name: "LookCloth",
                table: "Desks");

            migrationBuilder.DropColumn(
                name: "LookCostume",
                table: "Desks");

            migrationBuilder.DropColumn(
                name: "LookHair",
                table: "Desks");

            migrationBuilder.DropColumn(
                name: "LookHairStyle",
                table: "Desks");

            migrationBuilder.DropColumn(
                name: "LookOutfit",
                table: "Desks");

            migrationBuilder.DropColumn(
                name: "LookSkin",
                table: "Desks");
        }
    }
}
