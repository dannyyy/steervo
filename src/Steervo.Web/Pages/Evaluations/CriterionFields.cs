using Steervo.Core;
namespace Steervo.Web.Pages.Evaluations;
public sealed record CriterionFields(IReadOnlyList<CriterionGroup> Groups, Criterion? Criterion, string Prefix);
