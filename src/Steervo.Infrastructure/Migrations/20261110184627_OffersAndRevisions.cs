using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Steervo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OffersAndRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Evaluations",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Offer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offer", x => x.Id);
                    table.UniqueConstraint("AK_Offer_EvaluationId_Id", x => new { x.EvaluationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Offer_Candidate_EvaluationId_CandidateId",
                        columns: x => new { x.EvaluationId, x.CandidateId },
                        principalTable: "Candidate",
                        principalColumns: new[] { "EvaluationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Offer_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfferRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangeNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Terms_AnnualInterestPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_BasePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_Commitment = table.Column<string>(type: "text", nullable: false),
                    Terms_DeliveryNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Terms_DeliveryOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Terms_DownPayment = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_ExcessKilometrePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_IncludedKilometresPerYear = table.Column<int>(type: "integer", nullable: true),
                    Terms_IssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Terms_Kind = table.Column<string>(type: "text", nullable: false),
                    Terms_Lender = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Terms_MonthlyPayment = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Terms_Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Terms_Provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Terms_ResidualPayment = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_TermMonths = table.Column<int>(type: "integer", nullable: true),
                    Terms_UnusedKilometreCredit = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Terms_VehicleDescription = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferRevision", x => x.Id);
                    table.UniqueConstraint("AK_OfferRevision_EvaluationId_OfferId_Number", x => new { x.EvaluationId, x.OfferId, x.Number });
                    table.CheckConstraint("ValidOfferCommitment", "\"Terms_Commitment\" IN ('Requested','NonBinding','Binding')");
                    table.CheckConstraint("ValidOfferKind", "\"Terms_Kind\" IN ('Unknown','Purchase','Leasing','Financing')");
                    table.CheckConstraint("ValidOfferRevisionNumber", "\"Number\" > 0");
                    table.ForeignKey(
                        name: "FK_OfferRevision_Offer_EvaluationId_OfferId",
                        columns: x => new { x.EvaluationId, x.OfferId },
                        principalTable: "Offer",
                        principalColumns: new[] { "EvaluationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfferAssessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    BooleanValue = table.Column<bool>(type: "boolean", nullable: true),
                    MeasurementValue = table.Column<decimal>(type: "numeric", nullable: true),
                    RatingValue = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferAssessment", x => x.Id);
                    table.CheckConstraint("SingleOfferAssessmentValue", "(CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"MeasurementValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"RatingValue\" IS NULL THEN 0 ELSE 1 END) <= 1");
                    table.CheckConstraint("ValidOfferAssessmentRating", "\"RatingValue\" IS NULL OR \"RatingValue\" BETWEEN 1 AND 5");
                    table.CheckConstraint("ValidOfferAssessmentSource", "\"Source\" IN ('Unknown','Manufacturer','Offer','Inspection','TestDrive','Research')");
                    table.ForeignKey(
                        name: "FK_OfferAssessment_Criterion_EvaluationId_CriterionId",
                        columns: x => new { x.EvaluationId, x.CriterionId },
                        principalTable: "Criterion",
                        principalColumns: new[] { "EvaluationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfferAssessment_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfferAssessment_OfferRevision_EvaluationId_OfferId_Revision~",
                        columns: x => new { x.EvaluationId, x.OfferId, x.RevisionNumber },
                        principalTable: "OfferRevision",
                        principalColumns: new[] { "EvaluationId", "OfferId", "Number" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfferLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    PriceKind = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    Effect = table.Column<string>(type: "text", nullable: true),
                    CostCategory = table.Column<string>(type: "text", nullable: true),
                    DurationMonths = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferLine", x => x.Id);
                    table.CheckConstraint("ValidOfferLineKind", "\"Kind\" IN ('Accessory','Fee','Benefit','Equipment')");
                    table.CheckConstraint("ValidOfferLinePrice", "(\"PriceKind\" = 'Amount' AND \"Amount\" IS NOT NULL AND \"Amount\" >= 0) OR (\"PriceKind\" IN ('Unknown','Included') AND \"Amount\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_OfferLine_OfferRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "OfferRevision",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ValidEvaluationCurrency",
                table: "Evaluations",
                sql: "\"Currency\" IS NULL OR \"Currency\" ~ '^[A-Z]{3}$'");

            migrationBuilder.CreateIndex(
                name: "IX_Offer_EvaluationId_CandidateId",
                table: "Offer",
                columns: new[] { "EvaluationId", "CandidateId" });

            migrationBuilder.CreateIndex(
                name: "IX_OfferAssessment_EvaluationId_CriterionId",
                table: "OfferAssessment",
                columns: new[] { "EvaluationId", "CriterionId" });

            migrationBuilder.CreateIndex(
                name: "IX_OfferAssessment_EvaluationId_OfferId_RevisionNumber",
                table: "OfferAssessment",
                columns: new[] { "EvaluationId", "OfferId", "RevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_OfferAssessment_OfferId_CriterionId",
                table: "OfferAssessment",
                columns: new[] { "OfferId", "CriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfferLine_RevisionId_Position",
                table: "OfferLine",
                columns: new[] { "RevisionId", "Position" },
                unique: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION "RejectOfferRevisionUpdate"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Offer revisions are immutable.' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER "ImmutableOfferRevision" BEFORE UPDATE ON "OfferRevision"
                    FOR EACH ROW WHEN (OLD IS DISTINCT FROM NEW) EXECUTE FUNCTION "RejectOfferRevisionUpdate"();
                CREATE TRIGGER "ImmutableOfferLine" BEFORE UPDATE ON "OfferLine"
                    FOR EACH ROW WHEN (OLD IS DISTINCT FROM NEW) EXECUTE FUNCTION "RejectOfferRevisionUpdate"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION \"RejectOfferRevisionUpdate\"() CASCADE;");
            migrationBuilder.DropTable(
                name: "OfferAssessment");

            migrationBuilder.DropTable(
                name: "OfferLine");

            migrationBuilder.DropTable(
                name: "OfferRevision");

            migrationBuilder.DropTable(
                name: "Offer");

            migrationBuilder.DropCheckConstraint(
                name: "ValidEvaluationCurrency",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Evaluations");
        }
    }
}
