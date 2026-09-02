namespace SponsorCheck.Web.Models;

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
    IReadOnlyDictionary<string, string> Severities,
    int PrivateSponsorMaxTermMonths,
    // The bundled sponsor hashes, or null when they could not be read — the file is absent, or large
    // enough that downloading it would have cost more than the answer is worth. Null is deliberately
    // not the same as empty: an empty set is an authoritative "this package bundles nobody", and
    // reporting an unread list as empty would tell every sponsor they are missing from it.
    IReadOnlySet<string>? SponsorHashes = null)
{
    /// <summary>Mirrors PrivateSponsorTerm.DefaultMaxTermMonths in the task assembly, which the
    /// wizard cannot reference. RepoContractTests keeps the two in step.</summary>
    public const int DefaultPrivateSponsorMaxTermMonths = 12;

    public static PackageFacts WithoutSponsorCheck(string packageId, string version) =>
        new(packageId, version, false, false, false, null, null, null, [], [], new Dictionary<string, string>(), DefaultPrivateSponsorMaxTermMonths);

    /// <summary>Whether <paramref name="account"/> is in this package's bundled list for the given
    /// platform, or null when the list could not be read and there is no answer to give.
    ///
    /// A false is emphatically not "not a sponsor": private and incognito sponsorships are excluded
    /// from the list by design, and a sponsorship that began after the pack date cannot be in a list
    /// frozen before it. It means precisely what the verifier's own hash check means — this account
    /// will not match — which is why the callers pair it with those two routes.</summary>
    public bool? Bundles(string platformId, string account)
    {
        if (SponsorHashes is not { } hashes || string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        return hashes.Contains(SponsorAccountHash.For(platformId, account));
    }

    public PackageExemption? FindExemption(string name) =>
        Exemptions.FirstOrDefault(_ => string.Equals(_.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
