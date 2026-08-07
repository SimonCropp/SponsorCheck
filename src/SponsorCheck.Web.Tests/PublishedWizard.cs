namespace SponsorCheck.Web.Tests;

/// <summary>
/// Shared plumbing for browser-based tests: serves the published Blazor output
/// (bin/&lt;Configuration&gt;/blazor-publish) from an in-memory Kestrel host and provides a Chromium
/// instance, exercising the actual WASM runtime (which the bunit tests do not).
/// </summary>
public sealed class PublishedWizard : IAsyncDisposable
{
    readonly WebApplication app;
    readonly IPlaywright playwright;

    public IBrowser Browser { get; }
    public int Port { get; }

    static PublishedWizard? shared;

    /// <summary>
    /// One Kestrel host and one Chromium for the whole assembly. Every browser-based test class hangs
    /// off this: a per-class instance meant two cold WASM boots racing each other on a two-core CI
    /// agent, and the losers blew Playwright's 30s default before the runtime finished starting.
    /// </summary>
    public static PublishedWizard Shared =>
        shared ?? throw new("PublishedWizard has not been started.");

    [Before(HookType.Assembly)]
    public static async Task StartShared() => shared = await Start();

    [After(HookType.Assembly)]
    public static async Task StopShared()
    {
        if (shared != null)
        {
            await shared.DisposeAsync();
            shared = null;
        }
    }

    PublishedWizard(WebApplication app, IPlaywright playwright, IBrowser browser, int port)
    {
        this.app = app;
        this.playwright = playwright;
        Browser = browser;
        Port = port;
    }

    public string Url(string path = "/") => $"http://localhost:{Port}{path}";

    /// <summary>Fixed viewport so screenshots are deterministic across machines.</summary>
    public Task<IPage> NewPage() =>
        Browser.NewPageAsync(
            new()
            {
                ViewportSize = new()
                {
                    Width = 1280,
                    Height = 900
                }
            });

    public static async Task<PublishedWizard> Start()
    {
        var installExitCode = Program.Main(["install", "chromium"]);
        if (installExitCode != 0)
        {
            throw new($"Playwright Chromium install failed with exit code {installExitCode}.");
        }

        var port = GetAvailablePort();

        var testAssemblyDirectory = Path.GetDirectoryName(typeof(PublishedWizard).Assembly.Location)!;
        var wwwroot = Path.Combine(testAssemblyDirectory, "..", "blazor-publish", "wwwroot");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        var contentTypes = new FileExtensionContentTypeProvider
        {
            Mappings =
            {
                [".wasm"] = "application/wasm"
            }
        };
        var files = new PhysicalFileProvider(wwwroot);

        app.UseDefaultFiles(new DefaultFilesOptions {FileProvider = files});
        app.UseStaticFiles(
            new StaticFileOptions
            {
                FileProvider = files,
                ContentTypeProvider = contentTypes,
                ServeUnknownFileTypes = true
            });
        app.MapFallbackToFile("index.html", new StaticFileOptions {FileProvider = files});

        await app.StartAsync();

        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync();
        var wizard = new PublishedWizard(app, playwright, browser, port);
        await wizard.WarmUp();
        return wizard;
    }

    /// <summary>
    /// Boot the app once, serially, before any test runs. The first load downloads and initializes the
    /// WASM runtime — on a cold CI agent that alone can outlast Playwright's 30s default, and doing it
    /// concurrently in several pages only makes each one slower. After this the browser's HTTP cache is
    /// warm, so the pages the tests open start against an already-fetched runtime.
    /// </summary>
    async Task WarmUp()
    {
        var page = await NewPage();
        await page.GotoAsync(Url());
        await page.WaitForSelectorAsync(
            ".role-cards",
            new()
            {
                Timeout = 120_000
            });
        await page.CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Browser.CloseAsync();
        playwright.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }

    static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint) listener.LocalEndpoint).Port;
    }
}