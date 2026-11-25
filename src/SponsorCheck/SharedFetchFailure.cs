// Why a shared fetch failed, kept as the kind of exception it was so that the project reading it
// back reports the same diagnostic code its own fetch would have: BundleSponsorListTask maps the
// exception type to SC102, SC107, SC108 or SC100.
public sealed record SharedFetchFailure(string Kind, string Message)
{
    const string missingCredential = "MissingCredential";
    const string invalidCredential = "InvalidCredential";
    const string rateLimited = "RateLimited";
    const string platform = "Platform";
    const string unexpected = "Unexpected";

    public static SharedFetchFailure From(Exception exception)
    {
        var kind = exception switch
        {
            MissingCredentialException => missingCredential,
            InvalidCredentialException => invalidCredential,
            RateLimitedException => rateLimited,
            MaintenanceFeeException => platform,
            _ => unexpected
        };
        return new(kind, exception.Message);
    }

    public Exception ToException() =>
        Kind switch
        {
            missingCredential => new MissingCredentialException(Message),
            invalidCredential => new InvalidCredentialException(Message),
            rateLimited => new RateLimitedException(Message),
            platform => new MaintenanceFeeException(Message),
            _ => new SharedFetchException(Message)
        };
}

// Anything that was not a platform error (a dropped connection, a timeout). The bundler logs
// those by message alone, so the type only has to carry it.
public sealed class SharedFetchException(string message) :
    Exception(message);
