/// <summary>Locates real repo files so the anti-rot tests can compare the wizard's hardcoded names
/// against the shipped MSBuild targets, templates, and docs.</summary>
public static class RepoPaths
{
    public static string SrcDirectory { get; } = Path.GetDirectoryName(ProjectFiles.SolutionFile.FullPath)!;

    public static string RepoRoot { get; } = Path.GetFullPath(Path.Combine(SrcDirectory, ".."));

    public static string SrcFile(params string[] segments) =>
        Path.Combine([SrcDirectory, .. segments]);

    public static string RepoFile(params string[] segments) =>
        Path.Combine([RepoRoot, .. segments]);
}
