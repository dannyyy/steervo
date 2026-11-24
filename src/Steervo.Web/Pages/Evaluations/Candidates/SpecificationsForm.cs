using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Candidates;

/// <summary>
/// The posted technical data of a car. Numbers arrive as text so that an empty input, zero and an
/// unreadable value stay distinguishable; the property names are the form field names.
/// </summary>
public sealed class SpecificationsForm
{
    private const string InvalidSpecifications = "InvalidSpecifications";
    private const int MaximumSeats = 1_000_000;

    public PropulsionType? Propulsion { get; set; }
    public DrivenWheels? Drive { get; set; }
    public string? LengthMm { get; set; }
    public string? WidthMm { get; set; }
    public string? WidthWithMirrorsMm { get; set; }
    public string? HeightMm { get; set; }
    public string? WheelbaseMm { get; set; }
    public string? Seats { get; set; }
    public string? BootVolumeLitres { get; set; }
    public string? BootVolumeFoldedLitres { get; set; }
    public string? FrunkVolumeLitres { get; set; }
    public string? KerbWeightKg { get; set; }
    public string? GrossWeightKg { get; set; }
    public string? PayloadKg { get; set; }
    public string? PowerKw { get; set; }
    public string? PowerPs { get; set; }
    public string? AccelerationSeconds { get; set; }
    public string? TopSpeedKmh { get; set; }
    public string? SummerConsumptionKwhPer100Km { get; set; }
    public string? WinterConsumptionKwhPer100Km { get; set; }
    public string? BatteryUsableKwh { get; set; }
    public string? BatteryGrossKwh { get; set; }
    public string? AcChargingPowerKw { get; set; }
    public string? DcChargingPowerKw { get; set; }
    public string? Charging20To80Minutes { get; set; }
    public string? TankCapacityLitres { get; set; }
    public string? RangeKm { get; set; }
    public string? FuelConsumptionLitresPer100Km { get; set; }

    /// <summary>Reads the posted text; a value that is not a plain number is rejected.</summary>
    public VehicleSpecifications ToInput() =>
        new()
        {
            Propulsion = Propulsion,
            Drive = Drive,
            LengthMm = Parse(LengthMm),
            WidthMm = Parse(WidthMm),
            WidthWithMirrorsMm = Parse(WidthWithMirrorsMm),
            HeightMm = Parse(HeightMm),
            WheelbaseMm = Parse(WheelbaseMm),
            Seats = ParseSeats(Seats),
            BootVolumeLitres = Parse(BootVolumeLitres),
            BootVolumeFoldedLitres = Parse(BootVolumeFoldedLitres),
            FrunkVolumeLitres = Parse(FrunkVolumeLitres),
            KerbWeightKg = Parse(KerbWeightKg),
            GrossWeightKg = Parse(GrossWeightKg),
            PayloadKg = Parse(PayloadKg),
            PowerKw = Parse(PowerKw),
            PowerPs = Parse(PowerPs),
            AccelerationSeconds = Parse(AccelerationSeconds),
            TopSpeedKmh = Parse(TopSpeedKmh),
            SummerConsumptionKwhPer100Km = Parse(SummerConsumptionKwhPer100Km),
            WinterConsumptionKwhPer100Km = Parse(WinterConsumptionKwhPer100Km),
            BatteryUsableKwh = Parse(BatteryUsableKwh),
            BatteryGrossKwh = Parse(BatteryGrossKwh),
            AcChargingPowerKw = Parse(AcChargingPowerKw),
            DcChargingPowerKw = Parse(DcChargingPowerKw),
            Charging20To80Minutes = Parse(Charging20To80Minutes),
            TankCapacityLitres = Parse(TankCapacityLitres),
            RangeKm = Parse(RangeKm),
            FuelConsumptionLitresPer100Km = Parse(FuelConsumptionLitresPer100Km),
        };

