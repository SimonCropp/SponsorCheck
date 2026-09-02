namespace SponsorCheck.Web.Models;

/// <summary>
/// All consumer choices the wizard collects. Bound directly to the step components and consumed
/// (read-only) by <see cref="Services.ConsumerConfigGenerator"/> to produce the output.
/// </summary>
public sealed class ConsumerModel
{
    // Step: package
    public string PackageId { get; set; } = "";
    public string PackageVersion { get; set; } = "";

    /// <summary>Set when the package was inspected on nuget.org; drives pre-answered questions.</summary>
    public PackageFacts? Facts { get; set; }

    // Step: situation
    public string EnteredCode { get; set; } = "";
    public bool OwnerMode { get; set; }
    public string OwnerId { get; set; } = "";
    public bool Cpm { get; set; }

    // Step: license mode
    public ConsumerLicenseMode? Mode { get; set; }
    public Dictionary<PlatformKind, PlatformSelection> Platforms { get; } =
        Platform.All.ToDictionary(_ => _.Kind, _ => new PlatformSelection());
    public bool StartedAfterRelease { get; set; }
    public string SponsorshipStart { get; set; } = "";
    public bool PrivateSponsorship { get; set; }
    public string PrivateUntilMonth { get; set; } = "";
    public string LicensedUntilMonth { get; set; } = "";
    public string ExemptionName { get; set; } = "";
    public string ExemptionUntilMonth { get; set; } = "";

    public Placement Placement =>
        OwnerMode
            ? Placement.OwnerMode
            : Cpm
                ? Placement.PerPackageCpm
                : Placement.PerPackageProject;

    public PlatformSelection Selection(PlatformKind kind) => Platforms[kind];

    /// <summary>Enabled platforms that also have a non-blank account, paired with their account value.</summary>
    public IEnumerable<(Platform Platform, string Account)> EnabledPlatforms =>
        Platform.All
            .Where(_ => Platforms[_.Kind] is { Enabled: true } selection && !string.IsNullOrWhiteSpace(selection.Account))
            .Select(_ => (_, Platforms[_.Kind].Account.Trim()));

    public bool HasPlatform => EnabledPlatforms.Any();

    public bool IsLicensedUntilValid =>
        DateTime.TryParseExact(LicensedUntilMonth.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public bool IsSponsorshipStartValid =>
        DateTime.TryParseExact(SponsorshipStart.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>A private sponsorship is declared only when the box is ticked *and* an end month is
    /// supplied — the month is the whole mechanism, so the box alone declares nothing. Shared because
    /// the same predicate decides which attributes are emitted and which outcome prose is written.</summary>
    public bool PrivateDeclared => PrivateSponsorship && !string.IsNullOrWhiteSpace(PrivateUntilMonth);

    public bool IsPrivateUntilValid =>
        DateTime.TryParseExact(PrivateUntilMonth.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>How far ahead the publisher lets a private-sponsorship claim run. Taken from the
    /// inspected package when available, otherwise the shipped default — a package the wizard could
    /// not inspect may still have narrowed it, so the generated output says so rather than
    /// presenting the default as certain.</summary>
    public int PrivateSponsorMaxTermMonths =>
        Facts is { BundlesSponsorCheck: true } facts ? facts.PrivateSponsorMaxTermMonths : PackageFacts.DefaultPrivateSponsorMaxTermMonths;

    public bool HasExemptionUntil => !string.IsNullOrWhiteSpace(ExemptionUntilMonth);

    public bool IsExemptionUntilValid =>
        DateTime.TryParseExact(ExemptionUntilMonth.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>The inspected package's definition of the claimed exemption, when the package was
    /// looked up and the name matches one it defines.</summary>
    public PackageExemption? ClaimedExemption =>
        Facts is { BundlesSponsorCheck: true } facts ? facts.FindExemption(ExemptionName) : null;

    /// <summary>An end month is mandatory only when the publisher capped the claimed exemption. It
    /// stays optional otherwise — a consumer may self-bound an uncapped exemption, and a package the
    /// wizard could not inspect can't be assumed either way.</summary>
    public bool IsExemptionUntilRequired => ClaimedExemption?.MaxTermMonths is not null;

    /// <summary>Applies nupkg-derived facts: owner mode and owner id come from the package itself.
    /// Facts always win over earlier manual answers — they are what the verifier will actually do.</summary>
    public void ApplyFacts(PackageFacts facts)
    {
        Facts = facts;
        if (!facts.BundlesSponsorCheck)
        {
            return;
        }

        OwnerMode = facts.OwnerMode;
        OwnerId = facts.OwnerId ?? "";

        // Polar has no private/incognito notion, so a Polar-only package offers no private route and
        // the wizard stops showing the box. An answer given before the lookup would otherwise survive
        // as a hidden attribute in the generated snippet.
        if (facts.Platforms.Count > 0 &&
            !facts.Platforms.Any(_ => _.Kind is PlatformKind.GitHub or PlatformKind.OpenCollective))
        {
            PrivateSponsorship = false;
            PrivateUntilMonth = "";
        }

        if (string.IsNullOrWhiteSpace(PackageVersion))
        {
            PackageVersion = facts.Version;
        }
    }

    /// <summary>The facts-recorded severity override for the current placement's code, if any.</summary>
    public string? FactSeverity(string projectCode, string cpmCode, string ownerCode) =>
        Facts?.Severities.GetValueOrDefault(Placement switch
        {
            Placement.PerPackageProject => projectCode,
            Placement.PerPackageCpm => cpmCode,
            _ => ownerCode
        });

    public bool SituationComplete => !OwnerMode || !string.IsNullOrWhiteSpace(OwnerId);

    public bool PackageComplete => !string.IsNullOrWhiteSpace(PackageId);

    public bool ModeComplete => Mode switch
    {
        ConsumerLicenseMode.Sponsor =>
            HasPlatform &&
            (!StartedAfterRelease || IsSponsorshipStartValid) &&
            (!PrivateSponsorship || IsPrivateUntilValid),
        ConsumerLicenseMode.License => IsLicensedUntilValid,
        ConsumerLicenseMode.Exemption =>
            !string.IsNullOrWhiteSpace(ExemptionName) &&
            (!IsExemptionUntilRequired || HasExemptionUntil) &&
            (!HasExemptionUntil || IsExemptionUntilValid),
        ConsumerLicenseMode.Ignore => true,
        _ => false
    };

    public bool IsComplete => SituationComplete && PackageComplete && ModeComplete;
}
