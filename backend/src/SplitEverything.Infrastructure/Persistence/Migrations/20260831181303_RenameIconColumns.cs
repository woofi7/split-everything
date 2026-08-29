using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitEverything.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Both icon columns now hold a Font Awesome name rather than a placeholder
    /// word, so they are renamed and widened.
    ///
    /// Hand-written on purpose. The scaffolder produced a drop and an add for
    /// categories.emoji, because the property was renamed and retyped in one step
    /// and it could not infer the rename. That would have thrown away every
    /// category icon; a rename keeps the values.
    /// </summary>
    public partial class RenameIconColumns : Migration
    {
        /// <inheritdoc />
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Narrowing again would truncate any name longer than sixteen
            // characters, so the values are trimmed explicitly first rather than
            // letting the column change fail.
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
