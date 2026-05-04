public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyDiffPlex.Initialize();
        VerifierSettings.IgnoreMember<BuildEventArgs>(_ => _.ThreadId);
    }
}
