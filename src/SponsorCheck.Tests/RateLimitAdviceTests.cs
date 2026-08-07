public class RateLimitAdviceTests
{
    static readonly DateTime now = new(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);

    static string Epoch(TimeSpan fromNow) =>
        new DateTimeOffset(now + fromNow, TimeSpan.Zero)
            .ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);

    [Test]
    public async Task Reset_RendersAbsoluteStampAndRelativeDelay()
    {
        var advice = RateLimitAdvice.ResetAdvice(Epoch(TimeSpan.FromMinutes(23)), null, now);
        await Assert.That(advice).IsEqualTo("The limit resets at 2026-08-04 12:23:00 UTC, in 23 minutes.");
    }

    [Test]
    public async Task Reset_UnderAMinute()
    {
        var advice = RateLimitAdvice.ResetAdvice(Epoch(TimeSpan.FromSeconds(30)), null, now);
        await Assert.That(advice).Contains("in under a minute");
    }

    [Test]
    public async Task Reset_LongWindowSwitchesToHours()
    {
        var advice = RateLimitAdvice.ResetAdvice(Epoch(TimeSpan.FromMinutes(90)), null, now);
        await Assert.That(advice).Contains("in about 1.5 hours");
    }

    [Test]
    public async Task Reset_AlreadyPast_DoesNotRenderNegativeDelay()
    {
        // Clock skew between a build agent and the platform is routine; "in -3 minutes" would read
        // as a defect in SponsorCheck rather than as permission to retry immediately.
        var advice = RateLimitAdvice.ResetAdvice(Epoch(TimeSpan.FromMinutes(-3)), null, now);
        await Assert.That(advice).Contains("already elapsed");
        await Assert.That(advice).DoesNotContain("-3");
    }

    [Test]
    public async Task RetryAfter_UsedWhenThereIsNoAbsoluteReset()
    {
        // Secondary limits report only a relative Retry-After.
        var advice = RateLimitAdvice.ResetAdvice(null, "60", now);
        await Assert.That(advice).IsEqualTo("The platform asked for a retry after 60 seconds.");
    }

    [Test]
    public async Task AbsoluteResetWinsOverRetryAfter() =>
        await Assert.That(RateLimitAdvice.ResetAdvice(Epoch(TimeSpan.FromMinutes(23)), "60", now))
            .Contains("12:23:00 UTC");

    [Test]
    [Arguments(null, null)]
    [Arguments("", "")]
    [Arguments("not-a-number", "not-a-number")]
    [Arguments(null, "0")]
    public async Task NoUsableSignal(string? reset, string? retryAfter) =>
        await Assert.That(RateLimitAdvice.ResetAdvice(reset, retryAfter, now))
            .IsEqualTo("The platform reported no reset time.");
}
