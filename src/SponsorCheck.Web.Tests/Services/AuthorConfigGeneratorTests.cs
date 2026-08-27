namespace SponsorCheck.Web.Tests.Services;

public class AuthorConfigGeneratorTests
{
    static AuthorModel BaseModel() =>
        new()
        {
            PackageId = "MyOssLib",
            PackageVersion = "2.0.0",
            SponsorCheckVersion = "9.9.9"
        };

    static void Enable(AuthorModel model, PlatformKind kind, string account)
    {
        var selection = model.Selection(kind);
        selection.Enabled = true;
        selection.Account = account;
    }

    static string Dump(AuthorModel model)
    {
        var output = AuthorConfigGenerator.Generate(model);
        var builder = new StringBuilder();
        builder.AppendLine($"=== Reference ({output.ReferenceTitle}) ===");
        builder.AppendLine(output.Reference);
        builder.AppendLine();
        builder.AppendLine("=== Credentials ===");
        builder.AppendLine(output.Credentials);
        builder.AppendLine();
        builder.AppendLine("=== Release notes ===");
        builder.AppendLine(output.ReleaseNotes);
        builder.AppendLine();
        builder.AppendLine("=== Checklist ===");
        builder.AppendLine(output.Checklist);
        builder.AppendLine();
        builder.AppendLine("=== Markdown ===");
        builder.AppendLine(output.Markdown);
        return builder.ToString();
    }

    [Test]
    public async Task GitHubOnly()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        await Verify(Dump(model));
    }

    [Test]
    public async Task AllPlatforms()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        Enable(model, PlatformKind.OpenCollective, "acme-org");
        Enable(model, PlatformKind.Polar, "acme");
        await Verify(Dump(model));
    }

    [Test]
    public async Task PolarAndOpenCollective()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.OpenCollective, "acme-org");
        Enable(model, PlatformKind.Polar, "acme");
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerMode()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        Enable(model, PlatformKind.OpenCollective, "acme-org");
        model.OwnerMode = true;
        model.OwnerId = "acme";
        await Verify(Dump(model));
    }

    [Test]
    public async Task Transitive()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        model.CheckTransitive = true;
        await Verify(Dump(model));
    }

    [Test]
    public async Task SeverityAndMessageOverrides()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");

        var noLicense = model.Selection(OverrideKind.NoLicenseSpecified);
        noLicense.Severity = SeverityOverride.Warning;
        noLicense.Message = "Sponsoring MyOssLib keeps it maintained.";

        model.Selection(OverrideKind.LicenseIgnored).Severity = SeverityOverride.Error;

        await Verify(Dump(model));
    }

    [Test]
    public async Task LandingUrl()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        Enable(model, PlatformKind.OpenCollective, "acme-org");
        Enable(model, PlatformKind.Polar, "acme");
        model.LandingUrl = "https://acme.example.com/sponsor";
        await Verify(Dump(model));
    }

    [Test]
    public async Task Exemptions()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        model.Exemptions.Add(new()
        {
            Name = "Consulting",
            Message = "Organizations that engaged a maintainer for consulting are exempt for 6 months."
        });
        model.Exemptions.Add(new()
        {
            Name = "SmallRevenue",
            Message = "Consumers under US$10,000 annual gross revenue are exempt."
        });
        await Verify(Dump(model));
    }

    [Test]
    public async Task SingleProjectCpm()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        model.RepoShape = RepoShape.SingleProjectCpm;
        await Verify(Dump(model));
    }

    [Test]
    public async Task MonorepoCpm()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        model.RepoShape = RepoShape.MonorepoCpm;
        model.OwnerMode = true;
        model.OwnerId = "acme";
        model.CheckTransitive = true;
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerModeWithExemptions()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        model.OwnerMode = true;
        model.OwnerId = "acme";
        model.Exemptions.Add(new()
        {
            Name = "Consulting",
            Message = "Organizations that engaged a maintainer for consulting are exempt for 6 months."
        });
        await Verify(Dump(model));
    }

    [Test]
    public async Task TimeBoundedExemptions()
    {
        // A capped exemption has to reach the consumer snippet as a pair — the name alone would
        // fail their next build with SC038.
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        model.Exemptions.Add(new()
        {
            Name = "Consulting",
            Message = "Organizations that engaged a maintainer for consulting are exempt for 6 months.",
            MaxTermMonths = "6"
        });
        model.Exemptions.Add(new()
        {
            Name = "SmallRevenue",
            Message = "Consumers under US$10,000 annual gross revenue are exempt."
        });
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerModeWithTimeBoundedExemption()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        model.OwnerMode = true;
        model.OwnerId = "acme";
        model.Exemptions.Add(new()
        {
            Name = "Consulting",
            Message = "Organizations that engaged a maintainer for consulting are exempt for 6 months.",
            MaxTermMonths = "6"
        });
        await Verify(Dump(model));
    }

    [Test]
    public async Task InvalidMaxTermMonths_IsReported()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        model.Exemptions.Add(new()
        {
            Name = "Consulting",
            Message = "Consulting carve-out.",
            MaxTermMonths = "six"
        });
        await Assert.That(model.ExemptionErrors).IsNotEmpty();
        await Assert.That(model.IsComplete).IsFalse();
        // Invalid rows are excluded from the generated config rather than emitted as a broken item.
        await Assert.That(model.HasExemptions).IsFalse();
    }

    [Test]
    public async Task PrivateSponsorMaxTermMonths()
    {
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        model.PrivateSponsorMaxTermMonths = "6";
        await Verify(Dump(model));
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("six")]
    [Arguments("0")]
    [Arguments("-1")]
    public async Task PrivateSponsorMaxTermMonths_NotEmitted(string value)
    {
        // Blank means "use the default". A value the bundler would reject with SC109 is dropped
        // rather than written out — emitting it would generate a reference that cannot pack.
        var model = BaseModel();
        Enable(model, PlatformKind.GitHub, "acmecorp");
        model.PrivateSponsorMaxTermMonths = value;
        var output = AuthorConfigGenerator.Generate(model);
        await Assert.That(output.Reference).DoesNotContain("PrivateSponsorMaxTermMonths");
    }
}
