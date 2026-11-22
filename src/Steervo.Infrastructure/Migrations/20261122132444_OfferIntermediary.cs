using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Steervo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfferIntermediary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Terms_Intermediary",
                table: "OfferRevision",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Terms_Intermediary",
                table: "OfferRevision");
        }
    }
}
