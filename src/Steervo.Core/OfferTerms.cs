namespace Steervo.Core;

public enum OfferKind { Unknown, Purchase, Leasing, Financing }
public enum OfferCommitment { Requested, NonBinding, Binding }
public enum OfferLineKind { Accessory, Fee, Benefit, Equipment, Vehicle, Discount }
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
    // The total printed on the offer; the calculated total comes from the lines.
    public decimal? StatedTotal { get; init; }
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
        foreach (var amount in new[] { StatedTotal, MonthlyPayment, DownPayment, ResidualPayment, ExcessKilometrePrice, UnusedKilometreCredit })
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
    OfferBenefitEffect? Effect = null, CostCategory? CostCategory = null, int? DurationMonths = null, string? Notes = null,
    bool Financed = true, decimal? Value = null, Guid? CriterionId = null)
{
    internal OfferLineInput Validated()
    {
        Rules.Defined(Kind); Rules.Defined(PriceKind);
        if (Effect.HasValue) Rules.Defined(Effect.Value);
        if (CostCategory.HasValue) Rules.Defined(CostCategory.Value);
        if ((PriceKind == OfferPriceKind.Amount && !Amount.HasValue) || (PriceKind != OfferPriceKind.Amount && Amount.HasValue)
            || Amount is < 0 or > 1_000_000_000_000m || DurationMonths is < 1 or > 1200
            || Value is < 0 or > 1_000_000_000_000m || (PriceKind != OfferPriceKind.Included && Value.HasValue)
            || (Kind == OfferLineKind.Discount && PriceKind == OfferPriceKind.Included) || CriterionId == Guid.Empty
            || (Kind != OfferLineKind.Benefit && (Effect.HasValue || CostCategory.HasValue || DurationMonths.HasValue))
            || (Effect != OfferBenefitEffect.CostSaving && CostCategory.HasValue)) throw new RuleException("InvalidOfferLine");
        return this with { Name = Rules.Text(Name, 300), Notes = Rules.OptionalText(Notes, 2000) };
    }
}

// Sums the lines of one offer version. Discounts are stored as positive amounts and subtracted.
public sealed record OfferPricing(decimal Vehicle, decimal Extras, decimal Discounts, decimal BenefitValue, decimal Financed, int Priced, int Open)
{
    public bool Known => Priced > 0;
    public decimal BeforeDiscounts => Vehicle + Extras;
    public decimal Total => BeforeDiscounts - Discounts;
    public decimal DueAtHandover => Total - Financed;
    public decimal Concession => Discounts + BenefitValue;
    public decimal? ConcessionPercent => BeforeDiscounts > 0 && Concession > 0 ? decimal.Round(Concession * 100 / BeforeDiscounts, 1, MidpointRounding.AwayFromZero) : null;
    public decimal? Difference(decimal? statedTotal) => Known && statedTotal.HasValue && statedTotal != Total ? Total - statedTotal : null;

    public static OfferPricing From(IEnumerable<OfferLineInput> lines)
    {
        decimal vehicle = 0, extras = 0, discounts = 0, benefit = 0, financed = 0; int priced = 0, open = 0;
        foreach (var line in lines)
        {
            if (line.PriceKind == OfferPriceKind.Unknown) { open++; continue; }
            if (line.PriceKind == OfferPriceKind.Included) { benefit += line.Value ?? 0; continue; }
            var amount = line.Amount ?? 0; priced++;
            if (line.Kind == OfferLineKind.Discount) discounts += amount;
            else if (line.Kind is OfferLineKind.Fee or OfferLineKind.Benefit) extras += amount;
            else vehicle += amount;
            if (line.Financed) financed += line.Kind == OfferLineKind.Discount ? -amount : amount;
        }
        return new(vehicle, extras, discounts, benefit, financed, priced, open);
    }
}
