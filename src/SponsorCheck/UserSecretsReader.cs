/// Reads .NET user-secrets (the file written by `dotnet user-secrets set`) at the conventional path.
/// Returns a flat dictionary keyed by colon-separated paths (so `SponsorCheck:GitHubToken`
/// works whether the JSON is flat or nested).
public static class UserSecretsReader
{
    public static IReadOnlyDictionary<string, string> Read(string userSecretsId)
    {
        var path = ResolvePath(userSecretsId);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            Flatten(doc.RootElement, "", result);
        }

        return result;
    }

    public static string ResolvePath(string userSecretsId)
    {
        var baseDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets")
            : Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".microsoft", "usersecrets");
        return Path.Combine(baseDir, userSecretsId, "secrets.json");
    }

    static void Flatten(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        foreach (var prop in element.EnumerateObject())
        {
            var key = prefix.Length == 0 ? prop.Name : $"{prefix}:{prop.Name}";
            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(prop.Value, key, result);
                    break;
                case JsonValueKind.String:
                    result[key] = prop.Value.GetString() ?? "";
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    result[key] = prop.Value.GetRawText();
                    break;
                // Arrays and null are not relevant to credentials; skip.
            }
        }
    }
}
