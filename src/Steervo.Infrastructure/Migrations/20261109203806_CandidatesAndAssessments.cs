using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Steervo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CandidatesAndAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Criterion_EvaluationId_Id",
                table: "Criterion",
                columns: new[] { "EvaluationId", "Id" });

            migrationBuilder.CreateTable(
                name: "Candidate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Variant = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Condition = table.Column<string>(type: "text", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    Mileage = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Links = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidate", x => x.Id);
                    table.UniqueConstraint("AK_Candidate_EvaluationId_Id", x => new { x.EvaluationId, x.Id });
                    table.CheckConstraint("CandidateStatusReason", "\"Status\" NOT IN ('Deferred','Excluded') OR length(btrim(\"StatusReason\")) > 0");
                    table.CheckConstraint("ValidCandidateCondition", "\"Condition\" IN ('Unknown','New','Used')");
                    table.CheckConstraint("ValidCandidateMileage", "\"Mileage\" IS NULL OR \"Mileage\" >= 0");
                    table.CheckConstraint("ValidCandidateStatus", "\"Status\" IN ('Watching','Alternative','Favorite','Deferred','Excluded')");
                    table.CheckConstraint("ValidCandidateYear", "\"Year\" IS NULL OR \"Year\" BETWEEN 1 AND 9999");
                    table.ForeignKey(
                        name: "FK_Candidate_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BooleanValue = table.Column<bool>(type: "boolean", nullable: true),
                    MeasurementValue = table.Column<decimal>(type: "numeric", nullable: true),
                    RatingValue = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assessment", x => x.Id);
                    table.CheckConstraint("SingleAssessmentValue", "(CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"MeasurementValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"RatingValue\" IS NULL THEN 0 ELSE 1 END) <= 1");
                    table.CheckConstraint("ValidAssessmentRating", "\"RatingValue\" IS NULL OR \"RatingValue\" BETWEEN 1 AND 5");
                    table.CheckConstraint("ValidAssessmentSource", "\"Source\" IN ('Unknown','Manufacturer','Offer','Inspection','TestDrive','Research')");
                    table.ForeignKey(
                        name: "FK_Assessment_Candidate_EvaluationId_CandidateId",
                        columns: x => new { x.EvaluationId, x.CandidateId },
                        principalTable: "Candidate",
                        principalColumns: new[] { "EvaluationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Assessment_Criterion_EvaluationId_CriterionId",
                        columns: x => new { x.EvaluationId, x.CriterionId },
                        principalTable: "Criterion",
                        principalColumns: new[] { "EvaluationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Assessment_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assessment_CandidateId_CriterionId",
                table: "Assessment",
                columns: new[] { "CandidateId", "CriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assessment_EvaluationId_CandidateId",
                table: "Assessment",
                columns: new[] { "EvaluationId", "CandidateId" });

            migrationBuilder.CreateIndex(
                name: "IX_Assessment_EvaluationId_CriterionId",
                table: "Assessment",
                columns: new[] { "EvaluationId", "CriterionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assessment");

            migrationBuilder.DropTable(
                name: "Candidate");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Criterion_EvaluationId_Id",
                table: "Criterion");
        }
    }
}
