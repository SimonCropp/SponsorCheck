namespace SponsorCheck.Web.Models;

public sealed class OverrideSelection
{
    public SeverityOverride Severity { get; set; } = SeverityOverride.Default;
    public string Message { get; set; } = "";

    public bool HasSeverity => Severity != SeverityOverride.Default;
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public bool IsSet => HasSeverity || HasMessage;

    public string SeverityValue => Severity switch
    {
        SeverityOverride.Error => "error",
        SeverityOverride.Warning => "warning",
        SeverityOverride.Message => "message",
        _ => ""
    };
}
