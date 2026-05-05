public class SeverityOverrideFileTests
{
    [Test]
    public async Task ParseAuthorInput_Empty()
    {
        var result = SeverityOverrideFile.ParseAuthorInput("", out var error);
        await Assert.That(error).IsNull();
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ParseAuthorInput_SingleEntry()
    {
        var result = SeverityOverrideFile.ParseAuthorInput("SC001=warning", out var error);
        await Assert.That(error).IsNull();
        await Assert.That(result["SC001"]).IsEqualTo(Severity.Warning);
    }

    [Test]
    public async Task ParseAuthorInput_MultipleEntriesSemicolon()
    {
        var result = SeverityOverrideFile.ParseAuthorInput("SC001=warning;SC003=error;SC004=message", out var error);
        await Assert.That(error).IsNull();
        await Assert.That(result["SC001"]).IsEqualTo(Severity.Warning);
        await Assert.That(result["SC003"]).IsEqualTo(Severity.Error);
        await Assert.That(result["SC004"]).IsEqualTo(Severity.Message);
    }

    [Test]
    public async Task ParseAuthorInput_NormalizesCase()
    {
        var result = SeverityOverrideFile.ParseAuthorInput("sc001=WARNING", out var error);
        await Assert.That(error).IsNull();
        await Assert.That(result["SC001"]).IsEqualTo(Severity.Warning);
    }

    [Test]
    public async Task ParseAuthorInput_RejectsNonOverrideableCode()
    {
        var result = SeverityOverrideFile.ParseAuthorInput("SC002=warning", out var error);
        await Assert.That(error).IsNotNull();
        await Assert.That(error!).Contains("SC002");
        await Assert.That(error!).Contains("not overrideable");
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ParseAuthorInput_RejectsUnknownSeverity()
    {
        var result = SeverityOverrideFile.ParseAuthorInput("SC001=critical", out var error);
        await Assert.That(error).IsNotNull();
        await Assert.That(error!).Contains("critical");
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ParseAuthorInput_RejectsMalformedToken()
    {
        var result = SeverityOverrideFile.ParseAuthorInput("SC001warning", out var error);
        await Assert.That(error).IsNotNull();
        await Assert.That(result).IsEmpty();
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
        var lines = await File.ReadAllLinesAsync(path);
        await Assert.That(lines).IsEquivalentTo(new[] { "SC001=error", "SC003=message", "SC004=warning" });
    }
}
