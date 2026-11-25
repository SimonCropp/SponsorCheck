using System.Text.Json;

public class GraphQlPayloadTests
{
    [Test]
    public async Task Write_IsWhatTheSerializerProducedForTheAnonymousType()
    {
        // The platforms have been sent this shape since they were written. Spelling it out must
        // not change a byte of it, awkward characters included.
        var query =
            """
            query($login: String!, $cursor: String) {
              user(login: $login) { sponsors(first: 100, after: $cursor) { nodes { "quoted" <tag> & café } } }
            }
            """;
        var variables = new Dictionary<string, object?>
        {
            ["login"] = "acme\\corp \"quoted\"",
            ["cursor"] = null,
            ["offset"] = 200
        };

        var payload = GraphQlPayload.Write(query, variables);

        var expected = JsonSerializer.Serialize(
            new
            {
                query,
                variables
            });
        await Assert.That(payload).IsEqualTo(expected);
    }

    [Test]
    public async Task Write_CarriesTheQueryAndEachVariable()
    {
        var variables = new Dictionary<string, object?>
        {
            ["slug"] = "acme",
            ["offset"] = 100,
            ["cursor"] = null
        };

        var payload = GraphQlPayload.Write("query { viewer { login } }", variables);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("query").GetString()).IsEqualTo("query { viewer { login } }");
        var written = root.GetProperty("variables");
        await Assert.That(written.GetProperty("slug").GetString()).IsEqualTo("acme");
        await Assert.That(written.GetProperty("offset").GetInt32()).IsEqualTo(100);
        await Assert.That(written.GetProperty("cursor").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task Write_NoVariables_WritesAnEmptyObject()
    {
        var payload = GraphQlPayload.Write("query { viewer { login } }", new Dictionary<string, object?>());

        await Assert.That(payload).IsEqualTo("""{"query":"query { viewer { login } }","variables":{}}""");
    }

    [Test]
    public async Task Write_AVariableItCannotWrite_FailsHere()
    {
        // A value of a kind no query passes today. Dropping it, or writing it as its type name,
        // would surface as a puzzling answer from the platform.
        var variables = new Dictionary<string, object?>
        {
            ["first"] = 100L
        };

        await Assert.That(() => GraphQlPayload.Write("query", variables)).Throws<ArgumentException>();
    }
}
