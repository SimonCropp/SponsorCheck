namespace SponsorCheck.Web.Models;

public enum PlatformKind
{
    GitHub,
    OpenCollective,
    Polar
}

public sealed class PlatformSelection
{
    public bool Enabled { get; set; }
    public string Account { get; set; } = "";
}

/// <summary>
/// Static description of a sponsorship platform: the metadata names the author and consumer use,
/// how its credential is supplied, and how to render a sponsor URL. Single source for every
/// platform-derived name the generators emit; the anti-rot tests check these against the real
/// MSBuild targets shipped by SponsorCheck. <see cref="WireId"/> is the platform identifier used
/// inside bundled sidecar files (SponsorCheck.AuthorAccounts.txt).
/// </summary>
public sealed record Platform(
    PlatformKind Kind,
    string DisplayName,
    string WireId,
    string AuthorAccountMetadata,
    string ConsumerAccountMetadata,
    string TokenProperty,
    string UserSecretKey,
    string SponsorUrlTemplate,
    bool CredentialRequired,
    string TokenHelp)
{
    public string SponsorUrl(string account) => string.Format(SponsorUrlTemplate, account);

    public static Platform? FromWireId(string wireId) =>
        All.FirstOrDefault(_ => string.Equals(_.WireId, wireId, StringComparison.OrdinalIgnoreCase));

    public static readonly Platform GitHub = new(
        PlatformKind.GitHub,
        "GitHub Sponsors",
        "GitHubSponsors",
        "GitHubSponsorsAccount",
        "GitHubSponsorAccount",
        "GitHubToken",
        "SponsorCheck:GitHubToken",
        "https://github.com/sponsors/{0}",
        CredentialRequired: true,
        "Required. Create a classic PAT (https://github.com/settings/tokens/new) with read:user, plus read:org " +
        "if sponsored as an organization. Fine-grained PATs have no Sponsorships permission, so a classic PAT " +
        "is the only option. The token must be owned by the sponsored account (or an admin of the sponsored " +
        "org) — otherwise private sponsors are silently missing from the bundled list.");

    public static readonly Platform OpenCollective = new(
        PlatformKind.OpenCollective,
        "Open Collective",
        "OpenCollective",
        "OpenCollectiveAccount",
        "OpenCollectiveSponsorAccount",
        "OpenCollectiveToken",
        "SponsorCheck:OpenCollectiveToken",
        "https://opencollective.com/{0}",
        CredentialRequired: false,
        "Optional. Public collectives are queryable anonymously; a Personal Token " +
        "(https://opencollective.com/applications, no scopes) gives rate-limit headroom.");

    public static readonly Platform Polar = new(
        PlatformKind.Polar,
        "Polar",
        "Polar",
        "PolarAccount",
        "PolarSponsorAccount",
        "PolarToken",
        "SponsorCheck:PolarToken",
        "https://polar.sh/{0}",
        CredentialRequired: true,
        "Required. Create an organization access token " +
        "(https://docs.polar.sh/integrate/authentication/personal-access-token) " +
        "with scopes subscriptions:read, customers:read, organizations:read.");

    public static readonly IReadOnlyList<Platform> All = [GitHub, OpenCollective, Polar];
}
