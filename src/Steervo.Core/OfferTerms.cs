namespace Steervo.Core;

public enum OfferKind
{
    Unknown,
    Purchase,
    Leasing,
    Financing,
}

public enum OfferCommitment
{
    Requested,
    NonBinding,
    Binding,
}

public enum OfferLineKind
{
    Accessory,
    Fee,
    Benefit,
    Equipment,
    Vehicle,
    Discount,
}

/// <summary>Whether a line states its price, is included in another price, or leaves it open.</summary>
public enum OfferPriceKind
{
    Unknown,
    Amount,
    Included,
}

public enum OfferBenefitEffect
{
    CostSaving,
    Right,
}

public enum CostCategory
{
    Service,
    Tires,
    PublicCharging,
    HomeCharging,
    Fuel,
    Insurance,
    Tax,
    Parking,
    Other,
}

/// <summary>Everything an offer states apart from its priced lines: parties, dates and financing terms.</summary>
public sealed record OfferTerms
{
    private const int MaximumPartyLength = 200;
    private const int MaximumVehicleDescriptionLength = 400;
    private const int MaximumDeliveryNotesLength = 1000;
    private const decimal MaximumInterestPercent = 100;

    public string Name { get; init; } = "";
    public string? Provider { get; init; }
    public string? Lender { get; init; }

    /// <summary>A broker or platform that arranges the financing without selling or financing the car itself.</summary>
    public string? Intermediary { get; init; }
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
        Rules.Defined(Kind);
        Rules.Defined(Commitment);

        if (
            TermMonths is < 1 or > Rules.MaximumMonths
            || IncludedKilometresPerYear is < 0 or > Rules.MaximumKilometresPerYear
            || AnnualInterestPercent is < 0 or > MaximumInterestPercent
            || ValidUntil < IssuedOn
        )
        {
            throw new RuleException("InvalidOfferTerms");
        }

        var amounts = new[]
        {
            StatedTotal,
            MonthlyPayment,
            DownPayment,
            ResidualPayment,
            ExcessKilometrePrice,
            UnusedKilometreCredit,
        };

        foreach (var amount in amounts)
        {
            if (amount is < 0 or > Rules.MaximumAmount)
            {
                throw new RuleException("InvalidOfferTerms");
            }
        }

        return this with
        {
            Name = Rules.Text(Name),
            Provider = Rules.OptionalText(Provider, MaximumPartyLength),
            Lender = Rules.OptionalText(Lender, MaximumPartyLength),
            Intermediary = Rules.OptionalText(Intermediary, MaximumPartyLength),
            VehicleDescription = Rules.OptionalText(
                VehicleDescription,
                MaximumVehicleDescriptionLength
            ),
            DeliveryNotes = Rules.OptionalText(DeliveryNotes, MaximumDeliveryNotesLength),
            Notes = Rules.OptionalText(Notes, Rules.LongNoteLength),
        };
    }

    /// <summary>
    /// Names of the details an offer of this kind should state but does not, in the order they
    /// are asked for.
    /// </summary>
    public IEnumerable<string> MissingFields()
    {
        if (string.IsNullOrEmpty(Provider))
        {
            yield return nameof(Provider);
        }

        if (string.IsNullOrEmpty(VehicleDescription))
        {
            yield return nameof(VehicleDescription);
        }

        if (Kind == OfferKind.Unknown)
        {
            yield return nameof(Kind);
        }

        // A delivery note such as "about eight weeks" is as good as a date.
        if (!DeliveryOn.HasValue && string.IsNullOrEmpty(DeliveryNotes))
        {
            yield return nameof(DeliveryOn);
        }

        if (Kind is OfferKind.Leasing or OfferKind.Financing)
        {
            if (string.IsNullOrEmpty(Lender))
            {
                yield return nameof(Lender);
            }

            if (!TermMonths.HasValue)
            {
                yield return nameof(TermMonths);
            }

            if (!AnnualInterestPercent.HasValue)
            {
                yield return nameof(AnnualInterestPercent);
            }

            if (!MonthlyPayment.HasValue)
            {
                yield return nameof(MonthlyPayment);
            }

            if (!DownPayment.HasValue)
            {
                yield return nameof(DownPayment);
            }

            if (!ResidualPayment.HasValue)
            {
                yield return nameof(ResidualPayment);
            }
        }

        if (Kind == OfferKind.Leasing)
        {
            if (!IncludedKilometresPerYear.HasValue)
            {
                yield return nameof(IncludedKilometresPerYear);
            }

            if (!ExcessKilometrePrice.HasValue)
            {
                yield return nameof(ExcessKilometrePrice);
            }

            if (!UnusedKilometreCredit.HasValue)
            {
                yield return nameof(UnusedKilometreCredit);
            }
        }
    }
}

