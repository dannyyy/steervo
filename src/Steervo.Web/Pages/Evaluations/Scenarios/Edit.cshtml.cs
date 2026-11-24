using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Application.Scenarios;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Scenarios;

/// <summary>
/// Creates a scenario from an offer or edits an existing one. Without a scenario and without a
/// chosen offer the page first asks which offer the scenario is based on.
/// </summary>
public sealed class EditModel(
    ScenarioWorkspace workspace,
    ICurrentUser current,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string MonthPeriod = "month";
    private const string YearPeriod = "year";

    private const string ForbiddenError = "Forbidden";
    private const string NotFoundError = "NotFound";
    private const string ConflictError = "Conflict";

    private const string HtmxRequestHeader = "HX-Request";
    private const string HtmxRedirectHeader = "HX-Redirect";

    public Evaluation Evaluation { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    [BindProperty(SupportsGet = true)]
    public Guid? LeftId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? RightId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Period { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// Where cancelling leads: back to the scenario in the view it was opened from, or to the list
    /// while the scenario does not exist yet.
    /// </summary>
    public string ReturnUrl =>
        Scenario is null
            ? $"/evaluations/{Evaluation.Id}/scenarios"
            : $"/evaluations/{Evaluation.Id}/scenarios/{Scenario.Id}?month={Month}&period={Period}&pageNumber={PageNumber}&leftId={LeftId}&rightId={RightId}#scenario-title";

    public Scenario? Scenario { get; private set; }

    public Offer? Offer { get; private set; }

    [BindProperty]
    public ScenarioForm Input { get; set; } = new();

    [BindProperty]
    public long Version { get; set; }

    public string? Notice { get; private set; }

    public string TitleLabel => Scenario is null ? "ScenarioAdd" : "ScenarioEdit";

    public string BackLinkLabel => Scenario is null ? "Calculations" : "ScenarioOpen";

    /// <summary>
    /// The offers a new scenario can be based on, ordered by vehicle and offer name.
    /// </summary>
    public IEnumerable<Offer> OfferOptions =>
        Evaluation
            .Offers.OrderBy(offer => Evaluation.FindCandidate(offer.CandidateId).DisplayName)
            .ThenBy(offer => offer.Latest.Terms.Name);

    public static IEnumerable<IGrouping<string, ScenarioField>> FieldGroups =>
        ScenarioForm.Fields.GroupBy(scenarioField => scenarioField.Group);

    public static IEnumerable<OfferRevision> NewestRevisionsFirst(Offer offer) =>
        offer.Revisions.OrderByDescending(revision => revision.Number);

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? scenarioId, Guid? offerId)
    {
        try
        {
            await LoadAsync(id, scenarioId);

            var planningMissing = Evaluation.CostPlanning is null || Evaluation.Currency is null;
            if (planningMissing)
            {
                return RedirectToPage("Index", new { id });
            }

            Version = Evaluation.Version;

            if (Scenario is not null)
            {
                Offer = Evaluation.FindOffer(Scenario.OfferId);
                Input = ScenarioForm.From(Scenario);
            }
            else if (offerId.HasValue)
            {
                Offer = Evaluation.FindOffer(offerId.Value);
                Input = ScenarioForm.FromOffer(Offer);
            }

            return Page();
        }
        catch (RuleException error)
        {
            return StatusCode(error.Message == ForbiddenError ? 403 : 404);
        }
    }

    public async Task<IActionResult> OnPostAsync(Guid id, Guid? scenarioId)
    {
        try
        {
            await LoadAsync(id, scenarioId);
            Offer = Evaluation.FindOffer(Input.OfferId);

            if (!ModelState.IsValid)
            {
                throw new RuleException("InvalidScenario");
            }

            if (Scenario is null)
            {
                scenarioId = await workspace.AddAsync(id, Version, Input.ToInput());
            }
            else
            {
                await workspace.EditAsync(id, Version, Scenario.Id, Input.ToInput());
            }

            return RedirectTo(DetailsUrl(id, scenarioId));
        }
        catch (RuleException error)
        {
            return Failure(error);
        }
    }

    private string DetailsUrl(Guid id, Guid? scenarioId) =>
        Url.Page(
            "Details",
            null,
            new
            {
                id,
                scenarioId,
                month = Month,
                period = Period,
                pageNumber = PageNumber,
                leftId = LeftId,
                rightId = RightId,
            },
            null,
            null,
            "scenario-title"
        )!;

    /// <summary>
    /// HTMX follows the redirect itself only when it is announced in a response header.
    /// </summary>
    private IActionResult RedirectTo(string url)
    {
        if (Request.Headers.ContainsKey(HtmxRequestHeader))
        {
            Response.Headers[HtmxRedirectHeader] = url;

            return new EmptyResult();
        }

        return Redirect(url);
    }

    private IActionResult Failure(RuleException error)
    {
        if (error.Message is ForbiddenError or NotFoundError)
        {
            return StatusCode(error.Message == ForbiddenError ? 403 : 404);
        }

        Notice = text[error.Message];
        Response.StatusCode = error.Message == ConflictError ? 409 : 422;

        if (Request.Headers.ContainsKey(HtmxRequestHeader))
        {
            return new JsonResult(
                new
                {
                    error = Notice,
                    version = Evaluation.Version,
                    reviewUrl = ReturnUrl,
                    context = Scenario?.Name ?? Evaluation.Name,
                }
            );
        }

        return Page();
    }

    private async Task LoadAsync(Guid id, Guid? scenarioId)
    {
        Evaluation = await workspace.ReadAsync(id);
        Evaluation.Require(current.Id, AccessLevel.Editor);

        // The view the editor was opened from travels along in the query string, so saving or
        // cancelling can return to it. Values that are no longer valid fall back to the defaults.
        LeftId = ScenarioExists(LeftId) ? LeftId : scenarioId;
        RightId = ScenarioExists(RightId) ? RightId : null;
        Period = Period == YearPeriod ? YearPeriod : MonthPeriod;
        PageNumber = Math.Max(1, PageNumber);

        if (Evaluation.CostPlanning is { } planning)
        {
            Month = Math.Clamp(Month ?? planning.Months, 1, planning.Months);
        }

        Scenario = scenarioId.HasValue ? Evaluation.FindScenario(scenarioId.Value) : null;
    }

    private bool ScenarioExists(Guid? scenarioId) =>
        Evaluation.Scenarios.Any(scenario => scenario.Id == scenarioId);
}
