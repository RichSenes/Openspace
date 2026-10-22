using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Openspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FloorPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DoorOffset",
                table: "Rooms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoorWall",
                table: "Rooms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Left",
                table: "Rooms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Top",
                table: "Rooms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Windows",
                table: "Rooms",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "FloorDepth",
                table: "Offices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FloorWidth",
                table: "Offices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommonFurniture",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    X = table.Column<int>(type: "INTEGER", nullable: false),
                    Y = table.Column<int>(type: "INTEGER", nullable: false),
                    OfficeId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonFurniture", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonFurniture_Offices_OfficeId",
                        column: x => x.OfficeId,
                        principalTable: "Offices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommonFurniture_OfficeId",
                table: "CommonFurniture",
                column: "OfficeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommonFurniture");

            migrationBuilder.DropColumn(
                name: "DoorOffset",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "DoorWall",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Left",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Top",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "Windows",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "FloorDepth",
                table: "Offices");

            migrationBuilder.DropColumn(
                name: "FloorWidth",
                table: "Offices");
        }
    }
}