public sealed record OfferLineInput(
    string Name,
    OfferLineKind Kind = OfferLineKind.Accessory,
    OfferPriceKind PriceKind = OfferPriceKind.Unknown,
    decimal? Amount = null,
    OfferBenefitEffect? Effect = null,
    CostCategory? CostCategory = null,
    int? DurationMonths = null,
    string? Notes = null,
    bool Financed = true,
    decimal? Value = null,
    Guid? CriterionId = null
)
{
    private const int MaximumNameLength = 300;

    internal OfferLineInput Validated()
    {
        Rules.Defined(Kind);
        Rules.Defined(PriceKind);

        if (Effect.HasValue)
        {
            Rules.Defined(Effect.Value);
        }

        if (CostCategory.HasValue)
        {
            Rules.Defined(CostCategory.Value);
        }

        // An amount is stated exactly when the line is priced; the value of what a line is worth
        // is only recorded when it is included in another price.
        var amountContradictsPriceKind =
            (PriceKind == OfferPriceKind.Amount && !Amount.HasValue)
            || (PriceKind != OfferPriceKind.Amount && Amount.HasValue);
        var valueWithoutInclusion = PriceKind != OfferPriceKind.Included && Value.HasValue;
        var discountIsIncluded =
            Kind == OfferLineKind.Discount && PriceKind == OfferPriceKind.Included;

        var numberOutOfRange =
            Amount is < 0 or > Rules.MaximumAmount
            || DurationMonths is < 1 or > Rules.MaximumMonths
            || Value is < 0 or > Rules.MaximumAmount;

        // Effect, cost category and duration describe a benefit; only a cost saving has a category.
        var benefitDetailsOnOtherLine =
            Kind != OfferLineKind.Benefit
            && (Effect.HasValue || CostCategory.HasValue || DurationMonths.HasValue);
        var categoryWithoutSaving =
            Effect != OfferBenefitEffect.CostSaving && CostCategory.HasValue;

        if (
            amountContradictsPriceKind
            || numberOutOfRange
            || valueWithoutInclusion
            || discountIsIncluded
            || CriterionId == Guid.Empty
            || benefitDetailsOnOtherLine
            || categoryWithoutSaving
        )
        {
            throw new RuleException("InvalidOfferLine");
        }

        return this with
        {
            Name = Rules.Text(Name, MaximumNameLength),
            Notes = Rules.OptionalText(Notes, Rules.ShortNoteLength),
        };
    }
}

/// <summary>
/// Tells whether the instalment of a lease or loan follows from its price, down payment, residual, term and interest.
/// A mismatch usually means a discount, a down payment or the residual was captured differently from the document.
/// </summary>
public sealed record InstalmentCheck(decimal Expected, decimal Stated)
{
    // Rounding in the lender's own calculation is tolerated: one currency unit or half a percent.
    private const decimal AbsoluteTolerance = 1m;
    private const decimal RelativeTolerance = 0.005m;

    // Converts an annual rate in percent into a monthly rate as a fraction.
    private const double MonthlyRateDivisor = 1200d;

    public decimal Difference => Stated - Expected;
    public bool Consistent =>
        Math.Abs(Difference) <= Math.Max(AbsoluteTolerance, Expected * RelativeTolerance);

