namespace SponsorCheck.Benchmarks;

// One way a consumer can declare sponsorship to the benchmark's owner-mode package (owner 'acme'),
// as the property written to the generated solution's Directory.Build.props.
//
// The three differ in what the verifier has to say, which is what decided its cost: nothing at all,
// an audit message on every build, or a warning on every build. None of them is a rejection, so
// none should send a project off to ask its references whether they cover the package.
public sealed record Scenario(string Name, string Description, string Property)
{
    public static IReadOnlyList<Scenario> All { get; } =
    [
        new("sponsor", "valid sponsor, nothing to report", "<acme_GitHubSponsorAccount>alice</acme_GitHubSponsorAccount>"),
        new("exemption", "exemption claim, audit message on every build", "<acme_SponsorshipExemption>Consulting</acme_SponsorshipExemption>"),
        new("ignored", "ignored license, warning on every build", "<acme_SponsorshipLicenseIgnored>true</acme_SponsorshipLicenseIgnored>")
    ];
}
