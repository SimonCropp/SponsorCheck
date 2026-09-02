namespace SponsorCheck.Web.Tests.Services;

public class PackageLookupTests
{
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
        {
            Requested.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    static PackageLookup Lookup(HttpMessageHandler handler) => new(new(handler));

    static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

    static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Test]
    public async Task UnknownPackageReportsNotFound()
    {
        var lookup = Lookup(new Handler(_ => NotFound()));

        var exception = await Assert.ThrowsAsync<PackageLookupException>(() => lookup.Inspect("Nope", null));
        await Assert.That(exception!.Message).Contains("'Nope' was not found on nuget.org");
    }

    [Test]
    public async Task UnknownVersionReportsNotFound()
    {
        var lookup = Lookup(new Handler(_ => NotFound()));

        var exception = await Assert.ThrowsAsync<PackageLookupException>(() => lookup.Inspect("ThePackage", "9.9.9"));
        await Assert.That(exception!.Message).Contains("Version 9.9.9 of 'ThePackage' was not found");
    }

    [Test]
    public async Task LargePackageInspectedViaRangeRequests()
    {
        // Far bigger than MaxNupkgBytes, yet inspectable: against a range-capable server
        // only the central directory and the sidecar files are fetched. Content-Range is
        // hidden the way browser CORS hides it on nuget.org.
        var nupkg = TestNupkg.Build(paddingBytes: (int) PackageLookup.MaxNupkgBytes + 1);
        var server = new StubZipServer(nupkg)
        {
            ExposeContentRange = false
        };
        var lookup = Lookup(server);

        var facts = await lookup.Inspect("Big", "1.0.0");

        await Assert.That(facts.BundlesSponsorCheck).IsTrue();
        await Assert.That(facts.Platforms[0].Account).IsEqualTo("acmecorp");
        await Assert.That(server.BytesServed).IsLessThan(300_000);
        // Tail, then one coalesced read covering every sidecar and the targets file.
        await Assert.That(server.Requests).Count().IsEqualTo(2);
    }

    [Test]
    public async Task OversizeWithoutRangeSupportRejected()
    {
        // A server that ignores Range forces a full download, which the cap bounds.
        var oversized = new byte[PackageLookup.MaxNupkgBytes + 1];
        var lookup = Lookup(new Handler(_ => Bytes(oversized)));

        var exception = await Assert.ThrowsAsync<PackageLookupException>(() => lookup.Inspect("Chunked", "1.0.0"));
        await Assert.That(exception!.Message).Contains("could not be inspected in the browser");
        await Assert.That(exception.Message).Contains("Answer the questions manually");
    }

    [Test]
    public async Task BlankVersionResolvesNewestIncludingPrerelease()
    {
        var handler = new Handler(request =>
            request.RequestUri!.ToString().EndsWith("/index.json", StringComparison.Ordinal)
                ? Json("""{"versions":["0.9.0","1.0.0","2.0.0-beta.1"]}""")
                : Bytes(TestNupkg.Build()));
        var lookup = Lookup(handler);

        var facts = await lookup.Inspect("ThePackage", null);

        await Assert.That(facts.Version).IsEqualTo("2.0.0-beta.1");
        await Assert.That(handler.Requested[^1]).Contains("/thepackage/2.0.0-beta.1/thepackage.2.0.0-beta.1.nupkg");
    }

    [Test]
    public async Task BlankVersionResolvesNewestStableWhenItLeadsThePrereleases()
    {
        // The flat-container index puts a prerelease ahead of the release it precedes, so the
        // last entry is the newest either way.
        var handler = new Handler(request =>
            request.RequestUri!.ToString().EndsWith("/index.json", StringComparison.Ordinal)
                ? Json("""{"versions":["1.0.0","2.0.0-beta.1","2.0.0"]}""")
                : Bytes(TestNupkg.Build()));
        var lookup = Lookup(handler);

        var facts = await lookup.Inspect("ThePackage", null);

        await Assert.That(facts.Version).IsEqualTo("2.0.0");
        await Assert.That(handler.Requested[^1]).Contains("/thepackage/2.0.0/thepackage.2.0.0.nupkg");
    }

    [Test]
    public async Task CorruptDownloadSurfacesAsException()
    {
        var lookup = Lookup(new Handler(_ => Bytes([0x50, 0x4B, 0x00, 0x00, 0xFF])));

        await Assert.ThrowsAsync<Exception>(() => lookup.Inspect("Corrupt", "1.0.0"));
    }
}
