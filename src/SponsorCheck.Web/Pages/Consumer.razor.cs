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

    void CodeChanged()
    {
        classification = model.EnteredCode.Trim().Length == 0 ? null : ScCode.Classify(model.EnteredCode);
        if (classification is { Recognized: true, Placement: { } placement })
        {
            model.OwnerMode = placement == Placement.OwnerMode;
            model.Cpm = placement == Placement.PerPackageCpm;
        }
    }

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
