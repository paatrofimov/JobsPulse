using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JobsPulse.Storage.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectedPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rejected_posting",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    board_id = table.Column<string>(type: "text", nullable: false),
                    post_id = table.Column<string>(type: "text", nullable: false),
                    list_hash = table.Column<string>(type: "text", nullable: false),
                    filter_hash = table.Column<string>(type: "text", nullable: false),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rejected_posting", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rejected_posting_source_id_board_id_post_id",
                table: "rejected_posting",
                columns: new[] { "source_id", "board_id", "post_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rejected_posting");
        }
    }
}
