using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Musync.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Command = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DryRun = table.Column<bool>(type: "boolean", nullable: false),
                    Limit = table.Column<int>(type: "integer", nullable: true),
                    TracksAdded = table.Column<int>(type: "integer", nullable: false),
                    TracksRemovedLiked = table.Column<int>(type: "integer", nullable: false),
                    TracksRemovedManual = table.Column<int>(type: "integer", nullable: false),
                    TracksSkipped = table.Column<int>(type: "integer", nullable: false),
                    NewAlbumsEncountered = table.Column<int>(type: "integer", nullable: false),
                    QueueSizeAfter = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedAlbums",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AlbumId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AlbumName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ArtistName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FirstProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedAlbums", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TrackId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TrackName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ArtistName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AlbumName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovalReason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceTrackId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TargetProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetTrackId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Isrc = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FirstMappedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedAlbums_Provider_AlbumId",
                table: "ProcessedAlbums",
                columns: new[] { "Provider", "AlbumId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Provider_UpdatedAt",
                table: "RefreshTokens",
                columns: new[] { "Provider", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackHistories_JobRunId",
                table: "TrackHistories",
                column: "JobRunId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackHistories_Provider_TrackId",
                table: "TrackHistories",
                columns: new[] { "Provider", "TrackId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackHistories_RemovedAt",
                table: "TrackHistories",
                column: "RemovedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrackMappings_SourceProvider_SourceTrackId",
                table: "TrackMappings",
                columns: new[] { "SourceProvider", "SourceTrackId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobRuns");

            migrationBuilder.DropTable(
                name: "ProcessedAlbums");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "TrackHistories");

            migrationBuilder.DropTable(
                name: "TrackMappings");
        }
    }
}
