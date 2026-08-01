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
    // Step: situation
    public string EnteredCode { get; set; } = "";
    public bool OwnerMode { get; set; }
    public string OwnerId { get; set; } = "";
    public bool Cpm { get; set; }

    // Step: package
    public string PackageId { get; set; } = "";
    public string PackageVersion { get; set; } = "";

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

    public bool SituationComplete => !OwnerMode || !string.IsNullOrWhiteSpace(OwnerId);

    public bool PackageComplete => OwnerMode || !string.IsNullOrWhiteSpace(PackageId);

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
