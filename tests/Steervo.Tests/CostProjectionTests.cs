using Steervo.Calculations;
using Steervo.Core;
using Xunit;

namespace Steervo.Tests;

/// <summary>
/// Checks the projection of running costs onto the months of the cost plan.
/// </summary>
public sealed class CostProjectionTests
{
    [Fact]
    public void Actual_payment_months_partial_years_and_common_costs_reconcile_exactly()
    {
        var (evaluation, car) = CreateEvaluationWithCar();
        evaluation.AddCost(new("Annual", CostCategory.Insurance, CostFrequency.Yearly, 120, 2));
        evaluation.AddCost(new("Monthly", CostCategory.Parking, CostFrequency.Monthly, 25));
        evaluation.AddCost(
            new("Once", CostCategory.Other, CostFrequency.Once, 400, 3, CandidateId: car)
        );
        evaluation.AddCost(
            new("Temporary", CostCategory.Fuel, CostFrequency.Monthly, 10, 4, 6, car)
        );
        evaluation.AddCost(
            new(
                "Credit",
                CostCategory.Other,
                CostFrequency.Once,
                50,
                5,
                CandidateId: car,
                Direction: CostDirection.Income
            )
        );
        evaluation.AddCost(
            new("Included", CostCategory.Service, CostFrequency.Included, null, 1, 12, car)
        );
        evaluation.AddCost(
            new(
                "Later unknown",
                CostCategory.Tires,
                CostFrequency.Yearly,
                null,
                16,
                CandidateId: car
            )
        );

        var wholePlan = CostProjection.Calculate(evaluation, car);

        // Expenses over 15 months: 2 x 120 annual (months 2 and 14) + 15 x 25 monthly
        // + 400 once + 3 x 10 temporary (months 4 to 6) = 1045.
        Assert.Equal(1045, wholePlan.Totals.Expenses);
        Assert.Equal(50, wholePlan.Totals.Income);
        Assert.Equal(995, wholePlan.Totals.Net);
        Assert.True(wholePlan.Totals.Complete);

        // Without a car only the common costs count: 240 + 375.
        Assert.Equal(615, CostProjection.Calculate(evaluation).Totals.Net);

        // Months 1 to 3: 120 + 3 x 25 + 400.
        Assert.Equal(595, CostProjection.Calculate(evaluation, car, 1, 3).Totals.Net);

        // Months 4 to 15: 120 + 12 x 25 + 30 - 50.
        Assert.Equal(400, CostProjection.Calculate(evaluation, car, 4, 15).Totals.Net);

        // Month 5 alone: 25 + 10 - 50.
        Assert.Equal(-15, CostProjection.Calculate(evaluation, car, 5, 5).Totals.Net);

        // The single months add up to the total of the whole plan.
        Assert.Equal(
            995,
            Enumerable
                .Range(1, 15)
                .Sum(month => CostProjection.Calculate(evaluation, car, month, month).Totals.Net)
        );
        Assert.Equal(new DateOnly(2028, 12, 1), evaluation.CostPlanning!.DateFor(15));
    }

    [Fact]
    public void Unknown_zero_and_included_are_distinct_and_shorter_horizons_do_not_destroy_data()
    {
        var (evaluation, car) = CreateEvaluationWithCar();
        var unknown = evaluation.AddCost(
            new("Unknown", CostCategory.Service, CostFrequency.Monthly, null, 13, 15, car)
        );
        evaluation.AddCost(new("Zero", CostCategory.Other, CostFrequency.Monthly, 0));
        evaluation.AddCost(new("Included", CostCategory.Tires, CostFrequency.Included, null));

        // Only the cost without an amount counts as missing.
        var projection = CostProjection.Calculate(evaluation, car);

        Assert.Equal(1, projection.Totals.MissingItems);
        Assert.Equal(3, projection.Items.Single(i => i.Item.Id == unknown).Occurrences);
        Assert.Null(projection.Items.Single(i => i.Item.Id == unknown).Total);

        // A shorter plan ends before the unknown cost starts, but keeps the cost itself.
        evaluation.SetCostPlanning(new(2027, 10, 1), 12);

        Assert.True(CostProjection.Calculate(evaluation, car).Totals.Complete);
        Assert.Equal(3, evaluation.Costs.Count);

        // Extending the plan again brings the cost back, relative to the new start date.
        evaluation.SetCostPlanning(new(2028, 2, 1), 15);

        Assert.Equal(1, CostProjection.Calculate(evaluation, car).Totals.MissingItems);
        Assert.Equal(
            new DateOnly(2029, 2, 1),
            evaluation.CostPlanning!.DateFor(evaluation.FindCost(unknown).StartMonth)
        );
    }

