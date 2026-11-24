namespace Steervo.Web.Pages.Shared;

/// <summary>
/// The country and currency fields of an evaluation. The prefix keeps the element ids unique when
/// the fields appear in more than one editor; a fixed currency is shown but cannot be changed.
/// </summary>
public sealed record EvaluationRegionFields(
    string Prefix,
    string? Country,
    string? Currency,
    bool CurrencyFixed = false
);
