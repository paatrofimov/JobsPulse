using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchlistEventLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "location",
                table: "watchlist_event",
                type: "text",
                nullable: true);

            // Existing history learns where its vacancies are from `seen_vacancy` - the location, or the first office
            // when the board names none, as the commit writes it from now on.
            migrationBuilder.Sql(
                """
                UPDATE watchlist_event e
                SET location = COALESCE(NULLIF(btrim(s.location), ''), NULLIF(btrim(s.offices[1]), ''))
                FROM seen_vacancy s
                WHERE s.source_id = e.source_id AND s.board_id = e.board_id AND s.post_id = e.post_id
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "location",
                table: "watchlist_event");
        }
    }
}
