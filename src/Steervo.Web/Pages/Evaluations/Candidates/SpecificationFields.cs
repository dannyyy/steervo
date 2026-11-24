using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Candidates;

/// <summary>
/// One numeric value of the technical data: how it is labelled, entered and read. Values with a
/// <paramref name="Calculate"/> function can be derived from related values when not entered.
/// </summary>
public sealed record SpecificationField(
    string Name,
    string Group,
    string Unit,
    string Step,
    bool AllowZero,
    Func<VehicleSpecifications, decimal?> Read,
    Func<VehicleSpecifications, decimal?>? Calculate = null
)
{
    private const string WholeNumberStep = "1";

    /// <summary>The entered value or, for a derivable field, the value worked out from the others.</summary>
    public decimal? Value(VehicleSpecifications specifications) =>
        Calculate is null ? Read(specifications) : Calculate(specifications);

    /// <summary>True when the shown value was not entered but derived.</summary>
    public bool IsCalculated(VehicleSpecifications specifications) =>
        Read(specifications) is null && Value(specifications).HasValue;

    public string Label => "Spec" + Name;

    public bool HasCalculation => Calculate is not null;

    /// <summary>The smallest value the input accepts; zero is a statement of its own and only allowed where it makes sense.</summary>
    public string Minimum
    {
        get
        {
            if (AllowZero)
            {
                return "0";
            }

            return Step == WholeNumberStep ? "1" : "0.001";
        }
    }
}

/// <summary>All numeric technical data in the order and grouping in which they are shown and entered.</summary>
public static class SpecificationFields
{
    public const string DimensionsGroup = "Dimensions";
    public const string SpaceGroup = "Space";
    public const string WeightGroup = "Weight";

    /// <summary>The group that also shows propulsion and driven wheels, which are choices rather than numbers.</summary>
    public const string PerformanceGroup = "Performance";
    public const string ElectricGroup = "Electric";
    public const string CombustionGroup = "Combustion";

    private const string WholeNumberStep = "1";
    private const string AnyStep = "any";

