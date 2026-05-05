public class MessageOverrideFileTests
{
    [Test]
    public async Task WriteThenRead_RoundTrips()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");
        var input = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SC001"] = "Please sponsor before using",
            ["SC003"] = "You're free-riding!"
        };
        MessageOverrideFile.Write(path, input);
        var output = MessageOverrideFile.Read(path);
        await Assert.That(output["SC001"]).IsEqualTo("Please sponsor before using");
        await Assert.That(output["SC003"]).IsEqualTo("You're free-riding!");
    }

    [Test]
    public async Task Read_MissingFile_ReturnsEmpty()
    {
        var result = MessageOverrideFile.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Read_EmptyFile_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");
        await File.WriteAllTextAsync(path, "");
        var result = MessageOverrideFile.Read(path);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Read_MalformedJson_ReturnsEmpty()
    {
        // Bundler-side validation is the source of truth — corrupt sidecar falls back to defaults.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");
        await File.WriteAllTextAsync(path, "{not json");
        var result = MessageOverrideFile.Read(path);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Read_FiltersUnknownCodes()
    {
        // A sidecar with entries for codes outside the overrideable set is silently filtered.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");
        await File.WriteAllTextAsync(path, """{ "SC001": "valid", "SC002": "tampered", "SC999": "unknown" }""");
        var result = MessageOverrideFile.Read(path);
        await Assert.That(result.ContainsKey("SC001")).IsTrue();
        await Assert.That(result.ContainsKey("SC002")).IsFalse();
        await Assert.That(result.ContainsKey("SC999")).IsFalse();
    }

    [Test]
    public async Task Read_FiltersWhitespaceMessages()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");
        await File.WriteAllTextAsync(path, """{ "SC001": "real", "SC003": "   " }""");
        var result = MessageOverrideFile.Read(path);
        await Assert.That(result.ContainsKey("SC001")).IsTrue();
        await Assert.That(result.ContainsKey("SC003")).IsFalse();
    }

    [Test]
    public async Task Write_RoundTripsArbitraryCharacters()
    {
        // JSON encoding handles arbitrary text — quotes, backslashes, equals signs, unicode all
        // survive a round-trip without escaping logic in the format itself.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");
        var input = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SC001"] = "Says: \"hello\"; path=C:\\foo\\bar — café 🎉"
        };
        MessageOverrideFile.Write(path, input);
        var output = MessageOverrideFile.Read(path);
        await Assert.That(output["SC001"]).IsEqualTo(input["SC001"]);
    }
}