    [Fact]
    public void Annual_occurrences_match_explicit_monthly_calendar_at_every_boundary()
    {
        var (evaluation, car) = CreateEvaluationWithCar(planningMonths: 48);

        for (var start = 1; start <= 24; start++)
        {
            var cost = evaluation.AddCost(
                new("Annual", CostCategory.Tax, CostFrequency.Yearly, 7.35m, start, 37, car)
            );

            // Every possible period of the plan is compared with a month-by-month count.
            for (var from = 1; from <= 48; from++)
            {
                for (var through = from; through <= 48; through++)
                {
                    var expected = Enumerable
                        .Range(from, through - from + 1)
                        .Count(month => month >= start && month <= 37 && (month - start) % 12 == 0);

                    var actual = CostProjection
                        .Calculate(evaluation, car, from, through)
                        .Items.Single();

                    Assert.Equal(expected, actual.Occurrences);
                    Assert.Equal(expected * 7.35m, actual.Total);
                }
            }

            evaluation.RemoveCost(cost);
        }
    }

    [Fact]
    public void Candidate_copy_is_independent_and_deletion_preserves_common_costs()
    {
        var (evaluation, car) = CreateEvaluationWithCar();
        var common = evaluation.AddCost(
            new("Shared", CostCategory.Parking, CostFrequency.Monthly, 20)
        );
        var own = evaluation.AddCost(
            new("Own", CostCategory.Insurance, CostFrequency.Yearly, 100, CandidateId: car)
        );

        // The copied car receives its own copy of the car-specific cost.
        var copy = evaluation.CopyCandidate(car, "Copy");
        var copiedCost = evaluation.Costs.Single(c => c.CandidateId == copy);

        Assert.NotEqual(own, copiedCost.Id);

        evaluation.EditCost(copiedCost.Id, copiedCost.ToInput() with { Amount = 80 });

        Assert.Equal(100, evaluation.FindCost(own).Amount);
        Assert.Equal(3, evaluation.Costs.Count);

        // Removing the original car takes only its own cost along.
        evaluation.RemoveCandidate(car);

        Assert.Equal(2, evaluation.Costs.Count);
        Assert.NotNull(evaluation.FindCost(common));

        // 15 x 20 shared + 2 x 80 annual (months 1 and 13).
        Assert.Equal(460, CostProjection.Calculate(evaluation, copy).Totals.Net);
    }

    [Fact]
    public void Validation_requires_plan_currency_valid_scope_and_coherent_inputs()
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var input = new CostInput("Test", CostCategory.Other, CostFrequency.Monthly, 10);

        // A cost needs a cost plan first, then a currency.
        Assert.Throws<RuleException>(() => evaluation.AddCost(input));

        evaluation.SetCostPlanning(new(2027, 1, 1), 12);
        Assert.Throws<RuleException>(() => evaluation.AddCost(input));

        evaluation.SetCurrency("EUR");
        var invalidInputs = new[]
        {
            input with
            {
                Amount = -1,
            },
            input with
            {
                Amount = 1.001m,
            },
            input with
            {
                EndMonth = 0,
            },
            input with
            {
                StartMonth = 5,
                EndMonth = 4,
            },
            input with
            {
                CandidateId = Guid.NewGuid(),
            },
            input with
            {
                Frequency = CostFrequency.Included,
            },
            input with
            {
                Frequency = CostFrequency.Once,
                EndMonth = 12,
            },
            input with
            {
                Direction = (CostDirection)99,
            },
        };

        foreach (var invalid in invalidInputs)
        {
            Assert.Throws<RuleException>(() => evaluation.AddCost(invalid));
        }

        Assert.Empty(evaluation.Costs);

        // The plan starts on the first of a month and covers at least one month.
        Assert.Throws<RuleException>(() => evaluation.SetCostPlanning(new(2027, 1, 2), 12));
        Assert.Throws<RuleException>(() => evaluation.SetCostPlanning(new(2027, 1, 1), 0));
        Assert.Equal(12, evaluation.CostPlanning!.Months);
    }

    private static (Evaluation Evaluation, Guid Car) CreateEvaluationWithCar(
        int planningMonths = 15
    )
    {
        var evaluation = new Evaluation("owner", "Decision", null);
        var car = evaluation.AddCandidate(new("Vehicle"));
        evaluation.AddOffer(car, new() { Name = "Request" }, [], "EUR");
        evaluation.SetCostPlanning(new(2027, 10, 1), planningMonths);

        return (evaluation, car);
    }
}
