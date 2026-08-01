using System.Globalization;

namespace SponsorCheck.Web.Models;

public enum ConsumerLicenseMode
{
    Sponsor,
    License,
    Exemption,
    Ignore
}

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
    public string LicensedUntilMonth { get; set; } = "";
    public string ExemptionName { get; set; } = "";

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
        ConsumerLicenseMode.Sponsor => HasPlatform && (!StartedAfterRelease || IsSponsorshipStartValid),
        ConsumerLicenseMode.License => IsLicensedUntilValid,
        ConsumerLicenseMode.Exemption => !string.IsNullOrWhiteSpace(ExemptionName),
        ConsumerLicenseMode.Ignore => true,
        _ => false
    };

    public bool IsComplete => SituationComplete && PackageComplete && ModeComplete;
}
