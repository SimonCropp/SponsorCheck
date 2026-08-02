namespace SponsorCheck.Web.Pages;

public partial class Author
{
    readonly string[] steps =
    [
        "Package",
        "Platforms",
        "Mode & scope",
        "Options",
        "Output"
    ];

    const int outputStep = 4;

    AuthorModel model = new();
    int step;

    bool CanAdvance => step switch
    {
        0 => !string.IsNullOrWhiteSpace(model.PackageId),
        1 => model.HasPlatform,
        2 => !model.OwnerMode || model.IsOwnerIdValid,
        3 => model.ExemptionErrors.Count == 0,
        _ => true
    };

    void SetShape(RepoShape shape)
    {
        model.RepoShape = shape;
        if (shape == RepoShape.MonorepoCpm && !model.OwnerMode)
        {
            model.OwnerMode = true;
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
        step = 0;
    }
}
