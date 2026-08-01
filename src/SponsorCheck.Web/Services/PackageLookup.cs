namespace SponsorCheck.Web.Services;

public sealed class PackageLookupException(string message) : Exception(message);

/// <summary>
/// Downloads a package from nuget.org (the v3 flat container serves browsers with CORS enabled)
/// and extracts <see cref="PackageFacts"/> via <see cref="NupkgParser"/> — entirely client-side.
/// </summary>
public sealed class PackageLookup(HttpClient http)
{
    public const long MaxNupkgBytes = 30_000_000;
    const string flatContainer = "https://api.nuget.org/v3-flatcontainer";

    public async Task<PackageFacts> Inspect(string packageId, string? version)
    {
        var id = packageId.Trim();
        var idLower = id.ToLowerInvariant();
        var resolved = string.IsNullOrWhiteSpace(version) ? await LatestStableVersion(id, idLower) : version.Trim();
        var versionLower = resolved.ToLowerInvariant();

        using var response = await http.GetAsync(
            $"{flatContainer}/{idLower}/{versionLower}/{idLower}.{versionLower}.nupkg",
            HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PackageLookupException($"Version {resolved} of '{id}' was not found on nuget.org.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new PackageLookupException($"nuget.org returned {(int) response.StatusCode} downloading '{id}' {resolved}.");
        }

        if (response.Content.Headers.ContentLength is > MaxNupkgBytes)
        {
            throw new PackageLookupException(
                $"'{id}' {resolved} is {response.Content.Headers.ContentLength / 1_000_000} MB — too large to inspect in the browser. Answer the questions manually.");
        }

        // ZipArchive needs a seekable stream, so buffer the download — bounding the copy so a
        // response without a Content-Length header can't stream unbounded into browser memory.
        using var memory = new MemoryStream();
        await using (var stream = await response.Content.ReadAsStreamAsync())
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                memory.Write(buffer, 0, read);
                if (memory.Length > MaxNupkgBytes)
                {
                    throw new PackageLookupException(
                        $"'{id}' {resolved} exceeds {MaxNupkgBytes / 1_000_000} MB — too large to inspect in the browser. Answer the questions manually.");
                }
            }
        }

        memory.Position = 0;
        return NupkgParser.Parse(id, resolved, memory);
    }

    async Task<string> LatestStableVersion(string id, string idLower)
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

        // The flat-container index is ordered ascending; prefer the newest stable, else newest overall.
        var stable = versions.LastOrDefault(_ => !_.Contains('-'));
        return stable ?? versions[^1];
    }
}
