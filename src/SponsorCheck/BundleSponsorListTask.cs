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

    public string NoLicenseSpecifiedSeverityOverrideFromRef { get; set; } = "";
    public string NoLicenseSpecifiedSeverityOverrideFromVer { get; set; } = "";
    public string LicenseIgnoredSeverityOverrideFromRef { get; set; } = "";
    public string LicenseIgnoredSeverityOverrideFromVer { get; set; } = "";
    public string InvalidAccountSeverityOverrideFromRef { get; set; } = "";
    public string InvalidAccountSeverityOverrideFromVer { get; set; } = "";
    public string LicenseExpiredSeverityOverrideFromRef { get; set; } = "";
    public string LicenseExpiredSeverityOverrideFromVer { get; set; } = "";

    [Required] public string VerifierTargetsTemplatePath { get; set; } = "";
    [Required] public string ThePackageId { get; set; } = "";
    [Required] public string OutputHashListPath { get; set; } = "";
    [Required] public string OutputVerifierTargetsPath { get; set; } = "";
    [Required] public string OutputPackDatePath { get; set; } = "";
    [Required] public string OutputAuthorAccountsPath { get; set; } = "";
    [Required] public string OutputSeverityOverridesPath { get; set; } = "";
    public string OverridePackDate { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            EnsureDirectory(OutputHashListPath);
            EnsureDirectory(OutputVerifierTargetsPath);
            EnsureDirectory(OutputSeverityOverridesPath);

            if (!TryResolveSeverityOverrides(out var severityOverrides))
            {
                return false;
            }

            SeverityOverrideFile.Write(OutputSeverityOverridesPath, severityOverrides);

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
                .Select(e => SponsorHasher.Hash(e.Platform, e.Account))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(h => h, StringComparer.Ordinal)
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
                $"SponsorCheck: bundled {hashes.Count} sponsor entries across {entries.Select(e => e.Platform).Distinct(StringComparer.OrdinalIgnoreCase).Count()} platform(s) into '{ThePackageId}'.");
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
        foreach (var (code, metadataName) in SeverityOverrideFile.OverrideableCodes)
        {
            var (fromRef, fromVer) = ValuesFor(metadataName);
            var raw = PackageMetadataMerger.Merge(metadataName, fromRef, fromVer);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!SeverityOverrideFile.TryParseSeverity(raw!, out var severity))
            {
                SponsorCheckLog.Error(
                    Log,
                    "SC104",
                    $"{metadataName}='{raw}' is not a recognized severity. Allowed: error, warning, message.");
                return false;
            }

            overrides[code] = severity;
        }

        return true;
    }

    (string FromRef, string FromVer) ValuesFor(string metadataName) => metadataName switch
    {
        "NoLicenseSpecifiedSeverityOverride" => (NoLicenseSpecifiedSeverityOverrideFromRef, NoLicenseSpecifiedSeverityOverrideFromVer),
        "LicenseIgnoredSeverityOverride" => (LicenseIgnoredSeverityOverrideFromRef, LicenseIgnoredSeverityOverrideFromVer),
        "InvalidAccountSeverityOverride" => (InvalidAccountSeverityOverrideFromRef, InvalidAccountSeverityOverrideFromVer),
        "LicenseExpiredSeverityOverride" => (LicenseExpiredSeverityOverrideFromRef, LicenseExpiredSeverityOverrideFromVer),
        _ => throw new InvalidOperationException($"Unknown severity-override metadata name: {metadataName}")
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
            SponsorCheckLog.Warning(Log, "SC103",
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
        return userSecrets.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    static string Sanitize(string packageId)
    {
        var builder = new StringBuilder(packageId.Length);
        foreach (var c in packageId)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
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
