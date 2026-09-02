namespace SponsorCheck.Web.Services;

public sealed class PackageLookupException(string message) : Exception(message);

/// <summary>
/// Inspects a package on nuget.org (the v3 flat container serves browsers with CORS
/// enabled, Range header included) and extracts <see cref="PackageFacts"/> via
/// <see cref="NupkgParser"/> — entirely client-side. RemoteZip fetches only the zip
/// central directory and the SponsorCheck sidecar files, so the cost stays a few hundred
/// KB regardless of package size. <see cref="MaxNupkgBytes"/> only bounds the fallback
/// full download taken when a server ignores range requests.
/// </summary>
public sealed class PackageLookup(HttpClient http)
{
    public const long MaxNupkgBytes = 30_000_000;
    const string flatContainer = "https://api.nuget.org/v3-flatcontainer";

    static readonly RemoteZipOptions options = new()
    {
        MaxBufferLength = MaxNupkgBytes,
        // The browser HTTP cache can answer one range request with the cached body of
        // another; no-store keeps every range request on the network.
        ConfigureRequest = request => request.SetBrowserRequestCache(BrowserRequestCache.NoStore)
    };

    public async Task<PackageFacts> Inspect(string packageId, string? version)
    {
        var id = packageId.Trim();
        var idLower = id.ToLowerInvariant();
        var resolved = string.IsNullOrWhiteSpace(version) ? await LatestVersion(id, idLower) : version.Trim();
        var versionLower = resolved.ToLowerInvariant();

        try
        {
            var nupkg = await RemoteZipArchive.Open(
                http,
                $"{flatContainer}/{idLower}/{versionLower}/{idLower}.{versionLower}.nupkg",
                options);
            return await NupkgParser.Parse(id, resolved, nupkg);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PackageLookupException($"Version {resolved} of '{id}' was not found on nuget.org.");
        }
        catch (HttpRequestException exception) when (exception.StatusCode != null)
        {
            throw new PackageLookupException($"nuget.org returned {(int) exception.StatusCode} downloading '{id}' {resolved}.");
        }
        catch (RemoteZipException exception)
        {
            // Covers a corrupt archive, and the size cap when a server without range
            // support forces a full download.
            throw new PackageLookupException(
                $"'{id}' {resolved} could not be inspected in the browser: {exception.Message} Answer the questions manually.");
        }
    }

    async Task<string> LatestVersion(string id, string idLower)
    {
        using var response = await http.GetAsync($"{flatContainer}/{idLower}/index.json");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PackageLookupException($"Package '{id}' was not found on nuget.org.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new PackageLookupException($"nuget.org returned {(int) response.StatusCode} listing versions for '{id}'.");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var versions = document.RootElement.GetProperty("versions").EnumerateArray()
            .Select(_ => _.GetString())
            .Where(_ => !string.IsNullOrWhiteSpace(_))
            .Select(_ => _!)
            .ToList();
        if (versions.Count == 0)
        {
            throw new PackageLookupException($"Package '{id}' has no versions on nuget.org.");
        }

        // Prerelease included: an author adopting SponsorCheck usually ships it in a prerelease
        // first, and that is the version a consumer hitting the new diagnostic is on. The
        // flat-container index is ordered ascending with a prerelease ahead of the release it
        // precedes, so the newest published version is last either way.
        return versions[^1];
    }
}
