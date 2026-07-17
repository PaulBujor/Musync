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
            // Collapse duplicate active rows to one per (Provider, TrackId) so the unique index applies.
            migrationBuilder.Sql(
                """
                DELETE FROM "TrackHistories"
                WHERE "RemovedAt" IS NULL
                  AND "Id" NOT IN (
                      SELECT MIN("Id")
                      FROM "TrackHistories"
                      WHERE "RemovedAt" IS NULL
                      GROUP BY "Provider", "TrackId"
                  );
                """);

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
