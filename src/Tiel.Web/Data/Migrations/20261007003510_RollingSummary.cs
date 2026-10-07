using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiel.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RollingSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SummarizedThroughSequence",
                table: "Conversations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Conversations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SummarizedThroughSequence",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Conversations");
        }
    }
}
