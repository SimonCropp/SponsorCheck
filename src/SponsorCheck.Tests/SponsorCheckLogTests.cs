public class SponsorCheckLogTests
{
    static string[] allKnownCodes =
    [
        "SC001",
        "SC003",
        "SC005",
        "SC007",
        "SC009",
        "SC019",
        "SC011",
        "SC017",
        "SC018",
        "SC013",
        "SC015",
        "SC020",
        "SC100",
        "SC101",
        "SC102",
        "SC103",
        "SC104"
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
        foreach (var code in allKnownCodes)
        {
            var name = SponsorCheckLog.NameFor(code);
            await Assert.That(name).IsNotEqualTo(code).Because($"{code} is missing a name in SponsorCheckLog.NameFor");
            await Assert.That(name).IsNotEmpty();
        }

        var distinct = allKnownCodes.Select(SponsorCheckLog.NameFor).Distinct(StringComparer.Ordinal).Count();
        await Assert.That(distinct).IsEqualTo(allKnownCodes.Length).Because("each code should have a distinct name");
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
