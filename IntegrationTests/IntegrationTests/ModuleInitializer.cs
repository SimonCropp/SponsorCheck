namespace SponsorCheck.IntegrationTests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.Inline(maxLines: 10, applyMaxLinesToExisting: true);
        VerifyDiffPlex.Initialize();
    }
}
