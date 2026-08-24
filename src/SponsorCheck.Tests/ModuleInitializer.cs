public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyDiffPlex.Initialize();
        VerifierSettings.IgnoreMembers(
            "HelpKeyword",
            "SenderName",
            "ContinueOnError",
            "ProjectFileOfTaskNode",
            "File",
            "Subcategory",
            "Timestamp");
        VerifierSettings.IgnoreMember<BuildEventArgs>(_ => _.ThreadId);
        VerifierSettings.Inline(maxLines: 10, applyMaxLinesToExisting: true);
    }
}
