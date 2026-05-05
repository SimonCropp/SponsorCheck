public class MaintenanceFeeException(string message) :
    Exception(message);

/// Thrown by a platform when a required credential (e.g. Polar's mandatory token) is absent.
/// The bundler maps this to SC102, distinct from the generic SC100 used for other platform errors.
public sealed class MissingCredentialException(string message) :
    MaintenanceFeeException(message);
