namespace SponsorCheck.Benchmarks;

public static class Repo
{
    public static string Root { get; } = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", ".."));

    public static string SponsorListOverride => Path.Combine(Root, "IntegrationTests", "IntegrationTests", "Fixtures", "sponsors-override.json");

    // The package `dotnet build src --configuration Release` leaves in nugets/.
    public static string CurrentPackage()
    {
        var directory = Path.Combine(Root, "nugets");
        string[] packages = [];
        if (Directory.Exists(directory))
        {
            packages = Directory.GetFiles(directory, "SponsorCheck.*.nupkg");
        }

        if (packages.Length == 1)
        {
            return packages[0];
        }

        if (packages.Length == 0)
        {
            throw new BenchmarkException($"No SponsorCheck nupkg in {directory}. Run `dotnet build src --configuration Release` first.");
        }

        throw new BenchmarkException($"Expected exactly one SponsorCheck.*.nupkg in {directory}, found {packages.Length}. Clean stale nupkgs and rebuild src.");
    }
}
