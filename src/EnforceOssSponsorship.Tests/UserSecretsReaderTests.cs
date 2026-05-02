namespace EnforceOssSponsorship.Tests;

public class UserSecretsReaderTests
{
    static (string id, string secretsPath) WriteSecrets(string content)
    {
        var id = $"eoss-test-{Guid.NewGuid():N}";
        var path = UserSecretsReader.ResolvePath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return (id, path);
    }

    [Test]
    public async Task FlatKeys()
    {
        var (id, path) = WriteSecrets("""
        {
          "EnforceOssSponsorship:GitHubSponsorsToken": "ghp_xxx",
          "EnforceOssSponsorship:PolarToken": "polar_yyy"
        }
        """);
        try
        {
            var secrets = UserSecretsReader.Read(id);
            await Assert.That(secrets["EnforceOssSponsorship:GitHubSponsorsToken"]).IsEqualTo("ghp_xxx");
            await Assert.That(secrets["EnforceOssSponsorship:PolarToken"]).IsEqualTo("polar_yyy");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task NestedKeys()
    {
        var (id, path) = WriteSecrets("""
        {
          "EnforceOssSponsorship": {
            "GitHubSponsorsToken": "ghp_xxx",
            "PolarToken": "polar_yyy"
          }
        }
        """);
        try
        {
            var secrets = UserSecretsReader.Read(id);
            await Assert.That(secrets["EnforceOssSponsorship:GitHubSponsorsToken"]).IsEqualTo("ghp_xxx");
            await Assert.That(secrets["EnforceOssSponsorship:PolarToken"]).IsEqualTo("polar_yyy");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task MissingFileReturnsEmpty()
    {
        var secrets = UserSecretsReader.Read($"never-created-{Guid.NewGuid():N}");
        await Assert.That(secrets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task LookupIsCaseInsensitive()
    {
        var (id, path) = WriteSecrets("""{ "EnforceOssSponsorship:GitHubSponsorsToken": "ghp_xxx" }""");
        try
        {
            var secrets = UserSecretsReader.Read(id);
            await Assert.That(secrets["enforceosssponsorship:githubsponsorstoken"]).IsEqualTo("ghp_xxx");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ResolvePathFollowsConvention()
    {
        var path = UserSecretsReader.ResolvePath("abc-123");
        await Assert.That(path).EndsWith(Path.Combine("abc-123", "secrets.json"));
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            await Assert.That(path).Contains("Microsoft");
            await Assert.That(path).Contains("UserSecrets");
        }
    }
}
