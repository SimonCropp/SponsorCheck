public static class PackageMetadataMerger
{
    /// Merges the same metadatum sourced from PackageReference and PackageVersion under CPM.
    /// Empty/whitespace counts as unset. Setting on both raises, even when the values match.
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

        throw new MaintenanceFeeException(
            $"{metadataName}: set on both PackageReference ('{r}') and PackageVersion ('{v}'). Set on only one.");
    }

    static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
