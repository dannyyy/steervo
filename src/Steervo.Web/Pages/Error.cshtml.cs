using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Steervo.Web.Pages;
public sealed class ErrorModel : PageModel
{
    public int ErrorCode { get; private set; }
    public string TitleKey => ErrorCode switch { 403 => "ErrorForbiddenTitle", 404 => "ErrorNotFoundTitle", 429 => "ErrorRateLimitTitle", _ => "ErrorTitle" };
    public string HelpKey => ErrorCode switch { 403 => "ErrorForbiddenHelp", 404 => "ErrorNotFoundHelp", 429 => "ErrorRateLimitHelp", _ => "ErrorHelp" };
    public void OnGet(int? statusCode) => SetStatus(statusCode);
    public void OnPost(int? statusCode) => SetStatus(statusCode);
    private void SetStatus(int? statusCode)
    {
        ErrorCode = Response.StatusCode >= 400 ? Response.StatusCode : statusCode is >= 400 and <= 599 ? statusCode.Value : 500;
        Response.StatusCode = ErrorCode;
    }
}
