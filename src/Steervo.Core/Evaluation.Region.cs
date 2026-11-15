using System.Globalization;

namespace Steervo.Core;

public sealed partial class Evaluation
{
    // Older evaluations and imports retain an unknown country until explicitly completed.
    public string? Country { get; private set; }
    public bool HasRegionalSettings => Country is not null && Currency is not null;
    public bool IsCurrencyFixed => Offers.Count > 0 || Costs.Count > 0 || Scenarios.Count > 0;

    public void SetCountry(string country) => Country = EvaluationCountries.Validate(country);

    public void SetProperties(string name, string? purpose, string? country, string? currency)
    {
        var validName = Rules.Text(name);
        var validPurpose = Rules.OptionalText(purpose, 2000);
        var validCountry = EvaluationCountries.Validate(country);
        var validCurrency = ValidateCurrency(currency);
        if (IsCurrencyFixed && Currency != validCurrency) throw new RuleException("CurrencyFixed");
        Name = validName; Purpose = validPurpose; Country = validCountry; Currency = validCurrency;
    }
}

public static class EvaluationCountries
{
    private static readonly IReadOnlyDictionary<string, string> CulturesByCode = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => (Code: new RegionInfo(culture.Name).TwoLetterISORegionName, Culture: culture.Name))
        .Where(region => region.Code.Length == 2 && region.Code.All(character => character is >= 'A' and <= 'Z'))
        .DistinctBy(region => region.Code).ToDictionary(region => region.Code, region => region.Culture);
    public static IReadOnlyList<string> Codes { get; } = Array.AsReadOnly(CulturesByCode.Keys.Order().ToArray());

    public static string Validate(string? country)
    {
        var code = country?.Trim().ToUpperInvariant() ?? "";
        return Codes.Contains(code) ? code : throw new RuleException("CountryRequired");
    }
    public static string DisplayName(string code) => new RegionInfo(CulturesByCode[code]).DisplayName;
}