    public static readonly SpecificationField[] All =
    [
        new(
            nameof(VehicleSpecifications.LengthMm),
            DimensionsGroup,
            Unit: "mm",
            Step: WholeNumberStep,
            AllowZero: false,
            Read: specifications => specifications.LengthMm
        ),
        new(
            nameof(VehicleSpecifications.WidthMm),
            DimensionsGroup,
            Unit: "mm",
            Step: WholeNumberStep,
            AllowZero: false,
            Read: specifications => specifications.WidthMm
        ),
        new(
            nameof(VehicleSpecifications.WidthWithMirrorsMm),
            DimensionsGroup,
            Unit: "mm",
            Step: WholeNumberStep,
            AllowZero: false,
            Read: specifications => specifications.WidthWithMirrorsMm
        ),
        new(
            nameof(VehicleSpecifications.HeightMm),
            DimensionsGroup,
            Unit: "mm",
            Step: WholeNumberStep,
            AllowZero: false,
            Read: specifications => specifications.HeightMm
        ),
        new(
            nameof(VehicleSpecifications.WheelbaseMm),
            DimensionsGroup,
            Unit: "mm",
            Step: WholeNumberStep,
            AllowZero: false,
            Read: specifications => specifications.WheelbaseMm
        ),
        new(
            nameof(VehicleSpecifications.Seats),
            SpaceGroup,
            Unit: "",
            Step: WholeNumberStep,
            AllowZero: false,
            Read: specifications => specifications.Seats
        ),
        new(
            nameof(VehicleSpecifications.BootVolumeLitres),
            SpaceGroup,
            Unit: "l",
            Step: AnyStep,
            AllowZero: true,
            Read: specifications => specifications.BootVolumeLitres
        ),
        new(
            nameof(VehicleSpecifications.BootVolumeFoldedLitres),
            SpaceGroup,
            Unit: "l",
            Step: AnyStep,
            AllowZero: true,
            Read: specifications => specifications.BootVolumeFoldedLitres
        ),
        new(
            nameof(VehicleSpecifications.FrunkVolumeLitres),
            SpaceGroup,
            Unit: "l",
            Step: AnyStep,
            AllowZero: true,
            Read: specifications => specifications.FrunkVolumeLitres
        ),
        new(
            nameof(VehicleSpecifications.KerbWeightKg),
            WeightGroup,
            Unit: "kg",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.KerbWeightKg,
            Calculate: specifications => specifications.GetKerbWeightKg()
        ),
        new(
            nameof(VehicleSpecifications.GrossWeightKg),
            WeightGroup,
            Unit: "kg",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.GrossWeightKg,
            Calculate: specifications => specifications.GetGrossWeightKg()
        ),
        new(
            nameof(VehicleSpecifications.PayloadKg),
            WeightGroup,
            Unit: "kg",
            Step: AnyStep,
            AllowZero: true,
            Read: specifications => specifications.PayloadKg,
            Calculate: specifications => specifications.GetPayloadKg()
        ),
        new(
            nameof(VehicleSpecifications.PowerKw),
            PerformanceGroup,
            Unit: "kW",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.PowerKw,
            Calculate: specifications => specifications.GetPowerKw()
        ),
        new(
            nameof(VehicleSpecifications.PowerPs),
            PerformanceGroup,
            Unit: "PS",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.PowerPs,
            Calculate: specifications => specifications.GetPowerPs()
        ),
        new(
            nameof(VehicleSpecifications.AccelerationSeconds),
            PerformanceGroup,
            Unit: "s",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.AccelerationSeconds
        ),
        new(
            nameof(VehicleSpecifications.TopSpeedKmh),
            PerformanceGroup,
            Unit: "km/h",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.TopSpeedKmh
        ),
        new(
            nameof(VehicleSpecifications.SummerConsumptionKwhPer100Km),
            ElectricGroup,
            Unit: "kWh/100 km",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.SummerConsumptionKwhPer100Km
        ),
        new(
            nameof(VehicleSpecifications.WinterConsumptionKwhPer100Km),
            ElectricGroup,
            Unit: "kWh/100 km",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.WinterConsumptionKwhPer100Km
        ),
        new(
            nameof(VehicleSpecifications.BatteryUsableKwh),
            ElectricGroup,
            Unit: "kWh",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.BatteryUsableKwh
        ),
        new(
            nameof(VehicleSpecifications.BatteryGrossKwh),
            ElectricGroup,
            Unit: "kWh",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.BatteryGrossKwh
        ),
        new(
            nameof(VehicleSpecifications.AcChargingPowerKw),
            ElectricGroup,
            Unit: "kW",
            Step: AnyStep,
            AllowZero: true,
            Read: specifications => specifications.AcChargingPowerKw
        ),
        new(
            nameof(VehicleSpecifications.DcChargingPowerKw),
            ElectricGroup,
            Unit: "kW",
            Step: AnyStep,
            AllowZero: true,
            Read: specifications => specifications.DcChargingPowerKw
        ),
        new(
            nameof(VehicleSpecifications.Charging20To80Minutes),
            ElectricGroup,
            Unit: "min",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.Charging20To80Minutes
        ),
        new(
            nameof(VehicleSpecifications.TankCapacityLitres),
            CombustionGroup,
            Unit: "l",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.TankCapacityLitres
        ),
        new(
            nameof(VehicleSpecifications.RangeKm),
            CombustionGroup,
            Unit: "km",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.RangeKm
        ),
        new(
            nameof(VehicleSpecifications.FuelConsumptionLitresPer100Km),
            CombustionGroup,
            Unit: "l/100 km",
            Step: AnyStep,
            AllowZero: false,
            Read: specifications => specifications.FuelConsumptionLitresPer100Km
        ),
    ];

    /// <summary>
    /// Propulsion and driven wheels: choices rather than numbers, shown in front of the numbers of
    /// the performance group. The name is the prefix of their resource keys.
    /// </summary>
    public static readonly (
        string Name,
        Func<VehicleSpecifications, string?> Read
    )[] DriveChoices =
    [
        ("Propulsion", specifications => specifications.Propulsion?.ToString()),
        ("Drive", specifications => specifications.Drive?.ToString()),
    ];

    public static bool HasDriveChoice(VehicleSpecifications specifications) =>
        specifications.Propulsion.HasValue || specifications.Drive.HasValue;

    public static IEnumerable<IGrouping<string, SpecificationField>> Groups =>
        All.GroupBy(specification => specification.Group);

    /// <summary>Resource key of the explanation shown above the inputs of a group; null when it has none.</summary>
    public static string? HelpKey(string group) =>
        group switch
        {
            WeightGroup => "SpecificationsWeightHelp",
            PerformanceGroup => "SpecificationsPowerHelp",
            ElectricGroup => "SpecificationsElectricHelp",
            CombustionGroup => "SpecificationsHybridHelp",
            _ => null,
        };
}
