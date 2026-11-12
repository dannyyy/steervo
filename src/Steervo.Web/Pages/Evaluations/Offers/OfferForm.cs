using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

public sealed class OfferForm
{
    public string? Currency { get; set; }
    public Guid CandidateId { get; set; }
    public OfferKind Kind { get; set; }
    public OfferCommitment Commitment { get; set; }
    public string? ChangeNote { get; set; }
    public List<OfferLineForm> Lines { get; set; } = [];
    public string? Name { get; set; }
    public string? Provider { get; set; }
    public string? Lender { get; set; }
    public string? VehicleDescription { get; set; }
    public string? IssuedOn { get; set; }
    public string? ValidUntil { get; set; }
    public string? DeliveryOn { get; set; }
    public string? DeliveryNotes { get; set; }
    public string? BasePrice { get; set; }
    public string? TermMonths { get; set; }
    public string? AnnualInterestPercent { get; set; }
    public string? MonthlyPayment { get; set; }
    public string? DownPayment { get; set; }
    public string? ResidualPayment { get; set; }
    public string? IncludedKilometresPerYear { get; set; }
    public string? ExcessKilometrePrice { get; set; }
    public string? UnusedKilometreCredit { get; set; }
    public string? Notes { get; set; }
    public OfferTerms ToTerms() => new()
    {
        Kind = Kind, Commitment = Commitment,
        Name = Name ?? "",
        Provider = Provider,
        Lender = Lender,
        VehicleDescription = VehicleDescription,
        IssuedOn = ParseDate(IssuedOn),
        ValidUntil = ParseDate(ValidUntil),
        DeliveryOn = ParseDate(DeliveryOn),
        DeliveryNotes = DeliveryNotes,
        BasePrice = ParseNumber(BasePrice),
        TermMonths = ParseInteger(TermMonths),
        AnnualInterestPercent = ParseNumber(AnnualInterestPercent),
        MonthlyPayment = ParseNumber(MonthlyPayment),
        DownPayment = ParseNumber(DownPayment),
        ResidualPayment = ParseNumber(ResidualPayment),
        IncludedKilometresPerYear = ParseInteger(IncludedKilometresPerYear),
        ExcessKilometrePrice = ParseNumber(ExcessKilometrePrice),
        UnusedKilometreCredit = ParseNumber(UnusedKilometreCredit),
        Notes = Notes,
    };
    public static OfferForm From(Offer offer, string? currency) => FromRevision(offer.Latest, offer.CandidateId, currency);
    public static OfferForm FromRevision(OfferRevision revision, Guid candidateId, string? currency)
    {
        var terms = revision.Terms;
        return new()
        {
            CandidateId = candidateId, Currency = currency, Kind = terms.Kind, Commitment = terms.Commitment,
            Lines = revision.Lines.OrderBy(line => line.Position).Select(line => OfferLineForm.From(line.ToInput())).ToList(),
            Name = terms.Name,
            Provider = terms.Provider,
            Lender = terms.Lender,
            VehicleDescription = terms.VehicleDescription,
            IssuedOn = terms.IssuedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ValidUntil = terms.ValidUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DeliveryOn = terms.DeliveryOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DeliveryNotes = terms.DeliveryNotes,
            BasePrice = terms.BasePrice?.ToString(CultureInfo.InvariantCulture),
            TermMonths = terms.TermMonths?.ToString(CultureInfo.InvariantCulture),
            AnnualInterestPercent = terms.AnnualInterestPercent?.ToString(CultureInfo.InvariantCulture),
            MonthlyPayment = terms.MonthlyPayment?.ToString(CultureInfo.InvariantCulture),
            DownPayment = terms.DownPayment?.ToString(CultureInfo.InvariantCulture),
            ResidualPayment = terms.ResidualPayment?.ToString(CultureInfo.InvariantCulture),
            IncludedKilometresPerYear = terms.IncludedKilometresPerYear?.ToString(CultureInfo.InvariantCulture),
            ExcessKilometrePrice = terms.ExcessKilometrePrice?.ToString(CultureInfo.InvariantCulture),
            UnusedKilometreCredit = terms.UnusedKilometreCredit?.ToString(CultureInfo.InvariantCulture),
            Notes = terms.Notes,
        };
    }
    public string? Value(string name) => name switch
    {
        nameof(OfferTerms.Name) => Name,
        nameof(OfferTerms.Provider) => Provider,
        nameof(OfferTerms.Lender) => Lender,
        nameof(OfferTerms.VehicleDescription) => VehicleDescription,
        nameof(OfferTerms.IssuedOn) => IssuedOn,
        nameof(OfferTerms.ValidUntil) => ValidUntil,
        nameof(OfferTerms.DeliveryOn) => DeliveryOn,
        nameof(OfferTerms.DeliveryNotes) => DeliveryNotes,
        nameof(OfferTerms.BasePrice) => BasePrice,
        nameof(OfferTerms.TermMonths) => TermMonths,
        nameof(OfferTerms.AnnualInterestPercent) => AnnualInterestPercent,
        nameof(OfferTerms.MonthlyPayment) => MonthlyPayment,
        nameof(OfferTerms.DownPayment) => DownPayment,
        nameof(OfferTerms.ResidualPayment) => ResidualPayment,
        nameof(OfferTerms.IncludedKilometresPerYear) => IncludedKilometresPerYear,
        nameof(OfferTerms.ExcessKilometrePrice) => ExcessKilometrePrice,
        nameof(OfferTerms.UnusedKilometreCredit) => UnusedKilometreCredit,
        nameof(OfferTerms.Notes) => Notes,
        _ => null
    };
    public static decimal? ParseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)) throw new RuleException("InvalidOfferTerms");
        return parsed;
    }
    public static int? ParseInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) throw new RuleException("InvalidOfferTerms");
        return parsed;
    }
    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw new RuleException("InvalidOfferTerms");
        return parsed;
    }
}

public sealed class OfferLineForm
{
    public string? Name { get; set; }
    public OfferLineKind Kind { get; set; }
    public OfferPriceKind PriceKind { get; set; }
    public string? Amount { get; set; }
    public OfferBenefitEffect? Effect { get; set; }
    public CostCategory? CostCategory { get; set; }
    public string? DurationMonths { get; set; }
    public string? Notes { get; set; }
    public OfferLineInput ToInput() => new(Name ?? "", Kind, PriceKind, OfferForm.ParseNumber(Amount), Effect, CostCategory, OfferForm.ParseInteger(DurationMonths), Notes);
    public static OfferLineForm From(OfferLineInput input) => new()
    {
        Name = input.Name, Kind = input.Kind, PriceKind = input.PriceKind, Amount = input.Amount?.ToString(CultureInfo.InvariantCulture),
        Effect = input.Effect, CostCategory = input.CostCategory, DurationMonths = input.DurationMonths?.ToString(CultureInfo.InvariantCulture), Notes = input.Notes
    };
}
public sealed record OfferLineEditor(OfferLineForm Line, string Key);
