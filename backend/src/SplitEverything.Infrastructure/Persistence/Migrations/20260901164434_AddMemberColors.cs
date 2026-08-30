using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitEverything.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preferred_color_hex",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "color_hex",
                table: "group_members",
                type: "text",
                nullable: true);

            // Every existing member gets a colour of their own, in the order they
            // joined, cycling the palette in a group with more people than colours.
            //
            // Without this a client falls back to deriving one from the member id,
            // which is what it did before and which every screen computed from a
            // different list. Backfilling means a group's colours may shift once,
            // which is worth it for a value that is now explicit and editable.
            migrationBuilder.Sql("""
                WITH ordered AS (
                    SELECT
                        id,
                        row_number() OVER (
                            PARTITION BY group_id
                            ORDER BY joined_at NULLS LAST, created_at, id
                        ) - 1 AS position
                    FROM group_members
                )
                UPDATE group_members AS m
                SET color_hex = (ARRAY[
                    '#6366f1', '#f97316', '#14b8a6', '#ec4899',
                    '#84cc16', '#8b5cf6', '#f59e0b', '#06b6d4',
                    '#ef4444', '#22c55e', '#a855f7', '#eab308'
                ])[(o.position % 12) + 1]
                FROM ordered AS o
                WHERE o.id = m.id AND m.color_hex IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "preferred_color_hex",
                table: "users");

            migrationBuilder.DropColumn(
                name: "color_hex",
                table: "group_members");
        }
    }
}
