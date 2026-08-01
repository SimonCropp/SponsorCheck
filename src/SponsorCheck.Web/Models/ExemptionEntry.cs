namespace SponsorCheck.Web.Models;

public sealed class ExemptionEntry
{
    public string Name { get; set; } = "";
    public string Message { get; set; } = "";

    public bool IsComplete => Name.Trim().Length > 0 && Message.Trim().Length > 0;
    public bool IsBlank => Name.Trim().Length == 0 && Message.Trim().Length == 0;
}
