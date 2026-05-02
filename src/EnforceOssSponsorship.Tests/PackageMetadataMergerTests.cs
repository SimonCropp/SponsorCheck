namespace EnforceOssSponsorship.Tests;

public class PackageMetadataMergerTests
{
    [Test]
    public async Task BothNull()
    {
        var result = PackageMetadataMerger.Merge("X", null, null);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task BothEmpty()
    {
        var result = PackageMetadataMerger.Merge("X", "", "   ");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task OnlyReference()
    {
        var result = PackageMetadataMerger.Merge("X", "alice", null);
        await Assert.That(result).IsEqualTo("alice");
    }

    [Test]
    public async Task OnlyVersion()
    {
        var result = PackageMetadataMerger.Merge("X", null, "alice");
        await Assert.That(result).IsEqualTo("alice");
    }

    [Test]
    public async Task BothAgreeCaseInsensitively()
    {
        var result = PackageMetadataMerger.Merge("X", "Alice", "alice");
        await Assert.That(result).IsEqualTo("Alice");
    }

    [Test]
    public async Task BothDisagreeThrows()
    {
        var ex = Assert.Throws<MaintenanceFeeException>(() =>
            PackageMetadataMerger.Merge("MyMeta", "alice", "bob"));
        await Assert.That(ex.Message).Contains("MyMeta");
        await Assert.That(ex.Message).Contains("alice");
        await Assert.That(ex.Message).Contains("bob");
    }

    [Test]
    public async Task TrimsValues()
    {
        var result = PackageMetadataMerger.Merge("X", "  alice  ", null);
        await Assert.That(result).IsEqualTo("alice");
    }
}