    /// <summary>Annuity for a financed amount with a remaining value at the end; instalments at month end unless <paramref name="inAdvance"/>.</summary>
    public static decimal Payment(
        decimal principal,
        decimal residual,
        int months,
        decimal annualPercent,
        bool inAdvance = false
    )
    {
        if (annualPercent == 0)
        {
            return decimal.Round((principal - residual) / months, 2, MidpointRounding.AwayFromZero);
        }

        var rate = (double)annualPercent / MonthlyRateDivisor;
        var growth = Math.Pow(1 + rate, months);
        var payment = ((double)principal - (double)residual / growth) * rate / (1 - 1 / growth);

        return decimal.Round(
            (decimal)(inAdvance ? payment / (1 + rate) : payment),
            2,
            MidpointRounding.AwayFromZero
        );
    }

    /// <summary>The check for a lease or loan whose terms are complete; otherwise <c>null</c>.</summary>
    public static InstalmentCheck? From(OfferTerms terms, OfferPricing pricing)
    {
        if (
            terms.Kind is not (OfferKind.Leasing or OfferKind.Financing)
            || !pricing.Known
            || terms.TermMonths is not { } months
            || terms.MonthlyPayment is not { } stated
            || terms.AnnualInterestPercent is not { } interest
            || terms.DownPayment is not { } down
            || (terms.Kind == OfferKind.Leasing && !terms.ResidualPayment.HasValue)
        )
        {
            return null;
        }

        var principal = pricing.Financed - down;
        var residual = terms.ResidualPayment ?? 0;

        if (principal <= 0 || residual < 0 || residual >= principal)
        {
            return null;
        }

        // Lenders quote instalments at the start or the end of the month; the nearer reading is the one meant.
        var atMonthEnd = Payment(principal, residual, months, interest);
        var atMonthStart = Payment(principal, residual, months, interest, inAdvance: true);
        var startIsNearer = Math.Abs(stated - atMonthStart) < Math.Abs(stated - atMonthEnd);

        return new(startIsNearer ? atMonthStart : atMonthEnd, stated);
    }
}

/// <summary>
/// Sums the lines of one offer version. Discounts are stored as positive amounts and subtracted.
/// </summary>
public sealed record OfferPricing(
    decimal Vehicle,
    decimal Extras,
    decimal Discounts,
    decimal BenefitValue,
    decimal Financed,
    int Priced,
    int Open,
    bool VehicleOpen = false
)
{
    // Extras alone are not a price for the car: without the vehicle's own price the total stays open.
    public bool Known => Priced > 0 && !VehicleOpen;
    public decimal BeforeDiscounts => Vehicle + Extras;
    public decimal Total => BeforeDiscounts - Discounts;
    public decimal DueAtHandover => Total - Financed;

    // What the buyer is given: discounts plus the value of everything included at no charge.
    public decimal Concession => Discounts + BenefitValue;
    public decimal? ConcessionPercent =>
        BeforeDiscounts > 0 && Concession > 0
            ? decimal.Round(Concession * 100 / BeforeDiscounts, 1, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>How far the calculated total is from the one printed on the offer, if they differ.</summary>
    public decimal? Difference(decimal? statedTotal) =>
        Known && statedTotal.HasValue && statedTotal != Total ? Total - statedTotal : null;

    public static OfferPricing From(IEnumerable<OfferLineInput> lines)
    {
        decimal vehicle = 0;
        decimal extras = 0;
        decimal discounts = 0;
        decimal benefit = 0;
        decimal financed = 0;
        var priced = 0;
        var open = 0;
        var vehicleOpen = false;

        foreach (var line in lines)
        {
            if (line.PriceKind == OfferPriceKind.Unknown)
            {
                open++;
                vehicleOpen |= line.Kind == OfferLineKind.Vehicle;
                continue;
            }

            if (line.PriceKind == OfferPriceKind.Included)
            {
                benefit += line.Value ?? 0;
                continue;
            }

            var amount = line.Amount ?? 0;
            priced++;

            if (line.Kind == OfferLineKind.Discount)
            {
                discounts += amount;
            }
            else if (line.Kind is OfferLineKind.Fee or OfferLineKind.Benefit)
            {
                extras += amount;
            }
            else
            {
                vehicle += amount;
            }

            if (line.Financed)
            {
                financed += line.Kind == OfferLineKind.Discount ? -amount : amount;
            }
        }

        return new(vehicle, extras, discounts, benefit, financed, priced, open, vehicleOpen);
    }
}