    public static SpecificationsForm From(VehicleSpecifications specifications) =>
        new()
        {
            Propulsion = specifications.Propulsion,
            Drive = specifications.Drive,
            LengthMm = Text(specifications.LengthMm),
            WidthMm = Text(specifications.WidthMm),
            WidthWithMirrorsMm = Text(specifications.WidthWithMirrorsMm),
            HeightMm = Text(specifications.HeightMm),
            WheelbaseMm = Text(specifications.WheelbaseMm),
            Seats = Text(specifications.Seats),
            BootVolumeLitres = Text(specifications.BootVolumeLitres),
            BootVolumeFoldedLitres = Text(specifications.BootVolumeFoldedLitres),
            FrunkVolumeLitres = Text(specifications.FrunkVolumeLitres),
            KerbWeightKg = Text(specifications.KerbWeightKg),
            GrossWeightKg = Text(specifications.GrossWeightKg),
            PayloadKg = Text(specifications.PayloadKg),
            PowerKw = Text(specifications.PowerKw),
            PowerPs = Text(specifications.PowerPs),
            AccelerationSeconds = Text(specifications.AccelerationSeconds),
            TopSpeedKmh = Text(specifications.TopSpeedKmh),
            SummerConsumptionKwhPer100Km = Text(specifications.SummerConsumptionKwhPer100Km),
            WinterConsumptionKwhPer100Km = Text(specifications.WinterConsumptionKwhPer100Km),
            BatteryUsableKwh = Text(specifications.BatteryUsableKwh),
            BatteryGrossKwh = Text(specifications.BatteryGrossKwh),
            AcChargingPowerKw = Text(specifications.AcChargingPowerKw),
            DcChargingPowerKw = Text(specifications.DcChargingPowerKw),
            Charging20To80Minutes = Text(specifications.Charging20To80Minutes),
            TankCapacityLitres = Text(specifications.TankCapacityLitres),
            RangeKm = Text(specifications.RangeKm),
            FuelConsumptionLitresPer100Km = Text(specifications.FuelConsumptionLitresPer100Km),
        };

    /// <summary>The posted text of the field with the given <see cref="SpecificationField.Name"/>.</summary>
    public string? Value(string name) =>
        name switch
        {
            nameof(VehicleSpecifications.LengthMm) => LengthMm,
            nameof(VehicleSpecifications.WidthMm) => WidthMm,
            nameof(VehicleSpecifications.WidthWithMirrorsMm) => WidthWithMirrorsMm,
            nameof(VehicleSpecifications.HeightMm) => HeightMm,
            nameof(VehicleSpecifications.WheelbaseMm) => WheelbaseMm,
            nameof(VehicleSpecifications.Seats) => Seats,
            nameof(VehicleSpecifications.BootVolumeLitres) => BootVolumeLitres,
            nameof(VehicleSpecifications.BootVolumeFoldedLitres) => BootVolumeFoldedLitres,
            nameof(VehicleSpecifications.FrunkVolumeLitres) => FrunkVolumeLitres,
            nameof(VehicleSpecifications.KerbWeightKg) => KerbWeightKg,
            nameof(VehicleSpecifications.GrossWeightKg) => GrossWeightKg,
            nameof(VehicleSpecifications.PayloadKg) => PayloadKg,
            nameof(VehicleSpecifications.PowerKw) => PowerKw,
            nameof(VehicleSpecifications.PowerPs) => PowerPs,
            nameof(VehicleSpecifications.AccelerationSeconds) => AccelerationSeconds,
            nameof(VehicleSpecifications.TopSpeedKmh) => TopSpeedKmh,
            nameof(VehicleSpecifications.SummerConsumptionKwhPer100Km) =>
                SummerConsumptionKwhPer100Km,
            nameof(VehicleSpecifications.WinterConsumptionKwhPer100Km) =>
                WinterConsumptionKwhPer100Km,
            nameof(VehicleSpecifications.BatteryUsableKwh) => BatteryUsableKwh,
            nameof(VehicleSpecifications.BatteryGrossKwh) => BatteryGrossKwh,
            nameof(VehicleSpecifications.AcChargingPowerKw) => AcChargingPowerKw,
            nameof(VehicleSpecifications.DcChargingPowerKw) => DcChargingPowerKw,
            nameof(VehicleSpecifications.Charging20To80Minutes) => Charging20To80Minutes,
            nameof(VehicleSpecifications.TankCapacityLitres) => TankCapacityLitres,
            nameof(VehicleSpecifications.RangeKm) => RangeKm,
            nameof(VehicleSpecifications.FuelConsumptionLitresPer100Km) =>
                FuelConsumptionLitresPer100Km,
            _ => null,
        };

    // Posted numbers always use a decimal point, whatever the language of the page.
    private static string? Text(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Text(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    // An empty input means "not specified", which is not the same as zero.
    private static decimal? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var isNumber = decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var number
        );

        if (!isNumber)
        {
            throw new RuleException(InvalidSpecifications);
        }

        return number;
    }

    private static int? ParseSeats(string? value)
    {
        var number = Parse(value);

        if (!number.HasValue)
        {
            return null;
        }

        var isWholeNumber = decimal.Truncate(number.Value) == number.Value;

        if (number is < 1 or > MaximumSeats || !isWholeNumber)
        {
            throw new RuleException(InvalidSpecifications);
        }

        return (int)number.Value;
    }
}
