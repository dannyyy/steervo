namespace Steervo.Core;

public enum OfferKind { Unknown, Purchase, Leasing, Financing }
public enum OfferCommitment { Requested, NonBinding, Binding }
public enum OfferLineKind { Accessory, Fee, Benefit, Equipment }
public enum OfferPriceKind { Unknown, Amount, Included }
public enum OfferBenefitEffect { CostSaving, Right }
public enum CostCategory { Service, Tires, PublicCharging, HomeCharging, Fuel, Insurance, Tax, Parking, Other }

public sealed record OfferTerms
{
    public string Name { get; init; } = "";
    public string? Provider { get; init; }
    public string? Lender { get; init; }
    public string? VehicleDescription { get; init; }
    public OfferKind Kind { get; init; }
    public OfferCommitment Commitment { get; init; }
    public DateOnly? IssuedOn { get; init; }
    public DateOnly? ValidUntil { get; init; }
    public DateOnly? DeliveryOn { get; init; }
    public string? DeliveryNotes { get; init; }
    public decimal? BasePrice { get; init; }
    public int? TermMonths { get; init; }
    public decimal? AnnualInterestPercent { get; init; }
    public decimal? MonthlyPayment { get; init; }
    public decimal? DownPayment { get; init; }
    public int? IncludedKilometresPerYear { get; init; }
    public decimal? ResidualPayment { get; init; }
    public decimal? ExcessKilometrePrice { get; init; }
    public decimal? UnusedKilometreCredit { get; init; }
    public string? Notes { get; init; }

    internal OfferTerms Validated()
    {
        Rules.Defined(Kind); Rules.Defined(Commitment);
        if (TermMonths is < 1 or > 1200 || IncludedKilometresPerYear is < 0 or > 1_000_000
            || AnnualInterestPercent is < 0 or > 100 || ValidUntil < IssuedOn)
            throw new RuleException("InvalidOfferTerms");
        foreach (var amount in new[] { BasePrice, MonthlyPayment, DownPayment, ResidualPayment, ExcessKilometrePrice, UnusedKilometreCredit })
            if (amount is < 0 or > 1_000_000_000_000m) throw new RuleException("InvalidOfferTerms");
        return this with
        {
            Name = Rules.Text(Name), Provider = Rules.OptionalText(Provider, 200), Lender = Rules.OptionalText(Lender, 200),
            VehicleDescription = Rules.OptionalText(VehicleDescription, 400), DeliveryNotes = Rules.OptionalText(DeliveryNotes, 1000),
            Notes = Rules.OptionalText(Notes, 4000)
        };
    }

    public IEnumerable<string> MissingFields()
    {
        if (string.IsNullOrEmpty(Provider)) yield return nameof(Provider);
        if (string.IsNullOrEmpty(VehicleDescription)) yield return nameof(VehicleDescription);
        if (Kind == OfferKind.Unknown) yield return nameof(Kind);
        if (!BasePrice.HasValue) yield return nameof(BasePrice);
        if (!DeliveryOn.HasValue && string.IsNullOrEmpty(DeliveryNotes)) yield return nameof(DeliveryOn);
        if (Kind is OfferKind.Leasing or OfferKind.Financing)
        {
            if (string.IsNullOrEmpty(Lender)) yield return nameof(Lender);
            if (!TermMonths.HasValue) yield return nameof(TermMonths);
            if (!AnnualInterestPercent.HasValue) yield return nameof(AnnualInterestPercent);
            if (!MonthlyPayment.HasValue) yield return nameof(MonthlyPayment);
            if (!DownPayment.HasValue) yield return nameof(DownPayment);
            if (!ResidualPayment.HasValue) yield return nameof(ResidualPayment);
        }
        if (Kind == OfferKind.Leasing)
        {
            if (!IncludedKilometresPerYear.HasValue) yield return nameof(IncludedKilometresPerYear);
            if (!ExcessKilometrePrice.HasValue) yield return nameof(ExcessKilometrePrice);
            if (!UnusedKilometreCredit.HasValue) yield return nameof(UnusedKilometreCredit);
        }
    }
}

public sealed record OfferLineInput(string Name, OfferLineKind Kind = OfferLineKind.Accessory,
    OfferPriceKind PriceKind = OfferPriceKind.Unknown, decimal? Amount = null,
    OfferBenefitEffect? Effect = null, CostCategory? CostCategory = null, int? DurationMonths = null, string? Notes = null)
{
    internal OfferLineInput Validated()
    {
        Rules.Defined(Kind); Rules.Defined(PriceKind);
        if (Effect.HasValue) Rules.Defined(Effect.Value);
        if (CostCategory.HasValue) Rules.Defined(CostCategory.Value);
        if ((PriceKind == OfferPriceKind.Amount && !Amount.HasValue) || (PriceKind != OfferPriceKind.Amount && Amount.HasValue)
            || Amount is < 0 or > 1_000_000_000_000m || DurationMonths is < 1 or > 1200
            || (Kind != OfferLineKind.Benefit && (Effect.HasValue || CostCategory.HasValue || DurationMonths.HasValue))
            || (Effect != OfferBenefitEffect.CostSaving && CostCategory.HasValue)) throw new RuleException("InvalidOfferLine");
        return this with { Name = Rules.Text(Name, 300), Notes = Rules.OptionalText(Notes, 2000) };
    }
}
