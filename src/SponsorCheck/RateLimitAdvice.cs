// A rate limit is the one platform failure that is genuinely transient, so the actionable content is
// *when* to retry rather than what to change. Kept pure — header values already extracted, `now`
// passed in — so the reset arithmetic is testable without a clock or a live 429.
public static class RateLimitAdvice
{
    public static string ResetAdvice(HttpResponseMessage response, DateTime utcNow) =>
        ResetAdvice(
            Header(response, "x-ratelimit-reset"),
            Header(response, "retry-after"),
            utcNow);

    public static string ResetAdvice(string? resetEpochSeconds, string? retryAfterSeconds, DateTime utcNow)
    {
        if (long.TryParse(resetEpochSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            var reset = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
            var stamp = reset.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return $"The limit resets at {stamp} UTC, {Delay(reset - utcNow)}.";
        }

        // Secondary limits report a relative Retry-After instead of an absolute reset.
        if (int.TryParse(retryAfterSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 0)
        {
            return $"The platform asked for a retry after {seconds} seconds.";
        }

        return "The platform reported no reset time.";
    }

    public static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }

    static string Delay(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
        {
            // Clock skew, or the build sat between the response and this message. Saying "in -3
            // minutes" would read as a bug, and "retry now" is the correct instruction anyway.
            return "which has already elapsed";
        }

        if (span < TimeSpan.FromMinutes(1))
        {
            return "in under a minute";
        }

        var minutes = (int) Math.Ceiling(span.TotalMinutes);
        if (minutes == 1)
        {
            return "in 1 minute";
        }

        if (minutes < 60)
        {
            return $"in {minutes} minutes";
        }

        return $"in about {Math.Round(span.TotalHours, 1).ToString(CultureInfo.InvariantCulture)} hours";
    }
}
