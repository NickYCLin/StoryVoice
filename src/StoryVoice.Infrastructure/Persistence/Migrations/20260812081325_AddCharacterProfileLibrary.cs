using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterProfileLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cvp_character_scope",
                table: "character_voice_profiles");

            migrationBuilder.DropIndex(
                name: "IX_character_voice_profiles_OwnerId_SeriesId_CharacterId",
                table: "character_voice_profiles");

            migrationBuilder.DropIndex(
                name: "IX_cvp_character_status",
                table: "character_voice_profiles");

            migrationBuilder.DropIndex(
                name: "UX_cvp_base_per_character",
                table: "character_voice_profiles");

            migrationBuilder.DropIndex(
                name: "UX_cvp_scene_per_character",
                table: "character_voice_profiles");

            migrationBuilder.DropColumn(
                name: "CharacterId",
                table: "character_voice_profiles");

            migrationBuilder.RenameColumn(
                name: "SeriesId",
                table: "character_voice_profiles",
                newName: "CharacterProfileId");

            migrationBuilder.AddColumn<Guid>(
                name: "CharacterProfileId",
                table: "series_characters",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "character_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AvatarRelativePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Age = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Gender = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Birthday = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Personality = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Catchphrase = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Background = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SpeakingStyle = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_profiles", x => x.Id);
                    table.UniqueConstraint("AK_character_profiles_scope", x => new { x.OwnerId, x.Id });
                    table.ForeignKey(
                        name: "FK_character_profiles_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_series_characters_character_profile",
                table: "series_characters",
                columns: new[] { "OwnerId", "CharacterProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_cvp_character_status",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "CharacterProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_cvp_base_per_character",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "CharacterProfileId", "Kind" },
                unique: true,
                filter: "\"Kind\" = 'Base'");

            migrationBuilder.CreateIndex(
                name: "UX_cvp_scene_per_character",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "CharacterProfileId", "SceneCode" },
                unique: true,
                filter: "\"SceneCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_character_profiles_owner_name",
                table: "character_profiles",
                columns: new[] { "OwnerId", "CanonicalName" });

            migrationBuilder.AddForeignKey(
                name: "FK_cvp_character_profile",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "CharacterProfileId" },
                principalTable: "character_profiles",
                principalColumns: new[] { "OwnerId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_series_characters_character_profile",
                table: "series_characters",
                columns: new[] { "OwnerId", "CharacterProfileId" },
                principalTable: "character_profiles",
                principalColumns: new[] { "OwnerId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cvp_character_profile",
                table: "character_voice_profiles");

            migrationBuilder.DropForeignKey(
                name: "FK_series_characters_character_profile",
                table: "series_characters");

            migrationBuilder.DropTable(
                name: "character_profiles");

            migrationBuilder.DropIndex(
                name: "IX_series_characters_character_profile",
                table: "series_characters");

            migrationBuilder.DropIndex(
                name: "IX_cvp_character_status",
                table: "character_voice_profiles");

            migrationBuilder.DropIndex(
                name: "UX_cvp_base_per_character",
                table: "character_voice_profiles");

            migrationBuilder.DropIndex(
                name: "UX_cvp_scene_per_character",
                table: "character_voice_profiles");

            migrationBuilder.DropColumn(
                name: "CharacterProfileId",
                table: "series_characters");

            migrationBuilder.RenameColumn(
                name: "CharacterProfileId",
                table: "character_voice_profiles",
                newName: "SeriesId");

            migrationBuilder.AddColumn<Guid>(
                name: "CharacterId",
                table: "character_voice_profiles",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_character_voice_profiles_OwnerId_SeriesId_CharacterId",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "SeriesId", "CharacterId" });

            migrationBuilder.CreateIndex(
                name: "IX_cvp_character_status",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "CharacterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_cvp_base_per_character",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "CharacterId", "Kind" },
                unique: true,
                filter: "\"Kind\" = 'Base'");

            migrationBuilder.CreateIndex(
                name: "UX_cvp_scene_per_character",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "CharacterId", "SceneCode" },
                unique: true,
                filter: "\"SceneCode\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_cvp_character_scope",
                table: "character_voice_profiles",
                columns: new[] { "OwnerId", "SeriesId", "CharacterId" },
                principalTable: "series_characters",
                principalColumns: new[] { "OwnerId", "SeriesId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
