using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchlistEventAndDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "digest_sent_at",
                table: "watchlist",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "watchlist_event",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    watchlist_id = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    board_id = table.Column<string>(type: "text", nullable: false),
                    post_id = table.Column<string>(type: "text", nullable: false),
                    company_name = table.Column<string>(type: "text", nullable: false),
                    change_kind = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlist_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_watchlist_event_watchlist_watchlist_id",
                        column: x => x.watchlist_id,
                        principalTable: "watchlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_watchlist_event_watchlist_id_occurred_at",
                table: "watchlist_event",
                columns: new[] { "watchlist_id", "occurred_at" });

            // The history starts with what is open now: every current match becomes a `New` (1) event at the time the
            // vacancy was first seen, so «did this company have anything open» is answered from day one. Closures
            // before the upgrade are not known.
            migrationBuilder.Sql(
                """
                INSERT INTO watchlist_event
                    (watchlist_id, source_id, board_id, post_id, company_name, change_kind, occurred_at)
                SELECT m.watchlist_id, m.source_id, m.board_id, m.post_id,
                       COALESCE(e.company_name, m.board_id), 1, s.first_seen_at
                FROM watchlist_vacancy m
                JOIN seen_vacancy s
                  ON s.source_id = m.source_id AND s.board_id = m.board_id AND s.post_id = m.post_id
                LEFT JOIN watchlist_entry e
                  ON e.watchlist_id = m.watchlist_id AND e.source_id = m.source_id AND e.board_id = m.board_id
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "watchlist_event");

            migrationBuilder.DropColumn(
                name: "digest_sent_at",
                table: "watchlist");
        }
    }
}
