using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetroImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalKey",
                table: "Tasks",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Aliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aliases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_SourceId_ExternalKey",
                table: "Tasks",
                columns: new[] { "SourceId", "ExternalKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Aliases_Value",
                table: "Aliases",
                column: "Value",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Aliases");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_SourceId_ExternalKey",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ExternalKey",
                table: "Tasks");
        }
    }
}
