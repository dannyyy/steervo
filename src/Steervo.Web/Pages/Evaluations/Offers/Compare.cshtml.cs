using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Offers;
using Steervo.Core;
using Steervo.Web.Pages.Evaluations.Candidates;

namespace Steervo.Web.Pages.Evaluations.Offers;

/// <summary>Shows up to <see cref="ComparisonSelection.Maximum"/> offers of an evaluation side by side.</summary>
public sealed class CompareModel(OfferWorkspace workspace, ICurrentUser current) : PageModel
{
    // An empty "second" value in the query means the reader removed the second offer on purpose.
    private const string SecondSlot = "second";
    private const string ForbiddenCode = "Forbidden";

    public Evaluation Evaluation { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public IReadOnlyList<Offer> Selected { get; private set; } = [];

    public Offer? First => Selected.ElementAtOrDefault(0);

    public Offer? Second => Selected.ElementAtOrDefault(1);

    public IReadOnlyList<Guid> More => Selected.Skip(2).Select(offer => offer.Id).ToList();

    public IReadOnlyList<Offer> Available { get; private set; } = [];

    /// <summary>True when more offers were requested than fit; the surplus was dropped.</summary>
    public bool Limited { get; private set; }

    public string Query => ComparisonSelection.Query(Selected.Select(offer => offer.Id));

    /// <summary>The requirements that are answered per offer, in their configured order.</summary>
    public IReadOnlyList<Criterion> OfferCriteria =>
        Evaluation
            .Criteria.Where(criterion => criterion.Scope == CriterionScope.Offer)
            .OrderBy(criterion => criterion.Position)
            .ToList();

    public string QueryWithout(Offer removed)
    {
        var remaining = Selected.Where(offer => offer != removed).Select(offer => offer.Id);

        return ComparisonSelection.Query(remaining);
    }

    public string CandidateName(Offer offer)
    {
        return Evaluation.FindCandidate(offer.CandidateId).DisplayName;
    }

    /// <summary>Names an offer together with its car, for lists that mix the offers of several cars.</summary>
    public string OfferLabel(Offer offer)
    {
        return CandidateName(offer) + " · " + offer.Latest.Terms.Name;
    }

    /// <summary>
    /// Contract and mileage rows are left out when they apply to none of the compared offers; every
    /// other group is always shown.
    /// </summary>
    public bool ShowsGroup(string group)
    {
        var dependsOnOfferKind = group is OfferFields.ContractGroup or OfferFields.MileageGroup;

        return !dependsOnOfferKind
            || Selected.Any(offer => OfferFields.GroupApplies(group, offer.Latest.Terms));
    }

    /// <summary>
    /// The total is always shown. Any other price row needs at least one offer with an amount; only
    /// for the vehicle price a zero counts as an amount.
    /// </summary>
    public bool ShowsPriceRow(string key)
    {
        if (key == OfferPrices.Total)
        {
            return true;
        }

        var zeroCounts = key == OfferPrices.Vehicle;

        return Selected.Any(offer =>
            OfferPrices.Value(offer.Latest, key) is { } amount && (amount != 0 || zeroCounts)
        );
    }

    /// <summary>
    /// The compared offers may be leases and loans alike, so the financed amount has a neutral label.
    /// </summary>
    public string PriceLabel(string key)
    {
        return key == OfferPrices.Financed ? "OfferPriceFinancedAny" : "OfferPrice" + key;
    }

    /// <summary>A missing total is still open; any other missing amount simply does not apply.</summary>
    public string PriceText(string key, decimal? amount, IStringLocalizer text)
    {
        if (amount is not null)
        {
            return OfferPrices.Format(amount.Value);
        }

        return key == OfferPrices.Total ? text["OfferOpen"].Value : "–";
    }

    /// <summary>The share of the list price, which is shown next to a concession amount only.</summary>
    public decimal? ConcessionPercent(Offer offer, string key, decimal? amount)
    {
        if (key != OfferPrices.Concession || amount is null)
        {
            return null;
        }

        return offer.Latest.Pricing.ConcessionPercent;
    }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? first, Guid? second, Guid[]? more)
    {
        try
        {
            Evaluation = await workspace.ReadAsync(id);

            var ordered = Evaluation.Offers.OrderBy(offer => offer.Position).ToList();
            var further = (more ?? [])
                .Where(item => Evaluation.Offers.Any(offer => offer.Id == item))
                .ToList();

            var nothingRequested =
                !first.HasValue
                && !second.HasValue
                && further.Count == 0
                && !Request.Query.ContainsKey(SecondSlot);
            var chosen = nothingRequested
                ? ordered.Take(ComparisonSelection.Maximum).ToList()
                : ChooseRequested(ordered, first, second, further);

            Limited = chosen.Count > ComparisonSelection.Maximum;
            Selected = chosen.Take(ComparisonSelection.Maximum).ToList();
            Available = ordered.Where(offer => !Selected.Contains(offer)).ToList();

            return Page();
        }
        catch (RuleException error)
        {
            return StatusCode(
                error.Message == ForbiddenCode
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status404NotFound
            );
        }
    }

    private List<Offer> ChooseRequested(
        List<Offer> ordered,
        Guid? first,
        Guid? second,
        List<Guid> further
    )
    {
        var chosen = new List<Offer>();

        if (first.HasValue)
        {
            chosen.Add(Evaluation.FindOffer(first.Value));
        }

        if (second.HasValue)
        {
            chosen.Add(Evaluation.FindOffer(second.Value));
        }

        chosen.AddRange(further.Select(Evaluation.FindOffer));
        chosen = chosen.Distinct().ToList();

        if (chosen.Count == 0 && ordered.Count > 0)
        {
            chosen.Add(ordered[0]);
        }

        // A link that names only the first offer keeps opening it next to another offer for the same car.
        var onlyFirstNamed =
            chosen.Count == 1 && first.HasValue && !Request.Query.ContainsKey(SecondSlot);
        if (onlyFirstNamed)
        {
            var only = chosen[0];
            var sibling = ordered.FirstOrDefault(offer =>
                offer != only && offer.CandidateId == only.CandidateId
            );
            if (sibling is not null)
            {
                chosen.Add(sibling);
            }
        }

        return chosen;
    }
}
