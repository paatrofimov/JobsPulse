using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchlistEventRunId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "digest_sent_at",
                table: "watchlist");

            migrationBuilder.AddColumn<long>(
                name: "run_id",
                table: "watchlist_event",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "run_id",
                table: "watchlist_event");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "digest_sent_at",
                table: "watchlist",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
