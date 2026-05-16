public sealed class BundleSponsorListTask :
    Microsoft.Build.Utilities.Task
{
    public string GitHubSponsorsAccountFromRef { get; set; } = "";
    public string GitHubSponsorsAccountFromVer { get; set; } = "";
    public string OpenCollectiveAccountFromRef { get; set; } = "";
    public string OpenCollectiveAccountFromVer { get; set; } = "";
    public string PolarAccountFromRef { get; set; } = "";
    public string PolarAccountFromVer { get; set; } = "";

    public string GitHubToken { get; set; } = "";
    public string OpenCollectiveToken { get; set; } = "";
    public string PolarToken { get; set; } = "";
    public string UserSecretsId { get; set; } = "";
    public string OverrideListPath { get; set; } = "";

    public string SponsorLandingUrlFromRef { get; set; } = "";
    public string SponsorLandingUrlFromVer { get; set; } = "";

    public string NoLicenseSpecifiedSeverityOverrideFromRef { get; set; } = "";
    public string NoLicenseSpecifiedSeverityOverrideFromVer { get; set; } = "";
    public string LicenseIgnoredSeverityOverrideFromRef { get; set; } = "";
    public string LicenseIgnoredSeverityOverrideFromVer { get; set; } = "";
    public string InvalidAccountSeverityOverrideFromRef { get; set; } = "";
    public string InvalidAccountSeverityOverrideFromVer { get; set; } = "";
    public string LicenseExpiredSeverityOverrideFromRef { get; set; } = "";
    public string LicenseExpiredSeverityOverrideFromVer { get; set; } = "";

    public string NoLicenseSpecifiedMessageOverrideFromRef { get; set; } = "";
    public string NoLicenseSpecifiedMessageOverrideFromVer { get; set; } = "";
    public string LicenseIgnoredMessageOverrideFromRef { get; set; } = "";
    public string LicenseIgnoredMessageOverrideFromVer { get; set; } = "";
    public string InvalidAccountMessageOverrideFromRef { get; set; } = "";
    public string InvalidAccountMessageOverrideFromVer { get; set; } = "";
    public string LicenseExpiredMessageOverrideFromRef { get; set; } = "";
    public string LicenseExpiredMessageOverrideFromVer { get; set; } = "";

    [Required] public string VerifierTargetsTemplatePath { get; set; } = "";
    [Required] public string ThePackageId { get; set; } = "";
    [Required] public string OutputHashListPath { get; set; } = "";
    [Required] public string OutputVerifierTargetsPath { get; set; } = "";
    [Required] public string OutputPackDatePath { get; set; } = "";
    [Required] public string OutputAuthorAccountsPath { get; set; } = "";
    [Required] public string OutputSeverityOverridesPath { get; set; } = "";
    [Required] public string OutputMessageOverridesPath { get; set; } = "";
    [Required] public string OutputLandingUrlPath { get; set; } = "";
    public string OverridePackDate { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            EnsureDirectory(OutputHashListPath);
            EnsureDirectory(OutputVerifierTargetsPath);
            EnsureDirectory(OutputSeverityOverridesPath);
            EnsureDirectory(OutputMessageOverridesPath);
            EnsureDirectory(OutputLandingUrlPath);

            // Author-supplied SponsorLandingUrl replaces the per-platform sponsor URLs in
            // consumer-side diagnostic messages. Always write the sidecar (empty when unset)
            // so the verifier can rely on the file existing.
            var landingUrl = PackageMetadataMerger.Merge("SponsorLandingUrl", SponsorLandingUrlFromRef, SponsorLandingUrlFromVer) ?? "";
            File.WriteAllText(OutputLandingUrlPath, landingUrl);

            if (!TryResolveSeverityOverrides(out var severityOverrides))
            {
                return false;
            }

            SeverityOverrideFile.Write(OutputSeverityOverridesPath, severityOverrides);

            if (!TryResolveMessageOverrides(out var messageOverrides))
            {
                return false;
            }

            MessageOverrideFile.Write(OutputMessageOverridesPath, messageOverrides);

            var enabled = ResolveEnabledPlatforms();
            if (enabled.Count == 0)
            {
                SponsorCheckLog.Error(
                    Log,
                    "SC101",
                    "SponsorCheck: at least one platform account metadata must be set on the PackageReference or PackageVersion (e.g. GitHubSponsorsAccount=\"acmecorp\").");
                return false;
            }

            IReadOnlyList<SponsorEntry>? entries;
            if (string.IsNullOrWhiteSpace(OverrideListPath))
            {
                entries = FetchFromPlatformsAsync(enabled).GetAwaiter().GetResult();
            }
            else
            {
                entries = FetchFromOverride();
            }

            var hashes = entries
                .Select(_ => SponsorHasher.Hash(_.Platform, _.Account))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(_ => _, StringComparer.Ordinal)
                .ToList();

            File.WriteAllLines(OutputHashListPath, hashes);
            EnsureDirectory(OutputAuthorAccountsPath);
            AuthorAccountsFile.Write(OutputAuthorAccountsPath, enabled);
            EnsureDirectory(OutputPackDatePath);
            string packDate;
            if (string.IsNullOrWhiteSpace(OverridePackDate))
            {
                packDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                packDate = OverridePackDate.Trim();
            }
            File.WriteAllText(OutputPackDatePath, packDate);
            var template = File.ReadAllText(VerifierTargetsTemplatePath);
            // Substitute package id into target/item names. Package IDs are restricted to
            // alphanumeric + . _ - so MSBuild-safe; we replace . - with _ to keep MSBuild
            // identifier rules happy (no dots/dashes in target/item names).
            var sanitizedId = Sanitize(ThePackageId);
            var rendered = template
                .Replace("__SC_PACKAGE_ID__", sanitizedId)
                .Replace(">__SC_PACKAGE_ID_RAW__<", $">{ThePackageId}<");
            File.WriteAllText(OutputVerifierTargetsPath, rendered);

            Log.LogMessage(
                MessageImportance.High,
                $"SponsorCheck: bundled {hashes.Count} sponsor entries across {entries.Select(_ => _.Platform).Distinct(StringComparer.OrdinalIgnoreCase).Count()} platform(s) into '{ThePackageId}'.");
            return true;
        }
        catch (MissingCredentialException exception)
        {
            SponsorCheckLog.Error(Log, "SC102", exception.Message);
            return false;
        }
        catch (MaintenanceFeeException exception)
        {
            SponsorCheckLog.Error(Log, "SC100", exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
    }

    bool TryResolveSeverityOverrides(out Dictionary<string, Severity> overrides)
    {
        overrides = new(StringComparer.Ordinal);
        foreach (var entry in OverrideableCodes.All)
        {
            var (fromRef, fromVer) = SeverityValuesFor(entry.Stem);
            var raw = PackageMetadataMerger.Merge(entry.SeverityMetadataName, fromRef, fromVer);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!SeverityOverrideFile.TryParseSeverity(raw!, out var severity))
            {
                SponsorCheckLog.Error(
                    Log,
                    "SC104",
                    $"{entry.SeverityMetadataName}='{raw}' is not a recognized severity. Allowed: error, warning, message.");
                return false;
            }

            overrides[entry.Code] = severity;
            overrides[entry.CpmCode] = severity;
        }

        return true;
    }

    bool TryResolveMessageOverrides(out Dictionary<string, string> overrides)
    {
        overrides = new(StringComparer.Ordinal);
        foreach (var entry in OverrideableCodes.All)
        {
            var (fromRef, fromVer) = MessageValuesFor(entry.Stem);
            var raw = PackageMetadataMerger.Merge(entry.MessageMetadataName, fromRef, fromVer);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                overrides[entry.Code] = raw!;
                overrides[entry.CpmCode] = raw!;
            }
        }

        return true;
    }

    (string FromRef, string FromVer) SeverityValuesFor(string stem) => stem switch
    {
        "NoLicenseSpecified" => (NoLicenseSpecifiedSeverityOverrideFromRef, NoLicenseSpecifiedSeverityOverrideFromVer),
        "LicenseIgnored" => (LicenseIgnoredSeverityOverrideFromRef, LicenseIgnoredSeverityOverrideFromVer),
        "InvalidAccount" => (InvalidAccountSeverityOverrideFromRef, InvalidAccountSeverityOverrideFromVer),
        "LicenseExpired" => (LicenseExpiredSeverityOverrideFromRef, LicenseExpiredSeverityOverrideFromVer),
        _ => throw new InvalidOperationException($"Unknown override stem: {stem}")
    };

    (string FromRef, string FromVer) MessageValuesFor(string stem) => stem switch
    {
        "NoLicenseSpecified" => (NoLicenseSpecifiedMessageOverrideFromRef, NoLicenseSpecifiedMessageOverrideFromVer),
        "LicenseIgnored" => (LicenseIgnoredMessageOverrideFromRef, LicenseIgnoredMessageOverrideFromVer),
        "InvalidAccount" => (InvalidAccountMessageOverrideFromRef, InvalidAccountMessageOverrideFromVer),
        "LicenseExpired" => (LicenseExpiredMessageOverrideFromRef, LicenseExpiredMessageOverrideFromVer),
        _ => throw new InvalidOperationException($"Unknown override stem: {stem}")
    };

    Dictionary<string, string> ResolveEnabledPlatforms()
    {
        var enabled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Maybe("GitHubSponsors", GitHubSponsorsAccountFromRef, GitHubSponsorsAccountFromVer);
        Maybe("OpenCollective", OpenCollectiveAccountFromRef, OpenCollectiveAccountFromVer);
        Maybe("Polar", PolarAccountFromRef, PolarAccountFromVer);
        return enabled;

        void Maybe(string id, string fromRef, string fromVer)
        {
            var merged = PackageMetadataMerger.Merge($"{id}Account", fromRef, fromVer);
            if (!string.IsNullOrWhiteSpace(merged))
            {
                enabled[id] = merged!;
            }
        }
    }

    IReadOnlyList<SponsorEntry> FetchFromOverride()
    {
        Log.LogMessage(MessageImportance.High, $"SponsorCheck: using sponsor override list '{OverrideListPath}'.");
        return SponsorOverrideFile.Read(OverrideListPath);
    }

    async Task<IReadOnlyList<SponsorEntry>> FetchFromPlatformsAsync(Dictionary<string, string> enabled)
    {
        var results = new List<SponsorEntry>();
        foreach (var pair in enabled)
        {
            var platform = PlatformRegistry.Get(pair.Key);
            var token = TokenFor(pair.Key);
            var accounts = await platform.FetchSponsorAccounts(pair.Value, token, Log, Cancel.None)
                .ConfigureAwait(false);
            foreach (var account in accounts)
            {
                if (!string.IsNullOrWhiteSpace(account))
                {
                    results.Add(new(platform.Id, account));
                }
            }
        }

        return results;
    }

    IReadOnlyDictionary<string, string> UserSecrets => field ??= LoadUserSecrets();

    IReadOnlyDictionary<string, string> LoadUserSecrets()
    {
        if (string.IsNullOrWhiteSpace(UserSecretsId))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var secrets = UserSecretsReader.Read(UserSecretsId);
            if (secrets.Count > 0)
            {
                Log.LogMessage(MessageImportance.Low, $"SponsorCheck: loaded {secrets.Count} user-secrets from id '{UserSecretsId}'.");
            }

            return secrets;
        }
        catch (Exception exception)
        {
            SponsorCheckLog.Warning(
                Log,
                "SC103",
                $"SponsorCheck: could not read user-secrets at '{UserSecretsReader.ResolvePath(UserSecretsId)}': {exception.Message}");
            return new Dictionary<string, string>();
        }
    }

    string? TokenFor(string platformId)
    {
        var explicitToken = platformId switch
        {
            "GitHubSponsors" => GitHubToken,
            "OpenCollective" => OpenCollectiveToken,
            "Polar" => PolarToken,
            _ => null
        };
        return ResolveToken(platformId, explicitToken, UserSecrets);
    }

    // Token resolution: explicit MSBuild property (or env-var-promoted property) wins; otherwise
    // fall back to user-secrets convention. GitHub uses the standard "GitHubToken" name (matching
    // the GITHUB_TOKEN env var convention); other platforms use "SponsorCheck:{PlatformId}Token".
    // Static + injected secrets dict so this is unit-testable without writing to the real user-secrets directory.
    public static string? ResolveToken(string platformId, string? explicitToken, IReadOnlyDictionary<string, string> userSecrets)
    {
        if (!string.IsNullOrWhiteSpace(explicitToken))
        {
            return explicitToken;
        }

        var key = platformId == "GitHubSponsors"
            ? "SponsorCheck:GitHubToken"
            : $"SponsorCheck:{platformId}Token";
        if (userSecrets.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return null;
    }

    // MSBuild target/item names reject dots and dashes, so non-alphanumeric chars are
    // replaced with '_'. That alone collides ids like "Acme.Lib", "Acme-Lib", and
    // "Acme_Lib" (all -> "Acme_Lib"); a consumer that PackageReferences two such
    // packages would get duplicate target/item names at MSBuild import time. Append a
    // 32-bit SHA256 prefix of the raw id so each id maps to a unique sanitized name.
    public static string Sanitize(string packageId)
    {
        var builder = new StringBuilder(packageId.Length + 9);
        foreach (var character in packageId)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        builder.Append('_');
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(packageId));
        for (var i = 0; i < 4; i++)
        {
            builder.Append(digest[i].ToString("x2"));
        }

        return builder.ToString();
    }

    static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
