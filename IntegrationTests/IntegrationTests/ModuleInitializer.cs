namespace EnforceOssSponsorship.IntegrationTests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init() =>
        VerifyDiffPlex.Initialize();
}
