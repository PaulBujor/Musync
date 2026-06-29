using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyTools.Migrations
{
    /// <inheritdoc />
    public partial class TidalImportSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_refresh_tokens",
                table: "refresh_tokens");

            migrationBuilder.RenameTable(
                name: "refresh_tokens",
                newName: "RefreshTokens");

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "JobRuns",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RefreshTokens",
                table: "RefreshTokens",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "TidalTrackMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TidalTrackId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SpotifyTrackId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Isrc = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FirstMappedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TidalTrackMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Provider_UpdatedAt",
                table: "RefreshTokens",
                columns: new[] { "Provider", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TidalTrackMappings_TidalTrackId",
                table: "TidalTrackMappings",
                column: "TidalTrackId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TidalTrackMappings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RefreshTokens",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_Provider_UpdatedAt",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "JobRuns");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "RefreshTokens");

            migrationBuilder.RenameTable(
                name: "RefreshTokens",
                newName: "refresh_tokens");

            migrationBuilder.AddPrimaryKey(
                name: "PK_refresh_tokens",
                table: "refresh_tokens",
                column: "Id");
        }
    }
}
