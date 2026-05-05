public class SponsorCheckLogTests
{
    static readonly string[] AllKnownCodes =
    [
        "SC001", "SC002", "SC003", "SC004", "SC005", "SC006", "SC007", "SC008", "SC009", "SC010", "SC011",
        "SC100", "SC101", "SC102", "SC103", "SC104"
    ];

    [Test]
    public async Task NameFor_KnownCode_ReturnsHumanReadableName() =>
        await Assert.That(SponsorCheckLog.NameFor("SC001")).IsEqualTo("No license specified");

    [Test]
    public async Task NameFor_AllCodesHaveDistinctNames()
    {
        // Ensures every known code appears in the lookup. A new SC code added to the codebase
        // without a matching entry in NameFor would otherwise silently fall back to returning the
        // code itself — defeating the human-readable purpose.
        foreach (var code in AllKnownCodes)
        {
            var name = SponsorCheckLog.NameFor(code);
            await Assert.That(name).IsNotEqualTo(code).Because($"{code} is missing a name in SponsorCheckLog.NameFor");
            await Assert.That(name).IsNotEmpty();
        }

        var distinct = AllKnownCodes.Select(SponsorCheckLog.NameFor).Distinct(StringComparer.Ordinal).Count();
        await Assert.That(distinct).IsEqualTo(AllKnownCodes.Length).Because("each code should have a distinct name");
    }

    [Test]
    public async Task NameFor_UnknownCode_FallsBackToCode() =>
        await Assert.That(SponsorCheckLog.NameFor("SC999")).IsEqualTo("SC999");

    [Test]
    public async Task DocsUrl_VerifierCode_PointsAtVerifierDoc() =>
        await Assert.That(SponsorCheckLog.DocsUrl("SC001"))
            .IsEqualTo("https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc001");

    [Test]
    public async Task DocsUrl_BundlerCode_PointsAtBundlerDoc() =>
        await Assert.That(SponsorCheckLog.DocsUrl("SC101"))
            .IsEqualTo("https://github.com/SimonCropp/SponsorCheck/blob/main/docs/BundlerDiagnosticCodes.md#sc101");
}
