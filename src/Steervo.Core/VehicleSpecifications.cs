namespace Steervo.Core;

public enum PropulsionType
{
    Electric,
    Combustion,
    Hybrid,
}

public enum DrivenWheels
{
    All,
    Rear,
    Front,
}

/// <summary>
/// Technical data of a car. Every value is optional; weights and power that follow from the
/// other entered values are derived on request.
/// </summary>
public sealed record VehicleSpecifications
{
    public const decimal KilowattsPerPs = 0.73549875m;
    private const decimal MaximumValue = 1_000_000;
    private const decimal PowerToleranceKw = 0.5m;

    public PropulsionType? Propulsion { get; init; }
    public DrivenWheels? Drive { get; init; }
    public decimal? LengthMm { get; init; }
    public decimal? WidthMm { get; init; }
    public decimal? WidthWithMirrorsMm { get; init; }
    public decimal? HeightMm { get; init; }
    public decimal? WheelbaseMm { get; init; }
    public int? Seats { get; init; }
    public decimal? BootVolumeLitres { get; init; }
    public decimal? BootVolumeFoldedLitres { get; init; }
    public decimal? FrunkVolumeLitres { get; init; }
    public decimal? KerbWeightKg { get; init; }
    public decimal? GrossWeightKg { get; init; }
    public decimal? PayloadKg { get; init; }
    public decimal? PowerKw { get; init; }
    public decimal? PowerPs { get; init; }
    public decimal? AccelerationSeconds { get; init; }
    public decimal? TopSpeedKmh { get; init; }
    public decimal? SummerConsumptionKwhPer100Km { get; init; }
    public decimal? WinterConsumptionKwhPer100Km { get; init; }
    public decimal? BatteryUsableKwh { get; init; }
    public decimal? BatteryGrossKwh { get; init; }
    public decimal? AcChargingPowerKw { get; init; }
    public decimal? DcChargingPowerKw { get; init; }
    public decimal? Charging20To80Minutes { get; init; }
    public decimal? TankCapacityLitres { get; init; }
    public decimal? RangeKm { get; init; }
    public decimal? FuelConsumptionLitresPer100Km { get; init; }

    // Kerb weight, payload and gross weight depend on each other: any two give the third.
    public decimal? GetKerbWeightKg() => KerbWeightKg ?? (GrossWeightKg - PayloadKg);

    public decimal? GetGrossWeightKg() => GrossWeightKg ?? (KerbWeightKg + PayloadKg);

    public decimal? GetPayloadKg() => PayloadKg ?? (GrossWeightKg - KerbWeightKg);

    public decimal? GetPowerKw() => PowerKw ?? RoundPower(PowerPs * KilowattsPerPs);

    public decimal? GetPowerPs() => PowerPs ?? RoundPower(PowerKw / KilowattsPerPs);

    public bool HasValues() => this != new VehicleSpecifications();

    private static decimal? RoundPower(decimal? value) =>
        value.HasValue ? decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero) : null;

    internal void Validate()
    {
        if (Propulsion.HasValue)
        {
            Rules.Defined(Propulsion.Value);
        }

        if (Drive.HasValue)
        {
            Rules.Defined(Drive.Value);
        }

        ValidateRanges();
        ValidateWeights();
        ValidatePower();

        if (BatteryUsableKwh > BatteryGrossKwh)
        {
            throw new RuleException("InconsistentBatteryCapacity");
        }

        if (BootVolumeFoldedLitres < BootVolumeLitres)
        {
            throw new RuleException("InconsistentBootVolume");
        }

        if (WidthWithMirrorsMm < WidthMm)
        {
            throw new RuleException("InconsistentWidth");
        }
    }

    private void ValidateRanges()
    {
        var mustBePositive = new decimal?[]
        {
            LengthMm,
            WidthMm,
            WidthWithMirrorsMm,
            HeightMm,
            WheelbaseMm,
            Seats,
            KerbWeightKg,
            GrossWeightKg,
            PowerKw,
            PowerPs,
            AccelerationSeconds,
            TopSpeedKmh,
            SummerConsumptionKwhPer100Km,
            WinterConsumptionKwhPer100Km,
            BatteryUsableKwh,
            BatteryGrossKwh,
            Charging20To80Minutes,
            TankCapacityLitres,
            RangeKm,
            FuelConsumptionLitresPer100Km,
        };

        foreach (var value in mustBePositive)
        {
            if (value is <= 0 or > MaximumValue)
            {
                throw new RuleException("InvalidSpecifications");
            }
        }

        // These may legitimately be zero, for example for a car without a frunk or DC charging.
        var mayBeZero = new decimal?[]
        {
            BootVolumeLitres,
            BootVolumeFoldedLitres,
            FrunkVolumeLitres,
            PayloadKg,
            AcChargingPowerKw,
            DcChargingPowerKw,
        };

        foreach (var value in mayBeZero)
        {
            if (value is < 0 or > MaximumValue)
            {
                throw new RuleException("InvalidSpecifications");
            }
        }
    }

    private void ValidateWeights()
    {
        var allThreeEntered = KerbWeightKg.HasValue && GrossWeightKg.HasValue && PayloadKg.HasValue;
        var entriesContradict = allThreeEntered && KerbWeightKg + PayloadKg != GrossWeightKg;

        // The derived values are checked too: two entered weights must not imply an impossible third.
        var derivedIsImpossible =
            GetKerbWeightKg() is <= 0 || GetGrossWeightKg() is <= 0 || GetPayloadKg() is < 0;

        if (derivedIsImpossible || entriesContradict)
        {
            throw new RuleException("InconsistentWeights");
        }
    }

    private void ValidatePower()
    {
        if (!PowerKw.HasValue || !PowerPs.HasValue)
        {
            return;
        }

        // Whole-number manufacturer specifications may round both kW and metric PS.
        var deviationKw = Math.Abs(PowerKw.Value - PowerPs.Value * KilowattsPerPs);

        if (deviationKw > PowerToleranceKw)
        {
            throw new RuleException("InconsistentPower");
        }
    }
}
