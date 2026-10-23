using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameTracker.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTitlePlatformUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Games_Title_Platform",
                table: "Games",
                columns: new[] { "Title", "Platform" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Games_Title_Platform",
                table: "Games");
        }
    }
}
