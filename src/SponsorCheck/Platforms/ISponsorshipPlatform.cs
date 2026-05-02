namespace SponsorCheck.Tasks.Platforms;

public interface ISponsorshipPlatform
{
    string Id { get; }

    Task<IReadOnlyList<string>> FetchSponsorAccounts(
        string ownerAccount,
        string? token,
        TaskLoggingHelper log,
        CancellationToken cancellation);
}
