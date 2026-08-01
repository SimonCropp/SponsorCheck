using System.Text;
using SponsorCheck.Web.Models;

namespace SponsorCheck.Web.Services;

public sealed record ConsumerOutput(
    string SnippetTitle,
    string Snippet,
    string FileToEdit,
    string Instruction,
    string BuildOutcome,
    IReadOnlyList<string> Notes,
    string Markdown);

/// <summary>
/// Pure text generation from a <see cref="ConsumerModel"/>. No Blazor / DI dependencies so it can be
/// unit-tested directly and snapshot-verified. Produces the consumer-side snippet, the file it
/// belongs in, the expected build outcome, and a self-contained markdown document ("Copy as
/// markdown") suitable for handing to a teammate or an AI coding agent.
/// </summary>
public static class ConsumerConfigGenerator
{
    const string DocsBase = "https://github.com/SimonCropp/SponsorCheck";
    const string ConsumerDocs = DocsBase + "/blob/main/docs/ConsumerUsage.md";
    const string VerifierDocs = DocsBase + "/blob/main/docs/VerifierDiagnosticCodes.md";
    const string WizardUrl = "https://simoncropp.github.io/SponsorCheck/consumer";

    public static ConsumerOutput Generate(ConsumerModel model)
    {
        var id = string.IsNullOrWhiteSpace(model.PackageId) ? "ThePackage" : model.PackageId.Trim();
        var version = model.PackageVersion.Trim();
        var owner = string.IsNullOrWhiteSpace(model.OwnerId) ? "owner" : model.OwnerId.Trim();
        var placement = model.Placement;

        var attributes = AttributesFor(model);
        var (title, snippet, fileToEdit, instruction) = Placement(model, placement, id, version, owner, attributes);
        var outcome = BuildOutcome(model, placement);
        var notes = Notes(model, placement, owner);
        var markdown = BuildMarkdown(model, placement, id, version, owner, snippet, fileToEdit, instruction, outcome, notes);

        return new(title, snippet, fileToEdit, instruction, outcome, notes, markdown);
    }

    static List<(string Name, string Value)> AttributesFor(ConsumerModel model)
    {
        var attributes = new List<(string Name, string Value)>();
        switch (model.Mode)
        {
            case ConsumerLicenseMode.Sponsor:
                foreach (var (platform, account) in model.EnabledPlatforms)
                {
                    attributes.Add((platform.ConsumerAccountMetadata, account));
                }

                if (model.StartedAfterRelease && !string.IsNullOrWhiteSpace(model.SponsorshipStart))
                {
                    attributes.Add(("SponsorshipStart", model.SponsorshipStart.Trim()));
                }

                break;
            case ConsumerLicenseMode.License:
                attributes.Add(("SponsorshipLicensedUntil", model.LicensedUntilMonth.Trim()));
                break;
            case ConsumerLicenseMode.Exemption:
                attributes.Add(("SponsorshipExemption", model.ExemptionName.Trim()));
                break;
            case ConsumerLicenseMode.Ignore:
                attributes.Add(("SponsorshipLicenseIgnored", "true"));
                break;
        }

        return attributes;
    }

