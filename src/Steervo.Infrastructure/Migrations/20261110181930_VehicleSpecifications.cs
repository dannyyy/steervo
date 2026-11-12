using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Steervo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VehicleSpecifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_AcChargingPowerKw",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_AccelerationSeconds",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_BatteryGrossKwh",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_BatteryUsableKwh",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_BootVolumeFoldedLitres",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_BootVolumeLitres",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_Charging20To80Minutes",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_DcChargingPowerKw",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specifications_Drive",
                table: "Candidate",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_FrunkVolumeLitres",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_FuelConsumptionLitresPer100Km",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_GrossWeightKg",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_HeightMm",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_KerbWeightKg",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_LengthMm",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_PayloadKg",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_PowerKw",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_PowerPs",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specifications_Propulsion",
                table: "Candidate",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_RangeKm",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Specifications_Seats",
                table: "Candidate",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_SummerConsumptionKwhPer100Km",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_TankCapacityLitres",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_TopSpeedKmh",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_WheelbaseMm",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_WidthMm",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_WidthWithMirrorsMm",
                table: "Candidate",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Specifications_WinterConsumptionKwhPer100Km",
                table: "Candidate",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Specifications_AcChargingPowerKw",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_AccelerationSeconds",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_BatteryGrossKwh",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_BatteryUsableKwh",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_BootVolumeFoldedLitres",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_BootVolumeLitres",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_Charging20To80Minutes",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_DcChargingPowerKw",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_Drive",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_FrunkVolumeLitres",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_FuelConsumptionLitresPer100Km",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_GrossWeightKg",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_HeightMm",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_KerbWeightKg",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_LengthMm",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_PayloadKg",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_PowerKw",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_PowerPs",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_Propulsion",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_RangeKm",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_Seats",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_SummerConsumptionKwhPer100Km",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_TankCapacityLitres",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_TopSpeedKmh",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_WheelbaseMm",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_WidthMm",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_WidthWithMirrorsMm",
                table: "Candidate");

            migrationBuilder.DropColumn(
                name: "Specifications_WinterConsumptionKwhPer100Km",
                table: "Candidate");
        }
    }
}
