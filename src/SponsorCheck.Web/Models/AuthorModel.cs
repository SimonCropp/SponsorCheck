namespace SponsorCheck.Web.Models;

public enum RepoShape
{
    SingleProject,
    SingleProjectCpm,
    MonorepoCpm
}

public sealed class ExemptionEntry
{
    public string Name { get; set; } = "";
    public string Message { get; set; } = "";

    public bool IsComplete => Name.Trim().Length > 0 && Message.Trim().Length > 0;
    public bool IsBlank => Name.Trim().Length == 0 && Message.Trim().Length == 0;
}

/// <summary>
/// All author choices the wizard collects. Bound directly to the step components and consumed
/// (read-only) by <see cref="Services.AuthorConfigGenerator"/> to produce the outputs.
/// </summary>
public sealed class AuthorModel
{
    // Step: package
    public string PackageId { get; set; } = "";
    public string PackageVersion { get; set; } = "1.0.0";
    public string SponsorCheckVersion { get; set; } = WizardDefaults.SponsorCheckVersion;
    public RepoShape RepoShape { get; set; } = RepoShape.SingleProject;

    // Step: platforms (at least one enabled with an account is required)
    public Dictionary<PlatformKind, PlatformSelection> Platforms { get; } =
        Platform.All.ToDictionary(_ => _.Kind, _ => new PlatformSelection());

    // Step: mode & scope
    public bool OwnerMode { get; set; }
    public string OwnerId { get; set; } = "";
    public bool CheckTransitive { get; set; }

    // Step: options (all optional)
    public Dictionary<OverrideKind, OverrideSelection> Overrides { get; } =
        OverrideInfo.All.ToDictionary(_ => _.Kind, _ => new OverrideSelection());
    public List<ExemptionEntry> Exemptions { get; } = [];
    public string LandingUrl { get; set; } = "";

    public PlatformSelection Selection(PlatformKind kind) => Platforms[kind];

    public OverrideSelection Selection(OverrideKind kind) => Overrides[kind];

    /// <summary>Enabled platforms that also have a non-blank account, paired with their account value.</summary>
    public IEnumerable<(Platform Platform, string Account)> EnabledPlatforms =>
        Platform.All
            .Where(_ => Platforms[_.Kind] is { Enabled: true } selection && !string.IsNullOrWhiteSpace(selection.Account))
            .Select(_ => (_, Platforms[_.Kind].Account.Trim()));

    public bool HasPlatform => EnabledPlatforms.Any();

    public bool HasLandingUrl => !string.IsNullOrWhiteSpace(LandingUrl);

    /// <summary>Mirrors the SC105 rule: the owner id is baked into consumer-side property names, so it
    /// must start with a letter and contain only letters, digits, and underscores.</summary>
    public bool IsOwnerIdValid
    {
        get
        {
            var trimmed = OwnerId.Trim();
            if (trimmed.Length == 0 || !char.IsAsciiLetter(trimmed[0]))
            {
                return false;
            }

            return trimmed.All(_ => char.IsAsciiLetterOrDigit(_) || _ == '_');
        }
    }

    public IReadOnlyList<ExemptionEntry> CompletedExemptions =>
        [.. Exemptions.Where(_ => _.IsComplete)];

    public bool HasExemptions => CompletedExemptions.Count > 0;

    /// <summary>Mirrors the SC106 pack-time validation: no empty names, no empty criteria text, no
    /// case-insensitive duplicate names. Fully blank rows are ignored (treated as not-yet-filled).</summary>
    public IReadOnlyList<string> ExemptionErrors
    {
        get
        {
            var errors = new List<string>();
            foreach (var entry in Exemptions.Where(_ => !_.IsBlank))
            {
                if (entry.Name.Trim().Length == 0)
                {
                    errors.Add("An exemption needs a name (SC106).");
                }
                else if (entry.Message.Trim().Length == 0)
                {
                    errors.Add($"Exemption '{entry.Name.Trim()}' needs criteria text (SC106).");
                }
            }

            var duplicates = Exemptions
                .Where(_ => _.Name.Trim().Length > 0)
                .GroupBy(_ => _.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(_ => _.Count() > 1)
                .Select(_ => _.Key);
            foreach (var duplicate in duplicates)
            {
                errors.Add($"Duplicate exemption name '{duplicate}' — names are case-insensitive (SC106).");
            }

            return errors;
        }
    }

    /// <summary>The wizard can produce output once a package id and at least one platform account are set
    /// (and a valid owner id is present when owner mode is on, and exemption rows are valid).</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(PackageId) &&
        HasPlatform &&
        (!OwnerMode || IsOwnerIdValid) &&
        ExemptionErrors.Count == 0;
}
