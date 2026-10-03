/// <summary>
/// Caps how many browser tests drive a page at once. Every page boots its own .NET WASM runtime, which
/// is CPU-bound even with the runtime already cached, so on a two-core CI agent a dozen concurrent boots
/// starve each other past Playwright's 30s default. Applied to every class that opens pages through
/// <see cref="PublishedWizard"/>; the bunit tests are unaffected.
/// </summary>
public sealed class BrowserParallelLimit : TUnit.Core.Interfaces.IParallelLimit
{
    public int Limit { get; } = Math.Max(2, Environment.ProcessorCount);
}
