public class TokenShapeTests
{
    [Test]
    public async Task Describe_ClassicPat() =>
        await Assert.That(TokenShape.Describe($"ghp_{new string('x', 36)}")).IsEqualTo("ghp_…, 40 chars");

    [Test]
    public async Task Describe_FineGrainedPat()
    {
        // github_pat_<22>_<59>: the body carries an underscore of its own, which is why the marker
        // can't be found by taking either the first or the last underscore alone.
        var token = $"github_pat_{new string('x', 22)}_{new string('y', 59)}";
        await Assert.That(TokenShape.Describe(token)).IsEqualTo("github_pat_…, 93 chars");
    }

    [Test]
    public async Task Describe_NoVendorPrefix() =>
        await Assert.That(TokenShape.Describe("not-a-token")).IsEqualTo("no recognized prefix, 11 chars");

    [Test]
    public async Task Describe_SurroundingWhitespaceIsCalledOut()
    {
        // A secret pasted with a trailing newline is invisible in every UI that stores it, and the
        // character count alone won't give it away — so it gets named.
        var described = TokenShape.Describe($"ghp_{new string('x', 36)}\n");
        await Assert.That(described).Contains("40 chars");
        await Assert.That(described).Contains("stored with surrounding whitespace");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task Describe_NothingStored(string? token) =>
        await Assert.That(TokenShape.Describe(token)).IsEqualTo("empty");

    [Test]
    public async Task Describe_NeverEmitsTheBody()
    {
        // SC107 is printed into build logs that are routinely public, so the random body must not
        // reach the message under any input shape.
        var body = "S3cretBodyValue0000000000000000000000";
        await Assert.That(TokenShape.Describe($"ghp_{body}")).DoesNotContain("S3cret");
        await Assert.That(TokenShape.Describe(body)).DoesNotContain("S3cret");
    }

    [Test]
    [Arguments("ghp_abc", "ghp_")]
    [Arguments("ghs_abc", "ghs_")]
    [Arguments("github_pat_AAA_BBB", "github_pat_")]
    public async Task Prefix_KnownMarkers(string token, string expected) =>
        await Assert.That(TokenShape.Prefix(token)).IsEqualTo(expected);

    [Test]
    [Arguments("nounderscoreatall")]
    [Arguments("_leadingunderscore")]
    [Arguments("averylongfirstsegment_body")]
    [Arguments(null)]
    public async Task Prefix_NoMarker(string? token) =>
        await Assert.That(TokenShape.Prefix(token)).IsNull();
}
