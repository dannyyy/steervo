using System.Globalization;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

/// <summary>
/// The posted offer editor. Every value is kept as text so that an empty field stays "not stated"
/// instead of becoming zero; the conversion into typed terms happens in <see cref="ToTerms"/>.
/// </summary>
public sealed class OfferForm
{
    // HTML date inputs post and expect ISO dates, whatever the display language is.
    private const string DateInputFormat = "yyyy-MM-dd";
    private const string InvalidTerms = "InvalidOfferTerms";

    public string? Currency { get; set; }

    public Guid CandidateId { get; set; }

    public OfferKind Kind { get; set; }

    public OfferCommitment Commitment { get; set; }

    public string? ChangeNote { get; set; }

    public List<OfferLineForm> Lines { get; set; } = [];

    public string? Name { get; set; }

    public string? Provider { get; set; }

    public string? Lender { get; set; }

    public string? Intermediary { get; set; }

    public string? VehicleDescription { get; set; }

    public string? IssuedOn { get; set; }

    public string? ValidUntil { get; set; }

    public string? DeliveryOn { get; set; }

    public string? DeliveryNotes { get; set; }

    public string? StatedTotal { get; set; }

    public string? TermMonths { get; set; }

    public string? AnnualInterestPercent { get; set; }

    public string? MonthlyPayment { get; set; }

    public string? DownPayment { get; set; }

    public string? ResidualPayment { get; set; }

    public string? IncludedKilometresPerYear { get; set; }

    public string? ExcessKilometrePrice { get; set; }

    public string? UnusedKilometreCredit { get; set; }

    public string? Notes { get; set; }

    /// <summary>Converts the posted text into offer terms; an unreadable value is a rule violation.</summary>
    public OfferTerms ToTerms()
    {
        return new()
        {
            Kind = Kind,
            Commitment = Commitment,
            Name = Name ?? "",
            Provider = Provider,
            Lender = Lender,
            Intermediary = Intermediary,
            VehicleDescription = VehicleDescription,
            IssuedOn = ParseDate(IssuedOn),
            ValidUntil = ParseDate(ValidUntil),
            DeliveryOn = ParseDate(DeliveryOn),
            DeliveryNotes = DeliveryNotes,
            StatedTotal = ParseNumber(StatedTotal),
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
    }

    public static OfferForm From(Offer offer, string? currency)
    {
        return FromRevision(offer.Latest, offer.CandidateId, currency);
    }

    public static OfferForm FromRevision(OfferRevision revision, Guid candidateId, string? currency)
    {
        var terms = revision.Terms;
        var lines = revision
            .Lines.OrderBy(line => line.Position)
            .Select(line => OfferLineForm.From(line.ToInput()))
            .ToList();

        return new()
        {
            CandidateId = candidateId,
            Currency = currency,
            Kind = terms.Kind,
            Commitment = terms.Commitment,
            Lines = lines,
            Name = terms.Name,
            Provider = terms.Provider,
            Lender = terms.Lender,
            Intermediary = terms.Intermediary,
            VehicleDescription = terms.VehicleDescription,
            IssuedOn = FormatDate(terms.IssuedOn),
            ValidUntil = FormatDate(terms.ValidUntil),
            DeliveryOn = FormatDate(terms.DeliveryOn),
            DeliveryNotes = terms.DeliveryNotes,
            StatedTotal = FormatNumber(terms.StatedTotal),
            TermMonths = FormatInteger(terms.TermMonths),
            AnnualInterestPercent = FormatNumber(terms.AnnualInterestPercent),
            MonthlyPayment = FormatNumber(terms.MonthlyPayment),
            DownPayment = FormatNumber(terms.DownPayment),
            ResidualPayment = FormatNumber(terms.ResidualPayment),
            IncludedKilometresPerYear = FormatInteger(terms.IncludedKilometresPerYear),
            ExcessKilometrePrice = FormatNumber(terms.ExcessKilometrePrice),
            UnusedKilometreCredit = FormatNumber(terms.UnusedKilometreCredit),
            Notes = terms.Notes,
        };
    }

    /// <summary>Returns the posted text of the field with the given <see cref="OfferField.Name"/>.</summary>
    public string? Value(string name)
    {
        return name switch
        {
            nameof(OfferTerms.Name) => Name,
            nameof(OfferTerms.Provider) => Provider,
            nameof(OfferTerms.Lender) => Lender,
            nameof(OfferTerms.Intermediary) => Intermediary,
            nameof(OfferTerms.VehicleDescription) => VehicleDescription,
            nameof(OfferTerms.IssuedOn) => IssuedOn,
            nameof(OfferTerms.ValidUntil) => ValidUntil,
            nameof(OfferTerms.DeliveryOn) => DeliveryOn,
            nameof(OfferTerms.DeliveryNotes) => DeliveryNotes,
            nameof(OfferTerms.StatedTotal) => StatedTotal,
            nameof(OfferTerms.TermMonths) => TermMonths,
            nameof(OfferTerms.AnnualInterestPercent) => AnnualInterestPercent,
            nameof(OfferTerms.MonthlyPayment) => MonthlyPayment,
            nameof(OfferTerms.DownPayment) => DownPayment,
            nameof(OfferTerms.ResidualPayment) => ResidualPayment,
            nameof(OfferTerms.IncludedKilometresPerYear) => IncludedKilometresPerYear,
            nameof(OfferTerms.ExcessKilometrePrice) => ExcessKilometrePrice,
            nameof(OfferTerms.UnusedKilometreCredit) => UnusedKilometreCredit,
            nameof(OfferTerms.Notes) => Notes,
            _ => null,
        };
    }

    /// <summary>Reads an invariant decimal number; empty text means the value is not stated.</summary>
    public static decimal? ParseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var isNumber = decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var parsed
        );
        if (!isNumber)
        {
            throw new RuleException(InvalidTerms);
        }

