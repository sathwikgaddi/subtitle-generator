using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subtitles.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoLanguageHint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "language_hint",
                table: "videos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "language_hint",
                table: "videos");
        }
    }
}
