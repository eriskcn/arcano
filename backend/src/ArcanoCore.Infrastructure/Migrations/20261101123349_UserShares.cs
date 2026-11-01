using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcanoCore.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserShare",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: true),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserShare", x => x.Id);
                    table.CheckConstraint("CK_UserShare_OneTarget", "(\"FileId\" IS NULL) <> (\"FolderId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_UserShare_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserShare_AspNetUsers_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserShare_FileMetadata_FileId",
                        column: x => x.FileId,
                        principalTable: "FileMetadata",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserShare_Folder_FolderId",
                        column: x => x.FolderId,
                        principalTable: "Folder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserShare_FileId",
                table: "UserShare",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_UserShare_FolderId",
                table: "UserShare",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_UserShare_OwnerId_FileId",
                table: "UserShare",
                columns: new[] { "OwnerId", "FileId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserShare_OwnerId_FolderId",
                table: "UserShare",
                columns: new[] { "OwnerId", "FolderId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserShare_OwnerId_RecipientId_FileId",
                table: "UserShare",
                columns: new[] { "OwnerId", "RecipientId", "FileId" },
                unique: true,
                filter: "\"FileId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserShare_OwnerId_RecipientId_FolderId",
                table: "UserShare",
                columns: new[] { "OwnerId", "RecipientId", "FolderId" },
                unique: true,
                filter: "\"FolderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserShare_RecipientId",
                table: "UserShare",
                column: "RecipientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserShare");
        }
    }
}
