using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Candidates;

public sealed record SpecificationField(string Name, string Group, string Unit, string Step, bool AllowZero,
    Func<VehicleSpecifications, decimal?> Read, Func<VehicleSpecifications, decimal?>? Calculate = null)
{
    public decimal? Value(VehicleSpecifications specifications) => Calculate is null ? Read(specifications) : Calculate(specifications);
    public bool IsCalculated(VehicleSpecifications specifications) => Read(specifications) is null && Value(specifications).HasValue;
    public string Label => "Spec" + Name;
}

public static class SpecificationFields
{
    public static readonly SpecificationField[] All =
    [
        new(nameof(VehicleSpecifications.LengthMm), "Dimensions", "mm", "1", false, s => s.LengthMm),
        new(nameof(VehicleSpecifications.WidthMm), "Dimensions", "mm", "1", false, s => s.WidthMm),
        new(nameof(VehicleSpecifications.WidthWithMirrorsMm), "Dimensions", "mm", "1", false, s => s.WidthWithMirrorsMm),
        new(nameof(VehicleSpecifications.HeightMm), "Dimensions", "mm", "1", false, s => s.HeightMm),
        new(nameof(VehicleSpecifications.WheelbaseMm), "Dimensions", "mm", "1", false, s => s.WheelbaseMm),
        new(nameof(VehicleSpecifications.Seats), "Space", "", "1", false, s => s.Seats),
        new(nameof(VehicleSpecifications.BootVolumeLitres), "Space", "l", "any", true, s => s.BootVolumeLitres),
        new(nameof(VehicleSpecifications.BootVolumeFoldedLitres), "Space", "l", "any", true, s => s.BootVolumeFoldedLitres),
        new(nameof(VehicleSpecifications.FrunkVolumeLitres), "Space", "l", "any", true, s => s.FrunkVolumeLitres),
        new(nameof(VehicleSpecifications.KerbWeightKg), "Weight", "kg", "any", false, s => s.KerbWeightKg, s => s.GetKerbWeightKg()),
        new(nameof(VehicleSpecifications.GrossWeightKg), "Weight", "kg", "any", false, s => s.GrossWeightKg, s => s.GetGrossWeightKg()),
        new(nameof(VehicleSpecifications.PayloadKg), "Weight", "kg", "any", true, s => s.PayloadKg, s => s.GetPayloadKg()),
        new(nameof(VehicleSpecifications.PowerKw), "Performance", "kW", "any", false, s => s.PowerKw, s => s.GetPowerKw()),
        new(nameof(VehicleSpecifications.PowerPs), "Performance", "PS", "any", false, s => s.PowerPs, s => s.GetPowerPs()),
        new(nameof(VehicleSpecifications.AccelerationSeconds), "Performance", "s", "any", false, s => s.AccelerationSeconds),
        new(nameof(VehicleSpecifications.TopSpeedKmh), "Performance", "km/h", "any", false, s => s.TopSpeedKmh),
        new(nameof(VehicleSpecifications.SummerConsumptionKwhPer100Km), "Electric", "kWh/100 km", "any", false, s => s.SummerConsumptionKwhPer100Km),
        new(nameof(VehicleSpecifications.WinterConsumptionKwhPer100Km), "Electric", "kWh/100 km", "any", false, s => s.WinterConsumptionKwhPer100Km),
        new(nameof(VehicleSpecifications.BatteryUsableKwh), "Electric", "kWh", "any", false, s => s.BatteryUsableKwh),
        new(nameof(VehicleSpecifications.BatteryGrossKwh), "Electric", "kWh", "any", false, s => s.BatteryGrossKwh),
        new(nameof(VehicleSpecifications.AcChargingPowerKw), "Electric", "kW", "any", true, s => s.AcChargingPowerKw),
        new(nameof(VehicleSpecifications.DcChargingPowerKw), "Electric", "kW", "any", true, s => s.DcChargingPowerKw),
        new(nameof(VehicleSpecifications.Charging20To80Minutes), "Electric", "min", "any", false, s => s.Charging20To80Minutes),
        new(nameof(VehicleSpecifications.TankCapacityLitres), "Combustion", "l", "any", false, s => s.TankCapacityLitres),
        new(nameof(VehicleSpecifications.RangeKm), "Combustion", "km", "any", false, s => s.RangeKm),
        new(nameof(VehicleSpecifications.FuelConsumptionLitresPer100Km), "Combustion", "l/100 km", "any", false, s => s.FuelConsumptionLitresPer100Km),
    ];
}
