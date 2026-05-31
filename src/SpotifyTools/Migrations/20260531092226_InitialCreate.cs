using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyTools.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "JobRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TracksAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    TracksRemovedLiked = table.Column<int>(type: "INTEGER", nullable: false),
                    TracksRemovedManual = table.Column<int>(type: "INTEGER", nullable: false),
                    TracksSkipped = table.Column<int>(type: "INTEGER", nullable: false),
                    NewAlbumsEncountered = table.Column<int>(type: "INTEGER", nullable: false),
                    QueueSizeAfter = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedAlbums",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SpotifyAlbumId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AlbumName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ArtistName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FirstProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedAlbums", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SpotifyTrackId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TrackName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ArtistName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AlbumName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RemovalReason = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedAlbums_SpotifyAlbumId",
                table: "ProcessedAlbums",
                column: "SpotifyAlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackHistories_RemovedAt",
                table: "TrackHistories",
                column: "RemovedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrackHistories_SpotifyTrackId",
                table: "TrackHistories",
                column: "SpotifyTrackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "JobRuns");

            migrationBuilder.DropTable(
                name: "ProcessedAlbums");

            migrationBuilder.DropTable(
                name: "TrackHistories");
        }
    }
}
