using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitEverything.Infrastructure.Persistence.Migrations
{
    public partial class AddUserTheme : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "theme_name",
                table: "users",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "theme_name",
                table: "users");
        }
    }
}
