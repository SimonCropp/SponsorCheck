public class LogLevelsTests
{
    [Test]
    public async Task TryParse_Empty_KeepsBothDefaults()
    {
        await Assert.That(LogLevels.TryParse("", "  ", out var levels, out var invalid)).IsTrue();
        await Assert.That(levels).IsEqualTo(LogLevels.Default);
        await Assert.That(invalid).IsEmpty();
    }

    // Property values are hand-typed in a props file, so casing and surrounding whitespace don't count.
    [Test]
    public async Task TryParse_IgnoresCaseAndSurroundingWhitespace()
    {
        await Assert.That(LogLevels.TryParse(" Low ", "WARNING", out var levels, out _)).IsTrue();
        await Assert.That(levels).IsEqualTo(new(LogLevel.Low, LogLevel.Warning));
    }

    [Test]
    public async Task TryParse_Invalid_NamesEachProperty()
    {
        await Assert.That(LogLevels.TryParse(" loud ", "error", out var levels, out var invalid)).IsFalse();
        await Assert.That(levels).IsEqualTo(LogLevels.Default);
        await Assert.That(invalid)
            .IsEquivalentTo(
            [
                ("SponsorCheckMessageLevel", "loud"),
                ("SponsorCheckWarningLevel", "error")
            ]);
    }

    // A consumer's level decides both ways: above the local default of low, and below the
    // build-server default of high.
    [Test]
    [Arguments(LogLevel.High, false, MessageImportance.High)]
    [Arguments(LogLevel.Normal, true, MessageImportance.Normal)]
    [Arguments(LogLevel.Low, true, MessageImportance.Low)]
    public async Task Apply_MessageLevel_SetsTheImportanceOfAMessage(LogLevel level, bool onBuildServer, MessageImportance expected) =>
        await Assert.That(new LogLevels(level, null).Apply(Severity.Message, onBuildServer))
            .IsEqualTo((Severity.Message, expected));

    [Test]
    public async Task Apply_MessageLevelWarning_RaisesAMessageToAWarning() =>
        await Assert.That(new LogLevels(LogLevel.Warning, null).Apply(Severity.Message, onBuildServer: true).Severity)
            .IsEqualTo(Severity.Warning);

    [Test]
    [Arguments(LogLevel.High, MessageImportance.High)]
    [Arguments(LogLevel.Normal, MessageImportance.Normal)]
    [Arguments(LogLevel.Low, MessageImportance.Low)]
    public async Task Apply_WarningLevel_LowersAWarningToAMessage(LogLevel level, MessageImportance expected) =>
        await Assert.That(new LogLevels(null, level).Apply(Severity.Warning, onBuildServer: true))
            .IsEqualTo((Severity.Message, expected));

    [Test]
    public async Task Apply_WarningLevelWarning_LeavesAWarningAlone() =>
        await Assert.That(new LogLevels(null, LogLevel.Warning).Apply(Severity.Warning, onBuildServer: true).Severity)
            .IsEqualTo(Severity.Warning);

    // Each property governs its own kind only.
    [Test]
    public async Task Apply_EachLevel_LeavesTheOtherKindAlone()
    {
        await Assert.That(new LogLevels(LogLevel.Low, null).Apply(Severity.Warning, onBuildServer: true).Severity)
            .IsEqualTo(Severity.Warning);
        await Assert.That(new LogLevels(null, LogLevel.Low).Apply(Severity.Message, onBuildServer: true))
            .IsEqualTo((Severity.Message, MessageImportance.High));
    }

    // Errors are the enforcement, so no level reaches them.
    [Test]
    [Arguments(LogLevel.Warning)]
    [Arguments(LogLevel.High)]
    [Arguments(LogLevel.Normal)]
    [Arguments(LogLevel.Low)]
    public async Task Apply_NoLevel_ChangesAnError(LogLevel level) =>
        await Assert.That(new LogLevels(level, level).Apply(Severity.Error, onBuildServer: true).Severity)
            .IsEqualTo(Severity.Error);
}
