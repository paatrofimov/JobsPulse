using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "board_registry",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    board_id = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    board_url = table.Column<string>(type: "text", nullable: true),
                    job_count = table.Column<int>(type: "integer", nullable: false),
                    discovered_via = table.Column<string>(type: "text", nullable: false),
                    discovered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_board_registry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "crawl_index_state",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    collection_id = table.Column<string>(type: "text", nullable: false),
                    records_seen = table.Column<long>(type: "bigint", nullable: false),
                    tokens_found = table.Column<int>(type: "integer", nullable: false),
                    boards_added = table.Column<int>(type: "integer", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crawl_index_state", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_board_registry_source_id_board_id",
                table: "board_registry",
                columns: new[] { "source_id", "board_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_crawl_index_state_source_id_collection_id",
                table: "crawl_index_state",
                columns: new[] { "source_id", "collection_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "board_registry");

            migrationBuilder.DropTable(
                name: "crawl_index_state");
        }
    }
}
