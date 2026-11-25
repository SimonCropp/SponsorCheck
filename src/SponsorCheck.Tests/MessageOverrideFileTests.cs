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
            ["SC005"] = "You're free-riding!"
        };
        MessageOverrideFile.Write(path, input);
        var output = MessageOverrideFile.Read(path);
        await Assert.That(output["SC001"]).IsEqualTo("Please sponsor before using");
        await Assert.That(output["SC005"]).IsEqualTo("You're free-riding!");
    }

    [Test]
    public async Task Write_IsWhatTheSerializerProduced()
    {
        // The file ships in every package and is what the wizard's parser and older verifiers
        // read, so writing it by hand must not change a byte: indentation, key order, escaping.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");
        var input = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SC005"] = "You're free-riding!",
            ["SC001"] = "Says: \"hello\"; path=C:\\foo\\bar <b>&</b> café 🎉\nsecond line"
        };

        MessageOverrideFile.Write(path, input);

        var sorted = new SortedDictionary<string, string>(input, StringComparer.Ordinal);
        var expected = System.Text.Json.JsonSerializer.Serialize(sorted, indented);
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(expected);
    }

    // What MessageOverrideFile handed the serializer before it wrote the file itself.
    static System.Text.Json.JsonSerializerOptions indented = new()
    {
        WriteIndented = true
    };

    [Test]
    public async Task Write_Empty_IsAnEmptyObject()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");

        MessageOverrideFile.Write(path, new Dictionary<string, string>());

        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("{}");
    }

    [Test]
    [Arguments("""{ "SC001": "valid", "SC005": 5 }""")]
    [Arguments("""{ "SC001": "valid", "SC005": { "nested": "value" } }""")]
    [Arguments("""[ "SC001" ]""")]
    [Arguments("null")]
    public async Task Read_NotAnObjectOfStrings_ReturnsEmpty(string content)
    {
        // All or nothing, as it was when the serializer read it: a file with one value of the
        // wrong kind yields no overrides, not the entries that happened to be well formed.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");
        await File.WriteAllTextAsync(path, content);

        var result = MessageOverrideFile.Read(path);

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Read_ANullValue_IsSkipped()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");
        await File.WriteAllTextAsync(path, """{ "SC001": "valid", "SC005": null }""");

        var result = MessageOverrideFile.Read(path);

        await Assert.That(result.ContainsKey("SC001")).IsTrue();
        await Assert.That(result.ContainsKey("SC005")).IsFalse();
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
        await File.WriteAllTextAsync(path, """{ "SC001": "valid", "SC003": "tampered", "SC999": "unknown" }""");
        var result = MessageOverrideFile.Read(path);
        await Assert.That(result.ContainsKey("SC001")).IsTrue();
        await Assert.That(result.ContainsKey("SC003")).IsFalse();
        await Assert.That(result.ContainsKey("SC999")).IsFalse();
    }

    [Test]
    public async Task Read_FiltersWhitespaceMessages()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "overrides.json");
        await File.WriteAllTextAsync(path, """{ "SC001": "real", "SC005": "   " }""");
        var result = MessageOverrideFile.Read(path);
        await Assert.That(result.ContainsKey("SC001")).IsTrue();
        await Assert.That(result.ContainsKey("SC005")).IsFalse();
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
