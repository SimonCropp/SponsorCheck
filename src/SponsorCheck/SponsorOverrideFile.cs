public static class SponsorOverrideFile
{
    public static IReadOnlyList<SponsorEntry> Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new MaintenanceFeeException($"Override file not found: {path}");
        }

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new MaintenanceFeeException($"Override file root must be a JSON array: {path}");
        }

        var result = new List<SponsorEntry>();
        var index = 0;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new MaintenanceFeeException($"Override file entry [{index}] must be an object: {path}");
            }

            if (!item.TryGetProperty("platform", out var platformElem) ||
                platformElem.ValueKind != JsonValueKind.String)
            {
                throw new MaintenanceFeeException($"Override file entry [{index}] missing string 'platform': {path}");
            }

            if (!item.TryGetProperty("account", out var accountElem) ||
                accountElem.ValueKind != JsonValueKind.String)
            {
                throw new MaintenanceFeeException($"Override file entry [{index}] missing string 'account': {path}");
            }

            var platform = platformElem.GetString()!.Trim();
            var account = accountElem.GetString()!.Trim();
            if (platform.Length == 0 ||
                account.Length == 0)
            {
                throw new MaintenanceFeeException($"Override file entry [{index}] has empty platform or account: {path}");
            }

            result.Add(new(platform, account));
            index++;
        }

        return result;
    }
}
