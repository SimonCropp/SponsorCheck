public class SponsorshipExemptionsFileTests
{
    static Dictionary<string, ExemptionDefinition> Definitions(params (string name, string message, int? maxTermMonths)[] entries)
    {
        var dict = new Dictionary<string, ExemptionDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, message, maxTermMonths) in entries)
        {
            dict[name] = new(message, maxTermMonths);
        }

        return dict;
    }

    [Test]
    public async Task WriteThenRead_RoundTrips()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        var input = Definitions(
            ("Consulting", "Organizations that have engaged any of the core maintainers in consulting work could be exempt from the Maintenance Fee for 6 months from the final date of that work.", null),
            ("SmallRevenue", "Consumers under US$10,000 annual gross revenue are exempt.", null));
        SponsorshipExemptionsFile.Write(path, input);
        var output = SponsorshipExemptionsFile.Read(path);
        await Assert.That(output["Consulting"].Message).IsEqualTo(input["Consulting"].Message);
        await Assert.That(output["SmallRevenue"].Message).IsEqualTo(input["SmallRevenue"].Message);
        await Assert.That(output["Consulting"].MaxTermMonths).IsNull();
    }

    [Test]
    public async Task WriteThenRead_RoundTripsMaxTermMonths()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        SponsorshipExemptionsFile.Write(path, Definitions(("Consulting", "carve-out text", 6)));
        var output = SponsorshipExemptionsFile.Read(path);
        await Assert.That(output["Consulting"].MaxTermMonths).IsEqualTo(6);
    }

    [Test]
    public async Task Write_OmitsMaxTermMonthsWhenUnset()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        SponsorshipExemptionsFile.Write(path, Definitions(("Consulting", "carve-out text", null)));
        var text = await File.ReadAllTextAsync(path);
        await Assert.That(text).DoesNotContain("maxTermMonths");
    }

    [Test]
    public async Task Read_BareStringValue_IsUncappedExemption()
    {
        // The shape written before MaxTermMonths existed. Nupkgs already on nuget.org carry it, so
        // reading it must keep working — an old package simply has no time-bounded exemptions.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        await File.WriteAllTextAsync(path, """{ "Consulting": "carve-out text" }""");
        var result = SponsorshipExemptionsFile.Read(path);
        await Assert.That(result["Consulting"].Message).IsEqualTo("carve-out text");
        await Assert.That(result["Consulting"].MaxTermMonths).IsNull();
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
    public async Task Read_TruncatedAfterValidEntries_ReturnsEmpty()
    {
        // JsonDocument.Parse throws up-front on truncation, but assert it rather than leaving a
        // half-read dictionary as an implementation detail: a partial exemption set would silently
        // turn a valid claim into SC032.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        await File.WriteAllTextAsync(path, """{ "Consulting": { "message": "text" },""");
        var result = SponsorshipExemptionsFile.Read(path);
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task Read_FiltersWhitespaceMessages()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        await File.WriteAllTextAsync(path, """{ "Consulting": { "message": "real" }, "Empty": { "message": "   " }, "Bare": "   " }""");
        var result = SponsorshipExemptionsFile.Read(path);
        await Assert.That(result.ContainsKey("Consulting")).IsTrue();
        await Assert.That(result.ContainsKey("Empty")).IsFalse();
        await Assert.That(result.ContainsKey("Bare")).IsFalse();
    }

    [Test]
    public async Task Read_IgnoresNonPositiveOrNonNumericMaxTermMonths()
    {
        // The bundler rejects these at pack time, so reaching here means a hand-edited or corrupt
        // sidecar. Dropping the cap degrades to an uncapped exemption rather than failing builds.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "Zero": { "message": "a", "maxTermMonths": 0 },
              "Negative": { "message": "b", "maxTermMonths": -3 },
              "Text": { "message": "c", "maxTermMonths": "6" },
              "Fractional": { "message": "d", "maxTermMonths": 6.5 }
            }
            """);
        var result = SponsorshipExemptionsFile.Read(path);
        await Assert.That(result["Zero"].MaxTermMonths).IsNull();
        await Assert.That(result["Negative"].MaxTermMonths).IsNull();
        await Assert.That(result["Text"].MaxTermMonths).IsNull();
        await Assert.That(result["Fractional"].MaxTermMonths).IsNull();
    }

    [Test]
    public async Task Read_LookupIsCaseInsensitive()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        SponsorshipExemptionsFile.Write(path, Definitions(("Consulting", "carve-out text", null)));
        var output = SponsorshipExemptionsFile.Read(path);
        await Assert.That(output.TryGetValue("consulting", out var lower)).IsTrue();
        await Assert.That(lower!.Message).IsEqualTo("carve-out text");
        await Assert.That(output.TryGetValue("CONSULTING", out var upper)).IsTrue();
        await Assert.That(upper!.Message).IsEqualTo("carve-out text");
    }

    [Test]
    public async Task Write_SortedOrdinal_IsDeterministic()
    {
        // Reproducible nupkgs depend on a stable serialization order.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        SponsorshipExemptionsFile.Write(
            path,
            Definitions(("Zeta", "z", null), ("Alpha", "a", null), ("Mu", "m", null)));
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
        SponsorshipExemptionsFile.Write(path, new Dictionary<string, ExemptionDefinition>());
        await Assert.That(File.Exists(path)).IsTrue();
        var text = await File.ReadAllTextAsync(path);
        await Assert.That(text.Trim()).IsEqualTo("{}");
    }

    [Test]
    public async Task Write_RoundTripsArbitraryCharacters()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir, "exemptions.json");
        var input = Definitions(("Quirky", "Says: \"hello\"; path=C:\\foo\\bar — café 🎉", null));
        SponsorshipExemptionsFile.Write(path, input);
        var output = SponsorshipExemptionsFile.Read(path);
        await Assert.That(output["Quirky"].Message).IsEqualTo(input["Quirky"].Message);
    }
}
