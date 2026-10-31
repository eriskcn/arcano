using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcanoCore.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedFileTagAndShareFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SharedLink_Folder_FolderId",
                table: "SharedLink");

            migrationBuilder.DropTable(
                name: "FileTag");

            migrationBuilder.DropIndex(
                name: "IX_SharedLink_FolderId",
                table: "SharedLink");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "SharedLink");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "SharedLink",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FileTag",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileMetadataId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileTag", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileTag_FileMetadata_FileMetadataId",
                        column: x => x.FileMetadataId,
                        principalTable: "FileMetadata",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SharedLink_FolderId",
                table: "SharedLink",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_FileTag_FileMetadataId_Name",
                table: "FileTag",
                columns: new[] { "FileMetadataId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SharedLink_Folder_FolderId",
                table: "SharedLink",
                column: "FolderId",
                principalTable: "Folder",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
