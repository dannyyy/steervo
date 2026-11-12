using Steervo.Core;
namespace Steervo.Web.Pages.Evaluations;
public sealed record EvaluationHeader(Evaluation Evaluation, AccessLevel Access, string Active);
