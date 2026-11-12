using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Steervo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Scenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_CostItem_EvaluationId_Id",
                table: "CostItem",
                columns: new[] { "EvaluationId", "Id" });

            migrationBuilder.CreateTable(
                name: "CalculationSettings",
                columns: table => new
                {
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnnualReturnPercent = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalculationSettings", x => x.EvaluationId);
                    table.CheckConstraint("ValidAnnualReturn", "\"AnnualReturnPercent\" BETWEEN -99 AND 100");
                    table.ForeignKey(
                        name: "FK_CalculationSettings_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Scenario",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Terms_AnnualKilometres = table.Column<int>(type: "integer", nullable: true),
                    Terms_CostsReviewed = table.Column<bool>(type: "boolean", nullable: false),
                    Terms_DownPayment = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_ExcessKilometrePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_FinalPayment = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_IncludedKilometresPerYear = table.Column<int>(type: "integer", nullable: true),
                    Terms_InitialFees = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_LoanAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_LoanInterestPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_Mode = table.Column<string>(type: "text", nullable: false),
                    Terms_MonthlyPayment = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_PurchasePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    Terms_TermMonths = table.Column<int>(type: "integer", nullable: true),
                    Terms_UnusedKilometreCredit = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenario", x => x.Id);
                    table.UniqueConstraint("AK_Scenario_EvaluationId_Id", x => new { x.EvaluationId, x.Id });
                    table.CheckConstraint("ValidAcquisitionMode", "\"Terms_Mode\" IN ('Purchase','LeaseReturn','LeaseBuyout','Financing')");
                    table.CheckConstraint("ValidScenarioAnnualKilometres", "\"Terms_AnnualKilometres\" IS NULL OR \"Terms_AnnualKilometres\" BETWEEN 0 AND 1000000");
                    table.CheckConstraint("ValidScenarioDownPayment", "\"Terms_DownPayment\" IS NULL OR (\"Terms_DownPayment\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Terms_DownPayment\", 2) = \"Terms_DownPayment\")");
                    table.CheckConstraint("ValidScenarioExcessKilometrePrice", "\"Terms_ExcessKilometrePrice\" IS NULL OR \"Terms_ExcessKilometrePrice\" BETWEEN 0 AND 10000");
                    table.CheckConstraint("ValidScenarioFinalPayment", "\"Terms_FinalPayment\" IS NULL OR (\"Terms_FinalPayment\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Terms_FinalPayment\", 2) = \"Terms_FinalPayment\")");
                    table.CheckConstraint("ValidScenarioIncludedKilometresPerYear", "\"Terms_IncludedKilometresPerYear\" IS NULL OR \"Terms_IncludedKilometresPerYear\" BETWEEN 0 AND 1000000");
                    table.CheckConstraint("ValidScenarioInitialFees", "\"Terms_InitialFees\" IS NULL OR (\"Terms_InitialFees\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Terms_InitialFees\", 2) = \"Terms_InitialFees\")");
                    table.CheckConstraint("ValidScenarioLoan", "(\"Terms_LoanInterestPercent\" IS NULL OR \"Terms_LoanInterestPercent\" BETWEEN 0 AND 100) AND (\"Terms_Mode\" <> 'Financing' OR \"Terms_LoanAmount\" IS NULL OR \"Terms_PurchasePrice\" IS NULL OR \"Terms_LoanAmount\" <= \"Terms_PurchasePrice\")");
                    table.CheckConstraint("ValidScenarioLoanAmount", "\"Terms_LoanAmount\" IS NULL OR (\"Terms_LoanAmount\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Terms_LoanAmount\", 2) = \"Terms_LoanAmount\")");
                    table.CheckConstraint("ValidScenarioMonthlyPayment", "\"Terms_MonthlyPayment\" IS NULL OR (\"Terms_MonthlyPayment\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Terms_MonthlyPayment\", 2) = \"Terms_MonthlyPayment\")");
                    table.CheckConstraint("ValidScenarioPurchasePrice", "\"Terms_PurchasePrice\" IS NULL OR (\"Terms_PurchasePrice\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Terms_PurchasePrice\", 2) = \"Terms_PurchasePrice\")");
                    table.CheckConstraint("ValidScenarioTerm", "\"Terms_TermMonths\" IS NULL OR \"Terms_TermMonths\" BETWEEN 1 AND 1200");
                    table.CheckConstraint("ValidScenarioUnusedKilometreCredit", "\"Terms_UnusedKilometreCredit\" IS NULL OR \"Terms_UnusedKilometreCredit\" BETWEEN 0 AND 10000");
                    table.ForeignKey(
                        name: "FK_Scenario_Evaluations_EvaluationId",
                        column: x => x.EvaluationId,
                        principalTable: "Evaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Scenario_OfferRevision_EvaluationId_OfferId_RevisionNumber",
                        columns: x => new { x.EvaluationId, x.OfferId, x.RevisionNumber },
                        principalTable: "OfferRevision",
                        principalColumns: new[] { "EvaluationId", "OfferId", "Number" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResidualCheckpoint",
                columns: table => new
                {
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Low = table.Column<decimal>(type: "numeric", nullable: true),
                    Expected = table.Column<decimal>(type: "numeric", nullable: true),
                    High = table.Column<decimal>(type: "numeric", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidualCheckpoint", x => new { x.ScenarioId, x.Month });
                    table.CheckConstraint("OrderedResidualBand", "(\"Low\" IS NULL OR \"Expected\" IS NULL OR \"Low\" <= \"Expected\") AND (\"Expected\" IS NULL OR \"High\" IS NULL OR \"Expected\" <= \"High\") AND (\"Low\" IS NULL OR \"High\" IS NULL OR \"Low\" <= \"High\")");
                    table.CheckConstraint("ValidResidualExpected", "\"Expected\" IS NULL OR (\"Expected\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Expected\", 2) = \"Expected\")");
                    table.CheckConstraint("ValidResidualHigh", "\"High\" IS NULL OR (\"High\" BETWEEN 0 AND 1000000000000 AND ROUND(\"High\", 2) = \"High\")");
                    table.CheckConstraint("ValidResidualLow", "\"Low\" IS NULL OR (\"Low\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Low\", 2) = \"Low\")");
                    table.CheckConstraint("ValidResidualMonth", "\"Month\" BETWEEN 1 AND 1200");
                    table.ForeignKey(
                        name: "FK_ResidualCheckpoint_Scenario_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "Scenario",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScenarioCostAdjustment",
                columns: table => new
                {
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    StartMonth = table.Column<int>(type: "integer", nullable: false),
                    EndMonth = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioCostAdjustment", x => new { x.ScenarioId, x.CostId });
                    table.CheckConstraint("ValidAdjustmentAmount", "\"Amount\" IS NULL OR (\"Amount\" BETWEEN 0 AND 1000000000000 AND ROUND(\"Amount\", 2) = \"Amount\")");
                    table.CheckConstraint("ValidAdjustmentPeriod", "\"StartMonth\" BETWEEN 1 AND 1200 AND (\"EndMonth\" IS NULL OR \"EndMonth\" BETWEEN \"StartMonth\" AND 1200)");
                    table.ForeignKey(
                        name: "FK_ScenarioCostAdjustment_CostItem_EvaluationId_CostId",
                        columns: x => new { x.EvaluationId, x.CostId },
                        principalTable: "CostItem",
                        principalColumns: new[] { "EvaluationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScenarioCostAdjustment_Scenario_EvaluationId_ScenarioId",
                        columns: x => new { x.EvaluationId, x.ScenarioId },
                        principalTable: "Scenario",
                        principalColumns: new[] { "EvaluationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Scenario_EvaluationId_OfferId_RevisionNumber",
                table: "Scenario",
                columns: new[] { "EvaluationId", "OfferId", "RevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioCostAdjustment_EvaluationId_CostId",
                table: "ScenarioCostAdjustment",
                columns: new[] { "EvaluationId", "CostId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScenarioCostAdjustment_EvaluationId_ScenarioId",
                table: "ScenarioCostAdjustment",
                columns: new[] { "EvaluationId", "ScenarioId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalculationSettings");

            migrationBuilder.DropTable(
                name: "ResidualCheckpoint");

            migrationBuilder.DropTable(
                name: "ScenarioCostAdjustment");

            migrationBuilder.DropTable(
                name: "Scenario");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CostItem_EvaluationId_Id",
                table: "CostItem");
        }
    }
}
