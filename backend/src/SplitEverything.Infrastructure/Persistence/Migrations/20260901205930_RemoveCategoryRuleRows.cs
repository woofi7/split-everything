using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitEverything.Infrastructure.Persistence.Migrations
{
    public partial class RemoveCategoryRuleRows : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM sync_log WHERE entity_type = 7;");
            migrationBuilder.Sql("DELETE FROM sync_conflicts WHERE entity_type = 7;");
            migrationBuilder.Sql("DELETE FROM activity_log WHERE subject_type = 7;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
