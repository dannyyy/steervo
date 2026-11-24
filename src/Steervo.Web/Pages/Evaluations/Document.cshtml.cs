using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Documents;
using Steervo.Core;

namespace Steervo.Web.Pages.Evaluations;

/// <summary>Delivers one stored document of an evaluation as a download.</summary>
public sealed class DocumentModel(DocumentWorkspace workspace, IStringLocalizer<Ui> text)
    : PageModel
{
    private const string ForbiddenError = "Forbidden";
    private const string NotFoundError = "NotFound";

    public async Task<IActionResult> OnGetAsync(Guid id, Guid documentId, int number)
    {
        try
        {
            var result = await workspace.DownloadAsync(
                id,
                documentId,
                number,
                HttpContext.RequestAborted
            );

            return File(result.Content, "application/octet-stream", result.FileName);
        }
        catch (RuleException error)
        {
            if (error.Message is ForbiddenError or NotFoundError)
            {
                return StatusCode(error.Message == ForbiddenError ? 403 : 404);
            }

            // Any other failure means the stored file could not be delivered right now.
            return new ContentResult
            {
                StatusCode = 503,
                ContentType = "text/plain; charset=utf-8",
                Content = text[error.Message],
            };
        }
    }
}
