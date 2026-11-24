using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Costs;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Costs;

/// <summary>
/// Adds a cost item to an evaluation or edits an existing one, and afterwards returns to the
/// place in the cost planning the user came from.
/// </summary>
public sealed class EditModel(
    CostWorkspace workspace,
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

    // Period, page and scope describe the view of the cost planning to return to.
    [BindProperty(SupportsGet = true)]
    public string? Period { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public Guid? Scope { get; set; }

    /// <summary>
    /// The cost planning as it was shown before, scrolled to the edited item, or to the list
    /// when a new item is being added.
    /// </summary>
    public string ReturnUrl
    {
        get
        {
            var candidateId = Scope ?? Cost?.CandidateId ?? Input.CandidateId;
            var fragment = Cost is null ? "cost-items" : "cost-" + Cost.Id;

            return $"/evaluations/{Evaluation.Id}/costs?candidateId={candidateId}&period={Period}&pageNumber={PageNumber}#{fragment}";
        }
    }

    /// <summary>The cost item being edited; null while a new one is added.</summary>
    public CostItem? Cost { get; private set; }

    [BindProperty]
    public CostForm Input { get; set; } = new();

    [BindProperty]
    public long Version { get; set; }

    public string? Notice { get; private set; }

    public IEnumerable<Candidate> CandidatesInOrder =>
        Evaluation.Candidates.OrderBy(candidate => candidate.Position);

    public async Task<IActionResult> OnGetAsync(Guid id, Guid? costId, Guid? candidateId)
    {
        try
        {
            await LoadAsync(id, costId);

            // Costs need a planning period and a currency; both are set up on the overview.
            if (Evaluation.CostPlanning is null || Evaluation.Currency is null)
            {
                return RedirectToPage("Index", new { id });
            }

            Version = Evaluation.Version;

            if (Cost is not null)
            {
                Input = CostForm.From(Cost);
            }
            else if (candidateId.HasValue)
            {
                // A new item starts out assigned to the car it was added from.
                _ = Evaluation.FindCandidate(candidateId.Value);
                Input.CandidateId = candidateId;
            }

            return Page();
        }
        catch (RuleException error)
        {
            return StatusCode(AccessFailureStatus(error));
        }
    }

    public async Task<IActionResult> OnPostAsync(Guid id, Guid? costId)
    {
        try
        {
            await LoadAsync(id, costId);

            if (!ModelState.IsValid)
            {
                throw new RuleException("InvalidCost");
            }

            if (Cost is null)
            {
                costId = await workspace.AddAsync(id, Version, Input.ToInput());
            }
            else
            {
                await workspace.EditAsync(id, Version, Cost.Id, Input.ToInput());
            }

            var url = SavedCostUrl(id, costId);

            if (IsHtmxRequest())
            {
                Response.Headers[HtmxRedirectHeader] = url;

                return new EmptyResult();
            }

            return Redirect(url);
        }
        catch (RuleException error)
        {
            if (error.Message is ForbiddenError or NotFoundError)
            {
                return StatusCode(AccessFailureStatus(error));
            }

            Notice = text[error.Message];
            Response.StatusCode = error.Message == ConflictError ? 409 : 422;

            if (IsHtmxRequest())
            {
                return new JsonResult(
                    new
                    {
                        error = Notice,
                        version = Evaluation.Version,
                        reviewUrl = ReturnUrl,
                        context = Cost?.Name ?? Evaluation.Name,
                    }
                );
            }

            return Page();
        }
    }

    private static int AccessFailureStatus(RuleException error)
    {
        return error.Message == ForbiddenError ? 403 : 404;
    }

    private bool IsHtmxRequest()
    {
        return Request.Headers.ContainsKey(HtmxRequestHeader);
    }

    /// <summary>
    /// The cost planning scrolled to the saved item, in the scope the item now belongs to.
    /// </summary>
    private string SavedCostUrl(Guid id, Guid? costId)
    {
        var route = new
        {
            id,
            candidateId = Input.CandidateId ?? Scope,
            period = Period,
            pageNumber = PageNumber,
        };

        return Url.Page(
            "Index",
            pageHandler: null,
            values: route,
            protocol: null,
            host: null,
            fragment: "cost-" + costId
        )!;
    }

    private async Task LoadAsync(Guid id, Guid? costId)
    {
        Evaluation = await workspace.ReadAsync(id);
        Evaluation.Require(current.Id, AccessLevel.Editor);

        Period = Period == YearPeriod ? YearPeriod : MonthPeriod;
        PageNumber = Math.Max(1, PageNumber);

        if (Scope.HasValue)
        {
            // Fails for a car that does not belong to this evaluation.
            _ = Evaluation.FindCandidate(Scope.Value);
        }

        Cost = costId.HasValue ? Evaluation.FindCost(costId.Value) : null;
    }
}
