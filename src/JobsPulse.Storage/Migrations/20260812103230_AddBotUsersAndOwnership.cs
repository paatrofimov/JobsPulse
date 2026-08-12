using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddBotUsersAndOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "worked_at",
                table: "watchlist_entry",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "owner_user_id",
                table: "watchlist",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bot_user",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    language = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_user", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_watchlist_owner_user_id",
                table: "watchlist",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bot_user_telegram_user_id",
                table: "bot_user",
                column: "telegram_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_user");

            migrationBuilder.DropIndex(
                name: "ix_watchlist_owner_user_id",
                table: "watchlist");

            migrationBuilder.DropColumn(
                name: "worked_at",
                table: "watchlist_entry");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "watchlist");
        }
    }
}
