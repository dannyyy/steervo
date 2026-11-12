using Steervo.Calculations;
using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

public sealed class CostProjectionTests
{
    private static (Evaluation Evaluation, Guid Candidate) Setup(int months = 15)
    {
        var e = new Evaluation("owner", "Decision", null); var candidate = e.AddCandidate(new("Vehicle"));
        e.AddOffer(candidate, new() { Name = "Request" }, [], "EUR"); e.SetCostPlanning(new(2027, 10, 1), months);
        return (e, candidate);
    }
    [Fact]
    public void Actual_payment_months_partial_years_and_common_costs_reconcile_exactly()
    {
        var (e, car) = Setup();
        e.AddCost(new("Annual", CostCategory.Insurance, CostFrequency.Yearly, 120, 2));
        e.AddCost(new("Monthly", CostCategory.Parking, CostFrequency.Monthly, 25));
        e.AddCost(new("Once", CostCategory.Other, CostFrequency.Once, 400, 3, CandidateId: car));
        e.AddCost(new("Temporary", CostCategory.Fuel, CostFrequency.Monthly, 10, 4, 6, car));
        e.AddCost(new("Credit", CostCategory.Other, CostFrequency.Once, 50, 5, CandidateId: car, Direction: CostDirection.Income));
        e.AddCost(new("Included", CostCategory.Service, CostFrequency.Included, null, 1, 12, car));
        e.AddCost(new("Later unknown", CostCategory.Tires, CostFrequency.Yearly, null, 16, CandidateId: car));
        var all = CostProjection.Calculate(e, car);
        Assert.Equal(1045, all.Totals.Expenses); Assert.Equal(50, all.Totals.Income); Assert.Equal(995, all.Totals.Net); Assert.True(all.Totals.Complete);
        Assert.Equal(615, CostProjection.Calculate(e).Totals.Net);
        Assert.Equal(595, CostProjection.Calculate(e, car, 1, 3).Totals.Net);
        Assert.Equal(400, CostProjection.Calculate(e, car, 4, 15).Totals.Net);
        Assert.Equal(-15, CostProjection.Calculate(e, car, 5, 5).Totals.Net);
        Assert.Equal(995, Enumerable.Range(1, 15).Sum(month => CostProjection.Calculate(e, car, month, month).Totals.Net));
        Assert.Equal(new DateOnly(2028, 12, 1), e.CostPlanning!.DateFor(15));
    }
    [Fact]
    public void Unknown_zero_and_included_are_distinct_and_shorter_horizons_do_not_destroy_data()
    {
        var (e, car) = Setup();
        var unknown = e.AddCost(new("Unknown", CostCategory.Service, CostFrequency.Monthly, null, 13, 15, car));
        e.AddCost(new("Zero", CostCategory.Other, CostFrequency.Monthly, 0));
        e.AddCost(new("Included", CostCategory.Tires, CostFrequency.Included, null));
        var result = CostProjection.Calculate(e, car);
        Assert.Equal(1, result.Totals.MissingItems); Assert.Equal(3, result.Items.Single(i => i.Item.Id == unknown).Occurrences);
        Assert.Null(result.Items.Single(i => i.Item.Id == unknown).Total);
        e.SetCostPlanning(new(2027, 10, 1), 12);
        Assert.True(CostProjection.Calculate(e, car).Totals.Complete); Assert.Equal(3, e.Costs.Count);
        e.SetCostPlanning(new(2028, 2, 1), 15);
        Assert.Equal(1, CostProjection.Calculate(e, car).Totals.MissingItems);
        Assert.Equal(new DateOnly(2029, 2, 1), e.CostPlanning!.DateFor(e.FindCost(unknown).StartMonth));
    }
    [Fact]
    public void Annual_occurrences_match_explicit_monthly_calendar_at_every_boundary()
    {
        var (e, car) = Setup(48);
        for (var start = 1; start <= 24; start++)
        {
            var id = e.AddCost(new("Annual", CostCategory.Tax, CostFrequency.Yearly, 7.35m, start, 37, car));
            for (var from = 1; from <= 48; from++)
                for (var through = from; through <= 48; through++)
                {
                    var expected = Enumerable.Range(from, through - from + 1).Count(m => m >= start && m <= 37 && (m - start) % 12 == 0);
                    var actual = CostProjection.Calculate(e, car, from, through).Items.Single();
                    Assert.Equal(expected, actual.Occurrences); Assert.Equal(expected * 7.35m, actual.Total);
                }
            e.RemoveCost(id);
        }
    }
    [Fact]
    public void Candidate_copy_is_independent_and_deletion_preserves_common_costs()
    {
        var (e, car) = Setup();
        var common = e.AddCost(new("Shared", CostCategory.Parking, CostFrequency.Monthly, 20));
        var own = e.AddCost(new("Own", CostCategory.Insurance, CostFrequency.Yearly, 100, CandidateId: car));
        var copy = e.CopyCandidate(car, "Copy"); var copied = e.Costs.Single(c => c.CandidateId == copy);
        Assert.NotEqual(own, copied.Id);
        e.EditCost(copied.Id, copied.ToInput() with { Amount = 80 });
        Assert.Equal(100, e.FindCost(own).Amount); Assert.Equal(3, e.Costs.Count);
        e.RemoveCandidate(car); Assert.Equal(2, e.Costs.Count); Assert.NotNull(e.FindCost(common));
        Assert.Equal(460, CostProjection.Calculate(e, copy).Totals.Net);
    }
    [Fact]
    public void Validation_requires_plan_currency_valid_scope_and_coherent_inputs()
    {
        var e = new Evaluation("owner", "Decision", null); var input = new CostInput("Test", CostCategory.Other, CostFrequency.Monthly, 10);
        Assert.Throws<RuleException>(() => e.AddCost(input));
        e.SetCostPlanning(new(2027, 1, 1), 12); Assert.Throws<RuleException>(() => e.AddCost(input));
        e.SetCurrency("EUR");
        foreach (var invalid in new[] { input with { Amount = -1 }, input with { Amount = 1.001m }, input with { EndMonth = 0 }, input with { StartMonth = 5, EndMonth = 4 }, input with { CandidateId = Guid.NewGuid() }, input with { Frequency = CostFrequency.Included }, input with { Frequency = CostFrequency.Once, EndMonth = 12 }, input with { Direction = (CostDirection)99 } })
            Assert.Throws<RuleException>(() => e.AddCost(invalid));
        Assert.Empty(e.Costs);
        Assert.Throws<RuleException>(() => e.SetCostPlanning(new(2027, 1, 2), 12));
        Assert.Throws<RuleException>(() => e.SetCostPlanning(new(2027, 1, 1), 0));
        Assert.Equal(12, e.CostPlanning!.Months);
    }
}
