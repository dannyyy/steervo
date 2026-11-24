using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Steervo.Application.Documents;
using Steervo.Application.Evaluations;
using Steervo.Core;

namespace Steervo.Web.Pages;

/// <summary>The overview of all evaluations; creates a new one or imports an exported one.</summary>
[RequestSizeLimit(EvaluationArchive.MaximumBytes + 100_000)]
[RequestFormLimits(MultipartBodyLengthLimit = EvaluationArchive.MaximumBytes)]
public sealed class IndexModel(
    EvaluationWorkspace workspace,
    EvaluationArchive transfer,
    IStringLocalizer<Ui> text
) : PageModel
{
    private const string HtmxRequestHeader = "HX-Request";
    private const string DetailsPage = "/Evaluations/Details";

    public IReadOnlyList<Evaluation> Evaluations { get; private set; } = [];

    public bool CreationFailed { get; private set; }

    public bool ImportFailed { get; private set; }

    [BindProperty]
    public string Name { get; set; } = "";

    [BindProperty]
    public string? Purpose { get; set; }

    [BindProperty]
    public string? Country { get; set; }

    [BindProperty]
    public string? Currency { get; set; }

    /// <summary>
    /// The rejected input of the creation form as JSON, so that the form can be filled in again;
    /// null when there is nothing to restore.
    /// </summary>
    public string? SubmittedValuesJson
    {
        get
        {
            if (!CreationFailed)
            {
                return null;
            }

            return JsonSerializer.Serialize(
                new
                {
                    Name,
                    Purpose,
                    Country,
                    Currency,
                }
            );
        }
    }

    /// <summary>The resource key of the car count, which has its own singular wording.</summary>
    public string CandidateCountKey(Evaluation evaluation)
    {
        return evaluation.Candidates.Count == 1 ? "CandidateCountSingle" : "CandidateCount";
    }

    public async Task OnGetAsync()
    {
        Evaluations = await workspace.ListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var id = await workspace.CreateAsync(Name, Purpose, Country, Currency);

            return Created(id);
        }
        catch (RuleException error)
        {
            CreationFailed = true;
            ModelState.AddModelError("", text[error.Message]);
            await OnGetAsync();
            Response.StatusCode = 422;

            if (Request.Headers.ContainsKey(HtmxRequestHeader))
            {
                return Partial("_EvaluationOverview", this);
            }

            return Page();
        }
    }

    public async Task<IActionResult> OnPostImportAsync(IFormFile? upload)
    {
        try
        {
            if (!IsUsableUpload(upload))
            {
                throw new RuleException("InvalidImport");
            }

            await using var input = upload.OpenReadStream();
            var id = await transfer.ImportAsync(input, HttpContext.RequestAborted);

            return Created(id);
        }
        catch (RuleException error)
        {
            Response.StatusCode = 422;

            if (Request.Headers.ContainsKey(HtmxRequestHeader))
            {
                return Content(text[error.Message], "text/plain");
            }

            ImportFailed = true;
            ModelState.AddModelError("", text[error.Message]);
            await OnGetAsync();

            return Page();
        }
    }

    private bool IsUsableUpload([NotNullWhen(true)] IFormFile? upload)
    {
        return upload is not null
            && upload.Length <= EvaluationArchive.MaximumBytes
            && ModelState.IsValid;
    }

    /// <summary>Opens the new evaluation: HTMX follows the redirect header, a form post redirects.</summary>
    private IActionResult Created(Guid id)
    {
        if (!Request.Headers.ContainsKey(HtmxRequestHeader))
        {
            return RedirectToPage(DetailsPage, new { id });
        }

        Response.Headers["HX-Redirect"] = Url.Page(DetailsPage, new { id });

        return new EmptyResult();
    }
}
