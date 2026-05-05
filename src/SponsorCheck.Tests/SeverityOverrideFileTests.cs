public class SeverityOverrideFileTests
{
    [Test]
    public async Task TryParseSeverity_Error()
    {
        var ok = SeverityOverrideFile.TryParseSeverity("error", out var sev);
        await Assert.That(ok).IsTrue();
        await Assert.That(sev).IsEqualTo(Severity.Error);
    }

    [Test]
    public async Task TryParseSeverity_Warning()
    {
        var ok = SeverityOverrideFile.TryParseSeverity("warning", out var sev);
        await Assert.That(ok).IsTrue();
        await Assert.That(sev).IsEqualTo(Severity.Warning);
    }

    [Test]
    public async Task TryParseSeverity_Message()
    {
        var ok = SeverityOverrideFile.TryParseSeverity("message", out var sev);
        await Assert.That(ok).IsTrue();
        await Assert.That(sev).IsEqualTo(Severity.Message);
    }

    [Test]
    public async Task TryParseSeverity_NormalizesCase()
    {
        var ok = SeverityOverrideFile.TryParseSeverity("WARNING", out var sev);
        await Assert.That(ok).IsTrue();
        await Assert.That(sev).IsEqualTo(Severity.Warning);
    }

    [Test]
    public async Task TryParseSeverity_RejectsUnknown()
    {
        var ok = SeverityOverrideFile.TryParseSeverity("critical", out _);
        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task OverrideableCodes_HasExpectedMapping()
    {
        // Mapping is the single source of truth — bundler properties, targets file, docs all
        // derive from it. This guard catches accidental mapping reshuffles or new entries that
        // forget the property/targets plumbing.
        var pairs = OverrideableCodes.All
            .Select(_ => $"{_.Code}|{_.SeverityMetadataName}|{_.MessageMetadataName}")
            .ToArray();
        string[] expected =
        [
            "SC001|NoLicenseSpecifiedSeverityOverride|NoLicenseSpecifiedMessageOverride",
            "SC003|LicenseIgnoredSeverityOverride|LicenseIgnoredMessageOverride",
            "SC004|InvalidAccountSeverityOverride|InvalidAccountMessageOverride",
            "SC005|LicenseExpiredSeverityOverride|LicenseExpiredMessageOverride"
        ];
        await Assert.That(pairs).IsEquivalentTo(expected);
    }

    [Test]
    public async Task WriteThenRead_RoundTrips()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.txt");
        var input = new Dictionary<string, Severity>(StringComparer.Ordinal)
        {
            ["SC001"] = Severity.Warning,
            ["SC003"] = Severity.Error,
            ["SC004"] = Severity.Message
        };
        SeverityOverrideFile.Write(path, input);
        var output = SeverityOverrideFile.Read(path);
        await Assert.That(output["SC001"]).IsEqualTo(Severity.Warning);
        await Assert.That(output["SC003"]).IsEqualTo(Severity.Error);
        await Assert.That(output["SC004"]).IsEqualTo(Severity.Message);
    }

    [Test]
    public async Task Read_MissingFile_ReturnsEmpty()
    {
        var result = SeverityOverrideFile.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt"));
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Read_DeterministicSortedFile()
    {
        // Ensures output is deterministic (sorted) — author runs with multiple overrides should
        // produce reproducible nupkgs.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.txt");
        SeverityOverrideFile.Write(path, new Dictionary<string, Severity>(StringComparer.Ordinal)
        {
            ["SC004"] = Severity.Warning,
            ["SC001"] = Severity.Error,
            ["SC003"] = Severity.Message
        });
        string[] expected = ["SC001=error", "SC003=message", "SC004=warning"];
        var lines = await File.ReadAllLinesAsync(path);
        await Assert.That(lines).IsEquivalentTo(expected);
    }
}
