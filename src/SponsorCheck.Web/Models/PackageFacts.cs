namespace SponsorCheck.Web.Models;

public sealed record PackageExemption(string Name, string Message);

public sealed record PackagePlatformAccount(PlatformKind Kind, string Account);

/// <summary>
/// Facts read client-side from a published nupkg (api.nuget.org). Everything here is baked into the
/// package at pack time by the SponsorCheck bundler, so the wizard can pre-answer questions —
/// owner mode and owner id, the platforms the author accepts, defined exemptions, the pack date,
/// and any severity escalations — instead of asking the consumer to know them.
/// </summary>
public sealed record PackageFacts(
    string PackageId,
    string Version,
    bool BundlesSponsorCheck,
    bool CheckTransitive,
    bool OwnerMode,
    string? OwnerId,
    string? PackDate,
    string? LandingUrl,
    IReadOnlyList<PackagePlatformAccount> Platforms,
    IReadOnlyList<PackageExemption> Exemptions,
    IReadOnlyDictionary<string, string> Severities)
{
    public static PackageFacts WithoutSponsorCheck(string packageId, string version) =>
        new(packageId, version, false, false, false, null, null, null, [], [], new Dictionary<string, string>());

    public PackageExemption? FindExemption(string name) =>
        Exemptions.FirstOrDefault(_ => string.Equals(_.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
