using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Documents;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations;

public sealed class DocumentModel(DocumentWorkspace workspace, IStringLocalizer<Ui> text) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, Guid documentId, int number)
    {
        try
        {
            var result = await workspace.DownloadAsync(id, documentId, number, HttpContext.RequestAborted);
            return File(result.Content, "application/octet-stream", result.FileName);
        }
        catch (RuleException error)
        {
            if (error.Message is "Forbidden" or "NotFound") return StatusCode(error.Message == "Forbidden" ? 403 : 404);
            return new ContentResult { StatusCode = 503, ContentType = "text/plain; charset=utf-8", Content = text[error.Message] };
        }
    }
}
