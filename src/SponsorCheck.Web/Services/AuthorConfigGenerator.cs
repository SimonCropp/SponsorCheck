namespace SponsorCheck.Web.Services;

public sealed record AuthorOutput(
    string ReferenceTitle,
    string Reference,
    string Credentials,
    string ReleaseNotes,
    string Checklist,
    string Markdown);

/// <summary>
/// Pure text generation from an <see cref="AuthorModel"/>. No Blazor / DI dependencies so it can be
/// unit-tested directly and snapshot-verified. Produces the copy-pasteable blocks the author flow
/// shows — the SponsorCheck reference for the chosen repo shape, the credential setup, the
/// consumer-facing release notes, a checklist — plus a composed markdown document ("Copy as
/// markdown") suitable for handing to a teammate or an AI coding agent.
/// </summary>
public static class AuthorConfigGenerator
{
    const string docsBase = DocLinks.Base;
    const string authorDocs = DocLinks.AuthorSetup;
    const string bundlerDocs = DocLinks.BundlerCodes;
    const string verifierDocs = DocLinks.VerifierCodes;
    const string wizardUrl = "https://simoncropp.github.io/SponsorCheck/author";

    public static AuthorOutput Generate(AuthorModel model)
    {
        var (referenceTitle, reference) = BuildReference(model);
        var credentials = BuildCredentialSetup(model);
        var releaseNotes = BuildReleaseNotes(model);
        var checklist = BuildChecklist(model);
        var markdown = BuildMarkdown(model, reference, credentials, releaseNotes, checklist);
        return new(referenceTitle, reference, credentials, releaseNotes, checklist, markdown);
    }

    // ---- Output 1a: the SponsorCheck reference for the chosen repo shape ----

    static string Version(AuthorModel model)
    {
        if (string.IsNullOrWhiteSpace(model.SponsorCheckVersion))
        {
            return WizardDefaults.SponsorCheckVersion;
        }

        return model.SponsorCheckVersion.Trim();
    }

    static List<(string Name, string Value)> MetadataAttributes(AuthorModel model)
    {
        var attributes = new List<(string Name, string Value)>();
        foreach (var (platform, account) in model.EnabledPlatforms)
        {
            attributes.Add((platform.AuthorAccountMetadata, account));
        }

        if (model.OwnerMode && !string.IsNullOrWhiteSpace(model.OwnerId))
        {
            attributes.Add(("SponsorOwner", model.OwnerId.Trim()));
        }

        if (model.CheckTransitive)
        {
            attributes.Add(("CheckTransitiveReferences", "true"));
        }

        foreach (var info in OverrideInfo.All)
        {
            var selection = model.Selection(info.Kind);
            if (selection.HasSeverity)
            {
                attributes.Add((info.SeverityMetadata, selection.SeverityValue));
            }

            if (selection.HasMessage)
            {
                attributes.Add((info.MessageMetadata, selection.Message.Trim()));
            }
        }

        if (model.HasLandingUrl)
        {
            attributes.Add(("SponsorLandingUrl", model.LandingUrl.Trim()));
        }

        if (model.ParsedPrivateSponsorMaxTermMonths is { } privateMonths)
        {
            attributes.Add(("PrivateSponsorMaxTermMonths", privateMonths.ToString(CultureInfo.InvariantCulture)));
        }

        return attributes;
    }

    static string ExemptionItems(AuthorModel model) =>
        string.Join(
            '\n',
            model.CompletedExemptions.Select(_ => MsBuildXml.SelfClosingElement(
                "SponsorExemption",
                ExemptionAttributes(_),
                inlineCount: 1)));

    static IReadOnlyList<(string Name, string Value)> ExemptionAttributes(ExemptionEntry entry)
    {
        var attributes = new List<(string, string)>
        {
            ("Include", entry.Name.Trim()),
            ("Message", entry.Message.Trim())
        };
        if (entry.ParsedMaxTermMonths is { } months)
        {
            attributes.Add(("MaxTermMonths", months.ToString(CultureInfo.InvariantCulture)));
        }

        return attributes;
    }

