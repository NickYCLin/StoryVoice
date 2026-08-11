using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryVoice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "book_collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_collections", x => x.Id);
                    table.UniqueConstraint("AK_book_collections_OwnerId_Id", x => new { x.OwnerId, x.Id });
                    table.ForeignKey(
                        name: "FK_book_collections_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "book_collection_books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    VolumeLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_collection_books", x => x.Id);
                    table.CheckConstraint("CK_book_collection_books_sort_order", "\"SortOrder\" >= 0 AND \"SortOrder\" <= 1000000");
                    table.ForeignKey(
                        name: "FK_book_collection_books_book_collections_OwnerId_CollectionId",
                        columns: x => new { x.OwnerId, x.CollectionId },
                        principalTable: "book_collections",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection_shares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GranteeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GranteeEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_shares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_collection_shares_AspNetUsers_GranteeUserId",
                        column: x => x.GranteeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_collection_shares_book_collections_OwnerId_CollectionId",
                        columns: x => new { x.OwnerId, x.CollectionId },
                        principalTable: "book_collections",
                        principalColumns: new[] { "OwnerId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_collection_books_owner_collection_book",
                table: "book_collection_books",
                columns: new[] { "OwnerId", "CollectionId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_collection_books_owner_collection_sort",
                table: "book_collection_books",
                columns: new[] { "OwnerId", "CollectionId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_book_collections_OwnerId_NormalizedName",
                table: "book_collections",
                columns: new[] { "OwnerId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_shares_grantee",
                table: "collection_shares",
                column: "GranteeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_collection_shares_OwnerId_CollectionId",
                table: "collection_shares",
                columns: new[] { "OwnerId", "CollectionId" });

            migrationBuilder.CreateIndex(
                name: "UX_collection_shares_collection_grantee",
                table: "collection_shares",
                columns: new[] { "CollectionId", "GranteeUserId" },
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE "book_collection_books"
                ADD CONSTRAINT "FK_book_collection_books_books_OwnerId_BookId"
                FOREIGN KEY ("OwnerId", "BookId")
                REFERENCES "books" ("OwnerId", "Id")
                ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "book_collection_books"
                DROP CONSTRAINT IF EXISTS "FK_book_collection_books_books_OwnerId_BookId";
                """);

            migrationBuilder.DropTable(
                name: "book_collection_books");

            migrationBuilder.DropTable(
                name: "collection_shares");

            migrationBuilder.DropTable(
                name: "book_collections");
        }
    }
}
