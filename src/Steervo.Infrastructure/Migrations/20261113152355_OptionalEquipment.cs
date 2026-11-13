using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Steervo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptionalEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "SingleOfferAssessmentValue",
                table: "OfferAssessment");

            migrationBuilder.DropCheckConstraint(
                name: "SingleAssessmentValue",
                table: "Assessment");

            migrationBuilder.AddColumn<bool>(
                name: "OptionalAvailable",
                table: "OfferAssessment",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OptionalAvailable",
                table: "Assessment",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "SingleOfferAssessmentValue",
                table: "OfferAssessment",
                sql: "(CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"MeasurementValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"RatingValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"OptionalAvailable\" THEN 1 ELSE 0 END) <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "SingleAssessmentValue",
                table: "Assessment",
                sql: "(CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"MeasurementValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"RatingValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"OptionalAvailable\" THEN 1 ELSE 0 END) <= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Assessment" WHERE "OptionalAvailable")
                       OR EXISTS (SELECT 1 FROM "OfferAssessment" WHERE "OptionalAvailable") THEN
                        RAISE EXCEPTION 'Cannot downgrade while optional equipment assessments exist';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "SingleOfferAssessmentValue",
                table: "OfferAssessment");

            migrationBuilder.DropCheckConstraint(
                name: "SingleAssessmentValue",
                table: "Assessment");

            migrationBuilder.DropColumn(
                name: "OptionalAvailable",
                table: "OfferAssessment");

            migrationBuilder.DropColumn(
                name: "OptionalAvailable",
                table: "Assessment");

            migrationBuilder.AddCheckConstraint(
                name: "SingleOfferAssessmentValue",
                table: "OfferAssessment",
                sql: "(CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"MeasurementValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"RatingValue\" IS NULL THEN 0 ELSE 1 END) <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "SingleAssessmentValue",
                table: "Assessment",
                sql: "(CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"MeasurementValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"RatingValue\" IS NULL THEN 0 ELSE 1 END) <= 1");
        }
    }
}
