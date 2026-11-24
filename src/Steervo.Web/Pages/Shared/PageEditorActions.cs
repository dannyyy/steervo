namespace Steervo.Web.Pages.Shared;

/// <summary>The cancel link and save button at the end of an editor that fills a whole page.</summary>
public sealed record PageEditorActions(string ReturnUrl, string SubmitId, string SubmitLabel);
