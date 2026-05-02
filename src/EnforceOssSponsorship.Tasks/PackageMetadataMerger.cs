namespace EnforceOssSponsorship.Tasks;

public static class PackageMetadataMerger
{
    /// Merges the same metadatum sourced from PackageReference and PackageVersion under CPM.
    /// Empty/whitespace counts as unset. Disagreement raises; agreement (case-insensitive) wins.
    public static string? Merge(string metadataName, string? fromReference, string? fromVersion)
    {
        var r = Normalize(fromReference);
        var v = Normalize(fromVersion);
        if (r is null && v is null)
        {
            return null;
        }

        if (r is null)
        {
            return v;
        }

        if (v is null)
        {
            return r;
        }

        if (string.Equals(r, v, StringComparison.OrdinalIgnoreCase))
        {
            return r;
        }

        throw new MaintenanceFeeException(
            $"{metadataName}: conflicting values on PackageReference ('{r}') and PackageVersion ('{v}'). Set on only one.");
    }

    static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
