namespace SponsorCheck.Web.Tests;

/// <summary>Locates real repo files so the anti-rot tests can compare the wizard's hardcoded names
/// against the shipped MSBuild targets, templates, and docs.</summary>
public static class RepoPaths
{
    public static string SrcDirectory { get; } = FindSrcDirectory();

    public static string RepoRoot { get; } = Path.GetFullPath(Path.Combine(FindSrcDirectory(), ".."));

    static string FindSrcDirectory()
    {
        var directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "SponsorCheck.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new($"Could not locate the src directory (SponsorCheck.slnx) above {AppContext.BaseDirectory}");
    }

    public static string SrcFile(params string[] segments) =>
        Path.Combine([SrcDirectory, .. segments]);

    public static string RepoFile(params string[] segments) =>
        Path.Combine([RepoRoot, .. segments]);
}
