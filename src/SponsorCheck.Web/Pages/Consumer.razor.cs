namespace SponsorCheck.Web.Pages;

public partial class Consumer
{
    [Inject]
    public required PackageLookup PackageLookup { get; set; }

    readonly string[] steps = [
        "Package",
        "Situation",
        "License mode",
        "Output"];
    const int outputStep = 3;

    ConsumerModel model = new();
    int step;
    ScCodeClassification? classification;
    bool lookupBusy;
    string? lookupError;

    bool CanAdvance => step switch
    {
        0 => model.PackageComplete && !lookupBusy,
        1 => model.SituationComplete,
        2 => model.ModeComplete,
        _ => true
    };

    bool CanLookup => model.PackageComplete && !lookupBusy;

    async Task LookupPackage()
    {
        lookupBusy = true;
        lookupError = null;
        try
        {
            var facts = await PackageLookup.Inspect(model.PackageId, model.PackageVersion);
            model.ApplyFacts(facts);
        }
        catch (PackageLookupException exception)
        {
            lookupError = exception.Message;
        }
        catch (Exception exception)
        {
            lookupError = $"Lookup failed: {exception.Message}";
        }
        finally
        {
            lookupBusy = false;
        }
    }

    /// <summary>Any edit to the package identity invalidates previously read facts — they described
    /// a different package or version. A fresh lookup is one click.</summary>
    void PackageChanged()
    {
        model.Facts = null;
        lookupError = null;
    }

    /// <summary>The exemption card only renders when the package is known to define exemptions —
    /// or when nothing was looked up and the wizard can't know.</summary>
    bool ExemptionModeAvailable =>
        model.Facts is not { BundlesSponsorCheck: true } ||
        model.Facts.Exemptions.Count > 0;

    /// <summary>Flags a time-bounded exemption in the package summary, so the cap is visible while
    /// choosing a mode rather than only after picking the name.</summary>
    static string ExemptionLabel(PackageExemption exemption) =>
        exemption.MaxTermMonths is { } months
            ? $"{exemption.Name} (max {months}mo)"
            : exemption.Name;

    /// <summary>Facts narrow the platform list to the ones the author actually accepts.</summary>
    IEnumerable<Platform> SponsorPlatforms
    {
        get
        {
            if (model.Facts is {BundlesSponsorCheck: true, Platforms.Count: > 0} facts)
            {
                return Platform.All.Where(_ => facts.Platforms.Any(enabled => enabled.Kind == _.Kind));
            }

            return Platform.All;
        }
    }

    // Only GitHub Sponsors and Open Collective have a private/incognito notion at all. Polar
    // supporters are billing customers, not a published list, so there is nothing to be excluded
    // from — a Polar-only package must not offer the route or describe it in terms of two platforms
    // it does not use. The verifier itself has no platform gate, so this narrows what is offered, not
    // what is accepted. An uninspected package keeps the full platform list and so keeps the option.
    IEnumerable<Platform> PrivateCapablePlatforms =>
        SponsorPlatforms.Where(_ => _.Kind is PlatformKind.GitHub or PlatformKind.OpenCollective);

    bool PrivateSponsorshipOffered => PrivateCapablePlatforms.Any();

    string PrivateSponsorshipHint
    {
        get
        {
            var kinds = PrivateCapablePlatforms.Select(_ => _.Kind).ToList();
            var excluded = kinds switch
            {
                [PlatformKind.GitHub] => "Private sponsorships on GitHub Sponsors are",
                [PlatformKind.OpenCollective] => "Incognito contributions on Open Collective are",
                _ => "Private sponsorships on GitHub Sponsors, and incognito contributions on Open Collective, are"
            };
            return $"{excluded} never bundled into the hash list — so they can never match it. Declare an end month instead and the verifier trusts it, logging an SC059 audit line naming the account.";
        }
    }

    string? SponsorHint(Platform platform)
    {
        if (model.Facts is not { BundlesSponsorCheck: true } facts)
        {
            return null;
        }

        var enabled = facts.Platforms.FirstOrDefault(_ => _.Kind == platform.Kind);
        if (enabled == null)
        {
            return null;
        }

        var url = facts.LandingUrl ?? platform.SponsorUrl(enabled.Account);
        return $"Sponsor at {url}. Enter the account the sponsorship is made from (the consumer-side account).";
    }

