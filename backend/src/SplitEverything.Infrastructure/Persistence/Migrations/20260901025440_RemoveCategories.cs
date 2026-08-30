using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitEverything.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Drops categories from the schema.
    ///
    /// This loses data and is meant to: the category on every expense goes with the
    /// column, and both category tables are dropped outright. Down rebuilds the
    /// shape but cannot bring the values back, so it leaves every expense
    /// uncategorised. Nothing in the app reads any of it any more.
    /// </summary>
    public partial class RemoveCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_expenses_categories_category_id",
                table: "expenses");

            migrationBuilder.DropTable(
                name: "category_rules");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropIndex(
                name: "ix_expenses_category_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "category_id",
                table: "recurring_expenses");

            migrationBuilder.DropColumn(
                name: "category_id",
                table: "expenses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                table: "recurring_expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    icon_name = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    key = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "category_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hit_count = table.Column<int>(type: "integer", nullable: false),
                    is_built_in = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    keyword = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    last_writer_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false),
                    suggested_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vector_clock_json = table.Column<string>(type: "jsonb", nullable: false),
                    weight = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_category_rules_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_category_rules_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_category_id",
                table: "expenses",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_owner_user_id_key",
                table: "categories",
                columns: new[] { "owner_user_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_rules_category_id",
                table: "category_rules",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_rules_user_id_keyword",
                table: "category_rules",
                columns: new[] { "user_id", "keyword" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_expenses_categories_category_id",
                table: "expenses",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
