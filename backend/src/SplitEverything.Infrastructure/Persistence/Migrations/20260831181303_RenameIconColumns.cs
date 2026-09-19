using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitEverything.Infrastructure.Persistence.Migrations
{
    public partial class RenameIconColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "emoji_icon",
                table: "groups",
                newName: "icon_name");

            migrationBuilder.RenameColumn(
                name: "emoji",
                table: "categories",
                newName: "icon_name");

            migrationBuilder.AlterColumn<string>(
                name: "icon_name",
                table: "groups",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "icon_name",
                table: "categories",
                type: "character varying(48)",
                maxLength: 48,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE groups SET icon_name = left(icon_name, 16) WHERE length(icon_name) > 16;");
            migrationBuilder.Sql("UPDATE categories SET icon_name = left(icon_name, 16) WHERE length(icon_name) > 16;");

            migrationBuilder.AlterColumn<string>(
                name: "icon_name",
                table: "categories",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(48)",
                oldMaxLength: 48);

            migrationBuilder.AlterColumn<string>(
                name: "icon_name",
                table: "groups",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(48)",
                oldMaxLength: 48,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "icon_name",
                table: "categories",
                newName: "emoji");

            migrationBuilder.RenameColumn(
                name: "icon_name",
                table: "groups",
                newName: "emoji_icon");
        }
    }
}
