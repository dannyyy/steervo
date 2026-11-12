using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Steervo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CostPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CostItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Frequency = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    StartMonth = table.Column<int>(type: "integer", nullable: false),
                    EndMonth = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostItem", x => x.Id);
                    table.CheckConstraint("ValidCostAmount", "\"Amount\" IS NULL OR (\"Amount\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Amount\", 2) = \"Amount\")");
                    table.CheckConstraint("ValidCostCategory", "\"Category\" IN ('Service','Tires','PublicCharging','HomeCharging','Fuel','Insurance','Tax','Parking','Other')");
                    table.CheckConstraint("ValidCostDirection", "\"Direction\" IN ('Expense','Income') AND (\"Frequency\" <> 'Included' OR (\"Amount\" IS NULL AND \"Direction\" = 'Expense'))");
                    table.CheckConstraint("ValidCostFrequency", "\"Frequency\" IN ('Once','Monthly','Yearly','Included') AND (\"Frequency\" <> 'Once' OR \"EndMonth\" IS NULL)");
                    table.CheckConstraint("ValidCostPeriod", "\"StartMonth\" BETWEEN 1 AND 1200 AND (\"EndMonth\" IS NULL OR \"EndMonth\" BETWEEN \"StartMonth\" AND 1200)");
                    table.ForeignKey(
                        name: "FK_CostItem_Candidate_EvaluationId_CandidateId",
                        columns: x => new { x.EvaluationId, x.CandidateId },
                        principalTable: "Candidate",
                        principalColumns: new[] { "EvaluationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CostItem_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CostPlanning",
                columns: table => new
                {
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    Months = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostPlanning", x => x.EvaluationId);
                    table.CheckConstraint("ValidCostPlanning", "\"Months\" BETWEEN 1 AND 1200 AND EXTRACT(DAY FROM \"Start\") = 1 AND EXTRACT(YEAR FROM \"Start\") BETWEEN 1900 AND 9899");
                    table.ForeignKey(
                        name: "FK_CostPlanning_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostItem_EvaluationId_CandidateId",
                table: "CostItem",
                columns: new[] { "EvaluationId", "CandidateId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CostItem");

            migrationBuilder.DropTable(
                name: "CostPlanning");
        }
    }
}
