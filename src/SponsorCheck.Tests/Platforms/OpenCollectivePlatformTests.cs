public class OpenCollectivePlatformTests
{
    [Test]
    public async Task LiveLookup()
    {
        var token = LiveTokenResolver.ResolveOrSkip(
            "OpenCollectiveToken",
            "SponsorCheck:OpenCollectiveToken",
            "OpenCollective",
            "Anonymous calls hit rate limits on collectives with many backers; create a Personal Token at https://opencollective.com/applications.");
        var log = new TaskLoggingHelperFor(new StubBuildEngine());
        var platform = new OpenCollectivePlatform();
        var sponsors = await platform.FetchSponsorAccounts("webpack", token, log, Cancel.None);
        await Assert.That(sponsors).IsNotNull();
    }

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
        await Assert.That(page.RawItemCount).IsEqualTo(2);
        await Assert.That(page.TotalCount).IsEqualTo(2);
    }

    [Test]
    public async Task RawItemCount_CountsAllNodesIncludingUnusableOnes()
    {
        // Pagination must advance by raw node count, not filtered slug count. A node whose
        // account.slug is missing/null/empty gets dropped from MemberSlugs but still consumes
        // one of the page's `limit` rows. If we advanced by MemberSlugs.Count, a page where
        // every node was filtered would terminate the loop before reaching totalCount and
        // silently lose subsequent pages of sponsors.
        var json = """
        {
          "data": {
            "account": {
              "members": {
                "totalCount": 5,
                "nodes": [
                  { "account": { "slug": "alice" } },
                  { "account": { "slug": "" } },
                  { "account": { "slug": null } },
                  { "account": null },
                  { "account": { "slug": "bob" } }
                ]
              }
            }
          }
        }
        """;
        var page = OpenCollectivePlatform.ParseResponse(json);
        await Assert.That(page.AccountExists).IsTrue();
        await Assert.That(page.MemberSlugs.Count).IsEqualTo(2);
        await Assert.That(page.MemberSlugs).Contains("alice");
        await Assert.That(page.MemberSlugs).Contains("bob");
        await Assert.That(page.RawItemCount).IsEqualTo(5);
        await Assert.That(page.TotalCount).IsEqualTo(5);
    }

    [Test]
    public async Task EmptyNodesArray()
    {
        var json = """
        {
          "data": {
            "account": {
              "members": {
                "totalCount": 0,
                "nodes": []
              }
            }
          }
        }
        """;
        var page = OpenCollectivePlatform.ParseResponse(json);
        await Assert.That(page.AccountExists).IsTrue();
        await Assert.That(page.MemberSlugs.Count).IsEqualTo(0);
        await Assert.That(page.RawItemCount).IsEqualTo(0);
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
