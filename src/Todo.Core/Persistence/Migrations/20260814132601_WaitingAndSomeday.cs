using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WaitingAndSomeday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WaitingOn",
                table: "Tasks",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WaitingSince",
                table: "Tasks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WaitingOn",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "WaitingSince",
                table: "Tasks");
        }
    }
}
