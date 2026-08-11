using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "configuration",
                table: "watchlist_entry",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "configuration",
                table: "board_registry",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "configuration",
                table: "watchlist_entry");

            migrationBuilder.DropColumn(
                name: "configuration",
                table: "board_registry");
        }
    }
}
