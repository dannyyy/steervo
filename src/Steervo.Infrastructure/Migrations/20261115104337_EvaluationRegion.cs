using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Steervo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EvaluationRegion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Evaluations",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ValidEvaluationCountry",
                table: "Evaluations",
                sql: "\"Country\" IS NULL OR \"Country\" ~ '^[A-Z]{2}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ValidEvaluationCountry",
                table: "Evaluations");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Evaluations");
        }
    }
}
