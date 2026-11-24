using Steervo.Core;
using Steervo.Web.Pages.Evaluations.Candidates;

namespace Steervo.Web.Pages.Evaluations;

/// <summary>
/// The heading and navigation shown on every page of an evaluation. <paramref name="Active"/>
/// names the current page; the candidate and comparison selection are carried along so that
/// switching between the areas keeps them.
/// </summary>
public sealed record EvaluationHeader(
    Evaluation Evaluation,
    AccessLevel Access,
    string Active,
    Guid? CandidateId = null,
    Guid? First = null,
    Guid? Second = null,
    IReadOnlyList<Guid>? More = null
)
{
    /// <summary>The workflow step the current page belongs to.</summary>
    public string Group =>
        Active switch
        {
            "criteria" or "settings" => "setup",
            "candidates" => "cars",
            "offers" or "costs" => "offers",
            "comparison" or "scenarios" => "results",
            _ => "management",
        };

    /// <summary>Only the steps with more than one page show a second navigation level.</summary>
    public bool HasSections => Group is not ("cars" or "management");

    public string CandidatesUrl
    {
        get
        {
            if (!CandidateId.HasValue)
            {
                return $"/evaluations/{Evaluation.Id}/candidates";
            }

            return $"/evaluations/{Evaluation.Id}/candidates/{CandidateId}?first={First}&second={Second}{MoreQuery}";
        }
    }

    public string ComparisonUrl
    {
        get
        {
            if (!First.HasValue)
            {
                return $"/evaluations/{Evaluation.Id}/compare";
            }

            return $"/evaluations/{Evaluation.Id}/compare?first={First}&second={Second}{MoreQuery}";
        }
    }

    private string MoreQuery => ComparisonSelection.More(More);

    /// <summary>The CSS class of a workflow step link: set for the current step only.</summary>
    public string? StepClass(string group) => Group == group ? "active" : null;

    /// <summary>The <c>aria-current</c> value of a workflow step link.</summary>
    public string? StepCurrent(string group) => Group == group ? "true" : null;

    /// <summary>The <c>aria-current</c> value of a link to a page within the step.</summary>
    public string? PageCurrent(string page) => Active == page ? "page" : null;
}
