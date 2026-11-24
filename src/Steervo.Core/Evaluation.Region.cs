using System.Globalization;

namespace Steervo.Core;

public sealed partial class Evaluation
{
    // Older evaluations and imports retain an unknown country until explicitly completed.
    public string? Country { get; private set; }
    public bool HasRegionalSettings => Country is not null && Currency is not null;

    // Amounts already entered would silently change their meaning under another currency.
    public bool IsCurrencyFixed => Offers.Count > 0 || Costs.Count > 0 || Scenarios.Count > 0;

    public void SetCountry(string country) => Country = EvaluationCountries.Validate(country);

    public void SetProperties(string name, string? purpose, string? country, string? currency)
    {
        // Everything is validated first so that a rejected change leaves no property half-updated.
        var validName = Rules.Text(name);
        var validPurpose = Rules.OptionalText(purpose, MaximumPurposeLength);
        var validCountry = EvaluationCountries.Validate(country);
        var validCurrency = ValidateCurrency(currency);

        if (IsCurrencyFixed && Currency != validCurrency)
        {
            throw new RuleException("CurrencyFixed");
        }

        Name = validName;
        Purpose = validPurpose;
        Country = validCountry;
        Currency = validCurrency;
    }
}

/// <summary>The countries an evaluation can be set in: every region the runtime knows a culture for.</summary>
public static class EvaluationCountries
{
    private const int CountryCodeLength = 2;

    // One culture per two-letter region code, used to look up the region's display name.
    // Regions without an alphabetic ISO code, such as "001" for the world, are left out.
    private static readonly IReadOnlyDictionary<string, string> CulturesByCode = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Select(culture =>
            (Code: new RegionInfo(culture.Name).TwoLetterISORegionName, Culture: culture.Name)
        )
        .Where(region =>
            region.Code.Length == CountryCodeLength
            && region.Code.All(character => character is >= 'A' and <= 'Z')
        )
        .DistinctBy(region => region.Code)
        .ToDictionary(region => region.Code, region => region.Culture);

    public static IReadOnlyList<string> Codes { get; } =
        Array.AsReadOnly(CulturesByCode.Keys.Order().ToArray());

    public static string Validate(string? country)
    {
        var code = country?.Trim().ToUpperInvariant() ?? "";

        return Codes.Contains(code) ? code : throw new RuleException("CountryRequired");
    }

    public static string DisplayName(string code) =>
        new RegionInfo(CulturesByCode[code]).DisplayName;
}
