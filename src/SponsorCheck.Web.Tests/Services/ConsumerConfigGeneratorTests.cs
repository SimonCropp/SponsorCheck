namespace SponsorCheck.Web.Tests.Services;

public class ConsumerConfigGeneratorTests
{
    static ConsumerModel BaseModel(Placement placement)
    {
        var model = new ConsumerModel
        {
            PackageId = "ThePackage",
            PackageVersion = "1.2.3"
        };
        switch (placement)
        {
            case Placement.PerPackageCpm:
                model.Cpm = true;
                break;
            case Placement.OwnerMode:
                model.OwnerMode = true;
                model.OwnerId = "acme";
                break;
        }

        return model;
    }

    static void Sponsor(ConsumerModel model, PlatformKind kind, string account)
    {
        model.Mode = ConsumerLicenseMode.Sponsor;
        var selection = model.Selection(kind);
        selection.Enabled = true;
        selection.Account = account;
    }

    static string Dump(ConsumerModel model)
    {
        var output = ConsumerConfigGenerator.Generate(model);
        var builder = new StringBuilder();
        builder.AppendLine($"=== Snippet ({output.SnippetTitle}) ===");
        builder.AppendLine(output.Snippet);
        builder.AppendLine();
        builder.AppendLine("=== File to edit ===");
        builder.AppendLine(output.FileToEdit);
        builder.AppendLine();
        builder.AppendLine("=== Instruction ===");
        builder.AppendLine(output.Instruction);
        builder.AppendLine();
        builder.AppendLine("=== Build outcome ===");
        builder.AppendLine(output.BuildOutcome);
        builder.AppendLine();
        builder.AppendLine("=== Notes ===");
        foreach (var note in output.Notes)
        {
            builder.AppendLine($"- {note}");
        }

        builder.AppendLine();
        builder.AppendLine("=== Markdown ===");
        builder.AppendLine(output.Markdown);
        return builder.ToString();
    }

    [Test]
    public async Task ProjectSponsorSingle()
    {
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "alice");
        await Verify(Dump(model));
    }

    [Test]
    public async Task ProjectSponsorWithStart()
    {
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "carol");
        model.StartedAfterRelease = true;
        model.SponsorshipStart = "2026-04-30";
        await Verify(Dump(model));
    }

    [Test]
    public async Task ProjectSponsorMultiPlatform()
    {
        var model = BaseModel(Placement.PerPackageProject);
        Sponsor(model, PlatformKind.GitHub, "acmecorp");
        Sponsor(model, PlatformKind.OpenCollective, "acme-org");
        Sponsor(model, PlatformKind.Polar, "acme");
        await Verify(Dump(model));
    }

    [Test]
    public async Task ProjectLicense()
    {
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.License;
        model.LicensedUntilMonth = "2027-06";
        await Verify(Dump(model));
    }

    [Test]
    public async Task ProjectExemption()
    {
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.Exemption;
        model.ExemptionName = "Consulting";
        await Verify(Dump(model));
    }

    [Test]
    public async Task ProjectIgnore()
    {
        var model = BaseModel(Placement.PerPackageProject);
        model.Mode = ConsumerLicenseMode.Ignore;
        await Verify(Dump(model));
    }

    [Test]
    public async Task CpmSponsor()
    {
        var model = BaseModel(Placement.PerPackageCpm);
        Sponsor(model, PlatformKind.GitHub, "alice");
        await Verify(Dump(model));
    }

    [Test]
    public async Task CpmLicense()
    {
        var model = BaseModel(Placement.PerPackageCpm);
        model.Mode = ConsumerLicenseMode.License;
        model.LicensedUntilMonth = "2027-06";
        await Verify(Dump(model));
    }

    [Test]
    public async Task CpmExemption()
    {
        var model = BaseModel(Placement.PerPackageCpm);
        model.Mode = ConsumerLicenseMode.Exemption;
        model.ExemptionName = "SmallRevenue";
        await Verify(Dump(model));
    }

    [Test]
    public async Task CpmIgnore()
    {
        var model = BaseModel(Placement.PerPackageCpm);
        model.Mode = ConsumerLicenseMode.Ignore;
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerSponsor()
    {
        var model = BaseModel(Placement.OwnerMode);
        Sponsor(model, PlatformKind.GitHub, "alice");
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerSponsorWithStart()
    {
        var model = BaseModel(Placement.OwnerMode);
        Sponsor(model, PlatformKind.GitHub, "carol");
        model.StartedAfterRelease = true;
        model.SponsorshipStart = "2026-04-30";
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerLicense()
    {
        var model = BaseModel(Placement.OwnerMode);
        model.Mode = ConsumerLicenseMode.License;
        model.LicensedUntilMonth = "2027-06";
        await Verify(Dump(model));
    }

    [Test]
    public async Task OwnerIgnore()
    {
        var model = BaseModel(Placement.OwnerMode);
        model.Mode = ConsumerLicenseMode.Ignore;
        await Verify(Dump(model));
    }
}
