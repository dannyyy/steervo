using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Steervo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OfferPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ValidOfferLineKind",
                table: "OfferLine");

            migrationBuilder.DropColumn(
                name: "Archived",
                table: "Offer");

            migrationBuilder.RenameColumn(
                name: "Terms_BasePrice",
                table: "OfferRevision",
                newName: "Terms_StatedTotal");

            migrationBuilder.AddColumn<Guid>(
                name: "CriterionId",
                table: "OfferLine",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Financed",
                table: "OfferLine",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Value",
                table: "OfferLine",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "Offer",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ValidOfferLineKind",
                table: "OfferLine",
                sql: "\"Kind\" IN ('Accessory','Fee','Benefit','Equipment','Vehicle','Discount')");

            migrationBuilder.AddCheckConstraint(
                name: "ValidOfferLineValue",
                table: "OfferLine",
                sql: "\"Value\" IS NULL OR (\"PriceKind\" = 'Included' AND \"Value\" >= 0)");

            // The former base price becomes the first line of every version; the control total starts empty.
            // Stored versions are protected against updates; this one-time conversion is the only exception.
            migrationBuilder.Sql("""
                ALTER TABLE "OfferRevision" DISABLE TRIGGER "ImmutableOfferRevision";
                ALTER TABLE "OfferLine" DISABLE TRIGGER "ImmutableOfferLine";
                UPDATE "OfferLine" l SET "Position" = l."Position" + 100000
                FROM "OfferRevision" r WHERE r."Id" = l."RevisionId" AND r."Terms_StatedTotal" IS NOT NULL;
                UPDATE "OfferLine" l SET "Position" = l."Position" - 99999
                FROM "OfferRevision" r WHERE r."Id" = l."RevisionId" AND r."Terms_StatedTotal" IS NOT NULL;
                INSERT INTO "OfferLine" ("Id", "RevisionId", "Position", "Name", "Kind", "PriceKind", "Amount", "Notes", "Financed")
                SELECT gen_random_uuid(), r."Id", 0, LEFT(COALESCE(NULLIF(BTRIM(r."Terms_VehicleDescription"), ''), 'Fahrzeug'), 300), 'Vehicle', 'Amount', r."Terms_StatedTotal", '', TRUE
                FROM "OfferRevision" r WHERE r."Terms_StatedTotal" IS NOT NULL;
                UPDATE "OfferRevision" SET "Terms_StatedTotal" = NULL;
                UPDATE "Offer" o SET "Position" = ranked.position FROM (
                    SELECT o2."Id", ROW_NUMBER() OVER (PARTITION BY o2."EvaluationId" ORDER BY MAX(r."CreatedAt") DESC, o2."Id") - 1 AS position
                    FROM "Offer" o2 JOIN "OfferRevision" r ON r."OfferId" = o2."Id" GROUP BY o2."Id", o2."EvaluationId") ranked
                WHERE ranked."Id" = o."Id";
                ALTER TABLE "OfferRevision" ENABLE TRIGGER "ImmutableOfferRevision";
                ALTER TABLE "OfferLine" ENABLE TRIGGER "ImmutableOfferLine";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ValidOfferLineKind",
                table: "OfferLine");

            migrationBuilder.DropCheckConstraint(
                name: "ValidOfferLineValue",
                table: "OfferLine");

            migrationBuilder.DropColumn(
                name: "CriterionId",
                table: "OfferLine");

            migrationBuilder.DropColumn(
                name: "Financed",
                table: "OfferLine");

            migrationBuilder.DropColumn(
                name: "Value",
                table: "OfferLine");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Offer");

            migrationBuilder.Sql("""
                ALTER TABLE "OfferRevision" DISABLE TRIGGER "ImmutableOfferRevision";
                UPDATE "OfferRevision" r SET "Terms_StatedTotal" = COALESCE((SELECT SUM(l."Amount") FROM "OfferLine" l WHERE l."RevisionId" = r."Id" AND l."Kind" = 'Vehicle'), r."Terms_StatedTotal");
                DELETE FROM "OfferLine" WHERE "Kind" IN ('Vehicle', 'Discount');
                ALTER TABLE "OfferRevision" ENABLE TRIGGER "ImmutableOfferRevision";
                """);

            migrationBuilder.RenameColumn(
                name: "Terms_StatedTotal",
                table: "OfferRevision",
                newName: "Terms_BasePrice");

            migrationBuilder.AddColumn<bool>(
                name: "Archived",
                table: "Offer",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ValidOfferLineKind",
                table: "OfferLine",
                sql: "\"Kind\" IN ('Accessory','Fee','Benefit','Equipment')");
        }
    }
}
