using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Musync.Migrations
{
    /// <inheritdoc />
    public partial class UniqueActiveTrackHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackHistories_Provider_TrackId",
                table: "TrackHistories");

            migrationBuilder.CreateIndex(
                name: "IX_TrackHistories_Provider_TrackId",
                table: "TrackHistories",
                columns: new[] { "Provider", "TrackId" },
                unique: true,
                filter: "\"RemovedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrackHistories_Provider_TrackId",
                table: "TrackHistories");

            migrationBuilder.CreateIndex(
                name: "IX_TrackHistories_Provider_TrackId",
                table: "TrackHistories",
                columns: new[] { "Provider", "TrackId" });
        }
    }
}
