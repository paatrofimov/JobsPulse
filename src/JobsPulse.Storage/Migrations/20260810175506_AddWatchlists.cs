using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchlists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "watchlist_id",
                table: "outbox",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "watchlist_name",
                table: "outbox",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "watchlist",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    filter = table.Column<string>(type: "jsonb", nullable: false),
                    interval_minutes_override = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlist", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "watchlist_entry",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    watchlist_id = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    board_id = table.Column<string>(type: "text", nullable: false),
                    company_name = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlist_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_watchlist_entry_watchlist_watchlist_id",
                        column: x => x.watchlist_id,
                        principalTable: "watchlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "watchlist_vacancy",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    watchlist_id = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    board_id = table.Column<string>(type: "text", nullable: false),
                    post_id = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    filter_hash = table.Column<string>(type: "text", nullable: true),
                    matched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlist_vacancy", x => x.id);
                    table.ForeignKey(
                        name: "fk_watchlist_vacancy_watchlist_watchlist_id",
                        column: x => x.watchlist_id,
                        principalTable: "watchlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_watchlist_name",
                table: "watchlist",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_watchlist_entry_watchlist_id_source_id_board_id",
                table: "watchlist_entry",
                columns: new[] { "watchlist_id", "source_id", "board_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_watchlist_vacancy_source_id_board_id",
                table: "watchlist_vacancy",
                columns: new[] { "source_id", "board_id" });

            migrationBuilder.CreateIndex(
                name: "ix_watchlist_vacancy_watchlist_id_source_id_board_id_post_id",
                table: "watchlist_vacancy",
                columns: new[] { "watchlist_id", "source_id", "board_id", "post_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "watchlist_entry");

            migrationBuilder.DropTable(
                name: "watchlist_vacancy");

            migrationBuilder.DropTable(
                name: "watchlist");

            migrationBuilder.DropColumn(
                name: "watchlist_id",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "watchlist_name",
                table: "outbox");
        }
    }
}