    bool StartIsInFuture =>
        DateTime.TryParseExact(
            model.SponsorshipStart.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var start) &&
        start.Date > DateTime.UtcNow.Date;

    // The three "...Until" fields below carry the same shape of problem as StartIsInFuture: the value
    // parses, so the wizard would happily emit it, and the verifier then rejects it on the consumer's
    // very next build. Every input needed to catch that here is already loaded — the publisher's cap
    // came out of the nupkg and the ceiling is just arithmetic — so a wizard that prints the rule in
    // its own output ("a month further out fails with SC053") and then ignores it is the one place
    // the answer was known and not used.
    //
    // Warnings, not blocks: ModeComplete still turns on format alone. A cap read from an uninspected
    // package is the shipped default rather than that publisher's value, and blocking on a guess is
    // worse than saying so. Same call the future-start callout already makes.

    bool PrivateUntilExpired => MonthBound.IsExpired(model.PrivateUntilMonth, DateTime.UtcNow);

    bool PrivateUntilBeyondCap =>
        MonthBound.IsBeyondCeiling(model.PrivateUntilMonth, DateTime.UtcNow, model.PrivateSponsorMaxTermMonths);

    string PrivateUntilCeiling => MonthBound.Ceiling(DateTime.UtcNow, model.PrivateSponsorMaxTermMonths);

    bool LicensedUntilExpired => MonthBound.IsExpired(model.LicensedUntilMonth, DateTime.UtcNow);

    bool LicensedUntilBeyondCap =>
        MonthBound.IsBeyondCeiling(model.LicensedUntilMonth, DateTime.UtcNow, MonthBound.LicensedUntilMaxTermMonths);

    static string LicensedUntilCeiling => MonthBound.Ceiling(DateTime.UtcNow, MonthBound.LicensedUntilMaxTermMonths);

    bool ExemptionUntilExpired => MonthBound.IsExpired(model.ExemptionUntilMonth, DateTime.UtcNow);

    // Only a publisher-capped exemption has a ceiling. A consumer bounding an uncapped one picks any
    // month they like, so there is nothing to be beyond.
    bool ExemptionUntilBeyondCap =>
        model.ClaimedExemption?.MaxTermMonths is { } max &&
        MonthBound.IsBeyondCeiling(model.ExemptionUntilMonth, DateTime.UtcNow, max);

    string ExemptionUntilCeiling =>
        MonthBound.Ceiling(DateTime.UtcNow, model.ClaimedExemption?.MaxTermMonths ?? 0);

    // Said once rather than at each of the three call sites: an inspected package's cap is that
    // publisher's actual value, an uninspected one's is only the shipped default, and the difference
    // decides whether the warning is a fact or a likelihood.
    bool CapIsFromPackage => model.Facts is { BundlesSponsorCheck: true };

    // The codes above run as non-CPM/CPM/owner triples, so a callout that named one placement's code
    // would be wrong for the other two. Same selection ConsumerConfigGenerator.CodeFor makes when it
    // writes the expected-outcome prose, keyed off the placement the wizard has already settled.
    string CodeFor(string project, string cpm, string owner) =>
        model.Placement switch
        {
            Placement.PerPackageProject => project,
            Placement.PerPackageCpm => cpm,
            _ => owner
        };

    void CodeChanged()
    {
        classification = model.EnteredCode.Trim().Length == 0 ? null : ScCode.Classify(model.EnteredCode);
        if (classification is { Recognized: true, Placement: { } placement })
        {
            model.OwnerMode = placement == Placement.OwnerMode;
            model.Cpm = placement == Placement.PerPackageCpm;
        }
    }

    string CardClass(ConsumerLicenseMode mode) => model.Mode == mode ? "selected" : "";

    void SetMode(ConsumerLicenseMode mode) => model.Mode = mode;

    void Next()
    {
        if (step < outputStep && CanAdvance)
        {
            step++;
        }
    }

    void Back()
    {
        if (step > 0)
        {
            step--;
        }
    }

    void GoToStep(int target)
    {
        if (target < step)
        {
            step = target;
        }
    }

    void Restart()
    {
        model = new();
        classification = null;
        lookupError = null;
        step = 0;
    }
}
