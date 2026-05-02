namespace EnforceOssSponsorship.Tasks;

public sealed class MaintenanceFeeException : Exception
{
    public MaintenanceFeeException(string message) : base(message)
    {
    }

    public MaintenanceFeeException(string message, Exception inner) : base(message, inner)
    {
    }
}
