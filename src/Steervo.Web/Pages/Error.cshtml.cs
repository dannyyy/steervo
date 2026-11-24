using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Steervo.Web.Pages;

/// <summary>The error view for full browser pages; it keeps the status code of the failure.</summary>
public sealed class ErrorModel : PageModel
{
    public int ErrorCode { get; private set; }

    public string TitleKey =>
        ErrorCode switch
        {
            403 => "ErrorForbiddenTitle",
            404 => "ErrorNotFoundTitle",
            429 => "ErrorRateLimitTitle",
            _ => "ErrorTitle",
        };

    public string HelpKey =>
        ErrorCode switch
        {
            403 => "ErrorForbiddenHelp",
            404 => "ErrorNotFoundHelp",
            429 => "ErrorRateLimitHelp",
            _ => "ErrorHelp",
        };

    /// <summary>The page the way-out link leads to: the overview, or sign-in for visitors.</summary>
    public string ReturnPage => IsSignedIn ? "/Index" : "/Auth/Login";

    /// <summary>The resource key of the way-out link's text.</summary>
    public string ReturnLabelKey => IsSignedIn ? "BackOverview" : "BackSignIn";

    private bool IsSignedIn => User.Identity?.IsAuthenticated == true;

    public void OnGet(int? statusCode)
    {
        SetStatus(statusCode);
    }

    public void OnPost(int? statusCode)
    {
        SetStatus(statusCode);
    }

    private void SetStatus(int? statusCode)
    {
        ErrorCode = ResolveErrorCode(statusCode);
        Response.StatusCode = ErrorCode;
    }

    private int ResolveErrorCode(int? statusCode)
    {
        // A failure status that is already on the response wins over the query parameter.
        if (Response.StatusCode >= 400)
        {
            return Response.StatusCode;
        }

        if (statusCode is >= 400 and <= 599)
        {
            return statusCode.Value;
        }

        return 500;
    }
}
