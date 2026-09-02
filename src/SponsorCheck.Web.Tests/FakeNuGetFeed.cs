namespace SponsorCheck.Web.Tests;

/// <summary>
/// Fakes api.nuget.org inside the browser for the Playwright tests. The package page fetches on
/// landing, so a test that does not route the feed hits the real network: register this before
/// GotoAsync. The nupkg is served whole with 200 regardless of the Range header (RemoteZip's
/// fallback for a server without range support).
///
/// No CORS headers are written by hand. Playwright adds access-control-allow-origin to every
/// fulfilled cross-origin response, 404s included, and answers the preflight the Range header
/// provokes; both were verified against the 1.62 driver. A Playwright bump that changes either
/// shows up here as a "Failed to fetch" lookup error.
/// </summary>
public sealed class FakeNuGetFeed
{
    public const string Glob = "https://api.nuget.org/**";

    readonly byte[]? nupkg;
    readonly string version;
    readonly Lock requestsLock = new();

    /// <summary>Every request the page made, as "METHOD url". Proves the runtime consumed the fake.</summary>
    public List<string> Requests { get; } = [];

    FakeNuGetFeed(byte[]? nupkg, string version)
    {
        this.nupkg = nupkg;
        this.version = version;
    }

    public static async Task<FakeNuGetFeed> Route(IPage page, byte[] nupkg, string version)
    {
        var feed = new FakeNuGetFeed(nupkg, version);
        await page.RouteAsync(Glob, feed.Handle);
        return feed;
    }

    /// <summary>Every request 404s, so the version listing fails as "package not found".</summary>
    public static async Task<FakeNuGetFeed> RouteNotFound(IPage page)
    {
        var feed = new FakeNuGetFeed(null, "");
        await page.RouteAsync(Glob, feed.Handle);
        return feed;
    }

    Task Handle(IRoute route)
    {
        var request = route.Request;
        lock (requestsLock)
        {
            Requests.Add($"{request.Method} {request.Url}");
        }

        if (nupkg == null)
        {
            return route.FulfillAsync(new()
            {
                Status = 404
            });
        }

        if (request.Url.EndsWith("/index.json", StringComparison.Ordinal))
        {
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = $"{{\"versions\":[\"{version}\"]}}"
            });
        }

        if (request.Url.EndsWith(".nupkg", StringComparison.Ordinal))
        {
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/octet-stream",
                BodyBytes = nupkg
            });
        }

        return route.FulfillAsync(new()
        {
            Status = 404
        });
    }
}
