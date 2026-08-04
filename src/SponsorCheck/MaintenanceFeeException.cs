public class MaintenanceFeeException(string message) :
    Exception(message);

/// Thrown by a platform when a required credential (e.g. Polar's mandatory token) is absent.
/// The bundler maps this to SC102, distinct from the generic SC100 used for other platform errors.
public sealed class MissingCredentialException(string message) :
    MaintenanceFeeException(message);

/// Thrown by a platform when the API rejects the credential itself (HTTP 401). The bundler maps this
/// to SC107. Distinct from MissingCredentialException (SC102 — nothing configured at all) and from
/// the generic SC100: a rejected credential is never transient, so "re-run the build" is always the
/// wrong advice. The stored value has to be replaced, and the message says which stored value.
public sealed class InvalidCredentialException(string message) :
    MaintenanceFeeException(message);

/// Thrown by a platform when its API rate limit is exhausted. The bundler maps this to SC108. The
/// inverse of InvalidCredentialException: nothing is misconfigured and the same build succeeds once
/// the window rolls over, so the message carries a reset time rather than a fix.
public sealed class RateLimitedException(string message) :
    MaintenanceFeeException(message);
