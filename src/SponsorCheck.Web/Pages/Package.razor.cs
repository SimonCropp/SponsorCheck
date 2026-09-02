namespace SponsorCheck.Web.Pages;

/// <summary>
/// The package-specific entry point (<c>/package/{PackageId}</c>), meant to be linked from a
/// package's own readme or release notes. A separate page rather than a switch in the consumer
/// flow: it shares the step components but not the state machine. The package identity comes from
/// the url, the lookup runs on landing, and the questions the package already answers are never
/// shown. Owner mode leaves nothing to ask on the situation step (see the facts-known branch in
/// Consumer.razor), so that step is dropped and the visitor lands on the license mode directly.
/// </summary>
public partial class Package
{
    [Inject]
    public required PackageLookup PackageLookup { get; set; }

    /// <summary>The route value. Dots and dashes bind unchanged, so ids like Verify.Xunit work.</summary>
    [Parameter]
    public string? PackageId { get; set; }

    enum PackageStep
    {
        Situation,
        LicenseMode,
        Output
    }

    static readonly IReadOnlyList<PackageStep> perPackageSteps = [PackageStep.Situation, PackageStep.LicenseMode, PackageStep.Output];
    static readonly IReadOnlyList<PackageStep> ownerModeSteps = [PackageStep.LicenseMode, PackageStep.Output];

    IReadOnlyList<PackageStep> steps = perPackageSteps;
    IReadOnlyList<string> stepLabels = [];
    int stepIndex;
    ConsumerModel model = new();
    PackageFacts? facts;
    bool loading;
    string? error;

    // The route value the current lookup is for, and a token that outlives any one lookup: the
    // router reuses this instance when navigating from /package/A to /package/B, so B's lookup can
    // start while A's is still in flight, and A's completion must not overwrite B's state.
    string lookedUp = "";
    int loadToken;

    string requestedId => PackageId?.Trim() ?? "";

    string Title => requestedId.Length == 0 ? "package" : requestedId;

    protected override async Task OnParametersSetAsync()
    {
        var requested = requestedId;
        if (requested == lookedUp)
        {
            return;
        }

        lookedUp = requested;
        var token = ++loadToken;
        facts = null;
        error = null;
        loading = false;
        stepIndex = 0;
        model = new()
        {
            PackageId = requested
        };

        if (requested.Length == 0)
        {
            error = "The address needs a package id after /package/ — for example /package/ThePackage.";
            return;
        }

        loading = true;
        PackageFacts? result = null;
        string? failure = null;
        try
        {
            result = await PackageLookup.Inspect(requested, version: null);
        }
        catch (PackageLookupException exception)
        {
            failure = exception.Message;
        }
        catch (Exception exception)
        {
            failure = $"Lookup failed: {exception.Message}";
        }

        // Caught unconditionally and tested here rather than in a `when` filter: a stale lookup's
        // exception has to be swallowed too, or it escapes into Blazor's error ui.
        if (token != loadToken)
        {
            return;
        }

        loading = false;
        if (result == null)
        {
            error = failure;
            return;
        }

        Seed(result);
    }

    /// <summary>A fresh model seeded from the facts. Used on landing and by Start over, which keeps
    /// the facts rather than fetching them again.</summary>
    void Seed(PackageFacts source)
    {
        facts = source;
        model = new()
        {
            PackageId = source.PackageId
        };
        model.ApplyFacts(source);
        steps = source.OwnerMode ? ownerModeSteps : perPackageSteps;
        stepLabels = steps.Select(Label).ToList();
        stepIndex = 0;
    }

    static string Label(PackageStep step) =>
        step switch
        {
            PackageStep.Situation => "Situation",
            PackageStep.LicenseMode => "License mode",
            _ => "Output"
        };

    bool CanAdvance =>
        steps[stepIndex] switch
        {
            PackageStep.Situation => model.SituationComplete,
            PackageStep.LicenseMode => model.ModeComplete,
            _ => true
        };

    void Next()
    {
        if (CanAdvance && stepIndex < steps.Count - 1)
        {
            stepIndex++;
        }
    }

    void Back()
    {
        if (stepIndex > 0)
        {
            stepIndex--;
        }
    }

    void GoToStep(int target)
    {
        if (target < stepIndex)
        {
            stepIndex = target;
        }
    }

    void Restart()
    {
        if (facts is { } known)
        {
            Seed(known);
        }
    }
}
