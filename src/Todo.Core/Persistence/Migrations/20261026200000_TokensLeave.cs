using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Todo.Core.Persistence.Migrations
{
    /// <summary>
    /// From 2.0.0 the host never holds Jira's or Azure DevOps' token - the helper on the PC does - so
    /// the two rows that stored them in cleartext go. The base URLs stay: the helper's hello writes
    /// them, and they are not secret.
    /// <para>
    /// Written by hand rather than scaffolded, and without a Designer file: the model does not change,
    /// so there is no target model to record, and the design-time tool can no longer build the host,
    /// which refuses to start without a password hash. The two attributes below are what EF finds a
    /// migration by.
    /// </para>
    /// </summary>
    [DbContext(typeof(TodoDbContext))]
    [Migration("20261026200000_TokensLeave")]
    public partial class TokensLeave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM Settings WHERE Key IN ('jira.token', 'ado.token');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back: the tokens are gone, and that is the point of the migration.
        }
    }
}
