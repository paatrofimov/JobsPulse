using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardPollState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "board_poll_state",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    board_id = table.Column<string>(type: "text", nullable: false),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_board_poll_state", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_board_poll_state_source_id_board_id",
                table: "board_poll_state",
                columns: ["source_id", "board_id"],
                unique: true);

            // The scheduling state used to be in memory only. Seeding it from what the boards already hold means the
            // first cycle after the upgrade does not re-read the whole registry: a board whose vacancies were seen
            // recently is not due yet, and one nothing is known about stays least-recently-polled and goes first.
            migrationBuilder.Sql(
                """
                INSERT INTO board_poll_state (source_id, board_id, last_polled_at)
                SELECT source_id, board_id, MAX(GREATEST(first_seen_at, updated_at, closed_at))
                FROM seen_vacancy
                GROUP BY source_id, board_id
                ON CONFLICT (source_id, board_id) DO NOTHING
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "board_poll_state");
        }
    }
}
