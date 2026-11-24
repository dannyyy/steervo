namespace Steervo.Web.Pages.Shared;

/// <summary>
/// The cancel and save buttons at the end of an editor. Without a label the save button reads
/// "Save"; the accessible label names what is saved when the visible text alone is ambiguous.
/// </summary>
public sealed record EditorActions(
    string SubmitId,
    string? Label = null,
    string? AccessibleLabel = null
);
