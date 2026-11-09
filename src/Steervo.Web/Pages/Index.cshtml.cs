using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Evaluations;
using Steervo.Core;
namespace Steervo.Web.Pages;

[RequestSizeLimit(300_000)]
public sealed class IndexModel(EvaluationWorkspace workspace, EvaluationTransfer transfer, IStringLocalizer<Ui> text) : PageModel
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
            if (upload is null || upload.Length > EvaluationTransfer.MaximumBytes) throw new RuleException("InvalidImport");
            using var buffer = new MemoryStream();
            await upload.CopyToAsync(buffer);
            var id = await transfer.ImportAsync(buffer.ToArray());
            return RedirectToPage("/Evaluations/Details", new { id });
        }
        catch (RuleException error) { ImportFailed = true; ModelState.AddModelError("", text[error.Message]); await OnGetAsync(); return Page(); }
    }
}
