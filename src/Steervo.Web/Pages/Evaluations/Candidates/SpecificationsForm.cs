using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Candidates;

public sealed class SpecificationsForm
{
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
    public VehicleSpecifications ToInput() => new()
    {
        Propulsion = Propulsion, Drive = Drive,
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
    public static SpecificationsForm From(VehicleSpecifications specifications) => new()
    {
        Propulsion = specifications.Propulsion, Drive = specifications.Drive,
        LengthMm = specifications.LengthMm?.ToString(CultureInfo.InvariantCulture),
        WidthMm = specifications.WidthMm?.ToString(CultureInfo.InvariantCulture),
        WidthWithMirrorsMm = specifications.WidthWithMirrorsMm?.ToString(CultureInfo.InvariantCulture),
        HeightMm = specifications.HeightMm?.ToString(CultureInfo.InvariantCulture),
        WheelbaseMm = specifications.WheelbaseMm?.ToString(CultureInfo.InvariantCulture),
        Seats = specifications.Seats?.ToString(CultureInfo.InvariantCulture),
        BootVolumeLitres = specifications.BootVolumeLitres?.ToString(CultureInfo.InvariantCulture),
        BootVolumeFoldedLitres = specifications.BootVolumeFoldedLitres?.ToString(CultureInfo.InvariantCulture),
        FrunkVolumeLitres = specifications.FrunkVolumeLitres?.ToString(CultureInfo.InvariantCulture),
        KerbWeightKg = specifications.KerbWeightKg?.ToString(CultureInfo.InvariantCulture),
        GrossWeightKg = specifications.GrossWeightKg?.ToString(CultureInfo.InvariantCulture),
        PayloadKg = specifications.PayloadKg?.ToString(CultureInfo.InvariantCulture),
        PowerKw = specifications.PowerKw?.ToString(CultureInfo.InvariantCulture),
        PowerPs = specifications.PowerPs?.ToString(CultureInfo.InvariantCulture),
        AccelerationSeconds = specifications.AccelerationSeconds?.ToString(CultureInfo.InvariantCulture),
        TopSpeedKmh = specifications.TopSpeedKmh?.ToString(CultureInfo.InvariantCulture),
        SummerConsumptionKwhPer100Km = specifications.SummerConsumptionKwhPer100Km?.ToString(CultureInfo.InvariantCulture),
        WinterConsumptionKwhPer100Km = specifications.WinterConsumptionKwhPer100Km?.ToString(CultureInfo.InvariantCulture),
        BatteryUsableKwh = specifications.BatteryUsableKwh?.ToString(CultureInfo.InvariantCulture),
        BatteryGrossKwh = specifications.BatteryGrossKwh?.ToString(CultureInfo.InvariantCulture),
        AcChargingPowerKw = specifications.AcChargingPowerKw?.ToString(CultureInfo.InvariantCulture),
        DcChargingPowerKw = specifications.DcChargingPowerKw?.ToString(CultureInfo.InvariantCulture),
        Charging20To80Minutes = specifications.Charging20To80Minutes?.ToString(CultureInfo.InvariantCulture),
        TankCapacityLitres = specifications.TankCapacityLitres?.ToString(CultureInfo.InvariantCulture),
        RangeKm = specifications.RangeKm?.ToString(CultureInfo.InvariantCulture),
        FuelConsumptionLitresPer100Km = specifications.FuelConsumptionLitresPer100Km?.ToString(CultureInfo.InvariantCulture),
    };
    public string? Value(string name) => name switch
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
        nameof(VehicleSpecifications.SummerConsumptionKwhPer100Km) => SummerConsumptionKwhPer100Km,
        nameof(VehicleSpecifications.WinterConsumptionKwhPer100Km) => WinterConsumptionKwhPer100Km,
        nameof(VehicleSpecifications.BatteryUsableKwh) => BatteryUsableKwh,
        nameof(VehicleSpecifications.BatteryGrossKwh) => BatteryGrossKwh,
        nameof(VehicleSpecifications.AcChargingPowerKw) => AcChargingPowerKw,
        nameof(VehicleSpecifications.DcChargingPowerKw) => DcChargingPowerKw,
        nameof(VehicleSpecifications.Charging20To80Minutes) => Charging20To80Minutes,
        nameof(VehicleSpecifications.TankCapacityLitres) => TankCapacityLitres,
        nameof(VehicleSpecifications.RangeKm) => RangeKm,
        nameof(VehicleSpecifications.FuelConsumptionLitresPer100Km) => FuelConsumptionLitresPer100Km,
        _ => null
    };
    private static decimal? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            throw new RuleException("InvalidSpecifications");
        return number;
    }
    private static int? ParseSeats(string? value)
    {
        var number = Parse(value);
        if (!number.HasValue) return null;
        if (number is < 1 or > 1_000_000 || decimal.Truncate(number.Value) != number.Value) throw new RuleException("InvalidSpecifications");
        return (int)number.Value;
    }
}
