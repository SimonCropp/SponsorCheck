namespace SponsorCheck.Web.Tests;

/// <summary>
/// An in-memory nuget.org flat container for bunit tests: one package, one version. The nupkg is
/// served whole with 200 regardless of the Range header (RemoteZip's fallback for a server without
/// range support, which PackageLookupTests prove is enough), so these tests exercise the wizard,
/// not the transport.
/// </summary>
public sealed class StubNuGetHandler(byte[] nupkg, string version) : HttpMessageHandler
{
    readonly Lock requestsLock = new();

    public string Version { get; set; } = version;

    /// <summary>False answers the version listing with 404: the "package not found" path.</summary>
    public bool PackageExists { get; set; } = true;

    public List<string> Requests { get; } = [];

    public static StubNuGetHandler NotFound() =>
        new([], "0.0.0")
        {
            PackageExists = false
        };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
    {
        var url = request.RequestUri!.ToString();
        lock (requestsLock)
        {
            Requests.Add(url);
        }

        if (url.EndsWith("/index.json", StringComparison.Ordinal))
        {
            if (!PackageExists)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"versions\":[\"{Version}\"]}}")
            });
        }

        if (url.EndsWith(".nupkg", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(nupkg)
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