    static (string Title, string Snippet, string FileToEdit, string Instruction) Placement(
        ConsumerModel model,
        Placement placement,
        string id,
        string version,
        string owner,
        List<(string Name, string Value)> attributes)
    {
        if (placement == Models.Placement.OwnerMode)
        {
            var properties = attributes.Select(_ => ($"{owner}_{_.Name}", _.Value));
            return (
                "Global properties (owner mode)",
                MsBuildXml.PropertyGroup(properties),
                "Directory.Build.props (covers every project under it) — or any one consuming project's .csproj.",
                $"Add the properties below to a <PropertyGroup>. One declaration covers every package from owner '{owner}'; " +
                "the property names are exact (the owner prefix is baked into the package).");
        }

        var elementAttributes = new List<(string Name, string Value)> { ("Include", id) };
        if (version.Length > 0)
        {
            elementAttributes.Add(("Version", version));
        }

        elementAttributes.AddRange(attributes);
        var inlineCount = version.Length > 0 ? 2 : 1;

        if (placement == Models.Placement.PerPackageCpm)
        {
            return (
                "PackageVersion (Directory.Packages.props)",
                MsBuildXml.SelfClosingElement("PackageVersion", elementAttributes, inlineCount),
                "Directory.Packages.props — the file holding the <PackageVersion> items under Central Package Management.",
                $"Add the new attribute(s) to the existing <PackageVersion Include=\"{id}\"> element — " +
                "do not create a second element, and do not put the metadata on the <PackageReference>.");
        }

        return (
            "PackageReference (consuming .csproj)",
            MsBuildXml.SelfClosingElement("PackageReference", elementAttributes, inlineCount),
            $"The consuming project's .csproj — the file holding the <PackageReference> for '{id}'.",
            $"Add the new attribute(s) to the existing <PackageReference Include=\"{id}\"> element — " +
            "do not create a second reference.");
    }

    static string CodeFor(Placement placement, string project, string cpm, string owner) =>
        placement switch
        {
            Models.Placement.PerPackageProject => project,
            Models.Placement.PerPackageCpm => cpm,
            _ => owner
        };

    static string BuildOutcome(ConsumerModel model, Placement placement)
    {
        switch (model.Mode)
        {
            case ConsumerLicenseMode.Sponsor when model.StartedAfterRelease && !string.IsNullOrWhiteSpace(model.SponsorshipStart):
            {
                var futureCode = CodeFor(placement, "SC015", "SC016", "SC028");
                var start = model.SponsorshipStart.Trim();
                return
                    $"When {start} is after the package version's pack date, the build passes and logs a high-priority " +
                    "SC017 audit message naming the unverified sponsor — the declaration is trusted because the bundled " +
                    "list cannot contain a sponsorship that began after it was frozen. " +
                    $"A future date fails with {futureCode}. " +
                    "The attestation self-expires: after upgrading to a version packed later than the start date, the " +
                    "normal bundled-list check applies again and SponsorshipStart can be dropped.";
            }

            case ConsumerLicenseMode.Sponsor:
            {
                var noMatchCode = CodeFor(placement, "SC007", "SC008", "SC024");
                return
                    "Passes silently when any declared account was in the bundled sponsor list at the version's pack time. " +
                    $"If the build still fails with {noMatchCode}, either the account or platform doesn't match what the " +
                    "author bundled, or the sponsorship began after this version was packed — in that case add " +
                    "SponsorshipStart=\"yyyy-MM-dd\" (re-run the wizard and answer yes to the started-after question).";
            }

            case ConsumerLicenseMode.License:
            {
                var expiredCode = CodeFor(placement, "SC009", "SC010", "SC025");
                var capCode = CodeFor(placement, "SC035", "SC036", "SC037");
                var month = model.LicensedUntilMonth.Trim();
                return
                    $"Passes silently through the end of {month} (UTC). After that the build fails with {expiredCode} " +
                    "until the value is renewed — a one-line edit. Values more than one year from the build clock are " +
                    $"rejected with {capCode}.";
            }

            case ConsumerLicenseMode.Exemption:
            {
                var warnCode = CodeFor(placement, "SC029", "SC030", "SC031");
                var unknownCode = CodeFor(placement, "SC032", "SC033", "SC034");
                return
                    $"Passes with a {warnCode} warning whose body is the publisher's own criteria text — the build log " +
                    "records the specific carve-out being claimed rather than a generic breach message. A name the " +
                    $"publisher did not define fails with {unknownCode}; that error lists the available exemption names.";
            }

            case ConsumerLicenseMode.Ignore:
            {
                var warnCode = CodeFor(placement, "SC005", "SC006", "SC023");
                return
                    $"Passes with a {warnCode} breach-of-license warning on every build. The author may have raised this " +
                    "diagnostic's severity to error at pack time, in which case the build fails instead of warning.";
            }

            default:
                return "";
        }
    }

