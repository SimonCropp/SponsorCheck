namespace SponsorCheck.Web.Tests.Models;

public class ScCodeTests
{
    [Test]
    [Arguments("SC001", Placement.PerPackageProject)]
    [Arguments("sc001", Placement.PerPackageProject)]
    [Arguments(" SC007 ", Placement.PerPackageProject)]
    [Arguments("SC015", Placement.PerPackageProject)]
    [Arguments("SC002", Placement.PerPackageCpm)]
    [Arguments("SC008", Placement.PerPackageCpm)]
    [Arguments("SC016", Placement.PerPackageCpm)]
    [Arguments("SC019", Placement.PerPackageCpm)]
    [Arguments("SC020", Placement.PerPackageCpm)]
    [Arguments("SC021", Placement.OwnerMode)]
    [Arguments("SC028", Placement.OwnerMode)]
    [Arguments("SC029", Placement.PerPackageProject)]
    [Arguments("SC030", Placement.PerPackageCpm)]
    [Arguments("SC031", Placement.OwnerMode)]
    [Arguments("SC032", Placement.PerPackageProject)]
    [Arguments("SC033", Placement.PerPackageCpm)]
    [Arguments("SC034", Placement.OwnerMode)]
    [Arguments("SC035", Placement.PerPackageProject)]
    [Arguments("SC036", Placement.PerPackageCpm)]
    [Arguments("SC037", Placement.OwnerMode)]
    [Arguments("SC038", Placement.PerPackageProject)]
    [Arguments("SC039", Placement.PerPackageCpm)]
    [Arguments("SC040", Placement.OwnerMode)]
    [Arguments("SC041", Placement.PerPackageProject)]
    [Arguments("SC043", Placement.OwnerMode)]
    [Arguments("SC044", Placement.PerPackageProject)]
    [Arguments("SC045", Placement.PerPackageCpm)]
    [Arguments("SC047", Placement.PerPackageProject)]
    [Arguments("SC048", Placement.PerPackageCpm)]
    [Arguments("SC049", Placement.OwnerMode)]
    [Arguments("SC050", Placement.PerPackageProject)]
    [Arguments("SC051", Placement.PerPackageCpm)]
    [Arguments("SC052", Placement.OwnerMode)]
    [Arguments("SC053", Placement.PerPackageProject)]
    [Arguments("SC054", Placement.PerPackageCpm)]
    [Arguments("SC055", Placement.OwnerMode)]
    [Arguments("SC056", Placement.PerPackageProject)]
    [Arguments("SC057", Placement.PerPackageCpm)]
    [Arguments("SC058", Placement.OwnerMode)]
    public async Task ClassifiesPlacement(string code, Placement expected)
    {
        var classification = ScCode.Classify(code);
        await Assert.That(classification.Recognized).IsTrue();
        await Assert.That(classification.Placement).IsEqualTo(expected);
        await Assert.That(classification.AuthorSide).IsFalse();
    }

    [Test]
    [Arguments("SC017")]
    [Arguments("SC018")]
    [Arguments("SC059")]
    public async Task RecognizedWithoutPlacement(string code)
    {
        var classification = ScCode.Classify(code);
        await Assert.That(classification.Recognized).IsTrue();
        await Assert.That(classification.Placement).IsNull();
        await Assert.That(classification.Note).IsNotNull();
    }

    [Test]
    [Arguments("SC100")]
    [Arguments("SC106")]
    public async Task AuthorCodesRedirectToAuthorFlow(string code)
    {
        var classification = ScCode.Classify(code);
        await Assert.That(classification.Recognized).IsTrue();
        await Assert.That(classification.AuthorSide).IsTrue();
        await Assert.That(classification.Placement).IsNull();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("SC")]
    [Arguments("SC0")]
    [Arguments("SC000")]
    [Arguments("SC060")]
    [Arguments("SC099")]
    [Arguments("SC200")]
    [Arguments("SC-01")]
    [Arguments("CS0001")]
    [Arguments("banana")]
    public async Task Unrecognized(string code)
    {
        var classification = ScCode.Classify(code);
        await Assert.That(classification.Recognized).IsFalse();
        await Assert.That(classification.Placement).IsNull();
    }
}
