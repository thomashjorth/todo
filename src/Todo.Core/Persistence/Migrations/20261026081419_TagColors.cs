using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TagColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Color",
                table: "Tags",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // The tags there already take the rainbow in the order they were created: each its rank
            // by id, wrapped at the 21 slots (TagColors.Count). Written by hand, not scaffolded.
            migrationBuilder.Sql(
                "UPDATE Tags SET Color = (SELECT COUNT(*) FROM Tags AS Earlier WHERE Earlier.Id < Tags.Id) % 21;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "Tags");
        }
    }
}
