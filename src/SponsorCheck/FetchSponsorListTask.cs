// Fetches the sponsor list of each platform account once for a whole build.
//
// The bundler runs in every packable project and used to call the platform API in each: a repo of
// twenty packages asked the same question twenty times per pack, and drew all twenty from one
// rate-limit budget. SponsorCheck.targets now has each project <MSBuild>-call a target in that
// same file to do the fetch, and MSBuild caches a build result per (project file, global
// properties, targets) in the scheduler, so the first project's call runs this task and every
// other project in the build, on whatever node, is served its result.
//
// It never fails and never logs an error or a warning. What goes wrong here is reported by each
// project that asked, with the code and text its own fetch would have produced, so a failure
// travels back as a result (see SharedFetchResult) instead of being raised where no project owns
// it. A bare task name is safe for the reason BundleSponsorListTask's is: a project references
// one SponsorCheck version.
public sealed class FetchSponsorListTask :
    Microsoft.Build.Utilities.Task
{
    public string GitHubSponsorsAccount { get; set; } = "";
    public string OpenCollectiveAccount { get; set; } = "";
    public string PolarAccount { get; set; } = "";

    public string GitHubToken { get; set; } = "";
    public string OpenCollectiveToken { get; set; } = "";
    public string PolarToken { get; set; } = "";
    public string UserSecretsId { get; set; } = "";

    [Output]
    public ITaskItem[] Fetched { get; set; } = [];

    public override bool Execute()
    {
        var userSecrets = LoadUserSecrets();
        var results = new List<ITaskItem>();
        Fetch("GitHubSponsors", GitHubSponsorsAccount, GitHubToken);
        Fetch("OpenCollective", OpenCollectiveAccount, OpenCollectiveToken);
        Fetch("Polar", PolarAccount, PolarToken);
        Fetched = [.. results];
        return true;

        void Fetch(string platformId, string account, string explicitToken)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                return;
            }

            var owner = account.Trim();
            var platform = PlatformRegistry.Get(platformId);
            var tokens = BundleSponsorListTask.ResolveTokens(platformId, explicitToken, userSecrets);
            Log.LogMessage(MessageImportance.Low, $"SponsorCheck: fetching the {platform.Id} sponsor list of '{owner}' once for this build.");
            var result = SharedFetchResult.Run(platform, owner, tokens, Log).GetAwaiter().GetResult();
            if (result.Failure != null)
            {
                Log.LogMessage(
                    MessageImportance.Low,
                    $"SponsorCheck: the {platform.Id} fetch failed ({result.Failure.Message.Split('\n')[0]}); each project that asked for it reports the failure.");
            }

            results.Add(result.ToItem());
        }
    }

    // A secrets file that cannot be read is not reported from here: every project reads the same
    // file to work out its own credentials and raises SC103 itself.
    IReadOnlyDictionary<string, string> LoadUserSecrets()
    {
        if (string.IsNullOrWhiteSpace(UserSecretsId))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return UserSecretsReader.Read(UserSecretsId);
        }
        catch (Exception exception)
        {
            Log.LogMessage(MessageImportance.Low, $"SponsorCheck: could not read user-secrets at '{UserSecretsReader.ResolvePath(UserSecretsId)}': {exception.Message}");
            return new Dictionary<string, string>();
        }
    }
}
