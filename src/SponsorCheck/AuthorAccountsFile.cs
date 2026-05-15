public static class AuthorAccountsFile
{
    public static void Write(string path, IEnumerable<KeyValuePair<string, string>> accounts)
    {
        var lines = accounts
            .Where(_ => !string.IsNullOrWhiteSpace(_.Key) &&
                        !string.IsNullOrWhiteSpace(_.Value))
            .Select(_ => $"{_.Key.Trim()}={_.Value.Trim()}")
            .OrderBy(_ => _, StringComparer.Ordinal);
        File.WriteAllLines(path, lines);
    }

    public static IReadOnlyList<KeyValuePair<string, string>> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var result = new List<KeyValuePair<string, string>>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0 || eq == line.Length - 1)
            {
                continue;
            }

            var platform = line[..eq].Trim();
            var account = line[(eq + 1)..].Trim();
            if (platform.Length == 0 || account.Length == 0)
            {
                continue;
            }

            result.Add(new(platform, account));
        }

        return result;
    }
}
