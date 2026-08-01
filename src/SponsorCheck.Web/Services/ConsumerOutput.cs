namespace SponsorCheck.Web.Services;

public sealed record ConsumerOutput(
    string SnippetTitle,
    string Snippet,
    string FileToEdit,
    string Instruction,
    string BuildOutcome,
    IReadOnlyList<string> Notes,
    string Markdown);
