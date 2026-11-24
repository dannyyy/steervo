using Microsoft.Extensions.Localization;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Offers;

/// <summary>
/// Describes one value of the offer terms: where it is grouped, how it is entered and how it is shown.
/// </summary>
public sealed record OfferField(
    string Name,
    string Group,
    string Type,
    int MaxLength,
    Func<OfferTerms, string?> Display,
    Func<OfferTerms, object?> Raw
)
{
    public string Label => "Offer" + Name;

    /// <summary>Amounts in the offer currency; they are labelled with the currency code.</summary>
    public bool IsMoney => Type is OfferFields.MoneyType or OfferFields.UnitPriceType;

    public bool IsUnitPrice => Type == OfferFields.UnitPriceType;

    public bool IsTextArea => Type == OfferFields.TextAreaType;

    public bool IsText => Type is OfferFields.TextType or OfferFields.TextAreaType;

    public bool IsRequired => Name == nameof(OfferTerms.Name);

    /// <summary>The <c>type</c> of the HTML input; every numeric kind is entered as a number.</summary>
    public string InputType => IsNumeric ? "number" : Type;

    public string InputStep => Type == OfferFields.IntegerType ? "1" : "any";

    /// <summary>
    /// The smallest accepted number; a term lasts at least one month. Null for fields that are not numeric.
    /// </summary>
    public string? InputMinimum
    {
        get
        {
            if (!IsNumeric)
            {
                return null;
            }

            return Name == nameof(OfferTerms.TermMonths) ? "1" : "0";
        }
    }

    /// <summary>The length limit of a text field; null where no limit applies.</summary>
    public int? InputMaxLength => MaxLength > 0 ? MaxLength : null;

    /// <summary>The shown value, or the wording for a value that is still open.</summary>
    public string DisplayOrOpen(OfferTerms terms, IStringLocalizer text)
    {
        var value = Display(terms);

        return string.IsNullOrEmpty(value) ? text["OfferOpen"].Value : value;
    }

    private bool IsNumeric => IsMoney || Type is OfferFields.IntegerType or OfferFields.NumberType;
}

/// <summary>The catalogue of all offer term fields in the order in which they are shown and edited.</summary>
public static class OfferFields
{
    public const string HeaderGroup = "Header";
    public const string DeliveryGroup = "Delivery";
    public const string PriceGroup = "Price";
    public const string ContractGroup = "Contract";
    public const string MileageGroup = "Mileage";
    public const string NotesGroup = "Notes";

    public const string TextType = "text";
    public const string TextAreaType = "textarea";
    public const string DateType = "date";
    public const string MoneyType = "money";
    public const string UnitPriceType = "unitprice";
    public const string IntegerType = "integer";
    public const string NumberType = "number";

    private const string DateFormat = "dd.MM.yyyy";
    private const string MoneyFormat = "N2";
    private const string QuantityFormat = "0.####";

    public static readonly OfferField[] All =
    [
        Text(nameof(OfferTerms.Name), HeaderGroup, TextType, 160, terms => terms.Name),
        Text(nameof(OfferTerms.Provider), HeaderGroup, TextType, 200, terms => terms.Provider),
        Text(nameof(OfferTerms.Lender), HeaderGroup, TextType, 200, terms => terms.Lender),
        Text(
            nameof(OfferTerms.Intermediary),
            HeaderGroup,
            TextType,
            200,
            terms => terms.Intermediary
        ),
        Text(
            nameof(OfferTerms.VehicleDescription),
            HeaderGroup,
            TextType,
            400,
            terms => terms.VehicleDescription
        ),
        Date(nameof(OfferTerms.IssuedOn), DeliveryGroup, terms => terms.IssuedOn),
        Date(nameof(OfferTerms.ValidUntil), DeliveryGroup, terms => terms.ValidUntil),
        Date(nameof(OfferTerms.DeliveryOn), DeliveryGroup, terms => terms.DeliveryOn),
        Text(
            nameof(OfferTerms.DeliveryNotes),
            DeliveryGroup,
            TextAreaType,
            1000,
            terms => terms.DeliveryNotes
        ),
        Money(nameof(OfferTerms.StatedTotal), PriceGroup, terms => terms.StatedTotal),
        Count(nameof(OfferTerms.TermMonths), ContractGroup, terms => terms.TermMonths),
        Quantity(
            nameof(OfferTerms.AnnualInterestPercent),
            ContractGroup,
            NumberType,
            terms => terms.AnnualInterestPercent
        ),
        Money(nameof(OfferTerms.MonthlyPayment), ContractGroup, terms => terms.MonthlyPayment),
        Money(nameof(OfferTerms.DownPayment), ContractGroup, terms => terms.DownPayment),
        Money(nameof(OfferTerms.ResidualPayment), ContractGroup, terms => terms.ResidualPayment),
        Count(
            nameof(OfferTerms.IncludedKilometresPerYear),
            MileageGroup,
            terms => terms.IncludedKilometresPerYear
        ),
        Quantity(
            nameof(OfferTerms.ExcessKilometrePrice),
            MileageGroup,
            UnitPriceType,
            terms => terms.ExcessKilometrePrice
        ),
        Quantity(
            nameof(OfferTerms.UnusedKilometreCredit),
            MileageGroup,
            UnitPriceType,
            terms => terms.UnusedKilometreCredit
        ),
        Text(nameof(OfferTerms.Notes), NotesGroup, TextAreaType, 4000, terms => terms.Notes),
    ];

    /// <summary>
    /// The field groups of an offer record and of the comparison. The offer name is the heading there
    /// and therefore not repeated as a value.
    /// </summary>
    public static IEnumerable<IGrouping<string, OfferField>> DetailGroups =>
        All.Where(item => item.Name != nameof(OfferTerms.Name)).GroupBy(item => item.Group);

    /// <summary>
    /// Contract and mileage values only belong to leasing or financing offers. For any other kind the
    /// group is shown only when a value was entered nevertheless.
    /// </summary>
    public static bool GroupApplies(string group, OfferTerms terms)
    {
        var kindUsesGroup = group switch
        {
            ContractGroup => terms.Kind is OfferKind.Leasing or OfferKind.Financing,
            MileageGroup => terms.Kind == OfferKind.Leasing,
            _ => true,
        };

        return kindUsesGroup
            || All.Any(field => field.Group == group && field.Raw(terms) is not null);
    }

    private static OfferField Text(
        string name,
        string group,
        string type,
        int maxLength,
        Func<OfferTerms, string?> value
    )
    {
        return new(name, group, type, maxLength, value, value);
    }

    private static OfferField Date(string name, string group, Func<OfferTerms, DateOnly?> value)
    {
        return new(
            name,
            group,
            DateType,
            0,
            terms => value(terms)?.ToString(DateFormat),
            terms => value(terms)
        );
    }

    private static OfferField Money(string name, string group, Func<OfferTerms, decimal?> value)
    {
        return new(
            name,
            group,
            MoneyType,
            0,
            terms => value(terms)?.ToString(MoneyFormat),
            terms => value(terms)
        );
    }

    private static OfferField Count(string name, string group, Func<OfferTerms, int?> value)
    {
        return new(
            name,
            group,
            IntegerType,
            0,
            terms => value(terms)?.ToString(QuantityFormat),
            terms => value(terms)
        );
    }

    private static OfferField Quantity(
        string name,
        string group,
        string type,
        Func<OfferTerms, decimal?> value
    )
    {
        return new(
            name,
            group,
            type,
            0,
            terms => value(terms)?.ToString(QuantityFormat),
            terms => value(terms)
        );
    }
}
