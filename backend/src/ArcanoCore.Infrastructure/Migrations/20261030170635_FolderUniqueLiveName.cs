using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcanoCore.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FolderUniqueLiveName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Folder_OwnerId_ParentId_Name",
                table: "Folder");

            migrationBuilder.CreateIndex(
                name: "IX_Folder_OwnerId_ParentId_Name",
                table: "Folder",
                columns: new[] { "OwnerId", "ParentId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Folder_OwnerId_ParentId_Name",
                table: "Folder");

            migrationBuilder.CreateIndex(
                name: "IX_Folder_OwnerId_ParentId_Name",
                table: "Folder",
                columns: new[] { "OwnerId", "ParentId", "Name" },
                unique: true);
        }
    }
}
