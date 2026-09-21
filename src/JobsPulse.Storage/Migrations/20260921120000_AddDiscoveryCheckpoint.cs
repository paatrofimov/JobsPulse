using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_checkpoint",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    iteration = table.Column<int>(type: "integer", nullable: false),
                    is_full = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_from_collection_id = table.Column<string>(type: "text", nullable: true),
                    resume_from_collection_id = table.Column<string>(type: "text", nullable: true),
                    collections_total = table.Column<int>(type: "integer", nullable: false),
                    collections_done = table.Column<int>(type: "integer", nullable: false),
                    collections_processed = table.Column<int>(type: "integer", nullable: false),
                    collections_failed = table.Column<int>(type: "integer", nullable: false),
                    records_seen = table.Column<long>(type: "bigint", nullable: false),
                    tokens_found = table.Column<int>(type: "integer", nullable: false),
                    boards_added = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_checkpoint", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discovery_checkpoint_iteration",
                table: "discovery_checkpoint",
                column: "iteration",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovery_checkpoint");
        }
    }
}
