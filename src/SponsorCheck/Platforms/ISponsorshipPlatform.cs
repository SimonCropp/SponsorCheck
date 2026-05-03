public interface ISponsorshipPlatform
{
    string Id { get; }

    string SponsorPageUrl(string ownerAccount);

    Task<IReadOnlyList<string>> FetchSponsorAccounts(
        string ownerAccount,
        string? token,
        TaskLoggingHelper log,
        Cancel cancel);
}
