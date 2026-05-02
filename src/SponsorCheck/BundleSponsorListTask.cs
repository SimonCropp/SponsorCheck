public sealed class BundleSponsorListTask : Microsoft.Build.Utilities.Task
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

    [Required] public string VerifierTargetsTemplatePath { get; set; } = "";
    [Required] public string ThePackageId { get; set; } = "";
    [Required] public string OutputHashListPath { get; set; } = "";
    [Required] public string OutputVerifierTargetsPath { get; set; } = "";
    [Required] public string OutputPackDatePath { get; set; } = "";
    public string OverridePackDate { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            EnsureDirectory(OutputHashListPath);
            EnsureDirectory(OutputVerifierTargetsPath);

            var enabled = ResolveEnabledPlatforms();
            if (enabled.Count == 0)
            {
                Log.LogError(
                    "SponsorCheck",
                    "SC102",
                    "",
                    "",
                    0,
                    0,
                    0,
                    0,
                    "SponsorCheck: at least one platform account metadata must be set on the PackageReference or PackageVersion (e.g. GitHubSponsorsAccount=\"acmecorp\").");
                return false;
            }

            var entries = !string.IsNullOrWhiteSpace(OverrideListPath)
                ? FetchFromOverride()
                : FetchFromPlatformsAsync(enabled).GetAwaiter().GetResult();

            var hashes = entries
                .Select(e => SponsorHasher.Hash(e.Platform, e.Account))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(h => h, StringComparer.Ordinal)
                .ToList();

            File.WriteAllLines(OutputHashListPath, hashes);
            EnsureDirectory(OutputPackDatePath);
            var packDate = !string.IsNullOrWhiteSpace(OverridePackDate)
                ? OverridePackDate.Trim()
                : DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
        catch (MaintenanceFeeException ex)
        {
            Log.LogError("SponsorCheck", "SC100", "", "", 0, 0, 0, 0, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: false);
            return false;
        }
    }

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
            var accounts = await platform.FetchSponsorAccounts(pair.Value, token, Log, CancellationToken.None)
                .ConfigureAwait(false);
            foreach (var account in accounts)
            {
                if (!string.IsNullOrWhiteSpace(account))
                {
                    results.Add(new SponsorEntry(platform.Id, account));
                }
            }
        }

        return results;
    }

    Lazy<IReadOnlyDictionary<string, string>>? userSecretsCache;
    IReadOnlyDictionary<string, string> UserSecrets => (userSecretsCache ??= new(LoadUserSecrets)).Value;

    IReadOnlyDictionary<string, string> LoadUserSecrets()
    {
        if (string.IsNullOrWhiteSpace(UserSecretsId))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var secrets = UserSecretsReader.Read(UserSecretsId!);
            if (secrets.Count > 0)
            {
                Log.LogMessage(MessageImportance.Low, $"SponsorCheck: loaded {secrets.Count} user-secrets from id '{UserSecretsId}'.");
            }

            return secrets;
        }
        catch (Exception ex)
        {
            Log.LogWarning("SponsorCheck", "SC104", "", "", 0, 0, 0, 0,
                $"SponsorCheck: could not read user-secrets at '{UserSecretsReader.ResolvePath(UserSecretsId)}': {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    string? TokenFor(string platformId)
    {
        var explicitToken = platformId switch
        {
            "GitHubSponsors" => NullIfEmpty(GitHubToken),
            "OpenCollective" => NullIfEmpty(OpenCollectiveToken),
            "Polar" => NullIfEmpty(PolarToken),
            _ => null
        };
        if (explicitToken != null)
        {
            return explicitToken;
        }

        // Fall back to user-secrets convention. GitHub uses the standard "GitHubToken" name
        // (matching the GITHUB_TOKEN env var convention); the others use "<PlatformId>Token".
        var key = platformId == "GitHubSponsors"
            ? "SponsorCheck:GitHubToken"
            : $"SponsorCheck:{platformId}Token";
        return UserSecrets.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    static string Sanitize(string packageId)
    {
        var sb = new StringBuilder(packageId.Length);
        foreach (var c in packageId)
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return sb.ToString();
    }

    static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir!);
        }
    }
}
