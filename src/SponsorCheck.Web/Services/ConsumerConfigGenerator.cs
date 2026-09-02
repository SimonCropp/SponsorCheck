namespace SponsorCheck.Web.Services;

/// <summary>
/// Pure text generation from a <see cref="ConsumerModel"/>. No Blazor / DI dependencies so it can be
/// unit-tested directly and snapshot-verified. Produces the consumer-side snippet, the file it
/// belongs in, the expected build outcome, and a self-contained markdown document ("Copy as
/// markdown") suitable for handing to a teammate or an AI coding agent.
/// </summary>
public static class ConsumerConfigGenerator
{
    const string docsBase = DocLinks.Base;
    const string consumerDocs = DocLinks.ConsumerUsage;
    const string verifierDocs = DocLinks.VerifierCodes;

    // wizardUrl: The flow that produced the output. Named in the markdown so a reader
    // can re-run it: the consumer flow by default, the package-specific flow when that is where the
    // visitor came in.
    public static ConsumerOutput Generate(ConsumerModel model, string wizardUrl = WizardLinks.Consumer)
    {
        var id = string.IsNullOrWhiteSpace(model.PackageId) ? "ThePackage" : model.PackageId.Trim();
        var version = model.PackageVersion.Trim();
        var owner = string.IsNullOrWhiteSpace(model.OwnerId) ? "owner" : model.OwnerId.Trim();
        var placement = model.Placement;

        var attributes = AttributesFor(model);
        var (title, snippet, fileToEdit, instruction) = Placement(placement, id, version, owner, attributes);
        var outcome = BuildOutcome(model, placement);
        var notes = Notes(model, placement, owner);
        var markdown = BuildMarkdown(model, placement, id, version, owner, snippet, fileToEdit, instruction, outcome, notes, wizardUrl);

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

                // A private declaration decides the build outright — ApplySponsor checks it first, and
                // an expired one fails with SC056 rather than falling through to the start-date path.
                // So SponsorshipStart alongside it is never read. Emitting it anyway put a dead
                // attribute in the consumer's project that reads like a fallback for when the claim
                // lapses, which is the one thing it is not. BuildOutcome already leads with the
                // private wording for this case; the snippet now agrees with it.
                if (model.StartedAfterRelease &&
                    !string.IsNullOrWhiteSpace(model.SponsorshipStart) &&
                    !model.PrivateDeclared)
                {
                    attributes.Add(("SponsorshipStart", model.SponsorshipStart.Trim()));
                }

                if (model.PrivateDeclared)
                {
                    attributes.Add(("SponsorshipPrivateUntil", model.PrivateUntilMonth.Trim()));
                }

                break;
            case ConsumerLicenseMode.License:
                attributes.Add(("SponsorshipLicensedUntil", model.LicensedUntilMonth.Trim()));
                break;
            case ConsumerLicenseMode.Exemption:
                attributes.Add(("SponsorshipExemption", model.ExemptionName.Trim()));
                if (model.HasExemptionUntil)
                {
                    attributes.Add(("SponsorshipExemptionUntil", model.ExemptionUntilMonth.Trim()));
                }

                break;
            case ConsumerLicenseMode.Ignore:
                attributes.Add(("SponsorshipLicenseIgnored", "true"));
                break;
        }

