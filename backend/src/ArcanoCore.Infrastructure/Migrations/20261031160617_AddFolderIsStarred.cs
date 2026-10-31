using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArcanoCore.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderIsStarred : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStarred",
                table: "Folder",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsStarred",
                table: "Folder");
        }
    }
}