        return parsed;
    }

    /// <summary>Reads a whole number without sign; empty text means the value is not stated.</summary>
    public static int? ParseInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var isInteger = int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed
        );
        if (!isInteger)
        {
            throw new RuleException(InvalidTerms);
        }

        return parsed;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var isDate = DateOnly.TryParseExact(
            value,
            DateInputFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed
        );
        if (!isDate)
        {
            throw new RuleException(InvalidTerms);
        }

        return parsed;
    }

    private static string? FormatDate(DateOnly? value)
    {
        return value?.ToString(DateInputFormat, CultureInfo.InvariantCulture);
    }

    private static string? FormatNumber(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }

    private static string? FormatInteger(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>One posted price line of the offer editor, with its numbers still as text.</summary>
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

    public bool Financed { get; set; }

    public string? Value { get; set; }

    public Guid? CriterionId { get; set; }

    public OfferLineInput ToInput()
    {
        return new(
            Name ?? "",
            Kind,
            PriceKind,
            OfferForm.ParseNumber(Amount),
            Effect,
            CostCategory,
            OfferForm.ParseInteger(DurationMonths),
            Notes,
            Financed,
            OfferForm.ParseNumber(Value),
            CriterionId
        );
    }

    public static OfferLineForm From(OfferLineInput input)
    {
        return new()
        {
            Name = input.Name,
            Kind = input.Kind,
            PriceKind = input.PriceKind,
            Amount = input.Amount?.ToString(CultureInfo.InvariantCulture),
            Effect = input.Effect,
            CostCategory = input.CostCategory,
            DurationMonths = input.DurationMonths?.ToString(CultureInfo.InvariantCulture),
            Notes = input.Notes,
            Financed = input.Financed,
            Value = input.Value?.ToString(CultureInfo.InvariantCulture),
            CriterionId = input.CriterionId,
        };
    }
}

/// <summary>
/// The model of one price line editor. <paramref name="Key"/> is the index that binds the posted fields
/// of the line together; new lines added in the browser receive a generated key.
/// </summary>
public sealed record OfferLineEditor(
    OfferLineForm Line,
    string Key,
    IReadOnlyList<Criterion> Criteria
)
{
    /// <summary>The kinds offered for a line, in the order of the selection list.</summary>
    public static readonly OfferLineKind[] LineKinds =
    [
        OfferLineKind.Vehicle,
        OfferLineKind.Equipment,
        OfferLineKind.Accessory,
        OfferLineKind.Fee,
        OfferLineKind.Discount,
        OfferLineKind.Benefit,
    ];

    /// <summary>The ways a line can be priced, in the order of the selection list.</summary>
    public static readonly OfferPriceKind[] PriceKinds =
    [
        OfferPriceKind.Amount,
        OfferPriceKind.Included,
        OfferPriceKind.Unknown,
    ];

    /// <summary>The form field prefix that model binding maps to this line.</summary>
    public string FieldPrefix => $"Input.Lines[{Key}]";
}
