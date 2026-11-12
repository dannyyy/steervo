using Steervo.Core;
namespace Steervo.Web.Pages.Evaluations.Candidates;

public sealed class CandidateForm
{
    public string Model { get; set; } = "";
    public string? Brand { get; set; }
    public string? Variant { get; set; }
    public CandidateCondition Condition { get; set; }
    public int? Year { get; set; }
    public int? Mileage { get; set; }
    public string? Notes { get; set; }
    public string? Links { get; set; }
    public SpecificationsForm Specifications { get; set; } = new();
    public CandidateInput ToInput() => new(Model, Brand, Variant, Condition, Year, Mileage, Notes, Links, Specifications.ToInput());
    public static CandidateForm From(Candidate candidate) => new()
    {
        Model = candidate.Model, Brand = candidate.Brand, Variant = candidate.Variant, Condition = candidate.Condition,
        Year = candidate.Year, Mileage = candidate.Mileage, Notes = candidate.Notes, Links = candidate.Links,
        Specifications = SpecificationsForm.From(candidate.Specifications)
    };
}
