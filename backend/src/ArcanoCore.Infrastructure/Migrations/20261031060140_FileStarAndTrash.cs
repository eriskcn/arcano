using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcanoCore.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FileStarAndTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "FileMetadata",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStarred",
                table: "FileMetadata",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "FileMetadata");

            migrationBuilder.DropColumn(
                name: "IsStarred",
                table: "FileMetadata");
        }
    }
}
