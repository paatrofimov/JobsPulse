using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InititalCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    dedup_key = table.Column<string>(type: "text", nullable: false),
                    change_kind = table.Column<int>(type: "integer", nullable: false),
                    company_name = table.Column<string>(type: "text", nullable: false),
                    vacancy_payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seen_vacancy",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    board_id = table.Column<string>(type: "text", nullable: false),
                    post_id = table.Column<string>(type: "text", nullable: false),
                    group_id = table.Column<string>(type: "text", nullable: true),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    location = table.Column<string>(type: "text", nullable: true),
                    offices = table.Column<string[]>(type: "text[]", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    first_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seen_vacancy", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_dedup_key",
                table: "outbox",
                column: "dedup_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_status_next_attempt_at",
                table: "outbox",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_seen_vacancy_source_id_board_id",
                table: "seen_vacancy",
                columns: new[] { "source_id", "board_id" },
                filter: "closed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_seen_vacancy_source_id_board_id_post_id",
                table: "seen_vacancy",
                columns: new[] { "source_id", "board_id", "post_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "seen_vacancy");
        }
    }
}
