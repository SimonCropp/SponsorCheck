namespace SponsorCheck.Web.Tests;

/// <summary>
/// End-to-end tests that serve the published Blazor app from an in-memory Kestrel host and drive it with a
/// real Chromium browser, exercising the actual WASM runtime (which the bunit tests do not). Assertions are
/// text/selector based rather than pixel snapshots because PNG baselines are OS-specific and would break the
/// Linux CI deploy; snapshot coverage of the rendered text lives in the deterministic generator / bunit tests.
/// </summary>
public class EndToEndTests
{
    static WebApplication? app;
    static int port;
    static IPlaywright? playwright;
    static IBrowser? browser;

    [Before(Class)]
    public static async Task Setup()
    {
        var installExitCode = Program.Main(["install", "chromium"]);
        if (installExitCode != 0)
        {
            throw new($"Playwright Chromium install failed with exit code {installExitCode}.");
        }

        port = GetAvailablePort();

        var testAssemblyDirectory = Path.GetDirectoryName(typeof(EndToEndTests).Assembly.Location)!;
        var wwwroot = Path.Combine(testAssemblyDirectory, "..", "blazor-publish", "wwwroot");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.Logging.ClearProviders();
        app = builder.Build();

        var contentTypes = new FileExtensionContentTypeProvider
        {
            Mappings =
            {
                [".wasm"] = "application/wasm"
            }
        };
        var files = new PhysicalFileProvider(wwwroot);

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(
            new StaticFileOptions
            {
                FileProvider = files,
                ContentTypeProvider = contentTypes,
                ServeUnknownFileTypes = true
            });
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });

        await app.StartAsync();

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync();
    }

    [After(Class)]
    public static async Task Teardown()
    {
        if (browser != null)
        {
            await browser.CloseAsync();
        }

        playwright?.Dispose();

        if (app != null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Test]
    public async Task Boots()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");
        await page.WaitForSelectorAsync(".role-cards");

        var cards = await page.QuerySelectorAllAsync("a.role-card");
        await Assert.That(cards.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AuthorJourney()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");
        await page.ClickAsync("a.role-card[href='author']");
        await page.WaitForSelectorAsync("#packageId");

        await page.FillAsync("#packageId", "MyOssLib");
        await page.ClickAsync("button.primary");          // package -> platforms
        await page.CheckAsync("#enable-GitHub");
        await page.FillAsync("#account-GitHub", "acmecorp");
        await page.ClickAsync("button.primary");          // platforms -> mode
        await page.ClickAsync("button.primary");          // mode -> options
        await page.ClickAsync("button.primary");          // options -> output

        await page.WaitForSelectorAsync(".code-box");
        var body = await page.TextContentAsync("body");

        await Assert.That(body).Contains("GitHubSponsorsAccount=\"acmecorp\"");
        await Assert.That(body).Contains("Tell your consumers");
        await Assert.That(body).Contains("Copy as markdown");
    }

    [Test]
    public async Task ConsumerOwnerJourney()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");
        await page.ClickAsync("a.role-card[href='consumer']");
        await page.WaitForSelectorAsync("#packageId");

        await page.FillAsync("#packageId", "ThePackage");
        await page.ClickAsync("button.primary");          // package -> situation (no lookup: offline path)
        await page.WaitForSelectorAsync("#scCode");
        await page.FillAsync("#scCode", "SC021");
        await page.WaitForSelectorAsync("#ownerId");
        await page.FillAsync("#ownerId", "acme");
        await page.ClickAsync("button.primary");          // situation -> license mode

        await page.ClickAsync("button.mode-card >> nth=0");   // sponsor
        await page.CheckAsync("#sponsor-GitHub");
        await page.FillAsync("#sponsor-account-GitHub", "alice");
        await page.ClickAsync("button.primary");          // license mode -> output

        await page.WaitForSelectorAsync(".code-box");
        var body = await page.TextContentAsync("body");

        await Assert.That(body).Contains("<acme_GitHubSponsorAccount>alice</acme_GitHubSponsorAccount>");
        await Assert.That(body).Contains("Directory.Build.props");
    }

    [Test]
    public async Task DeepLinkToConsumerFlow()
    {
        // Exercises the SPA fallback (404.html / MapFallbackToFile) that GitHub Pages relies on.
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/consumer");
        await page.WaitForSelectorAsync("#packageId");

        var heading = await page.TextContentAsync("h2");
        await Assert.That(heading).IsEqualTo("The package");
    }

    static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint) listener.LocalEndpoint).Port;
    }
}
