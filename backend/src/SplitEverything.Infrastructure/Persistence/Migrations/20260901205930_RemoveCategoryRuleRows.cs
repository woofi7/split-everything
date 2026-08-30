using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitEverything.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCategoryRuleRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * Everything ever written down about a category rule.
             *
             * The type is gone from the enum, so a row still carrying 7 would be
             * read as nothing at all: the sync log would hand a client an entity it
             * cannot name, and the activity feed would show a line about a thing
             * that no longer exists. Categories were dropped before any of this data
             * was real - the development database has none of these rows - but a
             * database that has been running longer might.
             *
             * Deleted rather than migrated: there is nothing to migrate them to.
             */
            migrationBuilder.Sql("DELETE FROM sync_log WHERE entity_type = 7;");
            migrationBuilder.Sql("DELETE FROM sync_conflicts WHERE entity_type = 7;");
            migrationBuilder.Sql("DELETE FROM activity_log WHERE subject_type = 7;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back: the rows are gone and the type they described no
            // longer exists in the code either.
        }
    }
}
