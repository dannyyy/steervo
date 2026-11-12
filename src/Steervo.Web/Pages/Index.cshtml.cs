using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Core;
using Steervo.Application.Documents;
namespace Steervo.Web.Pages;

[RequestSizeLimit(EvaluationArchive.MaximumBytes + 100_000)]
[RequestFormLimits(MultipartBodyLengthLimit = EvaluationArchive.MaximumBytes)]
public sealed class IndexModel(EvaluationWorkspace workspace, EvaluationArchive transfer, IStringLocalizer<Ui> text) : PageModel
{
    public IReadOnlyList<Evaluation> Evaluations { get; private set; } = [];
    public bool CreationFailed { get; private set; }
    public bool ImportFailed { get; private set; }
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Purpose { get; set; }
    public async Task OnGetAsync() => Evaluations = await workspace.ListAsync();
    public async Task<IActionResult> OnPostAsync()
    {
        try { var id = await workspace.CreateAsync(Name, Purpose); return RedirectToPage("/Evaluations/Details", new { id }); }
        catch (RuleException error) { CreationFailed = true; ModelState.AddModelError("", text[error.Message]); await OnGetAsync(); return Page(); }
    }
    public async Task<IActionResult> OnPostImportAsync(IFormFile? upload)
    {
        try
        {
            if (upload is null || upload.Length > EvaluationArchive.MaximumBytes || !ModelState.IsValid) throw new RuleException("InvalidImport");
            await using var input = upload.OpenReadStream();
            var id = await transfer.ImportAsync(input, HttpContext.RequestAborted);
            return RedirectToPage("/Evaluations/Details", new { id });
        }
        catch (RuleException error) { ImportFailed = true; ModelState.AddModelError("", text[error.Message]); await OnGetAsync(); return Page(); }
    }
}
