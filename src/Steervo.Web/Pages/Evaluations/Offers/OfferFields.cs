using Steervo.Core;
namespace Steervo.Web.Pages.Evaluations.Offers;

public sealed record OfferField(string Name, string Group, string Type, int MaxLength, Func<OfferTerms, string?> Display, Func<OfferTerms, object?> Raw)
{
    public string Label => "Offer" + Name;
}
public static class OfferFields
{
    public static readonly OfferField[] All =
    [
        new(nameof(OfferTerms.Name), "Header", "text", 160, terms => terms.Name, terms => terms.Name),
        new(nameof(OfferTerms.Provider), "Header", "text", 200, terms => terms.Provider, terms => terms.Provider),
        new(nameof(OfferTerms.Lender), "Header", "text", 200, terms => terms.Lender, terms => terms.Lender),
        new(nameof(OfferTerms.VehicleDescription), "Header", "text", 400, terms => terms.VehicleDescription, terms => terms.VehicleDescription),
        new(nameof(OfferTerms.IssuedOn), "Delivery", "date", 0, terms => terms.IssuedOn?.ToString("dd.MM.yyyy"), terms => terms.IssuedOn),
        new(nameof(OfferTerms.ValidUntil), "Delivery", "date", 0, terms => terms.ValidUntil?.ToString("dd.MM.yyyy"), terms => terms.ValidUntil),
        new(nameof(OfferTerms.DeliveryOn), "Delivery", "date", 0, terms => terms.DeliveryOn?.ToString("dd.MM.yyyy"), terms => terms.DeliveryOn),
        new(nameof(OfferTerms.DeliveryNotes), "Delivery", "textarea", 1000, terms => terms.DeliveryNotes, terms => terms.DeliveryNotes),
        new(nameof(OfferTerms.BasePrice), "Price", "money", 0, terms => terms.BasePrice?.ToString("N2"), terms => terms.BasePrice),
        new(nameof(OfferTerms.TermMonths), "Contract", "integer", 0, terms => terms.TermMonths?.ToString("0.####"), terms => terms.TermMonths),
        new(nameof(OfferTerms.AnnualInterestPercent), "Contract", "number", 0, terms => terms.AnnualInterestPercent?.ToString("0.####"), terms => terms.AnnualInterestPercent),
        new(nameof(OfferTerms.MonthlyPayment), "Contract", "money", 0, terms => terms.MonthlyPayment?.ToString("N2"), terms => terms.MonthlyPayment),
        new(nameof(OfferTerms.DownPayment), "Contract", "money", 0, terms => terms.DownPayment?.ToString("N2"), terms => terms.DownPayment),
        new(nameof(OfferTerms.ResidualPayment), "Contract", "money", 0, terms => terms.ResidualPayment?.ToString("N2"), terms => terms.ResidualPayment),
        new(nameof(OfferTerms.IncludedKilometresPerYear), "Mileage", "integer", 0, terms => terms.IncludedKilometresPerYear?.ToString("0.####"), terms => terms.IncludedKilometresPerYear),
        new(nameof(OfferTerms.ExcessKilometrePrice), "Mileage", "unitprice", 0, terms => terms.ExcessKilometrePrice?.ToString("0.####"), terms => terms.ExcessKilometrePrice),
        new(nameof(OfferTerms.UnusedKilometreCredit), "Mileage", "unitprice", 0, terms => terms.UnusedKilometreCredit?.ToString("0.####"), terms => terms.UnusedKilometreCredit),
        new(nameof(OfferTerms.Notes), "Notes", "textarea", 4000, terms => terms.Notes, terms => terms.Notes),
    ];
}
