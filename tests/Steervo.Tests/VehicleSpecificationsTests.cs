using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

public sealed class VehicleSpecificationsTests
{
    private static Candidate Create(VehicleSpecifications? specifications = null)
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        return evaluation.FindCandidate(evaluation.AddCandidate(new("Vehicle", Specifications: specifications)));
    }

    [Fact]
    public void Empty_specifications_and_explicit_zero_are_distinct_and_do_not_create_assessments()
    {
        var empty = Create().Specifications;
        Assert.False(empty.HasValues());
        Assert.Null(empty.GetPayloadKg());
        Assert.Null(empty.GetPowerPs());
        var known = Create(new() { FrunkVolumeLitres = 0, DcChargingPowerKw = 0 }).Specifications;
        Assert.True(known.HasValues());
        Assert.Equal(0, known.FrunkVolumeLitres);
        Assert.Null(known.BatteryUsableKwh);
    }

    [Theory]
    [InlineData(null, 2500, 500)]
    [InlineData(2000, null, 500)]
    [InlineData(2000, 2500, null)]
    [InlineData(2000, 2500, 500)]
    public void Any_two_weights_determine_the_third_without_overwriting_the_inputs(int? kerb, int? gross, int? payload)
    {
        var specifications = Create(new() { KerbWeightKg = kerb, GrossWeightKg = gross, PayloadKg = payload }).Specifications;
        Assert.Equal(2000, specifications.GetKerbWeightKg());
        Assert.Equal(2500, specifications.GetGrossWeightKg());
        Assert.Equal(500, specifications.GetPayloadKg());
        Assert.Equal(kerb, specifications.KerbWeightKg);
        Assert.Equal(gross, specifications.GrossWeightKg);
        Assert.Equal(payload, specifications.PayloadKg);
    }

    [Fact]
    public void Metric_ps_conversion_is_bidirectional_and_keeps_its_source()
    {
        var ps = Create(new() { PowerPs = 100 }).Specifications;
        Assert.Equal(73.55m, ps.GetPowerKw());
        Assert.Null(ps.PowerKw);
        var kw = Create(new() { PowerKw = 100 }).Specifications;
        Assert.Equal(135.96m, kw.GetPowerPs());
        Assert.Null(kw.PowerPs);
        Assert.Equal(350, Create(new() { PowerKw = 350, PowerPs = 476 }).Specifications.GetPowerKw());
    }

    [Fact]
    public void Contradictory_or_invalid_values_are_rejected_without_changing_the_candidate()
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var id = evaluation.AddCandidate(new("Original", Specifications: new() { Seats = 5 }));
        VehicleSpecifications[] invalid =
        [
            new() { GrossWeightKg = 1500, KerbWeightKg = 2000 },
            new() { GrossWeightKg = 2000, PayloadKg = 2200 },
            new() { KerbWeightKg = 2000, GrossWeightKg = 2500, PayloadKg = 600 },
            new() { KerbWeightKg = 0 }, new() { PayloadKg = -1 },
            new() { PowerKw = 100, PowerPs = 100 }, new() { PowerPs = decimal.MaxValue },
            new() { Seats = 0 }, new() { AccelerationSeconds = -4.5m },
            new() { BatteryUsableKwh = 90, BatteryGrossKwh = 80 },
            new() { BootVolumeLitres = 500, BootVolumeFoldedLitres = 400 },
            new() { WidthMm = 2000, WidthWithMirrorsMm = 1800 },
            new() { Propulsion = (PropulsionType)42 }, new() { Drive = (DrivenWheels)42 }
        ];
        foreach (var specifications in invalid)
            Assert.Throws<RuleException>(() => evaluation.EditCandidate(id, new("Changed", Specifications: specifications)));
        Assert.Equal("Original", evaluation.FindCandidate(id).Model);
        Assert.Equal(5, evaluation.FindCandidate(id).Specifications.Seats);
    }

    [Fact]
    public void Copying_and_editing_specifications_are_independent_of_ratings_and_derived_values()
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var id = evaluation.AddCandidate(new("Original", Specifications: new() { KerbWeightKg = 2000, GrossWeightKg = 2500, PowerKw = 100 }));
        var copy = evaluation.CopyCandidate(id, "Copy");
        var copied = evaluation.FindCandidate(copy).Specifications;
        Assert.Equal(evaluation.FindCandidate(id).Specifications, copied);
        evaluation.EditCandidate(copy, new("Copy", Specifications: copied with { KerbWeightKg = 2100, PowerKw = 200 }));
        Assert.Equal(400, evaluation.FindCandidate(copy).Specifications.GetPayloadKg());
        Assert.Equal(500, evaluation.FindCandidate(id).Specifications.GetPayloadKg());
        Assert.Equal(271.92m, evaluation.FindCandidate(copy).Specifications.GetPowerPs());
        Assert.Equal(135.96m, evaluation.FindCandidate(id).Specifications.GetPowerPs());
        Assert.Empty(evaluation.Assessments);
    }
}
