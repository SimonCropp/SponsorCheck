// CpmCode is the CPM-mode sibling and OwnerCode is the owner-mode sibling that share the same
// override metadata. One author-supplied override value is duplicated into the Code, CpmCode, and
// OwnerCode entries at pack time so a single `NoLicenseSpecifiedSeverityOverride="warning"` applies
// regardless of how the consumer is configured (PackageReference, PackageVersion, or owner-mode property).
public sealed record OverrideableCode(string Code, string CpmCode, string OwnerCode, string Stem)
{
    public string SeverityMetadataName => $"{Stem}SeverityOverride";
    public string MessageMetadataName => $"{Stem}MessageOverride";
}