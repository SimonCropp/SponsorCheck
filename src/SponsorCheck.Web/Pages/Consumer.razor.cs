using System.Globalization;

namespace SponsorCheck.Web.Pages;

public partial class Consumer
{
    readonly string[] steps = ["Situation", "Package", "License mode", "Output"];
    const int OutputStep = 3;

    ConsumerModel model = new();
    int step;
    ScCodeClassification? classification;

    bool CanAdvance => step switch
    {
        0 => model.SituationComplete,
        1 => model.PackageComplete,
        2 => model.ModeComplete,
        _ => true
    };

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
        step = 0;
    }
}