        return attributes;
    }

    static (string Title, string Snippet, string FileToEdit, string Instruction) Placement(
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
                $"Add the properties below to a <PropertyGroup>. One declaration covers every package from owner '{owner}'; the property names are exact (the owner prefix is baked into the package).");
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
                $"Add the new attribute(s) to the existing <PackageVersion Include=\"{id}\"> element — do not create a second element, and do not put the metadata on the <PackageReference>.");
        }

        return (
            "PackageReference (consuming .csproj)",
            MsBuildXml.SelfClosingElement("PackageReference", elementAttributes, inlineCount),
            $"The consuming project's .csproj — the file holding the <PackageReference> for '{id}'.",
            $"Add the new attribute(s) to the existing <PackageReference Include=\"{id}\"> element — do not create a second reference.");
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
            // Private first: it decides the build outright, so when both qualifiers are set the
            // SponsorshipStart wording would describe a path that never runs.
            case ConsumerLicenseMode.Sponsor when model.PrivateDeclared:
            {
                var formatCode = CodeFor(placement, "SC050", "SC051", "SC052");
                var capCode = CodeFor(placement, "SC053", "SC054", "SC055");
                var expiredCode = CodeFor(placement, "SC056", "SC057", "SC058");
                var months = model.PrivateSponsorMaxTermMonths;
                var until = model.PrivateUntilMonth.Trim();
                var capSource = model.Facts is { BundlesSponsorCheck: true }
                    ? $"This package caps the claim at {months} month{(months == 1 ? "" : "s")}"
                    : $"The publisher caps the claim (the default is {months} months, and this package could not be inspected to confirm)";
                return
                    $"Private (GitHub Sponsors) and incognito (Open Collective) sponsorships are deliberately excluded from the bundled hash list, so there is nothing for the verifier to match — the declaration is trusted instead. The build passes through the end of {until} (UTC) and logs a high-priority SC059 audit message naming the account and the end month. After that it fails with {expiredCode} until the claim is renewed, which is the point: a private sponsorship that quietly lapsed should stop working rather than ride along forever. {capSource}, so a month further out fails with {capCode}. A value that isn't 'yyyy-MM' fails with {formatCode}.";
            }

            case ConsumerLicenseMode.Sponsor when model.StartedAfterRelease && !string.IsNullOrWhiteSpace(model.SponsorshipStart):
            {
                var futureCode = CodeFor(placement, "SC015", "SC016", "SC028");
                var start = model.SponsorshipStart.Trim();
                if (model.Facts?.PackDate is { } packDate &&
                    TryParseDate(start, out var startDate) &&
                    TryParseDate(packDate, out var packedDate))
                {
                    if (startDate <= packedDate)
                    {
                        var noMatchCode = CodeFor(placement, "SC007", "SC008", "SC024");
                        return
                            $"The declared start ({start}) is on or before this version's pack date ({packDate}) — the boundary is strict, so the attestation does NOT bypass the check. The normal bundled-list check applies: the account must have been in the list at pack time, or the build fails with {noMatchCode}. Re-check the actual start date, or drop SponsorshipStart.";
                    }

                    return
                        $"This version was packed on {packDate}, so the attested start ({start}) is after it: the build passes and logs a high-priority SC017 audit message naming the unverified sponsor. A future date fails with {futureCode}. The attestation self-expires: after upgrading to a version packed later than the start date, the normal bundled-list check applies again and SponsorshipStart can be dropped.";
                }

                return
                    $"When {start} is after the package version's pack date, the build passes and logs a high-priority SC017 audit message naming the unverified sponsor — the declaration is trusted because the bundled list cannot contain a sponsorship that began after it was frozen. A future date fails with {futureCode}. The attestation self-expires: after upgrading to a version packed later than the start date, the normal bundled-list check applies again and SponsorshipStart can be dropped.";
            }

            case ConsumerLicenseMode.Sponsor:
            {
                var noMatchCode = CodeFor(placement, "SC007", "SC008", "SC024");
                var packedClause = model.Facts?.PackDate is { } packedOn
                    ? $" This version was packed on {packedOn} — a sponsorship that began after that date needs the attested start."
                    : "";

                // When the bundled list was actually read, the outcome is not a forecast. Saying
                // "passes silently when the account was in the list" to someone whose account
                // demonstrably is not would be the wizard withholding an answer it already has.
                if (model.NoEnteredAccountIsBundled)
                {
                    // The pack date belongs inside the sentence here rather than appended as
                    // packedClause does: this branch already says "began after this version was
                    // packed", so the tail would repeat it back.
                    var packedOnClause = model.Facts?.PackDate is { } date ? $" ({date})" : "";
                    return
                        $"Fails with {noMatchCode}: no account declared here is in {model.PackageId} {model.PackageVersion}'s bundled list, which was checked directly against the package. Three things put a real sponsor in that position — the account or platform is not the one the sponsorship is under; the sponsorship began after this version was packed{packedOnClause}, which needs SponsorshipStart=\"yyyy-MM-dd\"; or it is private on GitHub Sponsors or incognito on Open Collective, which is never bundled at all and needs SponsorshipPrivateUntil=\"yyyy-MM\". Re-run the wizard and answer yes to whichever applies.";
                }

                return
                    $"Passes silently when any declared account was in the bundled sponsor list at the version's pack time. If the build still fails with {noMatchCode}, either the account or platform doesn't match what the author bundled, the sponsorship began after this version was packed — in that case add SponsorshipStart=\"yyyy-MM-dd\" — or the sponsorship is private (GitHub Sponsors) or incognito (Open Collective), which is never bundled and needs SponsorshipPrivateUntil=\"yyyy-MM\" instead. Re-run the wizard and answer yes to whichever applies.{packedClause}";
            }

            case ConsumerLicenseMode.License:
            {
                var expiredCode = CodeFor(placement, "SC009", "SC010", "SC025");
                var capCode = CodeFor(placement, "SC035", "SC036", "SC037");
                var month = model.LicensedUntilMonth.Trim();
                return
                    $"Passes silently through the end of {month} (UTC). After that the build fails with {expiredCode} until the value is renewed — a one-line edit. Values more than one year from the build clock are rejected with {capCode}.";
            }

            case ConsumerLicenseMode.Exemption:
            {
                var warnCode = CodeFor(placement, "SC029", "SC030", "SC031");
                var unknownCode = CodeFor(placement, "SC032", "SC033", "SC034");
                if (model.Facts is { BundlesSponsorCheck: true } facts)
                {
                    if (facts.Exemptions.Count == 0)
                    {
                        return
                            $"This package defines no exemptions — claiming one fails with {unknownCode}. Pick one of the other modes.";
                    }

                    var match = facts.FindExemption(model.ExemptionName);
                    if (match == null)
                    {
                        var names = string.Join(", ", facts.Exemptions.Select(_ => _.Name));
                        return
                            $"'{model.ExemptionName.Trim()}' is not an exemption this package defines ({names}) — the build fails with {unknownCode}, and that error lists the defined names.";
                    }

                    var criteria =
                        $"Passes with a {warnCode} warning quoting the publisher's criteria for '{match.Name}': \"{match.Message}\" — the build log records the specific carve-out being claimed rather than a generic breach message.";
                    if (match.MaxTermMonths is { } months)
                    {
                        var missingCode = CodeFor(placement, "SC038", "SC039", "SC040");
                        var capCode = CodeFor(placement, "SC044", "SC045", "SC046");
                        var expiredCode = CodeFor(placement, "SC047", "SC048", "SC049");
                        var until = model.ExemptionUntilMonth.Trim();
                        var through = until.Length == 0 ? "the declared month" : until;
                        return
                            $"{criteria} The publisher time-bounds this exemption to {months} month{(months == 1 ? "" : "s")}, so SponsorshipExemptionUntil is required — omitting it fails with {missingCode}, and a month more than {months} past the build clock fails with {capCode}. It passes through the end of {through} (UTC); after that the build fails with {expiredCode}, which is the point: the claim has to be re-checked rather than left in place.";
                    }

                    if (model.HasExemptionUntil)
                    {
                        var expiredCode = CodeFor(placement, "SC047", "SC048", "SC049");
                        return
                            $"{criteria} This exemption is not time-bounded by the publisher, but the end month declared here is still enforced: after {model.ExemptionUntilMonth.Trim()} the build fails with {expiredCode} until the claim is renewed or another mode is chosen.";
                    }

                    return criteria;
                }

                return
                    $"Passes with a {warnCode} warning whose body is the publisher's own criteria text — the build log records the specific carve-out being claimed rather than a generic breach message. A name the publisher did not define fails with {unknownCode}; that error lists the available exemption names.";
            }

            case ConsumerLicenseMode.Ignore:
            {
                var warnCode = CodeFor(placement, "SC005", "SC006", "SC023");
                var factSeverity = model.FactSeverity("SC005", "SC006", "SC023");
                if (factSeverity == "error")
                {
                    return
                        $"This publisher escalated the opt-out to an error at pack time — the build FAILS with {warnCode} instead of warning, so SponsorshipLicenseIgnored is not a usable escape hatch for this package. Pick one of the other modes.";
                }

                if (factSeverity == "message")
                {
                    return
                        $"Passes with a {warnCode} informational message on every build — this publisher softened the default breach-of-license warning to a message.";
                }

                return
                    $"Passes with a {warnCode} breach-of-license warning on every build. The author may have raised this diagnostic's severity to error at pack time, in which case the build fails instead of warning.";
            }

            default:
                return "";
        }
    }

    static bool TryParseDate(string value, out DateTime date) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    static List<string> Notes(ConsumerModel model, Placement placement, string owner)
    {
        var notes = new List<string>();

        if (model.Mode == ConsumerLicenseMode.Sponsor)
        {
            if (model.EnabledPlatforms.Count() > 1)
            {
                notes.Add(
                    "The verifier passes on any single match — declaring accounts for several platforms is one cheap hash lookup each, and keeps builds green if the author migrates platforms.");
            }

            notes.Add(
                "The bundled list is frozen per package version: versions that pass keep passing even if sponsorship later lapses; a lapse surfaces when upgrading to a version packed after it.");
        }

        if (model.Facts is { BundlesSponsorCheck: true, CheckTransitive: true })
        {
            notes.Add(
                "This package checks transitive references too — projects that pull it in indirectly also verify, and resolve it the same way: with a direct reference declaring a license mode.");
        }

        switch (placement)
        {
            case Models.Placement.PerPackageProject:
                notes.Add(
                    "If the solution adopts Central Package Management later, the metadata moves to the matching <PackageVersion> in Directory.Packages.props — wrong placement raises SC020, both places raises SC019.");
                break;
            case Models.Placement.PerPackageCpm:
                notes.Add(
                    "Keep the metadata in exactly one place: on the <PackageVersion>. Setting it on the <PackageReference> as well raises SC019; only on the <PackageReference> raises SC020.");
                break;
            default:
                notes.Add(
                    $"The properties cover every package from owner '{owner}' — one declaration for the whole family. The exact property names are also printed in the SC021 error message.");
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
        IReadOnlyList<string> notes,
        string wizardUrl)
    {
        var versioned = version.Length == 0 ? $"`{id}`" : $"`{id}` {version}";

        var builder = new StringBuilder();
        void Line(string text = "") => builder.AppendLine(text);

        Line($"# Configure SponsorCheck licensing for {id}");
        Line();
        Line($"Generated by the [SponsorCheck setup wizard]({wizardUrl}).");
        Line();
        Line($"Context: the build references {versioned}, which bundles the [SponsorCheck]({docsBase}) build-time sponsorship verifier, and requires a license-mode declaration on every build.");
        Line();
        Line($"- Chosen mode: {ModeDescription(model)}");
        Line($"- Placement: {PlacementDescription(placement, owner)}");
        if (model.Facts is { BundlesSponsorCheck: true } packageFacts)
        {
            var packedClause = packageFacts.PackDate is { } packedOn ? $", packed {packedOn}" : "";
            Line($"- Package facts (read from the published nupkg): version {packageFacts.Version}{packedClause}, transitive checking {(packageFacts.CheckTransitive ? "on" : "off")}");
        }

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
        Line($"- Consumer guide: {consumerDocs}");
        Line($"- Diagnostic codes: {verifierDocs}");

        return builder.ToString().TrimEnd();
    }
}
