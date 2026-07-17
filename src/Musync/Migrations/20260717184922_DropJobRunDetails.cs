using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Musync.Migrations
{
    /// <inheritdoc />
    public partial class DropJobRunDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Details",
                table: "JobRuns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "JobRuns",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }
    }
}