    static string SecretsId(AuthorModel model)
    {
        var source = GetSource(model);
        var slug = new string([.. source.Trim().ToLowerInvariant().Select(_ => char.IsAsciiLetterOrDigit(_) ? _ : '-')]);
        return $"{slug}-sponsorcheck-secrets";
    }

    static string GetSource(AuthorModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.OwnerId))
        {
            return model.OwnerId;
        }

        if (!string.IsNullOrWhiteSpace(model.PackageId))
        {
            return model.PackageId;
        }

        return "packages";
    }

    static (string Title, string Content) BuildReference(AuthorModel model)
    {
        var metadata = MetadataAttributes(model);
        var exemptions = ExemptionItems(model);

        if (model.RepoShape == RepoShape.SingleProject)
        {
            var attributes = new List<(string Name, string Value)>
            {
                ("Include", "SponsorCheck"),
                ("Version", Version(model)),
                ("PrivateAssets", "all")
            };
            attributes.AddRange(metadata);
            var reference = MsBuildXml.SelfClosingElement("PackageReference", attributes, inlineCount: 3);
            if (exemptions.Length > 0)
            {
                reference = $"{reference}\n{exemptions}";
            }

            return ("PackageReference (library .csproj)", reference);
        }

        var packageVersionAttributes = new List<(string Name, string Value)>
        {
            ("Include", "SponsorCheck"),
            ("Version", Version(model))
        };
        packageVersionAttributes.AddRange(metadata);
        var packageVersion = MsBuildXml.SelfClosingElement("PackageVersion", packageVersionAttributes, inlineCount: 2);

        var builder = new StringBuilder();
        builder.AppendLine("<!-- Directory.Packages.props — the version and SponsorCheck metadata live on the PackageVersion -->");
        builder.AppendLine(packageVersion);

        if (model.RepoShape == RepoShape.MonorepoCpm)
        {
            builder.AppendLine();
            builder.AppendLine("<!-- Directory.Build.props — one UserSecretsId shared by every project so all read the same secrets.json -->");
            builder.AppendLine(MsBuildXml.PropertyGroup([("UserSecretsId", SecretsId(model))]));
            if (exemptions.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("<!-- Directory.Build.props — exemptions declared once cover every packable project -->");
                builder.AppendLine(exemptions);
            }

            builder.AppendLine();
            builder.AppendLine("<!-- each packable .csproj — bare reference; the metadata comes from the PackageVersion -->");
            builder.Append("<PackageReference Include=\"SponsorCheck\" PrivateAssets=\"all\" />");
            return ("SponsorCheck reference (monorepo files)", builder.ToString());
        }

        builder.AppendLine();
        builder.AppendLine("<!-- the library .csproj — bare reference; the metadata comes from the PackageVersion -->");
        builder.Append("<PackageReference Include=\"SponsorCheck\" PrivateAssets=\"all\" />");
        if (exemptions.Length > 0)
        {
            builder.AppendLine();
            builder.Append(exemptions);
        }

        return ("SponsorCheck reference (Directory.Packages.props + library .csproj)", builder.ToString());
    }

    // ---- Output 1b: credential setup ----

    static string BuildCredentialSetup(AuthorModel model)
    {
        var platforms = model.EnabledPlatforms.Select(_ => _.Platform).ToList();
        if (platforms.Count == 0)
        {
            return "";
        }

        var builder = new StringBuilder();
        void Line(string text = "") => builder.AppendLine(text);

        Line("# Local dev — store tokens outside the repo with user-secrets.");
        if (model.RepoShape == RepoShape.MonorepoCpm)
        {
            Line("# The shared UserSecretsId lives in Directory.Build.props, so target it directly:");
            foreach (var platform in platforms)
            {
                var note = platform.CredentialRequired ? "required" : "optional";
                Line($"dotnet user-secrets set \"{platform.UserSecretKey}\" \"<token>\" --id {SecretsId(model)}   # {note}");
            }
        }
        else
        {
            Line("# Run from the directory containing the library .csproj:");
            Line("dotnet user-secrets init");
            foreach (var platform in platforms)
            {
                var note = platform.CredentialRequired ? "required" : "optional";
                Line($"dotnet user-secrets set \"{platform.UserSecretKey}\" \"<token>\"   # {note}");
            }
        }

        Line();
        Line("# CI — expose each token as an environment variable of the SAME name");
        Line("# (MSBuild auto-imports it as the matching <...Token> property; GITHUB_TOKEN does NOT auto-flow):");
        foreach (var platform in platforms)
        {
            var note = platform.CredentialRequired ? "required" : "optional";
            Line($"{platform.TokenProperty}   # {note}");
        }

        Line();
        Line("# Pull-request CI builds: providers withhold encrypted secrets, so bundling is skipped");
        Line("# (the package packs without the verifier and a build message records it). To force");
        Line("# bundling on PR builds that do have the credential, set the MSBuild property:");
        Line("#   <SponsorCheckBundleInPullRequest>true</SponsorCheckBundleInPullRequest>");

        Line();
        Line("# Token notes:");
        foreach (var platform in platforms)
        {
            Line($"# - {platform.DisplayName}: {platform.TokenHelp}");
        }

        return builder.ToString().TrimEnd();
    }

    // ---- Output 2: consumer release notes (markdown) ----

    static string BuildReleaseNotes(AuthorModel model)
    {
        var id = string.IsNullOrWhiteSpace(model.PackageId) ? "ThePackage" : model.PackageId.Trim();
        var version = string.IsNullOrWhiteSpace(model.PackageVersion) ? "" : model.PackageVersion.Trim();
        var versioned = version.Length == 0 ? $"`{id}`" : $"`{id}` {version}";
        var enabled = model.EnabledPlatforms.ToList();
        var primary = enabled.Count > 0 ? enabled[0].Platform : Platform.GitHub;

        var builder = new StringBuilder();
        void Line(string text = "") => builder.AppendLine(text);

        Line("## Sponsorship is now checked at build time");
        Line();
        Line($"{versioned} now bundles [SponsorCheck]({docsBase}) — a build-time check that asks teams using this package to sponsor its ongoing development. SponsorCheck adds **no runtime dependency**; the check runs only during the build.");
        Line();
        Line($"After upgrading, every build looks for one of the options below. {NoLicenseConsequence(model)}");
        Line();
        Line("### What you need to do");
        Line();
        Line(PlacementSentence(model));
        Line();

        var optionNumber = 1;

        // Option — sponsor
        Line($"#### Option {optionNumber++} — Sponsor");
        Line();
        Line("Sponsor the author, then declare the account you sponsor under:");
        Line();
        Line(MsBuildXml.Fenced(SponsorSnippet(model, id, version, primary)));
        if (enabled.Count > 1)
        {
            Line();
            Line("Declare the attribute for whichever platform you sponsor on (any single match is enough):");
            Line();
            foreach (var (platform, account) in enabled)
            {
                var url = model.HasLandingUrl ? model.LandingUrl.Trim() : platform.SponsorUrl(account);
                Line($"- `{OwnerPrefixed(model, platform.ConsumerAccountMetadata)}` — sponsor at {url}");
            }
        }
        else if (enabled.Count == 1)
        {
            var (platform, account) = enabled[0];
            var url = model.HasLandingUrl ? model.LandingUrl.Trim() : platform.SponsorUrl(account);
            Line();
            Line($"Sponsor at {url}");
        }

        Line();
        Line("_Started sponsoring only recently?_ If your sponsorship began after this version was published, the bundled list cannot contain you yet — add `SponsorshipStart=\"yyyy-MM-dd\"` alongside the account to attest to the start date until you upgrade to a later release.");
        Line();

        // Option — private license
        Line($"#### Option {optionNumber++} — Private license");
        Line();
        Line("If you have a private (B2B) licensing arrangement, set a time-bounded license. The value is `yyyy-MM` (the last covered month) and is valid through the end of that month (UTC). Values more than one year out are rejected, so renewal is a periodic one-line edit:");
        Line();
        Line(MsBuildXml.Fenced(LicenseSnippet(model, id, version)));
        Line();

        // Option — publisher-defined exemptions
        if (model.HasExemptions)
        {
            Line($"#### Option {optionNumber++} — Claim an exemption");
            Line();
            Line("The following exemptions are defined for this package. If one applies, claim it by name — the build passes with a warning quoting the exemption's criteria (not a breach message):");
            Line();
            foreach (var exemption in model.CompletedExemptions)
            {
                var bound = exemption.ParsedMaxTermMonths is { } months
                    ? $" **Time-bounded:** also set `SponsorshipExemptionUntil` (`yyyy-MM`), no more than {months} month{(months == 1 ? "" : "s")} ahead of the build date."
                    : "";
                Line($"- `{exemption.Name.Trim()}` — {exemption.Message.Trim()}{bound}");
            }

            Line();
            Line(MsBuildXml.Fenced(ExemptionSnippet(model, id, version)));
            Line();
            if (model.CompletedExemptions.Any(_ => _.ParsedMaxTermMonths is not null))
            {
                Line("A time-bounded exemption stops applying at the end of its `SponsorshipExemptionUntil` month and the build then fails, so the claim has to be reviewed and renewed rather than left in place indefinitely.");
                Line();
            }
        }

        // Option — opt out
        Line($"#### Option {optionNumber} — Opt out");
        Line();
        Line($"You can opt out of the check. {IgnoreConsequence(model)}");
        Line();
        Line(MsBuildXml.Fenced(IgnoreSnippet(model, id, version)));
        Line();

        if (model.CheckTransitive)
        {
            Line($"> This package checks **transitive** references too. A project that pulls in `{id}` indirectly has no `<PackageReference>` of its own to configure, so add a direct reference declaring one of the options above.");
            Line();
        }

        Line("### Reference");
        Line();
        Line($"Build diagnostics use `SC0xx` codes — see the [verifier diagnostic reference]({verifierDocs}).");

        return builder.ToString().TrimEnd();
    }

    // ---- snippet helpers ----

    static string OwnerPrefixed(AuthorModel model, string name) =>
        model.OwnerMode && !string.IsNullOrWhiteSpace(model.OwnerId)
            ? $"{model.OwnerId.Trim()}_{name}"
            : name;

    static string SponsorSnippet(AuthorModel model, string id, string version, Platform platform)
    {
        var attribute = platform.ConsumerAccountMetadata;
        if (model.OwnerMode)
        {
            var property = OwnerPrefixed(model, attribute);
            return OwnerPropertyGroup($"<{property}>your-account</{property}>");
        }

        return PerPackagePair(id, version, $"{attribute}=\"your-account\"");
    }

    static string LicenseSnippet(AuthorModel model, string id, string version)
    {
        if (model.OwnerMode)
        {
            var property = OwnerPrefixed(model, "SponsorshipLicensedUntil");
            return OwnerPropertyGroup($"<{property}>yyyy-MM</{property}>");
        }

        return PerPackagePair(id, version, "SponsorshipLicensedUntil=\"yyyy-MM\"");
    }

    static string ExemptionSnippet(AuthorModel model, string id, string version)
    {
        var exemption = model.CompletedExemptions[0];
        var name = exemption.Name.Trim();
        // A capped exemption is only claimable with an end month, so the snippet has to show both —
        // pasting the name alone would fail with SC038 on the consumer's next build.
        var bounded = exemption.ParsedMaxTermMonths is not null;
        if (model.OwnerMode)
        {
            var property = OwnerPrefixed(model, "SponsorshipExemption");
            var element = $"<{property}>{MsBuildXml.Escape(name)}</{property}>";
            if (bounded)
            {
                var untilProperty = OwnerPrefixed(model, "SponsorshipExemptionUntil");
                element = $"{element}\n  <{untilProperty}>yyyy-MM</{untilProperty}>";
            }

            return OwnerPropertyGroup(element);
        }

        var attribute = $"SponsorshipExemption=\"{MsBuildXml.Escape(name)}\"";
        if (bounded)
        {
            attribute = $"{attribute} SponsorshipExemptionUntil=\"yyyy-MM\"";
        }

        return PerPackagePair(id, version, attribute);
    }

    static string IgnoreSnippet(AuthorModel model, string id, string version)
    {
        if (model.OwnerMode)
        {
            var property = OwnerPrefixed(model, "SponsorshipLicenseIgnored");
            return OwnerPropertyGroup($"<{property}>true</{property}>");
        }

        return PerPackagePair(id, version, "SponsorshipLicenseIgnored=\"true\"");
    }

    /// <summary>Per-package mode shows both the non-CPM (<c>PackageReference</c>) and CPM (<c>PackageVersion</c>) forms.</summary>
    static string PerPackagePair(string id, string version, string attribute)
    {
        var versionAttribute = version.Length == 0 ? "" : $" Version=\"{MsBuildXml.Escape(version)}\"";
        return $"""
                <!-- Without Central Package Management: in the consuming .csproj -->
                <PackageReference Include="{MsBuildXml.Escape(id)}"{versionAttribute} {attribute} />

                <!-- With Central Package Management: on the matching PackageVersion in Directory.Packages.props -->
                <PackageVersion Include="{MsBuildXml.Escape(id)}"{versionAttribute} {attribute} />
                """;
    }

    static string OwnerPropertyGroup(string property) =>
        $"""
         <!-- Owner mode: set once as a global property, in Directory.Build.props or the consuming .csproj -->
         <PropertyGroup>
           {property}
         </PropertyGroup>
         """;

    // ---- wording helpers ----

    static string PlacementSentence(AuthorModel model)
    {
        if (model.OwnerMode)
        {
            return "This package uses **owner mode**, so you configure sponsorship **once** as a global MSBuild property (in `Directory.Build.props` or the consuming project) — it then covers every package from this author. Pick one of the following.";
        }

        return "Pick exactly one of the following and add it where the package is referenced (on the `<PackageReference>`, or on the `<PackageVersion>` in `Directory.Packages.props` if you use Central Package Management).";
    }

    static string NoLicenseConsequence(AuthorModel model)
    {
        var selection = model.Selection(OverrideKind.NoLicenseSpecified);
        var severity = selection.HasSeverity ? selection.SeverityValue : "error";
        return severity switch
        {
            "warning" => "If none is configured the build logs a warning, but does not fail.",
            "message" => "If none is configured the build logs an informational message, but does not fail.",
            _ => "If none is configured the build fails with an error."
        };
    }

    static string IgnoreConsequence(AuthorModel model)
    {
        var selection = model.Selection(OverrideKind.LicenseIgnored);
        var severity = selection.HasSeverity ? selection.SeverityValue : "warning";
        return severity switch
        {
            "error" => "The author has configured this to **fail** the build with a breach-of-license error, so opting out is not a usable escape hatch for this package.",
            "message" => "The build stays green and logs an informational breach-of-license message on every build.",
            _ => "The build stays green but logs a breach-of-license warning on every build."
        };
    }

    // ---- Output 3: checklist ----

    static string BuildChecklist(AuthorModel model)
    {
        var tokenNames = string.Join(", ", model.EnabledPlatforms
            .Where(_ => _.Platform.CredentialRequired)
            .Select(_ => $"`{_.Platform.TokenProperty}`"));

        var hashFile = model.CheckTransitive
            ? "buildTransitive/SponsorCheck.SponsorHashes.txt"
            : "build/SponsorCheck.SponsorHashes.txt";

        var builder = new StringBuilder();
        void Line(string text = "") => builder.AppendLine(text);

        if (tokenNames.Length > 0)
        {
            Line($"- [ ] Store the platform token(s) — user-secrets locally; on CI an encrypted secret exposed as {tokenNames}.");
        }

        Line("- [ ] Apply the reference snippet, then pack with the Release configuration.");
        Line($"- [ ] Inspect the produced nupkg: `{hashFile}` must be present (open the nupkg as a zip).");
        Line("- [ ] Add the release-notes markdown to the changelog / release description of the version that adds SponsorCheck.");
        if (model.HasExemptions)
        {
            Line("- [ ] Exemption names, criteria, and max terms are baked in at pack time — editing them requires a repack.");
        }

        Line($"- [ ] Full author documentation: {authorDocs}");
        Line($"- [ ] Pack-time diagnostics reference: {bundlerDocs}");

        return builder.ToString().TrimEnd();
    }

    // ---- Output 4: the composed markdown document ----

    static string BuildMarkdown(
        AuthorModel model,
        string reference,
        string credentials,
        string releaseNotes,
        string checklist)
    {
        var id = string.IsNullOrWhiteSpace(model.PackageId) ? "ThePackage" : model.PackageId.Trim();
        var platforms = string.Join(", ", model.EnabledPlatforms.Select(_ => $"{_.Platform.DisplayName} ({_.Account})"));

        var referenceInstruction = model.RepoShape switch
        {
            RepoShape.SingleProject =>
                $"Add the following to an <ItemGroup> in the packable library's .csproj (the project producing {id}):",
            RepoShape.SingleProjectCpm =>
                "Apply each block to the file named in its comment: the <PackageVersion> goes in Directory.Packages.props, the bare <PackageReference> in the packable library's .csproj (inside an <ItemGroup>).",
            _ =>
                "Apply each block to the file named in its comment: Directory.Packages.props, Directory.Build.props, and every packable .csproj (inside an <ItemGroup>)."
        };

        var builder = new StringBuilder();
        void Line(string text = "") => builder.AppendLine(text);

        Line($"# Add SponsorCheck to {id}");
        Line();
        Line($"Generated by the [SponsorCheck setup wizard]({wizardUrl}).");
        Line();
        Line($"[SponsorCheck]({docsBase}) bundles build-time sponsorship verification into the produced NuGet package at pack time — consumers of {id} are asked for a license mode on every build. No runtime dependency is added.");
        Line();
        Line($"- Platforms: {platforms}");
        Line($"- Owner mode: {(model.OwnerMode ? $"on (owner id '{model.OwnerId.Trim()}')" : "off")}");
        Line($"- Transitive checking: {(model.CheckTransitive ? "on" : "off")}");
        if (model.HasExemptions)
        {
            var names = model.CompletedExemptions.Select(_ => _.ParsedMaxTermMonths is { } months
                ? $"{_.Name.Trim()} (max {months}mo)"
                : _.Name.Trim());
            Line($"- Exemptions: {string.Join(", ", names)}");
        }

        Line();
        Line("## 1. Reference SponsorCheck");
        Line();
        Line(referenceInstruction);
        Line();
        Line(MsBuildXml.Fenced(reference));
        Line();
        Line("## 2. Provide platform credentials");
        Line();
        Line("The bundler fetches the sponsor list at pack time and needs these credentials:");
        Line();
        Line(MsBuildXml.Fenced(credentials, "sh"));
        Line();
        Line("## 3. Release notes for consumers");
        Line();
        Line("Paste into the release notes / changelog of the version that adds SponsorCheck:");
        Line();
        Line("````markdown");
        Line(releaseNotes);
        Line("````");
        Line();
        Line("## 4. Checklist");
        Line();
        Line(checklist);

        return builder.ToString().TrimEnd();
    }
}
