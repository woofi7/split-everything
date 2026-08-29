using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SplitEverything.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    quote_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "group_lineage_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    source_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moved_lineage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vector_clock_json = table.Column<string>(type: "jsonb", nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_lineage_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    base_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    emoji_icon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_counter = table.Column<long>(type: "bigint", nullable: false),
                    lineage_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    imported_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_label = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    expense_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_count = table.Column<int>(type: "integer", nullable: false),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rolled_back_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_conflicts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<int>(type: "integer", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stored_payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    stored_vector_clock_json = table.Column<string>(type: "jsonb", nullable: false),
                    stored_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    incoming_payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    incoming_vector_clock_json = table.Column<string>(type: "jsonb", nullable: false),
                    incoming_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    incoming_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conflicting_fields_json = table.Column<string>(type: "jsonb", nullable: false),
                    resolution = table.Column<int>(type: "integer", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_conflicts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    google_subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    avatar_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    default_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    prefers_light_theme = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "activity_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_type = table.Column<int>(type: "integer", nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_log_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    invited_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    claims_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    use_count = table.Column<int>(type: "integer", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_invites_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recurring_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    split_type = table.Column<int>(type: "integer", nullable: false),
                    split_template_json = table.Column<string>(type: "jsonb", nullable: false),
                    unit = table.Column<int>(type: "integer", nullable: false),
                    interval = table.Column<int>(type: "integer", nullable: false),
                    day_of_month = table.Column<int>(type: "integer", nullable: true),
                    day_of_week = table.Column<int>(type: "integer", nullable: true),
                    starts_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_occurrences = table.Column<int>(type: "integer", nullable: true),
                    occurrence_count = table.Column<int>(type: "integer", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_paused = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_recurring_expenses", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_expenses_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    up_to_server_seq = table.Column<long>(type: "bigint", nullable: false),
                    cutoff_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vector_clock_json = table.Column<string>(type: "jsonb", nullable: false),
                    state_json = table.Column<string>(type: "jsonb", nullable: false),
                    compacted_entry_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trimmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_sync_snapshots_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "category_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    keyword = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggested_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    weight = table.Column<int>(type: "integer", nullable: false),
                    hit_count = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_built_in = table.Column<bool>(type: "boolean", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_acked_server_seq = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_devices_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_group_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_members_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_group_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    auth = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failing_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_push_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_seq = table.Column<long>(type: "bigint", nullable: false),
                    entity_type = table.Column<int>(type: "integer", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<int>(type: "integer", nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vector_clock_json = table.Column<string>(type: "jsonb", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    lineage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    counterpart_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    superseded_by_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_sync_log_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sync_log_sync_snapshots_superseded_by_snapshot_id",
                        column: x => x.superseded_by_snapshot_id,
                        principalTable: "sync_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_in_base_currency = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    exchange_rate_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    spent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    split_type = table.Column<int>(type: "integer", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recurring_expense_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_lineage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    import_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_expenses", x => x.id);
                    table.ForeignKey(
                        name: "fk_expenses_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_expenses_group_members_paid_by_member_id",
                        column: x => x.paid_by_member_id,
                        principalTable: "group_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expenses_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expenses_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_expenses_recurring_expenses_recurring_expense_id",
                        column: x => x.recurring_expense_id,
                        principalTable: "recurring_expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_in_base_currency = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_lineage_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_settlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_settlements_group_members_from_member_id",
                        column: x => x.from_member_id,
                        principalTable: "group_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlements_group_members_to_member_id",
                        column: x => x.to_member_id,
                        principalTable: "group_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlements_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_settlements_receipts_receipt_id",
                        column: x => x.receipt_id,
                        principalTable: "receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "expense_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_comment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_expense_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_comments_expense_comments_parent_comment_id",
                        column: x => x.parent_comment_id,
                        principalTable: "expense_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expense_comments_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expense_comments_group_members_author_member_id",
                        column: x => x.author_member_id,
                        principalTable: "group_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_expense_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_items_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expense_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    edited_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    edited_by_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vector_clock_json = table.Column<string>(type: "jsonb", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    change_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_revisions_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expense_splits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount_in_base_currency = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    input_value = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: true),
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
                    table.PrimaryKey("pk_expense_splits", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_splits_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expense_splits_group_members_member_id",
                        column: x => x.member_id,
                        principalTable: "group_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_item_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_item_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_expense_item_shares_expense_items_expense_item_id",
                        column: x => x.expense_item_id,
                        principalTable: "expense_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expense_item_shares_group_members_member_id",
                        column: x => x.member_id,
                        principalTable: "group_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_log_actor_user_id",
                table: "activity_log",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_log_group_id_occurred_at",
                table: "activity_log",
                columns: new[] { "group_id", "occurred_at" });

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

            migrationBuilder.CreateIndex(
                name: "ix_devices_user_id",
                table: "devices",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_base_currency_quote_currency_rate_date",
                table: "exchange_rates",
                columns: new[] { "base_currency", "quote_currency", "rate_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_comments_author_member_id",
                table: "expense_comments",
                column: "author_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_comments_expense_id_created_at",
                table: "expense_comments",
                columns: new[] { "expense_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_expense_comments_group_id",
                table: "expense_comments",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_comments_parent_comment_id",
                table: "expense_comments",
                column: "parent_comment_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_item_shares_expense_item_id_member_id",
                table: "expense_item_shares",
                columns: new[] { "expense_item_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_item_shares_member_id",
                table: "expense_item_shares",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_items_expense_id",
                table: "expense_items",
                column: "expense_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_revisions_expense_id_revision",
                table: "expense_revisions",
                columns: new[] { "expense_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_splits_expense_id_member_id",
                table: "expense_splits",
                columns: new[] { "expense_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_splits_group_id",
                table: "expense_splits",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_splits_member_id",
                table: "expense_splits",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_category_id",
                table: "expenses",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_group_id_server_seq",
                table: "expenses",
                columns: new[] { "group_id", "server_seq" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_group_id_spent_at",
                table: "expenses",
                columns: new[] { "group_id", "spent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_import_batch_id",
                table: "expenses",
                column: "import_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_import_fingerprint",
                table: "expenses",
                column: "import_fingerprint");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_paid_by_member_id",
                table: "expenses",
                column: "paid_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_receipt_id",
                table: "expenses",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_recurring_expense_id",
                table: "expenses",
                column: "recurring_expense_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_invites_group_id",
                table: "group_invites",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_invites_invited_email",
                table: "group_invites",
                column: "invited_email");

            migrationBuilder.CreateIndex(
                name: "ix_group_invites_token_hash",
                table: "group_invites",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_lineage_links_moved_lineage_id",
                table: "group_lineage_links",
                column: "moved_lineage_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_lineage_links_source_group_id",
                table: "group_lineage_links",
                column: "source_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_lineage_links_target_group_id",
                table: "group_lineage_links",
                column: "target_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_members_group_id_user_id",
                table: "group_members",
                columns: new[] { "group_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_group_members_user_id",
                table: "group_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_groups_created_by_user_id",
                table: "groups",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_groups_lineage_id",
                table: "groups",
                column: "lineage_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_group_id",
                table: "import_batches",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_imported_by_user_id",
                table: "import_batches",
                column: "imported_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_endpoint",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_user_id",
                table: "push_subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_content_hash",
                table: "receipts",
                column: "content_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recurring_expenses_group_id",
                table: "recurring_expenses",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_expenses_is_paused_next_run_at",
                table: "recurring_expenses",
                columns: new[] { "is_paused", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_expires_at",
                table: "refresh_tokens",
                columns: new[] { "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_settlements_from_member_id",
                table: "settlements",
                column: "from_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlements_group_id_server_seq",
                table: "settlements",
                columns: new[] { "group_id", "server_seq" });

            migrationBuilder.CreateIndex(
                name: "ix_settlements_group_id_settled_at",
                table: "settlements",
                columns: new[] { "group_id", "settled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_settlements_receipt_id",
                table: "settlements",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlements_to_member_id",
                table: "settlements",
                column: "to_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_conflicts_entity_type_entity_id",
                table: "sync_conflicts",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_conflicts_group_id_resolution",
                table: "sync_conflicts",
                columns: new[] { "group_id", "resolution" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_log_entity_type_entity_id",
                table: "sync_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_log_group_id_server_seq",
                table: "sync_log",
                columns: new[] { "group_id", "server_seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_log_lineage_id",
                table: "sync_log",
                column: "lineage_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_log_superseded_by_snapshot_id",
                table: "sync_log",
                column: "superseded_by_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_snapshots_group_id_up_to_server_seq",
                table: "sync_snapshots",
                columns: new[] { "group_id", "up_to_server_seq" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_users_google_subject",
                table: "users",
                column: "google_subject",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_log");

            migrationBuilder.DropTable(
                name: "category_rules");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "expense_comments");

            migrationBuilder.DropTable(
                name: "expense_item_shares");

            migrationBuilder.DropTable(
                name: "expense_revisions");

            migrationBuilder.DropTable(
                name: "expense_splits");

            migrationBuilder.DropTable(
                name: "group_invites");

            migrationBuilder.DropTable(
                name: "group_lineage_links");

            migrationBuilder.DropTable(
                name: "import_batches");

            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "settlements");

            migrationBuilder.DropTable(
                name: "sync_conflicts");

            migrationBuilder.DropTable(
                name: "sync_log");

            migrationBuilder.DropTable(
                name: "expense_items");

            migrationBuilder.DropTable(
                name: "sync_snapshots");

            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "group_members");

            migrationBuilder.DropTable(
                name: "receipts");

            migrationBuilder.DropTable(
                name: "recurring_expenses");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "groups");
        }
    }
}
