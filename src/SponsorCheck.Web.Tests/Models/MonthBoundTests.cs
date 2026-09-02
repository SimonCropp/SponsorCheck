namespace SponsorCheck.Web.Tests.Models;

public class MonthBoundTests
{
    // A fixed clock, not DateTime.UtcNow: every case here is a boundary, and a boundary asserted
    // against the wall clock stops testing the boundary the moment the month turns over.
    static readonly DateTime now = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("2026-09", false)] // the current month is valid through its own end
    [Arguments("2026-10", false)]
    [Arguments("2026-08", true)]
    [Arguments("2025-12", true)]
    public async Task ExpiryIsDecidedAtMonthGranularity(string value, bool expected) =>
        await Assert.That(MonthBound.IsExpired(value, now)).IsEqualTo(expected);

    [Test]
    [Arguments("2027-09", false)] // exactly at the 12 month ceiling
    [Arguments("2027-08", false)]
    [Arguments("2027-10", true)]
    [Arguments("9999-12", true)]
    public async Task TheCeilingIsInclusive(string value, bool expected) =>
        await Assert.That(MonthBound.IsBeyondCeiling(value, now, 12)).IsEqualTo(expected);

    [Test]
    [Arguments(1, "2026-10")]
    [Arguments(3, "2026-12")]
    [Arguments(4, "2027-01")] // rolls the year without DateTime.AddMonths
    [Arguments(12, "2027-09")]
    [Arguments(120, "2036-09")]
    public async Task CeilingRollsTheYear(int months, string expected) =>
        await Assert.That(MonthBound.Ceiling(now, months)).IsEqualTo(expected);

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("May 2027")]
    [Arguments("2027-13")]
    [Arguments("2027")]
    public async Task AnUnparseableValueIsNeitherExpiredNorBeyond(string value)
    {
        // The format error is the caller's own, separately reported problem. Reporting it a second
        // time as "expired" would put two contradictory callouts under one field.
        await Assert.That(MonthBound.IsExpired(value, now)).IsFalse();
        await Assert.That(MonthBound.IsBeyondCeiling(value, now, 12)).IsFalse();
    }

    [Test]
    // DecisionApplier compares against {utcNow.Year + 1}-{utcNow.Month}, which is what 12 months of
    // this arithmetic has to produce for the SC035 callout to name the same ceiling the build enforces.
    public async Task TheLicensedUntilCapMatchesTheVerifiersOneYearRule() =>
        await Assert.That(MonthBound.Ceiling(now, MonthBound.LicensedUntilMaxTermMonths))
            .IsEqualTo($"{now.Year + 1:0000}-{now.Month:00}");
}
