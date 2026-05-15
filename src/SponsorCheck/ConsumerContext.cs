// Surfaces enough about the consumer's project layout for the verifier's diagnostic messages
// to render copy-pasteable XML snippets pointing at the right file. ResolvedVersion may be
// "<version>" when neither the PackageReference nor the PackageVersion item carried %(Version),
// which keeps the rendered snippet syntactically valid as a placeholder.
public sealed record ConsumerContext(
    bool IsCpm,
    string ConsumerProjectPath,
    string DirectoryPackagesPropsPath,
    string PackageId,
    string ResolvedVersion)
{
    public string ElementName => IsCpm ? "PackageVersion" : "PackageReference";

    public string TargetFilePath =>
        IsCpm && !string.IsNullOrWhiteSpace(DirectoryPackagesPropsPath)
            ? DirectoryPackagesPropsPath
            : ConsumerProjectPath;

    public string DisplayVersion =>
        string.IsNullOrWhiteSpace(ResolvedVersion) ? "<version>" : ResolvedVersion;
}
