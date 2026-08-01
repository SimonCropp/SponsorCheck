namespace SponsorCheck.Web.Tests.Services;

public class PackageLookupTests
{
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requested.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    static PackageLookup Lookup(Handler handler) => new(new(handler));

    static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

    static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Test]
    public async Task UnknownPackageReportsNotFound()
    {
        var lookup = Lookup(new(_ => NotFound()));

        var exception = await Assert.ThrowsAsync<PackageLookupException>(() => lookup.Inspect("Nope", null));
        await Assert.That(exception!.Message).Contains("'Nope' was not found on nuget.org");
    }

    [Test]
    public async Task UnknownVersionReportsNotFound()
    {
        var lookup = Lookup(new(_ => NotFound()));

        var exception = await Assert.ThrowsAsync<PackageLookupException>(() => lookup.Inspect("ThePackage", "9.9.9"));
        await Assert.That(exception!.Message).Contains("Version 9.9.9 of 'ThePackage' was not found");
    }

    [Test]
    public async Task OversizeByContentLengthRejectedBeforeDownload()
    {
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentLength = PackageLookup.MaxNupkgBytes + 1;
        var lookup = Lookup(new(_ => new(HttpStatusCode.OK) { Content = content }));

        var exception = await Assert.ThrowsAsync<PackageLookupException>(() => lookup.Inspect("Big", "1.0.0"));
        await Assert.That(exception!.Message).Contains("too large to inspect in the browser");
    }

    [Test]
    public async Task OversizeWithoutContentLengthRejectedDuringDownload()
    {
        // A chunked response carries no Content-Length, so the cap must bite mid-copy.
        var oversized = new byte[PackageLookup.MaxNupkgBytes + 1];
        var content = new ByteArrayContent(oversized);
        content.Headers.ContentLength = null;
        var lookup = Lookup(new(_ => new(HttpStatusCode.OK) { Content = content }));

        var exception = await Assert.ThrowsAsync<PackageLookupException>(() => lookup.Inspect("Chunked", "1.0.0"));
        await Assert.That(exception!.Message).Contains("too large to inspect in the browser");
    }

    [Test]
    public async Task BlankVersionResolvesLatestStable()
    {
        var handler = new Handler(request =>
            request.RequestUri!.ToString().EndsWith("/index.json", StringComparison.Ordinal)
                ? Json("""{"versions":["0.9.0","1.0.0","2.0.0-beta.1"]}""")
                : Bytes(TestNupkg.Build()));
        var lookup = Lookup(handler);

        var facts = await lookup.Inspect("ThePackage", null);

        await Assert.That(facts.Version).IsEqualTo("1.0.0");
        await Assert.That(handler.Requested[^1]).Contains("/thepackage/1.0.0/thepackage.1.0.0.nupkg");
    }

    [Test]
    public async Task CorruptDownloadSurfacesAsException()
    {
        var lookup = Lookup(new(_ => Bytes([0x50, 0x4B, 0x00, 0x00, 0xFF])));

        await Assert.ThrowsAsync<Exception>(() => lookup.Inspect("Corrupt", "1.0.0"));
    }
}
