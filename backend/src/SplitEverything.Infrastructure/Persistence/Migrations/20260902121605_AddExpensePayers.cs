using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SplitEverything.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpensePayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_payers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount_in_base_currency = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vector_clock_json = table.Column<string>(type: "jsonb", nullable: false),
                    last_writer_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    server_seq = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_payers", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_payers_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expense_payers_group_members_member_id",
                        column: x => x.member_id,
                        principalTable: "group_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expense_payers_expense_id_member_id",
                table: "expense_payers",
                columns: new[] { "expense_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_payers_group_id",
                table: "expense_payers",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_payers_member_id",
                table: "expense_payers",
                column: "member_id");

            // Every expense that already exists had exactly one payer, and the new
            // table is where the money now lives: without this backfill every balance
            // in the app reads as though nobody paid for anything.
            //
            // Deleted expenses are carried over as they are. They are excluded from
            // every balance by the expense's own flag, and dropping them here would
            // lose the payer of an expense somebody later restores.
            migrationBuilder.Sql(
                """
                INSERT INTO expense_payers (
                    id, expense_id, member_id, amount, amount_in_base_currency, group_id,
                    vector_clock_json, server_seq, created_at, updated_at, is_deleted, deleted_at)
                SELECT gen_random_uuid(), e.id, e.paid_by_member_id, e.amount,
                       e.amount_in_base_currency, e.group_id, '{}', 0,
                       e.created_at, e.updated_at, e.is_deleted, e.deleted_at
                FROM expenses e;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_payers");
        }
    }
}
