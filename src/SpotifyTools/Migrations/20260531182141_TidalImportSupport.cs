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
            migrationBuilder.RenameTable(
                name: "refresh_tokens",
                newName: "RefreshTokens");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "spotify");

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
                name: "IX_TidalTrackMappings_TidalTrackId",
                table: "TidalTrackMappings",
                column: "TidalTrackId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "RefreshTokens");

            // Rename back to original table name
            migrationBuilder.RenameTable(
                name: "RefreshTokens",
                newName: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "TidalTrackMappings");
        }
    }
}
