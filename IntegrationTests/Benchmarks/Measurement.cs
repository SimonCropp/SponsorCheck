namespace SponsorCheck.Benchmarks;

// What one build cost.
//
// Blocked is the time spent inside the verifier's target, summed over the projects it ran in, read
// from MSBuild's own performance summary. It is wall time per project, so it counts the waiting a
// project does while an <MSBuild> call it made is served elsewhere — which is the whole cost being
// measured; the verification itself is a hash lookup. It is a sum across projects that wait in
// parallel, so it is not time added to the build: a waiting project hands its node back.
public sealed partial record Measurement(TimeSpan Wall, TimeSpan Blocked, int Projects)
{
    const string verifyTargetPrefix = "_SponsorCheck_Verify_";

    public static Measurement Parse(DotnetResult result)
    {
        var blocked = TimeSpan.Zero;
        var projects = 0;
        foreach (var line in result.Output.Split('\n'))
        {
            var match = SummaryLine().Match(line);
            if (!match.Success ||
                !match.Groups["name"].Value.StartsWith(verifyTargetPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            blocked += TimeSpan.FromMilliseconds(int.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture));
            projects += int.Parse(match.Groups["calls"].Value, CultureInfo.InvariantCulture);
        }

        if (projects == 0)
        {
            throw new BenchmarkException($"The build's performance summary lists no {verifyTargetPrefix}* target, so the verifier never ran:\n{result.Output}");
        }

        return new(result.Elapsed, blocked, projects);
    }

    // "      266 ms  _SponsorCheck_Verify_ThePackage_64716728          12 calls"
    [GeneratedRegex(@"^\s*(?<ms>\d+) ms\s+(?<name>\S+)\s+(?<calls>\d+) calls\s*$")]
    private static partial Regex SummaryLine();
}
