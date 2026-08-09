using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddSeenVacancyFilterHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "filter_hash",
                table: "seen_vacancy",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "filter_hash",
                table: "seen_vacancy");
        }
    }
}
