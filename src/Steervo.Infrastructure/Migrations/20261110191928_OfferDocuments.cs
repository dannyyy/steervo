using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Steervo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfferDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OfferDocument",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferDocument", x => x.Id);
                    table.UniqueConstraint("AK_OfferDocument_EvaluationId_OfferId_Id", x => new { x.EvaluationId, x.OfferId, x.Id });
                    table.ForeignKey(
                        name: "FK_OfferDocument_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfferDocument_Offer_EvaluationId_OfferId",
                        columns: x => new { x.EvaluationId, x.OfferId },
                        principalTable: "Offer",
                        principalColumns: new[] { "EvaluationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PendingDocumentDeletion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingDocumentDeletion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    OfferRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    File_ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    File_FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    File_Length = table.Column<long>(type: "bigint", nullable: false),
                    File_Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRevision", x => x.Id);
                    table.CheckConstraint("ValidDocumentFile", "\"File_Length\" BETWEEN 1 AND 26214400 AND \"File_Sha256\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ValidDocumentNumber", "\"Number\" BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "FK_DocumentRevision_OfferDocument_EvaluationId_OfferId_Documen~",
                        columns: x => new { x.EvaluationId, x.OfferId, x.DocumentId },
                        principalTable: "OfferDocument",
                        principalColumns: new[] { "EvaluationId", "OfferId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentRevision_OfferRevision_EvaluationId_OfferId_OfferRe~",
                        columns: x => new { x.EvaluationId, x.OfferId, x.OfferRevisionNumber },
                        principalTable: "OfferRevision",
                        principalColumns: new[] { "EvaluationId", "OfferId", "Number" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevision_DocumentId_Number",
                table: "DocumentRevision",
                columns: new[] { "DocumentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevision_EvaluationId_OfferId_DocumentId",
                table: "DocumentRevision",
                columns: new[] { "EvaluationId", "OfferId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevision_EvaluationId_OfferId_OfferRevisionNumber",
                table: "DocumentRevision",
                columns: new[] { "EvaluationId", "OfferId", "OfferRevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingDocumentDeletion_AvailableAt",
                table: "PendingDocumentDeletion",
                column: "AvailableAt");
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "IX_DocumentRevision_ContentId" ON "DocumentRevision" ("File_ContentId");
                CREATE FUNCTION "ProtectDocumentRevision"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Document versions are immutable' USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER "ImmutableDocumentRevision" BEFORE UPDATE ON "DocumentRevision"
                    FOR EACH ROW WHEN (OLD IS DISTINCT FROM NEW) EXECUTE FUNCTION "ProtectDocumentRevision"();
                CREATE FUNCTION "QueueDocumentDeletion"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO "PendingDocumentDeletion" ("Id", "AvailableAt") VALUES (OLD."File_ContentId", CURRENT_TIMESTAMP)
                    ON CONFLICT ("Id") DO UPDATE SET "AvailableAt" = CURRENT_TIMESTAMP;
                    RETURN OLD;
                END $$;
                CREATE TRIGGER "DeletedDocumentContent" AFTER DELETE ON "DocumentRevision"
                    FOR EACH ROW EXECUTE FUNCTION "QueueDocumentDeletion"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION \"ProtectDocumentRevision\"() CASCADE; DROP FUNCTION \"QueueDocumentDeletion\"() CASCADE;");
            migrationBuilder.DropTable(
                name: "DocumentRevision");

            migrationBuilder.DropTable(
                name: "PendingDocumentDeletion");

            migrationBuilder.DropTable(
                name: "OfferDocument");
        }
    }
}