    static List<string> Notes(ConsumerModel model, Placement placement, string owner)
    {
        var notes = new List<string>();

        if (model.Mode == ConsumerLicenseMode.Sponsor)
        {
            if (model.EnabledPlatforms.Count() > 1)
            {
                notes.Add(
                    "The verifier passes on any single match — declaring accounts for several platforms is one cheap " +
                    "hash lookup each, and keeps builds green if the author migrates platforms.");
            }

            notes.Add(
                "The bundled list is frozen per package version: versions that pass keep passing even if sponsorship " +
                "later lapses; a lapse surfaces when upgrading to a version packed after it.");
        }

        switch (placement)
        {
            case Models.Placement.PerPackageProject:
                notes.Add(
                    "If the solution adopts Central Package Management later, the metadata moves to the matching " +
                    "<PackageVersion> in Directory.Packages.props — wrong placement raises SC020, both places raises SC019.");
                break;
            case Models.Placement.PerPackageCpm:
                notes.Add(
                    "Keep the metadata in exactly one place: on the <PackageVersion>. Setting it on the " +
                    "<PackageReference> as well raises SC019; only on the <PackageReference> raises SC020.");
                break;
            default:
                notes.Add(
                    $"The properties cover every package from owner '{owner}' — one declaration for the whole family. " +
                    "The exact property names are also printed in the SC021 error message.");
                break;
        }

        return notes;
    }

    static string ModeDescription(ConsumerModel model) => model.Mode switch
    {
        ConsumerLicenseMode.Sponsor => "sponsor account match",
        ConsumerLicenseMode.License => "time-bounded private license",
        ConsumerLicenseMode.Exemption => $"publisher-defined exemption '{model.ExemptionName.Trim()}'",
        ConsumerLicenseMode.Ignore => "explicit ignore (breach-of-license warning accepted)",
        _ => "not chosen"
    };

    static string PlacementDescription(Placement placement, string owner) => placement switch
    {
        Models.Placement.PerPackageProject => "per-package metadata on the <PackageReference> (no Central Package Management)",
        Models.Placement.PerPackageCpm => "per-package metadata on the <PackageVersion> (Central Package Management)",
        _ => $"owner mode — global MSBuild properties prefixed '{owner}_'"
    };

    static string BuildMarkdown(
        ConsumerModel model,
        Placement placement,
        string id,
        string version,
        string owner,
        string snippet,
        string fileToEdit,
        string instruction,
        string outcome,
        IReadOnlyList<string> notes)
    {
        var versioned = version.Length == 0 ? $"`{id}`" : $"`{id}` {version}";

        var builder = new StringBuilder();
        void Line(string text = "") => builder.AppendLine(text);

        Line($"# Configure SponsorCheck licensing for {id}");
        Line();
        Line($"Generated by the [SponsorCheck setup wizard]({WizardUrl}).");
        Line();
        Line($"Context: the build references {versioned}, which bundles the [SponsorCheck]({DocsBase}) build-time " +
             "sponsorship verifier, and requires a license-mode declaration on every build.");
        Line();
        Line($"- Chosen mode: {ModeDescription(model)}");
        Line($"- Placement: {PlacementDescription(placement, owner)}");
        Line();
        Line("## Change to make");
        Line();
        Line($"File to edit: {fileToEdit}");
        Line();
        Line(instruction);
        Line();
        Line(MsBuildXml.Fenced(snippet));
        Line();
        Line("## Expected build outcome");
        Line();
        Line(outcome);

        if (notes.Count > 0)
        {
            Line();
            Line("Notes:");
            Line();
            foreach (var note in notes)
            {
                Line($"- {note}");
            }
        }

        Line();
        Line("## Reference");
        Line();
        Line($"- Consumer guide: {ConsumerDocs}");
        Line($"- Diagnostic codes: {VerifierDocs}");

        return builder.ToString().TrimEnd();
    }
}
