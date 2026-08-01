namespace SponsorCheck.Web.Tests;

/// <summary>bunit base context for component tests: loose JS interop (clipboard is a no-op) and the
/// app's DI services registered. The default HttpClient refuses all requests — a test exercising the
/// nuget.org lookup registers a stub client on top (last registration wins).</summary>
public abstract class WebTestContext : BunitContext
{
    protected WebTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<ClipboardService>();
        Services.AddScoped(_ => new HttpClient(new NoNetworkHandler()));
        Services.AddScoped<PackageLookup>();
    }

    sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel) =>
            throw new HttpRequestException("No network in component tests — register a stub HttpClient.");
    }
}
