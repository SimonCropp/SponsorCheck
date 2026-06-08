using System.Net;

public class OpenCollectivePlatformTests
{
    [Test]
    public async Task LiveLookup()
    {
        var tokens = LiveTokenResolver.ResolveAllOrSkip(
            "OpenCollectiveToken",
            "SponsorCheck:OpenCollectiveToken",
            "OpenCollective",
            "Anonymous calls hit rate limits on collectives with many backers; create a Personal Token at https://opencollective.com/applications.");
        var log = new TaskLoggingHelperFor(new StubBuildEngine());
        var platform = new OpenCollectivePlatform();
        var sponsors = await LivePlatformFetcher.FetchWithCandidateTokens(platform, "webpack", tokens, log);
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
        var json = """
                   {
                     "data": {
                       "account": null
                     }
                   }
                   """;
        var page = OpenCollectivePlatform.ParseResponse(json);
        await Assert.That(page.AccountExists).IsFalse();
    }

    [Test]
    public void ErrorsThrow()
    {
        var json = """
                   {
                     "errors": [
                       { "message": "boom" }
                     ]
                   }
                   """;
        Assert.Throws<MaintenanceFeeException>(() => OpenCollectivePlatform.ParseResponse(json));
    }

    [Test]
    public async Task FetchSponsorAccounts_AsksApiForBackerRole()
    {
        // Open Collective's MemberRole enum has no SPONSOR — orgs and individuals both come back
        // as BACKER. Adding SPONSOR to the filter is rejected with GRAPHQL_VALIDATION_FAILED.
        var emptyPage = """
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
        var capture = new CapturingHandler(emptyPage);
        using var client = new HttpClient(capture);
        var platform = new OpenCollectivePlatform(client);
        var log = new TaskLoggingHelperFor(new StubBuildEngine());

        await platform.FetchSponsorAccounts("anycollective", token: null, log, Cancel.None);

        await Assert.That(capture.LastRequestBody).IsNotNull();
        await Assert.That(capture.LastRequestBody!).Contains("BACKER");
        await Assert.That(capture.LastRequestBody!).DoesNotContain("SPONSOR");
    }

    [Test]
    public async Task FetchSponsorAccounts_KeepsPagingWhenTotalCountMissing()
    {
        // If the GraphQL response omits `members.totalCount`, ParseResponse defaults TotalCount
        // to 0. The pagination loop must not rely on `offset >= TotalCount` for termination —
        // any positive offset is >= 0, which would silently drop subsequent pages. Termination
        // must come from the page being shorter than the API's `limit` (100).
        var pageOneNodes = string.Join(",", Enumerable.Range(0, 100).Select(_ => "{\"account\":{\"slug\":\"backer" + _.ToString("D3") + "\"}}"));
        var pageOne = $$"""
                        {
                          "data": {
                            "account": {
                              "members": {
                                "nodes": [{{pageOneNodes}}]
                              }
                            }
                          }
                        }
                        """;
        var pageTwo = """
                      {
                        "data": {
                          "account": {
                            "members": {
                              "nodes": [
                                { "account": { "slug": "lateBacker" } }
                              ]
                            }
                          }
                        }
                      }
                      """;
        var handler = new SequenceHandler([pageOne, pageTwo]);
        using var client = new HttpClient(handler);
        var platform = new OpenCollectivePlatform(client);
        var log = new TaskLoggingHelperFor(new StubBuildEngine());

        var sponsors = await platform.FetchSponsorAccounts("anycollective", token: null, log, Cancel.None);

        await Assert.That(sponsors).Contains("backer000");
        await Assert.That(sponsors).Contains("backer099");
        await Assert.That(sponsors).Contains("lateBacker");
    }

    sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
        {
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancel);
            }

            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    sealed class SequenceHandler(IReadOnlyList<string> bodies) : HttpMessageHandler
    {
        int index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
        {
            var body = index < bodies.Count ? bodies[index] : bodies[^1];
            index++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
        }
    }
}
