using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Costs;
using Steervo.Application.Evaluations;
using Steervo.Calculations;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations.Costs;

/// <summary>The costs of one timeline row: a single planning month or a calendar year.</summary>
public sealed record CostPeriod(int From, int Through, CostProjection Projection);

/// <summary>
/// The cost planning of an evaluation: the planning period, the recorded cost items and their
/// projection over time, for all cars together or for one of them.
/// </summary>
public sealed class IndexModel(
    CostWorkspace workspace,
    ICurrentUser current,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string MonthPeriod = "month";
    private const string YearPeriod = "year";
    private const int PeriodsPerPage = 24;

    private const string PlanAction = "plan";
    private const string RemoveCostAction = "remove-cost";

    private const string ForbiddenError = "Forbidden";
    private const string NotFoundError = "NotFound";
    private const string ConflictError = "Conflict";

    private const string HtmxRequestHeader = "HX-Request";
    private const string HtmxRedirectHeader = "HX-Redirect";

    private const string MonthFormat = "MM.yyyy";
    private const string YearFormat = "yyyy";

    public Evaluation Evaluation { get; private set; } = null!;

    public AccessLevel Access => Evaluation.AccessFor(current.Id)!.Value;

    public bool CanEdit => Access >= AccessLevel.Editor;

    /// <summary>The car whose costs are shown; null shows the costs common to all cars.</summary>
    public Guid? CandidateId { get; private set; }

    public string Period { get; private set; } = MonthPeriod;

    public int PageNumber { get; private set; } = 1;

    public int PageCount { get; private set; }

    public CostProjection? Projection { get; private set; }

    public IReadOnlyList<CostPeriod> Periods { get; private set; } = [];

    public string? Notice { get; private set; }

    public string? FailedAction { get; private set; }

    /// <summary>The address of this page with the current car, period and timeline page.</summary>
    public string ContextUrl =>
        $"/evaluations/{Evaluation.Id}/costs?candidateId={CandidateId}&period={Period}&pageNumber={PageNumber}";

    [BindProperty]
    public CostPlanningForm Plan { get; set; } = new();

    [BindProperty]
    public long Version { get; set; }

    /// <summary>Costs can be recorded once the planning period and the currency are known.</summary>
    public bool CanAddCosts =>
        CanEdit && Evaluation.CostPlanning is not null && Evaluation.Currency is not null;

    /// <summary>The planning editor is open until a period exists and after a failed save.</summary>
    public bool IsPlanEditorOpen => Evaluation.CostPlanning is null || FailedAction == PlanAction;

    public string PlanEditorExpanded => IsPlanEditorOpen.ToString().ToLowerInvariant();

    /// <summary>The notice of a failed planning save is shown inside the planning editor.</summary>
    public bool ShowsPageNotice => Notice is not null && FailedAction != PlanAction;

    public string PlanStartLabel => Planning.Start.ToString(MonthFormat);

    public string PlanEndLabel => Planning.DateFor(Planning.Months).ToString(MonthFormat);

    public IEnumerable<Candidate> CandidatesInOrder =>
        Evaluation.Candidates.OrderBy(candidate => candidate.Position);

    public string ScopeName
    {
        get
        {
            if (CandidateId is { } candidateId)
            {
                return Evaluation.FindCandidate(candidateId).DisplayName;
            }

            return text["CostCommon"].Value;
        }
    }

    private CostPlanning Planning => Evaluation.CostPlanning!;

    public CostTotals TotalsFor(Candidate candidate)
    {
        return CostProjection.Calculate(Evaluation, candidate.Id).Totals;
    }

    public decimal AverageNetPerMonth(CostProjection projection)
    {
        return projection.Totals.Net / Planning.Months;
    }

    /// <summary>Common costs first, then by first month and name.</summary>
    public IEnumerable<ProjectedCost> ItemsInOrder(CostProjection projection)
    {
        return projection
            .Items.OrderBy(row => row.Item.CandidateId.HasValue)
            .ThenBy(row => row.Item.StartMonth)
            .ThenBy(row => row.Item.Name);
    }

    public ProjectedCost[] ActiveItems(CostPeriod period)
    {
        return period.Projection.Items.Where(row => row.Active).ToArray();
    }

    public string MonthLabel(int month)
    {
        return Planning.DateFor(month).ToString(MonthFormat);
    }

    public string PeriodLabel(CostPeriod period)
    {
        var format = Period == YearPeriod ? YearFormat : MonthFormat;

        return Planning.DateFor(period.From).ToString(format);
    }

    /// <summary>The amount of a single payment as entered, or why there is none.</summary>
    public string AmountLabel(CostItem item)
    {
        if (item.Frequency == CostFrequency.Included)
        {
            return text["CostFrequencyIncluded"].Value;
        }

        return MoneyOrOpen(item.Amount);
    }

    public string TotalLabel(ProjectedCost row)
    {
        return MoneyOrOpen(row.Total);
    }

    /// <summary>The share of a cost item in one timeline row.</summary>
    public string TimelineLabel(ProjectedCost row)
    {
        if (row.Item.Frequency == CostFrequency.Included)
        {
            return text["CostFrequencyIncluded"].Value;
        }

        return MoneyOrOpen(row.Total);
    }

    public async Task<IActionResult> OnGetAsync(
        Guid id,
        Guid? candidateId,
        string? period,
        int pageNumber = 1
    )
    {
        try
        {
            await LoadAsync(id, candidateId, period, pageNumber);

            Version = Evaluation.Version;
            Plan = CostPlanningForm.From(Evaluation.CostPlanning);

            return Page();
        }
        catch (RuleException error)
        {
            return StatusCode(AccessFailureStatus(error));
        }
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        Guid? candidateId,
        string? period,
        string action,
        Guid costId,
        int pageNumber = 1
    )
    {
        try
        {
            await LoadAsync(id, candidateId, period, pageNumber);
            Evaluation.Require(current.Id, AccessLevel.Editor);

            if (!ModelState.IsValid)
            {
                throw new RuleException("InvalidCostPlanning");
            }

            switch (action)
            {
                case PlanAction:
                    await workspace.PlanAsync(id, Version, Plan.ParseStart(), Plan.ParseMonths());
                    break;

                case RemoveCostAction:
                    await workspace.RemoveAsync(id, Version, costId);
                    break;

                default:
                    throw new RuleException("InvalidValue");
            }

            // Return to the part of the page the change belongs to.
            var fragment = action == RemoveCostAction ? "#cost-items" : "#cost-plan-title";
            var url = ContextUrl + fragment;

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

            FailedAction = action;
            Notice = text[error.Message];
            Response.StatusCode = error.Message == ConflictError ? 409 : 422;

            if (IsHtmxRequest())
            {
                return new JsonResult(
                    new
                    {
                        error = Notice,
                        version = Evaluation.Version,
                        reviewUrl = ContextUrl,
                        context = FailureContext(action, costId),
                    }
                );
            }

            // A failed planning save keeps what was typed; any other failure shows the stored plan.
            if (action != PlanAction)
            {
                Plan = CostPlanningForm.From(Evaluation.CostPlanning);
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

    private string MoneyOrOpen(decimal? amount)
    {
        if (amount.HasValue)
        {
            return $"{amount:N2} {Evaluation.Currency}";
        }

        return text["OfferOpen"].Value;
    }

    /// <summary>Names what a failed action was about: the cost item or the planning period.</summary>
    private string FailureContext(string action, Guid costId)
    {
        if (action == RemoveCostAction)
        {
            var cost = Evaluation.Costs.FirstOrDefault(item => item.Id == costId);

            return cost?.Name ?? Evaluation.Name;
        }

        if (Evaluation.CostPlanning is { } planning)
        {
            return $"{planning.Start:MM.yyyy} · {text["CostMonthCount", planning.Months]}";
        }

        return Evaluation.Name;
    }

    private async Task LoadAsync(Guid id, Guid? candidateId, string? period, int pageNumber)
    {
        Evaluation = await workspace.ReadAsync(id);
        CandidateId = candidateId;
        Period = period == YearPeriod ? YearPeriod : MonthPeriod;

        if (candidateId.HasValue)
        {
            // Fails for a car that does not belong to this evaluation.
            _ = Evaluation.FindCandidate(candidateId.Value);
        }

        if (Evaluation.CostPlanning is not { } planning)
        {
            return;
        }

        Projection = CostProjection.Calculate(Evaluation, candidateId);

        var ranges = Period == YearPeriod ? CalendarYears(planning) : SingleMonths(planning);
        PageCount = (ranges.Length + PeriodsPerPage - 1) / PeriodsPerPage;
        PageNumber = Math.Clamp(pageNumber, 1, PageCount);

        Periods = ranges
            .Skip((PageNumber - 1) * PeriodsPerPage)
            .Take(PeriodsPerPage)
            .Select(range => new CostPeriod(
                range.From,
                range.Through,
                CostProjection.Calculate(Evaluation, candidateId, range.From, range.Through)
            ))
            .ToArray();
    }

    private static (int From, int Through)[] SingleMonths(CostPlanning planning)
    {
        return Enumerable
            .Range(1, planning.Months)
            .Select(month => (From: month, Through: month))
            .ToArray();
    }

    /// <summary>The planning months grouped by the calendar year they fall into.</summary>
    private static (int From, int Through)[] CalendarYears(CostPlanning planning)
    {
        return Enumerable
            .Range(1, planning.Months)
            .GroupBy(month => planning.DateFor(month).Year)
            .Select(year => (From: year.Min(), Through: year.Max()))
            .ToArray();
    }
}
