// The body of a GraphQL request: {"query": ..., "variables": {...}}.
//
// Written out with Utf8JsonWriter and not handed to JsonSerializer as an anonymous type. A
// serializer reads that type's properties by reflection, which nothing in the assembly's own code
// does, so the trimmer that runs over this assembly (Squash) removes the getters as unreachable and
// the request goes out as "{}". The platform then answers that no query was specified.
public static class GraphQlPayload
{
    public static string Write(string query, IReadOnlyDictionary<string, object?> variables)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("query", query);
            writer.WriteStartObject("variables");
            foreach (var variable in variables)
            {
                WriteVariable(writer, variable.Key, variable.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // Only the kinds of value the platform queries pass. Anything else is a query written without
    // teaching this about its variable, which should fail here and not at the platform.
    static void WriteVariable(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(name);
                break;
            case string text:
                writer.WriteString(name, text);
                break;
            case int number:
                writer.WriteNumber(name, number);
                break;
            default:
                throw new ArgumentException($"GraphQL variable '{name}' is a {value.GetType().Name}, which is not written.", nameof(value));
        }
    }
}
