public class SponsorshipExemptionsFileTests
{
    [Test]
    public async Task WriteThenRead_RoundTrips()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        var input = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Consulting"] = "Organizations that have engaged any of the core maintainers in consulting work could be exempt from the Maintenance Fee for 6 months from the final date of that work.",
            ["SmallRevenue"] = "Consumers under US$10,000 annual gross revenue are exempt."
        };
        SponsorshipExemptionsFile.Write(path, input);
        var output = SponsorshipExemptionsFile.Read(path);
        await Assert.That(output["Consulting"]).IsEqualTo(input["Consulting"]);
        await Assert.That(output["SmallRevenue"]).IsEqualTo(input["SmallRevenue"]);
    }

    [Test]
    public async Task Read_MissingFile_ReturnsEmpty()
    {
        var result = SponsorshipExemptionsFile.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Read_EmptyPath_ReturnsEmpty()
    {
        var result = SponsorshipExemptionsFile.Read("");
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Read_EmptyFile_ReturnsEmpty()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        await File.WriteAllTextAsync(path, "");
        var result = SponsorshipExemptionsFile.Read(path);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Read_MalformedJson_ReturnsEmpty()
    {
        // Bundler-side validation is the source of truth — corrupt sidecar falls back to "no exemptions defined".
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        await File.WriteAllTextAsync(path, "{not json");
        var result = SponsorshipExemptionsFile.Read(path);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Read_FiltersWhitespaceMessages()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        await File.WriteAllTextAsync(path, """{ "Consulting": "real", "Empty": "   " }""");
        var result = SponsorshipExemptionsFile.Read(path);
        await Assert.That(result.ContainsKey("Consulting")).IsTrue();
        await Assert.That(result.ContainsKey("Empty")).IsFalse();
    }

    [Test]
    public async Task Read_LookupIsCaseInsensitive()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        var input = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Consulting"] = "carve-out text"
        };
        SponsorshipExemptionsFile.Write(path, input);
        var output = SponsorshipExemptionsFile.Read(path);
        await Assert.That(output.TryGetValue("consulting", out var lower)).IsTrue();
        await Assert.That(lower).IsEqualTo("carve-out text");
        await Assert.That(output.TryGetValue("CONSULTING", out var upper)).IsTrue();
        await Assert.That(upper).IsEqualTo("carve-out text");
    }

    [Test]
    public async Task Write_SortedOrdinal_IsDeterministic()
    {
        // Reproducible nupkgs depend on a stable serialization order.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        var unordered = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Zeta"] = "z",
            ["Alpha"] = "a",
            ["Mu"] = "m"
        };
        SponsorshipExemptionsFile.Write(path, unordered);
        var text = await File.ReadAllTextAsync(path);
        await Assert.That(text.IndexOf("Alpha", StringComparison.Ordinal))
            .IsLessThan(text.IndexOf("Mu", StringComparison.Ordinal));
        await Assert.That(text.IndexOf("Mu", StringComparison.Ordinal))
            .IsLessThan(text.IndexOf("Zeta", StringComparison.Ordinal));
    }

    [Test]
    public async Task Write_EmptyDict_WritesEmptyObject()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        SponsorshipExemptionsFile.Write(path, new Dictionary<string, string>());
        await Assert.That(File.Exists(path)).IsTrue();
        var text = await File.ReadAllTextAsync(path);
        await Assert.That(text.Trim()).IsEqualTo("{}");
    }

    [Test]
    public async Task Write_RoundTripsArbitraryCharacters()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        var input = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Quirky"] = "Says: \"hello\"; path=C:\\foo\\bar — café 🎉"
        };
        SponsorshipExemptionsFile.Write(path, input);
        var output = SponsorshipExemptionsFile.Read(path);
        await Assert.That(output["Quirky"]).IsEqualTo(input["Quirky"]);
    }
}
