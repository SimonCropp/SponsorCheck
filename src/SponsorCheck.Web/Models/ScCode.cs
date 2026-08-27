namespace SponsorCheck.Web.Models;

/// <summary>
/// Pure classifier for consumer-entered SC codes. The code families deterministically encode the
/// configuration placement: SC001–SC016 interleave non-CPM (odd) with CPM (even); SC021–SC028 are
/// owner mode; SC029–SC058 run as non-CPM/CPM/owner triples. See docs/VerifierDiagnosticCodes.md.
/// </summary>
public static class ScCode
{
    public static ScCodeClassification Classify(string? input)
    {
        var trimmed = input?.Trim() ?? "";
        if (trimmed.Length < 3 ||
            !trimmed.StartsWith("SC", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(trimmed[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return ScCodeClassification.Unrecognized;
        }

        return number switch
        {
            >= 1 and <= 16 when number % 2 == 1 => new(true, Placement.PerPackageProject, false, null),
            >= 1 and <= 16 => new(true, Placement.PerPackageCpm, false, null),
            17 => new(true, null, false,
                "SC017 is the SponsorshipStart audit message — it appears for any placement, so answer the questions below."),
            18 => new(true, null, false,
                "SC018 indicates a corrupt package install — restore or repair the package; no license metadata change fixes it."),
            19 => new(true, Placement.PerPackageCpm, false,
                "SC019 means the metadata is set on both the <PackageReference> and the <PackageVersion> — keep it in one place only."),
            20 => new(true, Placement.PerPackageCpm, false,
                "SC020 names the misplaced attributes and the file they belong in — the output below targets that file."),
            >= 21 and <= 28 => new(true, Placement.OwnerMode, false, null),
            59 => new(true, null, false,
                "SC059 is the private-sponsorship audit message — it appears for any placement, so answer the questions below."),
            >= 29 and <= 58 => new(
                true,
                ((number - 29) % 3) switch
                {
                    0 => Placement.PerPackageProject,
                    1 => Placement.PerPackageCpm,
                    _ => Placement.OwnerMode
                },
                false,
                null),
            >= 100 and <= 199 => new(true, null, true,
                "SC1xx codes are emitted at pack time in the package author's own build — switch to the author flow."),
            _ => ScCodeClassification.Unrecognized
        };
    }
}
