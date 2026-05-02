namespace EnforceOssSponsorship.Tests.Platforms;

using EnforceOssSponsorship.Tasks.Platforms;

public class OpenCollectivePlatformTests
{
    [Test]
    public async Task PublicCollectiveBackers()
    {
        var json = """
        {
          "data": {
            "account": {
              "members": {
                "totalCount": 2,
                "nodes": [
                  { "account": { "slug": "alice" } },
                  { "account": { "slug": "acme-org" } }
                ]
              }
            }
          }
        }
        """;
        var page = OpenCollectivePlatform.ParseResponse(json);
        await Assert.That(page.AccountExists).IsTrue();
        await Assert.That(page.MemberSlugs).Contains("alice");
        await Assert.That(page.MemberSlugs).Contains("acme-org");
        await Assert.That(page.TotalCount).IsEqualTo(2);
    }

    [Test]
    public async Task UnknownAccount()
    {
        var json = """{ "data": { "account": null } }""";
        var page = OpenCollectivePlatform.ParseResponse(json);
        await Assert.That(page.AccountExists).IsFalse();
    }

    [Test]
    public void ErrorsThrow()
    {
        var json = """{ "errors": [ { "message": "boom" } ] }""";
        Assert.Throws<MaintenanceFeeException>(() => OpenCollectivePlatform.ParseResponse(json));
    }
}
