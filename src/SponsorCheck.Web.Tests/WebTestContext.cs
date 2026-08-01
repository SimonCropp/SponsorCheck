namespace SponsorCheck.Web.Tests;

/// <summary>bunit base context for component tests: loose JS interop (clipboard is a no-op) and the
/// app's DI services registered.</summary>
public abstract class WebTestContext : BunitContext
{
    protected WebTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<ClipboardService>();
    }
}
