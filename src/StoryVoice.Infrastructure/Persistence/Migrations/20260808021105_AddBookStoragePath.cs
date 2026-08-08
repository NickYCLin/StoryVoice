using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookStoragePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StoragePath",
                table: "books",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoragePath",
                table: "books");
        }
    }
}
