namespace SponsorCheck.Web.Pages;

public partial class Consumer
{
    [Inject]
    public required PackageLookup PackageLookup { get; set; }

    readonly string[] steps = ["Package", "Situation", "License mode", "Output"];
    const int OutputStep = 3;

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

    /// <summary>Facts narrow the platform list to the ones the author actually accepts.</summary>
    IEnumerable<Platform> SponsorPlatforms =>
        model.Facts is { BundlesSponsorCheck: true, Platforms.Count: > 0 } facts
            ? Platform.All.Where(_ => facts.Platforms.Any(enabled => enabled.Kind == _.Kind))
            : Platform.All;

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
        if (step < OutputStep && CanAdvance)
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

    void Restart()
    {
        model = new();
        classification = null;
        lookupError = null;
        step = 0;
    }
}
